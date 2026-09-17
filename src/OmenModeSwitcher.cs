using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

[assembly:AssemblyTitle("OMEN Lite Control")]
[assembly:AssemblyDescription("Lightweight controls for HP OMEN 15-dc0xxx (84DB)")]
[assembly:AssemblyVersion("0.4.2.0")]
[assembly:AssemblyFileVersion("0.4.2.0")]
[assembly:AssemblyInformationalVersion("0.4.2")]
namespace OmenModeSwitcher
{
    static class HpBios
    {
        static ManagementObject Bios()
        {
            var s = new ManagementScope(@"root\wmi");
            s.Connect();
            var q = new ObjectQuery("SELECT * FROM hpqBIntM");
            var all = new ManagementObjectSearcher(s, q).Get().Cast<ManagementObject>().ToArray();
            var b = all.FirstOrDefault(x => String.Equals(Convert.ToString(x["InstanceName"]),
                                                          @"ACPI\PNP0C14\0_0",
                                                          StringComparison.OrdinalIgnoreCase)) ??
                    all.FirstOrDefault();
            if (b == null)
                throw new InvalidOperationException("找不到 HP WMI BIOS 接口。");
            return b;
        }

        static ManagementBaseObject Call(string method, uint cmd, uint type, byte[] data)
        {
            return HardwareAccess.Run(() => CallCore(method, cmd, type, data));
        }

        static ManagementBaseObject CallCore(string method, uint cmd, uint type, byte[] data)
        {
            using (var b = Bios())
            {
                var dc = new ManagementClass(b.Scope, new ManagementPath("hpqBDataIn"), null);
                var i = dc.CreateInstance();
                i["Sign"] = new byte[] { 0x53, 0x45, 0x43, 0x55 };
                i["Command"] = cmd;
                i["CommandType"] = type;
                i["Size"] = (uint)data.Length;
                i["hpqBData"] = data;
                var p = b.GetMethodParameters(method);
                p["InData"] = i;
                var r = b.InvokeMethod(method, p, null);
                var o = r["OutData"] as ManagementBaseObject;
                if (o == null)
                    throw new InvalidOperationException("BIOS 没有返回结果。");
                int c = Convert.ToInt32(o["rwReturnCode"]);
                if (c != 0)
                    throw new InvalidOperationException("BIOS 返回错误码 " + c + "。");
                return o;
            }
        }

        public static void SetMode(byte m)
        {
            if (m > 2)
                throw new ArgumentOutOfRangeException("m");
            HardwareStatus.RequireSupportedBoard();
            Call("hpqBIOSInt0", 0x20008, 0x1A, new byte[] { 0xFF, m, 0, 0 });
        }

        public static byte[] GetColors()
        {
            var d =
                Call("hpqBIOSInt128", 0x20009, 0x02, new byte[] { 0, 0, 0, 0 })["Data"] as byte[];
            if (d == null || d.Length != 128)
                throw new InvalidOperationException("键盘色表长度不正确。");
            return d;
        }

        public static byte[] SetZones(Color[] colors, int[] brightness)
        {
            byte[] d = GetColors();
            int n = Math.Min((int)d[0] + 1, 4);
            if (n < 1)
                throw new InvalidOperationException("没有可用的键盘分区。");
            if (colors == null || brightness == null || colors.Length < n || brightness.Length < n)
                throw new ArgumentException("分区颜色参数不完整。");
            for (int x = 0; x < n; x++)
            {
                int p = 25 + x * 3, b = Math.Max(0, Math.Min(100, brightness[x]));
                d[p] = (byte)Math.Round(colors[x].R * b / 100.0);
                d[p + 1] = (byte)Math.Round(colors[x].G * b / 100.0);
                d[p + 2] = (byte)Math.Round(colors[x].B * b / 100.0);
            }
            Call("hpqBIOSInt0", 0x20009, 0x03, d);
            return d;
        }
    }

    static class UserData
    {
        public static readonly string DirectoryPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");

