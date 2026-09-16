using System; using System.Threading.Tasks; using System.Collections.Generic; using System.Drawing; using System.IO; using System.Linq; using System.Management; using System.Reflection; using System.Text; using System.Windows.Forms;
[assembly: AssemblyTitle("OMEN Lite Control")]
[assembly: AssemblyDescription("Lightweight controls for HP OMEN 15-dc0xxx (84DB)")]
[assembly: AssemblyVersion("0.3.0.0")]
[assembly: AssemblyFileVersion("0.3.0.0")]
[assembly: AssemblyInformationalVersion("0.3.0")]
namespace OmenModeSwitcher {
 static class HpBios {
  static ManagementObject Bios(){var s=new ManagementScope(@"root\wmi");s.Connect();var q=new ObjectQuery("SELECT * FROM hpqBIntM");var all=new ManagementObjectSearcher(s,q).Get().Cast<ManagementObject>().ToArray();var b=all.FirstOrDefault(x=>String.Equals(Convert.ToString(x["InstanceName"]),@"ACPI\PNP0C14\0_0",StringComparison.OrdinalIgnoreCase))??all.FirstOrDefault();if(b==null)throw new InvalidOperationException("找不到 HP WMI BIOS 接口。");return b;}
  static ManagementBaseObject Call(string method,uint cmd,uint type,byte[] data){return HardwareAccess.Run(()=>CallCore(method,cmd,type,data));}
  static ManagementBaseObject CallCore(string method,uint cmd,uint type,byte[] data){using(var b=Bios()){var dc=new ManagementClass(b.Scope,new ManagementPath("hpqBDataIn"),null);var i=dc.CreateInstance();i["Sign"]=new byte[]{0x53,0x45,0x43,0x55};i["Command"]=cmd;i["CommandType"]=type;i["Size"]=(uint)data.Length;i["hpqBData"]=data;var p=b.GetMethodParameters(method);p["InData"]=i;var r=b.InvokeMethod(method,p,null);var o=r["OutData"] as ManagementBaseObject;if(o==null)throw new InvalidOperationException("BIOS 没有返回结果。");int c=Convert.ToInt32(o["rwReturnCode"]);if(c!=0)throw new InvalidOperationException("BIOS 返回错误码 "+c+"。");return o;}}
  public static void SetMode(byte m){if(m>2)throw new ArgumentOutOfRangeException("m");HardwareStatus.RequireSupportedBoard();Call("hpqBIOSInt0",0x20008,0x1A,new byte[]{0xFF,m,0,0});}
  public static byte[] GetColors(){var d=Call("hpqBIOSInt128",0x20009,0x02,new byte[]{0,0,0,0})["Data"] as byte[];if(d==null||d.Length!=128)throw new InvalidOperationException("键盘色表长度不正确。");return d;}
  public static byte[] SetZones(Color[] colors,int[] brightness){byte[] d=GetColors();int n=Math.Min((int)d[0]+1,4);if(n<1)throw new InvalidOperationException("没有可用的键盘分区。");if(colors==null||brightness==null||colors.Length<n||brightness.Length<n)throw new ArgumentException("分区颜色参数不完整。");for(int x=0;x<n;x++){int p=25+x*3,b=Math.Max(0,Math.Min(100,brightness[x]));d[p]=(byte)Math.Round(colors[x].R*b/100.0);d[p+1]=(byte)Math.Round(colors[x].G*b/100.0);d[p+2]=(byte)Math.Round(colors[x].B*b/100.0);}Call("hpqBIOSInt0",0x20009,0x03,d);return d;}
 }
 static class UserData {
  public static readonly string DirectoryPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data");
  public static string File(string name){
   Directory.CreateDirectory(DirectoryPath);
   string marker=Path.Combine(DirectoryPath,".initialized");
   if(!System.IO.File.Exists(marker)){
    string old=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OMEN-Lite-Control");
    foreach(string item in new[]{"language.txt","keyboard-presets.txt"}){
     string source=Path.Combine(old,item),target=Path.Combine(DirectoryPath,item);
     if(!System.IO.File.Exists(target)&&System.IO.File.Exists(source))System.IO.File.Copy(source,target,false);
    }
    System.IO.File.WriteAllText(marker,"Portable preferences initialized.");
   }
   return Path.Combine(DirectoryPath,name);
  }
 }
 sealed class KeyboardPreset {
  public string Name; public Color[] Colors=new Color[4]; public int[] Brightness=new int[4];
  public override string ToString(){return Name;}
 }
 static class PresetStore {
  static string PathName{get{return UserData.File("keyboard-presets.txt");}}
  static string Clean(string s){return (s??"").Replace("|"," ").Replace("\r"," ").Replace("\n"," ").Trim();}
  public static List<KeyboardPreset> Load(){var list=new List<KeyboardPreset>();if(!File.Exists(PathName))return list;foreach(string line in File.ReadAllLines(PathName,Encoding.UTF8)){string[] p=line.Split('|');if(p.Length!=9||String.IsNullOrWhiteSpace(p[0]))continue;var x=new KeyboardPreset{Name=p[0]};bool ok=true;for(int i=0;i<4;i++){int rgb=0,b=0;ok&=Int32.TryParse(p[1+i*2],out rgb)&&Int32.TryParse(p[2+i*2],out b)&&rgb>=0&&rgb<=0xFFFFFF&&b>=0&&b<=100;x.Colors[i]=Color.FromArgb(Math.Max(0,Math.Min(0xFFFFFF,rgb)));x.Brightness[i]=Math.Max(0,Math.Min(100,b));}if(ok&&list.Count<8)list.Add(x);}return list;}
  public static void Save(List<KeyboardPreset> list){var lines=list.Take(8).Select(x=>Clean(x.Name)+"|"+String.Join("|",Enumerable.Range(0,4).SelectMany(i=>new[]{(x.Colors[i].ToArgb()&0xFFFFFF).ToString(),x.Brightness[i].ToString()})));File.WriteAllLines(PathName,lines,Encoding.UTF8);}
 }
 static class StatusReader {
  public static string Colors(byte[] d,string[] z){int n=Math.Min((int)d[0]+1,4);string[] a=new string[n];for(int i=0;i<n;i++){int p=25+i*3;a[i]=z[i]+" #"+d[p].ToString("X2")+d[p+1].ToString("X2")+d[p+2].ToString("X2");}return string.Join("  ",a);}
 }
 sealed class MainForm:Form {
  Action renderMode; byte[] displayedColors; string colorError; bool colorsWritten;
  bool english,zonesLoaded,installing,hardwareBusy; Label title,lastLabel,mode,color,msg,ecDetails; GroupBox modeBox,kb; Button enableDriver,refresh,def,perf,cool,white,red,blue,purple,apply,savePreset,deletePreset,language; TextBox presetName; ComboBox presetList;
  Color[] zoneColors=new Color[4]; TrackBar[] zoneBrightness=new TrackBar[4]; Button[] zoneButtons=new Button[4]; Label[] zoneLabels=new Label[4],zonePercent=new Label[4]; List<KeyboardPreset> presets;
  string T(string zh,string en){return english?en:zh;}
  string[] Zones(){return english?new[]{"Right","Middle","Left","WASD"}:new[]{"右区","中区","左区","WASD"};}
  public MainForm(){string lf=UserData.File("language.txt");english=File.Exists(lf)&&File.ReadAllText(lf).Trim()=="en";presets=PresetStore.Load();Font=new Font("Microsoft YaHei UI",9.5F);ClientSize=new Size(650,710);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
   title=L("",20,6,500,48);title.Font=new Font(Font.FontFamily,16F,FontStyle.Bold);language=B("",535,14,88,30);language.Click+=(s,e)=>SwitchLanguage();lastLabel=L("",22,65,175,26);mode=L("",200,65,285,26);refresh=B("",518,61,105,32);refresh.Click+=(s,e)=>RefreshAll();ecDetails=L("",22,96,601,32);enableDriver=B("",388,96,235,32);enableDriver.Visible=false;enableDriver.Click+=(s,e)=>InstallDriver();
   modeBox=G("",20,135,610,105);def=B("",18,30,180,48,modeBox);perf=B("",213,30,180,48,modeBox);cool=B("",408,30,180,48,modeBox);def.Click+=(s,e)=>Mode(0);perf.Click+=(s,e)=>Mode(1);cool.Click+=(s,e)=>Mode(2);
   kb=G("",20,250,610,365);for(int i=0;i<4;i++){int k=i,y=27+i*47;zoneLabels[i]=L("",15,y,62,32,kb);zoneButtons[i]=B("",80,y,115,32,kb);zoneButtons[i].Click+=(s,e)=>PickZone(k);zoneBrightness[i]=new TrackBar{Minimum=0,Maximum=100,TickFrequency=10,Value=100};zoneBrightness[i].SetBounds(210,y-2,285,40);kb.Controls.Add(zoneBrightness[i]);zonePercent[i]=L("100%",505,y,55,32,kb);zoneBrightness[i].ValueChanged+=(s,e)=>zonePercent[k].Text=zoneBrightness[k].Value+"%";}
   color=L("",15,207,575,25,kb);white=B("",15,240,100,34,kb);red=B("",125,240,100,34,kb);blue=B("",235,240,100,34,kb);purple=B("",345,240,100,34,kb);apply=B("",475,236,115,42,kb);white.Click+=(s,e)=>AllColor(Color.White);red.Click+=(s,e)=>AllColor(Color.FromArgb(220,0,0));blue.Click+=(s,e)=>AllColor(Color.FromArgb(0,90,255));purple.Click+=(s,e)=>AllColor(Color.FromArgb(145,30,210));apply.Click+=(s,e)=>ApplyZones();
   presetName=new TextBox();presetName.SetBounds(15,293,140,28);kb.Controls.Add(presetName);presetList=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};presetList.SetBounds(165,293,190,28);kb.Controls.Add(presetList);savePreset=B("",365,290,105,34,kb);deletePreset=B("",480,290,110,34,kb);presetList.SelectedIndexChanged+=(s,e)=>ShowPreset();savePreset.Click+=(s,e)=>SavePreset();deletePreset.Click+=(s,e)=>DeletePreset();
   msg=L("",20,628,610,68);msg.TextAlign=ContentAlignment.MiddleCenter;ApplyLanguage();ReloadPresetList();RefreshAll();}
  Label L(string t,int x,int y,int w,int h,Control p=null){var l=new Label{Text=t,UseMnemonic=false,TextAlign=ContentAlignment.MiddleLeft};l.SetBounds(x,y,w,h);(p??this).Controls.Add(l);return l;} Button B(string t,int x,int y,int w,int h,Control p=null){var b=new Button{Text=t};b.SetBounds(x,y,w,h);(p??this).Controls.Add(b);return b;} GroupBox G(string t,int x,int y,int w,int h){var g=new GroupBox{Text=t};g.SetBounds(x,y,w,h);Controls.Add(g);return g;}
  void ApplyLanguage(){Text=T("OMEN 独立控制器","OMEN Lite Control");title.Text=T("OMEN 独立性能与键盘控制","OMEN Performance & Lighting Control");language.Text=english?"中文":"English";lastLabel.Text=T("硬件当前模式 (EC)：","Current mode (EC):");refresh.Text=T("刷新状态","Refresh");modeBox.Text=T("本机 HP BIOS 性能策略（84DB 专用）","HP BIOS Performance Control (84DB only)");def.Text=T("默认","Balanced");perf.Text=T("狂暴 / 性能","Performance");cool.Text=T("酷冷","Comfort");kb.Text=T("键盘四分区静态颜色与亮度（RGB 强度）","4-Zone Keyboard Lighting (static RGB intensity)");string[] z=Zones();for(int i=0;i<4;i++){zoneLabels[i].Text=z[i];zoneButtons[i].Text=T("选择颜色","Choose Color");}white.Text=T("全部白色","All White");red.Text=T("全部红色","All Red");blue.Text=T("全部蓝色","All Blue");purple.Text=T("全部紫色","All Purple");apply.Text=T("应用","Apply");presetName.Text=(String.IsNullOrWhiteSpace(presetName.Text)||presetName.Text=="预设名称"||presetName.Text=="Preset name")?T("预设名称","Preset name"):presetName.Text;savePreset.Text=T("保存预设","Save Preset");deletePreset.Text=T("删除预设","Delete Preset");msg.Text=T("完全独立：不加载、不启动、不连接 OMEN Gaming Hub。","Independent: OMEN Gaming Hub is not loaded, started or contacted.");if(renderMode!=null)renderMode();RenderColors();}
  void SwitchLanguage(){english=!english;File.WriteAllText(UserData.File("language.txt"),english?"en":"zh");ApplyLanguage();}
  string ModeName(string id){if(id=="balanced"||id=="默认模式")return T("默认模式","Balanced");if(id=="performance"||id=="狂暴 / 性能模式")return T("狂暴 / 性能模式","Performance");if(id=="comfort"||id=="酷冷模式")return T("酷冷模式","Comfort");return T("未知 / 标志冲突","Unknown / conflicting flags");}
  bool BeginHardwareOperation(){if(hardwareBusy)return false;hardwareBusy=true;SetHardwareControls(false);return true;}
  void SetHardwareControls(bool enabled){enableDriver.Enabled=modeBox.Enabled=apply.Enabled=refresh.Enabled=enabled;}
  async void EndHardwareOperation(){await Task.Delay(1000);hardwareBusy=false;if(!IsDisposed&&!Disposing)SetHardwareControls(true);}
  async void RefreshAll(){if(!BeginHardwareOperation())return;try{await RefreshAllCore();}catch(Exception e){Fail(e);}finally{EndHardwareOperation();}}
  async Task RefreshAllCore(){await RefreshMode();try{byte[] d=await Task.Run(()=>HpBios.GetColors());displayedColors=d;colorsWritten=false;colorError=null;RenderColors();if(!zonesLoaded){int n=Math.Min((int)d[0]+1,4);for(int i=0;i<4;i++){int p=25+Math.Min(i,Math.Max(0,n-1))*3;zoneColors[i]=Color.FromArgb(d[p],d[p+1],d[p+2]);PaintZone(i);}zonesLoaded=true;}}catch(Exception e){displayedColors=null;colorError=e.Message;RenderColors();}}
  void ShowState(PerformanceState state){renderMode=()=>ShowState(state);enableDriver.Visible=false;ecDetails.Width=601;mode.Text=ModeName(state.Id);mode.ForeColor=state.Mode<0?Color.DarkOrange:SystemColors.ControlText;ecDetails.Text=state.Raw;}
  void RenderDriverState(DriverState state){renderMode=()=>RenderDriverState(state);
   mode.Text=T("未知（无法回读）","Unknown (read unavailable)");mode.ForeColor=Color.DarkOrange;
   enableDriver.Visible=state==DriverState.Missing||state==DriverState.UpdateRequired;
   enableDriver.Text=state==DriverState.UpdateRequired?T("更新硬件读取驱动","Update readback driver"):T("启用硬件读取（安装驱动）","Enable readback (install driver)");
   ecDetails.Width=enableDriver.Visible?356:601;
   ecDetails.Text=state==DriverState.Missing?T("未安装读取组件；模式切换和键盘灯仍可用。","Driver needed for readback. Controls still work."):state==DriverState.UpdateRequired?T("读取驱动需要更新；仅在点击后更新。","Update needed for hardware readback."):T("读取驱动暂不可用。请确认管理员权限，或重启后刷新。","Readback driver unavailable. Run as administrator, or restart and refresh.");
  }
  async Task RefreshMode(){try{DriverState state=await Task.Run(()=>{HardwareStatus.RequireSupportedBoard();return DriverSetup.Probe();});if(state==DriverState.Available)ShowState(await Task.Run(()=>HardwareStatus.Read()));else RenderDriverState(state);}catch(Exception e){ShowReadFailure(e);}}
  void RenderReadFailure(Exception e){
   renderMode=()=>RenderReadFailure(e);
   mode.Text=T("未知（无法回读）","Unknown (read unavailable)");mode.ForeColor=Color.DarkOrange;enableDriver.Visible=false;ecDetails.Width=601;ecDetails.Text=e.Message;
  }
  void ShowReadFailure(Exception e){
   RenderReadFailure(e);
   if(e is NotSupportedException)return;
   try{DriverState state=DriverSetup.Probe();if(state!=DriverState.Available)RenderDriverState(state);}catch{}
  }
  void RenderColors(){
   if(displayedColors!=null)color.Text=(colorsWritten?T("已写入色值：","Written colors: "):T("当前色值：","Current colors: "))+StatusReader.Colors(displayedColors,Zones());
   else color.Text=colorError==null?T("当前色值：尚未读取","Current colors: not read yet"):T("当前色值：读取失败（","Current colors: read failed (")+colorError+")";
  }
  async void InstallDriver(){
   if(!BeginHardwareOperation())return;installing=true;
   msg.ForeColor=SystemColors.ControlText;msg.Text=T("正在安装签名读取驱动，仅需一次；不会自动重启。","Installing the signed readback driver once. Windows will not restart automatically.");
   try{bool restart=await Task.Run(()=>DriverSetup.Install());await RefreshMode();if(restart)Warn(T("驱动已安装，需要重启 Windows；模式切换和键盘灯仍可用。","Driver installed. Restart Windows to enable readback; mode and lighting controls still work."));else if(enableDriver.Visible)Warn(T("驱动尚未就绪，请稍后刷新状态。","Driver is not ready; refresh again shortly."));else Ok(T("驱动已就绪；以后直接运行，无需再次安装。","Driver ready. Future launches need no installation."));}
   catch(Exception e){ShowReadFailure(e);Fail(e);}
   finally{installing=false;EndHardwareOperation();}
  }
  protected override void OnFormClosing(FormClosingEventArgs e){if(installing&&e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Warn(T("正在准备读取组件，请等待安装结束。","Please wait for driver setup to finish."));}base.OnFormClosing(e);}
  async void Mode(byte value){if(!BeginHardwareOperation())return;try{await Task.Run(()=>HpBios.SetMode(value));try{PerformanceState state=await Task.Run(()=>HardwareStatus.WaitForMode(value));ShowState(state);if(state.Mode==value)Ok(T("BIOS 指令已接受，EC 回读确认：","BIOS request accepted; EC confirmed: ")+ModeName(state.Id));else Warn(T("BIOS 指令已接受，但 EC 当前状态与请求不一致：","BIOS request accepted, but EC differs from requested mode: ")+ModeName(state.Id));}catch(Exception e){ShowReadFailure(e);Warn(T("BIOS 指令已接受；无法确认实际状态：","BIOS request accepted; actual state could not be confirmed: ")+e.Message);}}catch(Exception e){Fail(e);}finally{EndHardwareOperation();}}
  void PaintZone(int i){zoneButtons[i].BackColor=zoneColors[i];zoneButtons[i].ForeColor=zoneColors[i].GetBrightness()<0.45f?Color.White:Color.Black;}
  void PickZone(int i){using(var d=new ColorDialog{Color=zoneColors[i],FullOpen=true})if(d.ShowDialog(this)==DialogResult.OK){zoneColors[i]=d.Color;PaintZone(i);}}
  void AllColor(Color c){for(int i=0;i<4;i++){zoneColors[i]=c;PaintZone(i);}}
  async void ApplyZones(){if(!BeginHardwareOperation())return;try{Color[] colors=zoneColors.ToArray();int[] brightness=zoneBrightness.Select(x=>x.Value).ToArray();displayedColors=await Task.Run(()=>HpBios.SetZones(colors,brightness));colorsWritten=true;colorError=null;RenderColors();Ok(T("四个分区的颜色和亮度已写入 BIOS 色表。","Four-zone colors and brightness were written to the BIOS color table."));}catch(Exception e){Fail(e);}finally{EndHardwareOperation();}}
  void ReloadPresetList(){int old=presetList.SelectedIndex;presetList.Items.Clear();foreach(var p in presets)presetList.Items.Add(p);if(presetList.Items.Count>0)presetList.SelectedIndex=Math.Min(Math.Max(old,0),presetList.Items.Count-1);}
  void SavePreset(){string name=presetName.Text.Trim();if(String.IsNullOrWhiteSpace(name)||name=="预设名称"||name=="Preset name"){Warn(T("请输入预设名称。","Enter a preset name."));return;}int index=presets.FindIndex(x=>String.Equals(x.Name,name,StringComparison.OrdinalIgnoreCase));if(index<0&&presets.Count>=8){Warn(T("最多只能保存 8 个预设。","A maximum of 8 presets can be saved."));return;}var p=new KeyboardPreset{Name=name,Colors=zoneColors.ToArray(),Brightness=zoneBrightness.Select(x=>x.Value).ToArray()};if(index>=0)presets[index]=p;else presets.Add(p);PresetStore.Save(presets);ReloadPresetList();presetList.SelectedItem=presets.First(x=>String.Equals(x.Name,name,StringComparison.OrdinalIgnoreCase));Ok(T("预设已保存：","Preset saved: ")+name);}
  void ShowPreset(){var p=presetList.SelectedItem as KeyboardPreset;if(p==null)return;for(int i=0;i<4;i++){zoneColors[i]=p.Colors[i];zoneBrightness[i].Value=p.Brightness[i];PaintZone(i);}presetName.Text=p.Name;msg.ForeColor=SystemColors.ControlText;msg.Text=T("预设已载入到界面，点击“应用”后写入键盘。","Preset loaded into the editor. Click Apply to write it to the keyboard.");}
  void DeletePreset(){var p=presetList.SelectedItem as KeyboardPreset;if(p==null){Warn(T("请先选择一个预设。","Select a preset first."));return;}presets.Remove(p);PresetStore.Save(presets);ReloadPresetList();Ok(T("预设已删除。","Preset deleted."));}
  void Ok(string s){msg.ForeColor=Color.DarkGreen;msg.Text=s;} void Warn(string s){msg.ForeColor=Color.DarkOrange;msg.Text=s;} void Fail(Exception e){msg.ForeColor=Color.DarkRed;msg.Text=T("操作失败：","Operation failed: ")+e.Message;}
 }
 static class Program{
  [STAThread]static int Main(string[] args){
   if(args.Length==2&&args[0]=="--status"){
    try{var state=HardwareStatus.Read();File.WriteAllText(args[1],"mode="+state.Id+Environment.NewLine+state.Raw+Environment.NewLine+"read_at_utc="+state.ReadAtUtc.ToString("o"),Encoding.UTF8);return state.Mode<0?2:0;}
    catch(Exception e){File.WriteAllText(args[1],"mode=unknown"+Environment.NewLine+e.ToString(),Encoding.UTF8);return 1;}
   }
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);try{Application.Run(new MainForm());return 0;}catch(Exception e){MessageBox.Show("Unable to open portable data folder or initialize application. Extract to a writable folder.\n"+e.Message,"OMEN Lite Control",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
  }
 }
}