        public static string File(string name)
        {
            Directory.CreateDirectory(DirectoryPath);
            string marker = Path.Combine(DirectoryPath, ".initialized");
            if (!System.IO.File.Exists(marker))
            {
                string old = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OMEN-Lite-Control");
                foreach (string item in new[] { "language.txt", "keyboard-presets.txt" })
                {
                    string source = Path.Combine(old, item),
                           target = Path.Combine(DirectoryPath, item);
                    if (!System.IO.File.Exists(target) && System.IO.File.Exists(source))
                        System.IO.File.Copy(source, target, false);
                }
                System.IO.File.WriteAllText(marker, "Portable preferences initialized.");
            }
            return Path.Combine(DirectoryPath, name);
        }
    }

    sealed class KeyboardPreset
    {
        public string Name;
        public Color[] Colors = new Color[4];
        public int[] Brightness = new int[4];

        public override string ToString()
        {
            return Name;
        }
    }

    static class PresetStore
    {
        static string PathName
        {
            get {
                return UserData.File("keyboard-presets.txt");
            }
        }

        static string Clean(string s)
        {
            return (s ?? "").Replace("|", " ").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        public static List<KeyboardPreset> Load(bool english = false)
        {
            var list = new List<KeyboardPreset>();
            string[] stored =
                File.Exists(PathName) ? File.ReadAllLines(PathName, Encoding.UTF8) : new string[0];
            foreach (string line in stored)
            {
                string[] p = line.Split('|');
                if (p.Length != 9 || String.IsNullOrWhiteSpace(p[0]))
                    continue;
                var x = new KeyboardPreset { Name = p[0] };
                bool ok = true;
                for (int i = 0; i < 4; i++)
                {
                    int rgb = 0, b = 0;
                    ok &= Int32.TryParse(p[1 + i * 2], out rgb) &&
                          Int32.TryParse(p[2 + i * 2], out b) && rgb >= 0 && rgb <= 0xFFFFFF &&
                          b >= 0 && b <= 100;
                    x.Colors[i] =
                        Color.FromArgb(255, Color.FromArgb(Math.Max(0, Math.Min(0xFFFFFF, rgb))));
                    x.Brightness[i] = Math.Max(0, Math.Min(100, b));
                }
                if (ok)
                    list.Add(x);
            }
            // Seed once, including upgrades. The header survives an empty list so deleted defaults
            // stay deleted.
            if (!stored.Contains("# presets-v2"))
            {
                string[] names = english
                                     ? new[] { "All White", "All Red", "All Blue", "All Purple" }
                                     : new[] { "全部白色", "全部红色", "全部蓝色", "全部紫色" };
                Color[] colors = { Color.White, Color.FromArgb(220, 0, 0),
                                   Color.FromArgb(0, 90, 255), Color.FromArgb(145, 30, 210) };
                for (int i = 0; i < colors.Length; i++)
                {
                    string name = names[i];
                    for (int suffix = 2; list.Any(
                             x => String.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                         suffix++)
                        name = names[i] + " " + suffix;
                    list.Add(
                        new KeyboardPreset { Name = name,
                                             Colors = Enumerable.Repeat(colors[i], 4).ToArray(),
                                             Brightness = new[] { 100, 100, 100, 100 } });
                }
                Save(list);
            }
            return list;
        }

        public static void Save(List<KeyboardPreset> list)
        {
            var lines = list.Select(
                x => Clean(x.Name) + "|" +
                     String.Join("|", Enumerable.Range(0, 4).SelectMany(
                                          i => new[] { (x.Colors[i].ToArgb() & 0xFFFFFF).ToString(),
                                                       x.Brightness[i].ToString() })));
            string path = PathName, temp = path + ".tmp";
            File.WriteAllLines(temp, new[] { "# presets-v2" }.Concat(lines), Encoding.UTF8);
            if (File.Exists(path))
                File.Replace(temp, path, null);
            else
                File.Move(temp, path);
        }
    }

    sealed class MainForm : Form
    {
        Action renderMode;
        bool english, installing, hardwareBusy, biosColorsLoaded, biosReadFailed;
        readonly KeyboardPreset biosPreset =
            new KeyboardPreset { Brightness = new[] { 100, 100, 100, 100 } };
        Label lastLabel, mode, ecDetails;
        ToolStripStatusLabel msg;
        ToolTip detailsTip = new ToolTip();
        GroupBox modeBox, kb;
        Button enableDriver, refresh, def, perf, cool, newPreset, apply, savePreset, deletePreset,
            language;
        TextBox presetName;
        ListBox presetList;
        bool loadingPresets;
        Color[] zoneColors = new Color[4];
        int[] zoneBrightness = { 100, 100, 100, 100 };
        KeyboardLightingControl keyboard;
        TrackBar brightnessEditor;
        Button colorEditor;
        Label brightnessPercent;
        bool updatingEditor;
        List<KeyboardPreset> presets;

        string T(string zh, string en)
        {
            return english ? en : zh;
        }

        string[] Zones()
        {
            return english ? new[] { "Right", "Middle", "Left", "WASD" }
                           : new[] { "右区", "中区", "左区", "WASD" };
        }

        public MainForm()
        {
            string lf = UserData.File("language.txt");
            english = File.Exists(lf) && File.ReadAllText(lf).Trim() == "en";
            presets = PresetStore.Load(english);
            Font = new Font("Microsoft YaHei UI", 9.5F);
            ClientSize = new Size(650, 650);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            language = B("", 535, 12, 88, 30);
            language.Click += (s, e) => SwitchLanguage();
            lastLabel = L("", 22, 15, 175, 28);
            mode = L("", 200, 15, 190, 28);
            mode.Font = new Font(Font, FontStyle.Bold);
            refresh = B("", 415, 12, 105, 32);
            refresh.Click += (s, e) => RefreshAll();
            ecDetails = L("", 22, 52, 601, 48);
            enableDriver = B("", 388, 61, 235, 32);
            enableDriver.Visible = false;
            enableDriver.Click += (s, e) => InstallDriver();
            modeBox = G("", 20, 108, 610, 92);
            def = B("", 18, 27, 180, 46, modeBox);
            perf = B("", 213, 27, 180, 46, modeBox);
            cool = B("", 408, 27, 180, 46, modeBox);
            def.Click += (s, e) => Mode(0);
            perf.Click += (s, e) => Mode(1);
            cool.Click += (s, e) => Mode(2);
            kb = G("", 20, 210, 610, 407);
            keyboard = new KeyboardLightingControl();
            keyboard.SetBounds(15, 26, 575, 190);
            keyboard.ZoneSelected += (sender, args) => UpdateZoneEditor();
            keyboard.ZoneActivated += (sender, args) => PickZone(keyboard.SelectedZone);
            kb.Controls.Add(keyboard);
            colorEditor = B("", 15, 228, 170, 32, kb);
            colorEditor.Click += (sender, args) => PickZone(keyboard.SelectedZone);
            brightnessEditor =
                new TrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, Value = 100 };
            brightnessEditor.SetBounds(195, 224, 205, 40);
            kb.Controls.Add(brightnessEditor);
            brightnessPercent = L("100%", 402, 228, 65, 30, kb);
            brightnessEditor.ValueChanged += (sender, args) =>
            {
                if (updatingEditor)
                    return;
                int zone = keyboard.SelectedZone;
                zoneBrightness[zone] = brightnessEditor.Value;
                PaintZone(zone);
            };
            apply = B("", 475, 224, 115, 38, kb);
            apply.Click += (sender, args) => ApplyZones();
            presetList = new ListBox { DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 27,
                                       IntegralHeight = false };
            presetList.SetBounds(15, 280, 220, 114);
            presetList.DrawItem += DrawPreset;
            kb.Controls.Add(presetList);
            presetName = new TextBox();
            presetName.SetBounds(250, 280, 340, 28);
            kb.Controls.Add(presetName);
            savePreset = B("", 250, 322, 105, 34, kb);
            newPreset = B("", 367, 322, 105, 34, kb);
            deletePreset = B("", 484, 322, 106, 34, kb);
            presetList.SelectedIndexChanged += (sender, args) => ShowPreset();
            savePreset.Click += (sender, args) => SavePreset();
            newPreset.Click += (sender, args) => NewPreset();
            deletePreset.Click += (sender, args) => DeletePreset();
            var statusBar = new StatusStrip { SizingGrip = false, ShowItemToolTips = true };
            msg = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft,
                                             AutoToolTip = false };
            msg.TextChanged += (sender, args) => msg.ToolTipText = msg.Text;
            statusBar.Items.Add(msg);
            Controls.Add(statusBar);
            FormClosed += (sender, args) => detailsTip.Dispose();
            ApplyLanguage();
            ReloadPresetList();
            RefreshAll();
        }

        Label L(string t, int x, int y, int w, int h, Control p = null)
        {
            var l = new Label { Text = t, UseMnemonic = false,
                                TextAlign = ContentAlignment.MiddleLeft };
            l.SetBounds(x, y, w, h);
            (p ?? this).Controls.Add(l);
            return l;
        }

        Button B(string t, int x, int y, int w, int h, Control p = null)
        {
            var b = new Button { Text = t };
            b.SetBounds(x, y, w, h);
            (p ?? this).Controls.Add(b);
            return b;
        }

        GroupBox G(string t, int x, int y, int w, int h)
        {
            var g = new GroupBox { Text = t };
            g.SetBounds(x, y, w, h);
            Controls.Add(g);
            return g;
        }

        void ApplyLanguage()
        {
            Text = T("OMEN 独立控制器", "OMEN Lite Control") + " v" + Application.ProductVersion;
            language.Text = english ? "中文" : "English";
            lastLabel.Text = T("当前模式 · EC 回读", "Mode · EC readback");
            refresh.Text = T("刷新状态", "Refresh");
            modeBox.Text = T("BIOS 性能策略", "BIOS Performance Policy");
            def.Text = T("默认", "Balanced");
            perf.Text = T("狂暴", "Performance");
            cool.Text = T("酷冷", "Comfort");
            kb.Text = T("键盘四分区示意 · 点击选区，双击选色",
                        "4-Zone keyboard · Click to select, double-click for color");
            keyboard.SetNames(Zones());
            UpdateZoneEditor();
            apply.Text = T("应用", "Apply");
            presetName.Text = (String.IsNullOrWhiteSpace(presetName.Text) ||
                               presetName.Text == "预设名称" || presetName.Text == "Preset name")
                                  ? T("预设名称", "Preset name")
                                  : presetName.Text;
            savePreset.Text = T("保存修改", "Save changes");
            newPreset.Text = T("另存新预设", "Save as new");
            deletePreset.Text = T("删除", "Delete");
            msg.ForeColor = SystemColors.ControlText;
            msg.Text = T("就绪", "Ready");
            UpdateBiosPresetName();
            if (renderMode != null)
                renderMode();
        }

        void SwitchLanguage()
        {
            english = !english;
            File.WriteAllText(UserData.File("language.txt"), english ? "en" : "zh");
            ApplyLanguage();
        }

        string ModeName(string id)
        {
            if (id == "balanced" || id == "默认模式")
                return T("默认模式", "Balanced");
            if (id == "performance" || id == "狂暴模式")
                return T("狂暴模式", "Performance");
            if (id == "comfort" || id == "酷冷模式")
                return T("酷冷模式", "Comfort");
            return T("未知 / 标志冲突", "Unknown / conflicting flags");
        }

        bool BeginHardwareOperation()
        {
            if (hardwareBusy)
                return false;
            hardwareBusy = true;
            SetHardwareControls(false);
            return true;
        }

        void SetHardwareControls(bool enabled)
        {
            enableDriver.Enabled = modeBox.Enabled = apply.Enabled = refresh.Enabled = enabled;
        }

        async void EndHardwareOperation()
        {
            await Task.Delay(1000);
            hardwareBusy = false;
            if (!IsDisposed && !Disposing)
                SetHardwareControls(true);
        }

        async void RefreshAll()
        {
            if (!BeginHardwareOperation())
                return;
            try
            {
                await RefreshAllCore();
            }
            catch (Exception e)
            {
                Fail(e);
            }
            finally
            {
                EndHardwareOperation();
            }
        }

        async Task RefreshAllCore()
        {
            await RefreshMode();
            try
            {
                await RefreshKeyboard(true);
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        void UpdateBiosPresetName()
        {
            string previous = biosPreset.Name;
            biosPreset.Name = biosColorsLoaded ? T("当前 BIOS", "Current BIOS")
                              : biosReadFailed ? T("BIOS · 读取失败", "BIOS · Read failed")
                                               : T("BIOS · 未读取", "BIOS · Unread");
            if (presetList.SelectedItem == biosPreset && presetName.Text == previous)
                presetName.Text = biosPreset.Name;
            presetList.Invalidate();
        }

        async Task RefreshKeyboard(bool updateEditor)
        {
            try
            {
                UpdateBiosColors(await Task.Run(() => HpBios.GetColors()), updateEditor);
            }
            catch
            {
                biosColorsLoaded = false;
                biosReadFailed = true;
                UpdateBiosPresetName();
                throw;
            }
        }

        void UpdateBiosColors(byte[] data, bool updateEditor)
        {
            if (data == null || data.Length != 128)
                throw new InvalidDataException("Invalid BIOS keyboard color table.");
            int count = Math.Min((int)data[0] + 1, 4);
            for (int i = 0; i < 4; i++)
            {
                int offset = 25 + Math.Min(i, count - 1) * 3;
                biosPreset.Colors[i] =
                    Color.FromArgb(data[offset], data[offset + 1], data[offset + 2]);
                // BIOS returns effective RGB, already scaled by any previously applied brightness.
                biosPreset.Brightness[i] = 100;
            }
            biosColorsLoaded = true;
            biosReadFailed = false;
            UpdateBiosPresetName();
            if (updateEditor)
            {
                ReloadPresetList(biosPreset);
                LoadPresetColors(biosPreset);
            }
        }

        void LoadPresetColors(KeyboardPreset preset)
        {
            for (int i = 0; i < 4; i++)
            {
                zoneColors[i] = preset.Colors[i];
                zoneBrightness[i] = preset.Brightness[i];
                PaintZone(i);
            }
            presetName.Text = preset.Name;
        }

        void ShowState(PerformanceState state)
        {
            renderMode = () => ShowState(state);
            enableDriver.Visible = false;
            ecDetails.Width = 601;
            mode.Text = ModeName(state.Id);
            mode.ForeColor = state.Mode < 0 ? Color.DarkOrange : SystemColors.ControlText;
            string performance = (state.F8 & 2) != 0 ? T("开启", "On") : T("关闭", "Off");
            string comfort = (state.EC & 1) != 0 ? T("开启", "On") : T("关闭", "Off");
            ecDetails.ForeColor = SystemColors.GrayText;
            ecDetails.Text = T("狂暴标志：", "Performance flag: ") + performance +
                             "    ·    F8 bit 1 = " + ((state.F8 >> 1) & 1) + "    ·    0x" +
                             state.F8.ToString("X2") + Environment.NewLine +
                             T("酷冷标志：", "Comfort flag: ") + comfort +
                             "    ·    EC bit 0 = " + (state.EC & 1) + "    ·    0x" +
                             state.EC.ToString("X2");
            detailsTip.SetToolTip(
                ecDetails,
                T("从嵌入式控制器回读的 BIOS 模式标志。F8、EC 是寄存器地址，十六进制数为完整原值。",
                  "BIOS mode flags read from the embedded controller. F8 and EC are register addresses; hex numbers show the full raw values."));
        }

        void RenderDriverState(DriverState state)
        {
            renderMode = () => RenderDriverState(state);
            mode.Text = T("未知（无法回读）", "Readback unavailable");
            mode.ForeColor = Color.DarkOrange;
            enableDriver.Visible =
                state == DriverState.Missing || state == DriverState.UpdateRequired;
            enableDriver.Text =
                state == DriverState.UpdateRequired
                    ? T("更新硬件读取驱动", "Update readback driver")
                    : T("启用硬件读取（安装驱动）", "Enable readback (install driver)");
            ecDetails.Width = enableDriver.Visible ? 356 : 601;
            ecDetails.ForeColor = SystemColors.GrayText;
            detailsTip.SetToolTip(ecDetails, null);
            ecDetails.Text =
                state == DriverState.Missing ? T("未安装读取组件；模式切换和键盘灯仍可用。",
                                                 "Driver needed for readback. Controls still work.")
                : state == DriverState.UpdateRequired
                    ? T("读取驱动需要更新；仅在点击后更新。",
                        "Update needed for hardware readback.")
                    : T("读取驱动暂不可用。请确认管理员权限，或重启后刷新。",
                        "Readback driver unavailable. Run as administrator, or restart and refresh.");
        }

        async Task RefreshMode()
        {
            try
            {
                DriverState state = await Task.Run(() =>
                                                   {
                                                       HardwareStatus.RequireSupportedBoard();
                                                       return DriverSetup.Probe();
                                                   });
                if (state == DriverState.Available)
                    ShowState(await Task.Run(() => HardwareStatus.Read()));
                else
                    RenderDriverState(state);
            }
            catch (Exception e)
            {
                ShowReadFailure(e);
            }
        }

        void RenderReadFailure(Exception e)
        {
            renderMode = () => RenderReadFailure(e);
            mode.Text = T("未知（无法回读）", "Readback unavailable");
            mode.ForeColor = Color.DarkOrange;
            enableDriver.Visible = false;
            ecDetails.Width = 601;
            ecDetails.Text = T("模式回读失败", "Mode readback failed");
            detailsTip.SetToolTip(ecDetails, e.Message);
            Fail(e);
        }

        void ShowReadFailure(Exception e)
        {
            RenderReadFailure(e);
            if (e is NotSupportedException)
                return;
            try
            {
                DriverState state = DriverSetup.Probe();
                if (state != DriverState.Available)
                    RenderDriverState(state);
            }
            catch
            {
            }
        }

        async void InstallDriver()
        {
            if (!BeginHardwareOperation())
                return;
            installing = true;
            msg.ForeColor = SystemColors.ControlText;
            msg.Text = T(
                "正在安装签名读取驱动，仅需一次；不会自动重启。",
                "Installing the signed readback driver once. Windows will not restart automatically.");
            try
            {
                bool restart = await Task.Run(() => DriverSetup.Install());
                await RefreshMode();
                if (restart)
                    Warn(T(
                        "驱动已安装，需要重启 Windows；模式切换和键盘灯仍可用。",
                        "Driver installed. Restart Windows to enable readback; mode and lighting controls still work."));
                else if (enableDriver.Visible)
                    Warn(T("驱动尚未就绪，请稍后刷新状态。",
                           "Driver is not ready; refresh again shortly."));
                else
                    Ok(T("驱动已就绪；以后直接运行，无需再次安装。",
                         "Driver ready. Future launches need no installation."));
            }
            catch (Exception e)
            {
                ShowReadFailure(e);
                Fail(e);
            }
            finally
            {
                installing = false;
                EndHardwareOperation();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (installing && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Warn(T("正在准备读取组件，请等待安装结束。",
                       "Please wait for driver setup to finish."));
            }
            base.OnFormClosing(e);
        }

        async void Mode(byte value)
        {
            if (!BeginHardwareOperation())
                return;
            try
            {
                await Task.Run(() => HpBios.SetMode(value));
                try
                {
                    PerformanceState state =
                        await Task.Run(() => HardwareStatus.WaitForMode(value));
                    ShowState(state);
                    if (state.Mode == value)
                        Ok(T("BIOS 指令已接受，EC 回读确认：",
                             "BIOS request accepted; EC confirmed: ") +
                           ModeName(state.Id));
                    else
                        Warn(T("BIOS 指令已接受，但 EC 当前状态与请求不一致：",
                               "BIOS request accepted, but EC differs from requested mode: ") +
                             ModeName(state.Id));
                }
                catch (Exception e)
                {
                    ShowReadFailure(e);
                    Warn(T("BIOS 指令已接受；无法确认实际状态：",
                           "BIOS request accepted; actual state could not be confirmed: ") +
                         e.Message);
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
            finally
            {
                EndHardwareOperation();
            }
        }

        void PaintZone(int i)
        {
            keyboard.SetZone(i, zoneColors[i], zoneBrightness[i]);
            if (keyboard.SelectedZone == i)
                UpdateZoneEditor();
        }

        void UpdateZoneEditor()
        {
            int zone = keyboard.SelectedZone;
            updatingEditor = true;
            try
            {
                brightnessEditor.Value = zoneBrightness[zone];
            }
            finally
            {
                updatingEditor = false;
            }
            brightnessPercent.Text = zoneBrightness[zone] + "%";
            brightnessEditor.AccessibleName = Zones() [zone] + T("亮度", " brightness");
            colorEditor.Text = Zones() [zone] + T(" · 选择颜色", " · Color");
            colorEditor.BackColor = zoneColors[zone];
            colorEditor.ForeColor =
                zoneColors[zone].GetBrightness() < 0.45f ? Color.White : Color.Black;
        }

        void PickZone(int i)
        {
            using (var d = new ColorDialog { Color = zoneColors[i],
                                             FullOpen = true }) if (d.ShowDialog(this) ==
                                                                    DialogResult.OK)
            {
                zoneColors[i] = d.Color;
                PaintZone(i);
            }
        }

        async void ApplyZones()
        {
            if (!BeginHardwareOperation())
                return;
            try
            {
                Color[] colors = zoneColors.ToArray();
                int[] brightness = zoneBrightness.ToArray();
                await Task.Run(() => HpBios.SetZones(colors, brightness));
                try
                {
                    await RefreshKeyboard(false);
                    Ok(T("键盘灯已应用。", "Keyboard lighting applied."));
                }
                catch (Exception e)
                {
                    Warn(
                        T("键盘灯已应用，但回读失败：", "Lighting applied, but readback failed: ") +
                        e.Message);
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
            finally
            {
                EndHardwareOperation();
            }
        }

        void DrawPreset(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;
            var preset = (KeyboardPreset)presetList.Items[e.Index];
            e.DrawBackground();
            bool available = preset != biosPreset || biosColorsLoaded;
            var text = new Rectangle(e.Bounds.X + 5, e.Bounds.Y,
                                     e.Bounds.Width - (available ? 78 : 10), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, preset.Name, Font, text, e.ForeColor,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            int[] order = { 2, 3, 1, 0 };
            for (int i = 0; available && i < 4; i++)
            {
                int zone = order[i];
                Color c = preset.Colors[zone];
                double level = preset.Brightness[zone] / 100.0;
                using (var brush = new SolidBrush(Color.FromArgb(
                           (int)(c.R * level), (int)(c.G * level), (int)(c.B * level))))
                    e.Graphics.FillRectangle(brush, e.Bounds.Right - 70 + i * 16, e.Bounds.Y + 7,
                                             13, 13);
                e.Graphics.DrawRectangle(Pens.Gray, e.Bounds.Right - 70 + i * 16, e.Bounds.Y + 7,
                                         13, 13);
            }
            e.DrawFocusRectangle();
        }

        void ReloadPresetList(KeyboardPreset selected = null)
        {
            loadingPresets = true;
            try
            {
                presetList.Items.Clear();
                presetList.Items.Add(biosPreset);
                foreach (var preset in presets)
                    presetList.Items.Add(preset);
                presetList.SelectedItem = selected;
            }
            finally
            {
                loadingPresets = false;
            }
            savePreset.Enabled = deletePreset.Enabled =
                selected != null && presets.Contains(selected);
        }

        KeyboardPreset CapturePreset(string name)
        {
            return new KeyboardPreset { Name = name, Colors = zoneColors.ToArray(),
                                        Brightness = zoneBrightness.ToArray() };
        }

        void SavePreset()
        {
            var selected = presetList.SelectedItem as KeyboardPreset;
            if (selected == null || !presets.Contains(selected))
                return;
            string name = presetName.Text.Replace("|", " ").Trim();
            if (String.IsNullOrWhiteSpace(name))
            {
                Warn(T("请输入预设名称。", "Enter a preset name."));
                return;
            }
            if (presets.Any(p => p != selected &&
                                 String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                Warn(T("已有同名预设。", "A preset with this name already exists."));
                return;
            }
            try
            {
                var updated = CapturePreset(name);
                var next = new List<KeyboardPreset>(presets);
                next[next.IndexOf(selected)] = updated;
                PresetStore.Save(next);
                presets = next;
                ReloadPresetList(updated);
                Ok(T("预设已保存：", "Preset saved: ") + name);
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        void NewPreset()
        {
            string root = presetName.Text.Replace("|", " ").Trim();
            if (String.IsNullOrWhiteSpace(root) || root == "预设名称" || root == "Preset name")
                root = T("新预设", "New preset");
            string name = root;
            for (int suffix = 2;
                 presets.Any(p => String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                 suffix++)
                name = root + " " + suffix;
            try
            {
                var added = CapturePreset(name);
                var next = new List<KeyboardPreset>(presets);
                next.Add(added);
                PresetStore.Save(next);
                presets = next;
                ReloadPresetList(added);
                presetName.Text = name;
                presetName.Focus();
                presetName.SelectAll();
                Ok(T("新预设已保存，可修改名称。", "New preset saved. You can edit its name."));
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        void ShowPreset()
        {
            if (loadingPresets)
                return;
            var preset = presetList.SelectedItem as KeyboardPreset;
            savePreset.Enabled = deletePreset.Enabled = preset != null && presets.Contains(preset);
            if (preset == null)
                return;
            if (preset == biosPreset && !biosColorsLoaded)
            {
                Warn(T("请刷新状态以读取键盘颜色。", "Refresh to read keyboard colors."));
                return;
            }
            LoadPresetColors(preset);
            msg.ForeColor = SystemColors.ControlText;
            msg.Text = T("预设已载入。", "Preset loaded.");
        }

        void DeletePreset()
        {
            var selected = presetList.SelectedItem as KeyboardPreset;
            if (selected == null || !presets.Contains(selected))
                return;
            try
            {
                var next = new List<KeyboardPreset>(presets);
                next.Remove(selected);
                PresetStore.Save(next);
                presets = next;
                ReloadPresetList();
                presetName.Text = "";
                Ok(T("预设已删除。", "Preset deleted."));
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        void Ok(string s)
        {
            msg.ForeColor = Color.DarkGreen;
            msg.Text = s;
        }

        void Warn(string s)
        {
            msg.ForeColor = Color.DarkOrange;
            msg.Text = s;
        }

        void Fail(Exception e)
        {
            msg.ForeColor = Color.DarkRed;
            msg.Text = T("操作失败：", "Operation failed: ") + e.Message;
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--status")
            {
                try
                {
                    var state = HardwareStatus.Read();
                    File.WriteAllText(args[1],
                                      "mode=" + state.Id + Environment.NewLine + state.Raw +
                                          Environment.NewLine +
                                          "read_at_utc=" + state.ReadAtUtc.ToString("o"),
                                      Encoding.UTF8);
                    return state.Mode < 0 ? 2 : 0;
                }
                catch (Exception e)
                {
                    File.WriteAllText(args[1], "mode=unknown" + Environment.NewLine + e.ToString(),
                                      Encoding.UTF8);
                    return 1;
                }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new MainForm());
                return 0;
            }
            catch (Exception e)
            {
                MessageBox.Show(
                    "Unable to open portable data folder or initialize application. Extract to a writable folder.\n" +
                        e.Message,
                    "OMEN Lite Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
