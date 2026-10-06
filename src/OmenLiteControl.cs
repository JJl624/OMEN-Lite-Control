using System;
using System.Diagnostics;
using System.Threading;
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
[assembly:AssemblyVersion("0.7.1.0")]
[assembly:AssemblyFileVersion("0.7.1.0")]
[assembly:AssemblyInformationalVersion("0.7.1")]
namespace OmenLiteControl
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
            return HardwareAccess.Run(() => CallCore(method, cmd, type, data),
                                      (cmd == 0x20008 && type == 0x1A) ||
                                          (cmd == 0x20009 && type == 0x03));
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
            HardwarePlatform.RequireSupportedBoard();
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
        public static bool LightingEnabled;
        public static string[] ModePresets = new string[3];

        static string Clean(string s)
        {
            return (s ?? "").Replace("|", " ").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        public static List<KeyboardPreset> Load(bool english = false)
        {
            var list = new List<KeyboardPreset>();
            string[] stored = ConfigStore.ReadPresets();
            LightingEnabled = stored.Contains("# lighting-enabled");
            ModePresets = new string[3];
            foreach (string setting in stored.Where(x => x.StartsWith("# mode|")))
            {
                string[] parts = setting.Split('|');
                int mode;
                if (parts.Length == 3 && Int32.TryParse(parts[1], out mode) && mode >= 0 &&
                    mode < 3)
                    ModePresets[mode] = parts[2];
            }
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

        public static void Save(List<KeyboardPreset> list, string[] bindings = null,
                                bool? enabled = null)
        {
            bindings = (bindings ?? ModePresets)
                           .Select(name => list.Any(p => p.Name == name) ? name : null)
                           .ToArray();
            bool active = enabled ?? LightingEnabled;
            var header = new List<string> { "# presets-v2" };
            if (active)
                header.Add("# lighting-enabled");
            for (int i = 0; i < 3; i++)
                if (!String.IsNullOrEmpty(bindings[i]))
                    header.Add("# mode|" + i + "|" + Clean(bindings[i]));
            var lines = list.Select(
                x => Clean(x.Name) + "|" +
                     String.Join("|", Enumerable.Range(0, 4).SelectMany(
                                          i => new[] { (x.Colors[i].ToArgb() & 0xFFFFFF).ToString(),
                                                       x.Brightness[i].ToString() })));
            ConfigStore.SetPresets(header.Concat(lines));
            ModePresets = bindings;
            LightingEnabled = active;
        }
    }

    sealed class MainForm : Form
    {
        CheckBox minimizeToTray;
        bool trayEnabled, updatingTray, restoringFromTray;
        NotifyIcon trayIcon;
        ContextMenu trayMenu;
        readonly MenuItem[] trayModes = new MenuItem[3];
        MenuItem trayExit;
        readonly ModeIcons modeIcons = new ModeIcons();
        bool english, biosColorsLoaded, biosReadFailed;
        volatile bool hardwareBusy;
        readonly Stopwatch operationTime = new Stopwatch();
        readonly KeyboardPreset biosPreset =
            new KeyboardPreset { Brightness = new[] { 100, 100, 100, 100 } };
        Label lastLabel, mode;
        CheckBox hotkeyEnabled;
        ComboBox hotkeyChoice;
        TextBox hotkeyEdit;
        Panel headerDivider;
        HotkeySettings hotkeySettings;
        ModeHotkey hotkey;
        bool updatingHotkey, editingHotkey;
        volatile int hotkeyGeneration;
        int hotkeyPending;
        ToolStripStatusLabel msg;
        ToolTip detailsTip = new ToolTip();
        GroupBox modeBox, kb;
        Button refresh, newPreset, apply, savePreset, deletePreset;
        RadioButton def, perf, cool;
        CheckBox lightingLink;
        readonly ComboBox[] modePresets = new ComboBox[3];
        bool loadingBindings;
        int currentMode = -1, lastLightingMode = -1;
        LanguageSwitch language;
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
            english = ConfigStore.Get("language", "zh") == "en";
            presets = PresetStore.Load(english);
            Font = new Font("Microsoft YaHei UI", 9.5F);
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer,
                     true);
            ClientSize = new Size(650, 680);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Icon = modeIcons.ForMode(-1);
            StartPosition = FormStartPosition.CenterScreen;
            language = new LanguageSwitch();
            language.SetBounds(510, 12, 114, 32);
            Controls.Add(language);
            language.SelectionChanged += (s, e) => SwitchLanguage();
            lastLabel = L("", 22, 15, 76, 28);
            mode = L("", 100, 15, 170, 28);
            mode.Font = new Font(Font, FontStyle.Bold);
            refresh = B("", 274, 12, 90, 32);
            refresh.Click += (s, e) => RefreshAll();
            headerDivider = new Panel { BackColor = SystemColors.ControlDark };
            headerDivider.SetBounds(373, 12, 1, 84);
            Controls.Add(headerDivider);
            var settings = new Panel();
            settings.SetBounds(394, 46, 230, 52);
            minimizeToTray = new CheckBox { AutoSize = false };
            minimizeToTray.SetBounds(0, 0, 230, 24);
            settings.Controls.Add(minimizeToTray);
            trayEnabled = ConfigStore.Get("minimizeToTray", "false") == "true";
            minimizeToTray.Checked = trayEnabled;
            minimizeToTray.CheckedChanged += (sender, args) => SaveTraySetting();
            Controls.Add(settings);
            hotkeyEnabled =
                new CheckBox { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
            hotkeyEnabled.SetBounds(0, 24, 100, 28);
            settings.Controls.Add(hotkeyEnabled);
            hotkeyChoice = new HotkeyComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            hotkeyChoice.SetBounds(116, 24, 114, 28);
            settings.Controls.Add(hotkeyChoice);
            hotkeyChoice.SelectedIndexChanged += (sender, args) =>
            {
                if (updatingHotkey)
                    return;
                editingHotkey = false;
                SaveHotkey(hotkeySettings.Enabled,
                           hotkeyChoice.SelectedIndex == 0
                               ? Keys.None
                               : (hotkeySettings.Key == Keys.None ? Keys.Control | Keys.Alt | Keys.Q
                                                                  : hotkeySettings.Key));
            };
            hotkeyEdit = new TextBox { ReadOnly = true, TextAlign = HorizontalAlignment.Center };
            hotkeyEdit.SetBounds(0, 56, 230, 26);
            settings.Controls.Add(hotkeyEdit);
            hotkeyEdit.Enter += (sender, args) => BeginHotkeyCapture();
            hotkeyEdit.Leave += (sender, args) => FinishHotkeyCapture(hotkeySettings.Key);
            hotkeyEdit.KeyDown += (sender, args) =>
            {
                args.SuppressKeyPress = true;
                if (args.KeyCode == Keys.Escape)
                    FinishHotkeyCapture(hotkeySettings.Key);
                else if (HotkeySettings.IsValid(args.KeyData))
                    FinishHotkeyCapture(args.KeyData);
            };
            hotkeyEnabled.CheckedChanged += (sender, args) =>
                SaveHotkey(hotkeyEnabled.Checked, hotkeySettings.Key);
            hotkeySettings = HotkeySettings.Load();
            modeBox = G("", 20, 108, 610, 122);
            def = ModeButton(18);
            perf = ModeButton(213);
            cool = ModeButton(408);
            def.Click += (s, e) => Mode(0);
            perf.Click += (s, e) => Mode(1);
            cool.Click += (s, e) => Mode(2);
            lightingLink = new CheckBox { AutoSize = true, BackColor = SystemColors.Control,
                                          TextAlign = ContentAlignment.MiddleLeft,
                                          CheckAlign = ContentAlignment.MiddleLeft };
            lightingLink.SetBounds(215, 0, 180, 24);
            lightingLink.SizeChanged += (sender, args) => lightingLink.Left =
                (modeBox.ClientSize.Width - lightingLink.Width) / 2;
            modeBox.Controls.Add(lightingLink);

            lightingLink.CheckedChanged += (sender, args) => SaveLightingSettings();
            for (int i = 0; i < 3; i++)
            {
                var choice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                choice.SetBounds(18 + i * 195, 79, 180, 28);
                modeBox.Controls.Add(choice);
                modePresets[i] = choice;
                choice.SelectedIndexChanged += (sender, args) => SaveLightingSettings();
            }
            kb = G("", 20, 240, 610, 407);

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
            trayMenu = new ContextMenu();
            for (int i = 0; i < 3; i++)
            {
                byte target = (byte)i;
                trayModes[i] =
                    new MenuItem("", (sender, args) => Mode(target)) { RadioCheck = true };
                trayMenu.MenuItems.Add(trayModes[i]);
            }
            trayExit = new MenuItem("", (sender, args) => Close());
            trayMenu.MenuItems.Add(trayExit);
            trayMenu.Popup += (sender, args) => RenderTray();
            trayIcon = new NotifyIcon { Icon = Icon, ContextMenu = trayMenu, Visible = false };
            trayIcon.MouseDoubleClick += (sender, args) =>
            {
                if (args.Button == MouseButtons.Left)
                    RestoreFromTray();
            };
            FormClosed += (sender, args) =>
            {
                if (hotkey != null)
                    hotkey.Dispose();
                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayMenu.Dispose();
                detailsTip.Dispose();
                Icon = null;
                modeIcons.Dispose();
            };
            Shown += (sender, args) => InitializeHotkey();
            Deactivate += (sender, args) => FinishHotkeyCapture(hotkeySettings.Key, false);
            ApplyLanguage();
            ReloadPresetList();
            RefreshAll();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool RedrawWindow(IntPtr window, IntPtr rect, IntPtr region, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        void SyncModeIcon()
        {
            if (modeIcons == null || IsDisposed || Disposing)
                return;
            Icon = modeIcons.ForMode(currentMode);
            if (!IsHandleCreated)
                return;
            // Reapply native icons even when Form.Icon still references the same cached icon.
            SendMessage(Handle, 0x0080, IntPtr.Zero, modeIcons.SmallForMode(currentMode).Handle);
            SendMessage(Handle, 0x0080, new IntPtr(1), Icon.Handle);
            if (Visible && WindowState != FormWindowState.Minimized)
                RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, 0x0401); // Frame, no erase.
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SyncModeIcon();
        }

        bool wasMinimized;

        void SaveTraySetting()
        {
            if (updatingTray)
                return;
            try
            {
                ConfigStore.Set("minimizeToTray", minimizeToTray.Checked ? "true" : "false");
                trayEnabled = minimizeToTray.Checked;
            }
            catch (Exception e)
            {
                updatingTray = true;
                minimizeToTray.Checked = trayEnabled;
                updatingTray = false;
                Fail(e);
            }
        }

        void RenderTray()
        {
            if (trayIcon == null || IsDisposed || Disposing)
                return;
            string[] names = { T("默认", "Balanced"), T("狂暴", "Performance"),
                               T("酷冷", "Comfort") };
            for (int i = 0; i < 3; i++)
            {
                trayModes[i].Text = names[i];
                trayModes[i].Checked = currentMode == i;
                trayModes[i].Enabled = !hardwareBusy;
            }
            trayExit.Text = T("退出", "Exit");
            trayExit.Enabled = !hardwareBusy;
            trayIcon.Icon = modeIcons.ForMode(currentMode);
            trayIcon.Text =
                "OMEN Lite Control · " + T("记录：", "Recorded: ") +
                (currentMode >= 0 && currentMode < 3 ? names[currentMode] : T("未知", "Unknown"));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            bool restoring = wasMinimized && WindowState != FormWindowState.Minimized;
            wasMinimized = WindowState == FormWindowState.Minimized;
            if (restoring)
                SyncModeIcon();
            if (restoring && IsHandleCreated && Visible)
                RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero,
                             0x0585); // Repaint frame + children now.
            if (trayIcon != null && trayEnabled && !restoringFromTray &&
                WindowState == FormWindowState.Minimized)
            {
                FinishHotkeyCapture(hotkeySettings.Key, false);
                RenderTray();
                trayIcon.Visible = true;
                Hide();
            }
        }

        void RestoreFromTray()
        {
            if (IsDisposed || Disposing)
                return;
            restoringFromTray = true;
            try
            {
                Show();
                WindowState = FormWindowState.Normal;
            }
            finally
            {
                restoringFromTray = false;
            }
            trayIcon.Visible = false;
            SyncModeIcon();
            Activate();
            RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, 0x0585);
        }

        void LayoutHeader()
        {
            if (modeBox == null || kb == null)
                return;
            int top = hotkeySettings.Key == Keys.None ? 108 : 140;
            SuspendLayout();
            try
            {
                hotkeyEdit.Parent.Height = top - 56;
                headerDivider.Height = top - 24;
                modeBox.Top = top;
                kb.Top = modeBox.Bottom + 10;
                if (ClientSize.Height != kb.Bottom + 33)
                    ClientSize = new Size(ClientSize.Width, kb.Bottom + 33);
            }
            finally
            {
                ResumeLayout(true);
            }
        }

        void RenderHotkey()
        {
            updatingHotkey = true;
            hotkeyEnabled.Checked = hotkeySettings.Enabled;
            hotkeyEnabled.Text = T("快捷键", "Hotkey");
            hotkeyChoice.Items.Clear();
            hotkeyChoice.Items.Add("OMEN");
            hotkeyChoice.Items.Add(T("自定义", "Custom"));
            hotkeyChoice.SelectedIndex = hotkeySettings.Key == Keys.None ? 0 : 1;
            hotkeyEdit.Visible = hotkeySettings.Key != Keys.None;
            LayoutHeader();
            hotkeyChoice.Enabled = hotkeyEdit.Enabled = hotkeySettings.Enabled;
            hotkeyEdit.Text = HotkeySettings.Display(hotkeySettings.Key);
            detailsTip.SetToolTip(hotkeyEdit,
                                  T("点击此处，直接按 Ctrl / Alt 组合键；Esc 取消。",
                                    "Click here and press a Ctrl / Alt combination; Esc cancels."));
            updatingHotkey = false;
        }

        void InitializeHotkey()
        {
            if (hotkey != null)
                return;
            try
            {
                hotkey = new ModeHotkey();
                hotkey.Blocked = hardwareBusy;
                hotkey.Pressed += (sender, args) =>
                {
                    int generation = hotkeyGeneration;
                    if (hardwareBusy || editingHotkey || !IsHandleCreated || IsDisposed ||
                        Interlocked.CompareExchange(ref hotkeyPending, 1, 0) != 0)
                        return;
                    try
                    {
                        BeginInvoke((Action)(() =>
                                             {
                                                 try
                                                 {
                                                     if (generation == hotkeyGeneration &&
                                                         !hardwareBusy && hotkeySettings.Enabled &&
                                                         !editingHotkey && !IsDisposed)
                                                         CycleMode();
                                                 }
                                                 finally
                                                 {
                                                     Interlocked.Exchange(ref hotkeyPending, 0);
                                                 }
                                             }));
                    }
                    catch (InvalidOperationException)
                    {
                        Interlocked.Exchange(ref hotkeyPending, 0);
                    }
                };
                hotkey.Configure(hotkeySettings.Enabled, hotkeySettings.Key);
            }
            catch (Exception e)
            {
                hotkeySettings.Enabled = false;
                Warn(T("快捷键未启用：", "Hotkey unavailable: ") + e.Message);
            }
            RenderHotkey();
        }

        void SaveHotkey(bool enabled, Keys key)
        {
            if (updatingHotkey)
                return;
            var previous = hotkeySettings;
            try
            {
                if (hotkey == null)
                    throw new InvalidOperationException(
                        T("快捷键尚未就绪。", "Hotkey is not ready."));
                hotkeyGeneration++;
                hotkey.Configure(enabled, key);
                var next = new HotkeySettings { Enabled = enabled, Key = key };
                next.Save();
                hotkeySettings = next;
                Ok(enabled ? T("模式切换快捷键已启用。", "Mode hotkey enabled.")
                           : T("快捷键已关闭。", "Hotkey disabled."));
            }
            catch (Exception e)
            {
                try
                {
                    if (hotkey != null)
                        hotkey.Configure(previous.Enabled, previous.Key);
                }
                catch
                {
                    previous.Enabled = false;
                }
                Warn(T("快捷键设置失败：", "Cannot set hotkey: ") + e.Message);
            }
            RenderHotkey();
        }

        void BeginHotkeyCapture()
        {
            if (updatingHotkey || !hotkeySettings.Enabled || hotkeySettings.Key == Keys.None ||
                editingHotkey)
                return;
            try
            {
                hotkey.Configure(false, hotkeySettings.Key);
                editingHotkey = true;
                hotkeyGeneration++;
                hotkeyEdit.Text = T("请按组合键…", "Press shortcut…");
            }
            catch (Exception e)
            {
                Warn(e.Message);
            }
        }

        void FinishHotkeyCapture(Keys key, bool moveFocus = true)
        {
            if (!editingHotkey)
                return;
            editingHotkey = false;
            hotkeyGeneration++;
            SaveHotkey(hotkeySettings.Enabled, key);
            if (moveFocus)
                hotkeyChoice.Focus();
        }

        async void CycleMode()
        {
            if (!BeginHardwareOperation())
                return;
            try
            {
                await SwitchModeCore((byte)((currentMode + 1) % 3));
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

        RadioButton ModeButton(int x)
        {
            var button = new RadioButton { Appearance = Appearance.Button, AutoCheck = false,
                                           TextAlign = ContentAlignment.MiddleCenter };
            button.SetBounds(x, 27, 180, 46);
            modeBox.Controls.Add(button);
            return button;
        }

        void SelectMode(int value)
        {
            currentMode = value;
            SyncModeIcon();
            def.Checked = value == 0;
            perf.Checked = value == 1;
            cool.Checked = value == 2;
            RenderTray();
        }

        void ReloadLightingSettings()
        {
            loadingBindings = true;
            try
            {
                lightingLink.Checked = PresetStore.LightingEnabled;
                for (int i = 0; i < 3; i++)
                {
                    var choice = modePresets[i];
                    choice.Items.Clear();
                    choice.Items.Add(T("不改变灯光", "Keep lighting"));
                    foreach (var preset in presets)
                        choice.Items.Add(preset);
                    choice.SelectedItem =
                        presets.FirstOrDefault(p => p.Name == PresetStore.ModePresets[i]);
                    if (choice.SelectedIndex < 0)
                        choice.SelectedIndex = 0;
                    choice.Enabled = !hardwareBusy && PresetStore.LightingEnabled;
                }
            }
            finally
            {
                loadingBindings = false;
            }
        }

        void SaveLightingSettings()
        {
            if (loadingBindings)
                return;
            try
            {
                var names = modePresets.Select(c => c.SelectedItem as KeyboardPreset)
                                .Select(p => p == null ? null : p.Name)
                                .ToArray();
                PresetStore.Save(presets, names, lightingLink.Checked);
                lastLightingMode = -1;
                ReloadLightingSettings();
                Ok(T("灯光联动设置已保存，下次切换模式时生效。",
                     "Lighting link saved; takes effect on the next mode change."));
            }
            catch (Exception e)
            {
                ReloadLightingSettings();
                Fail(e);
            }
        }

        async Task<bool> ApplyModeLighting(bool updateEditor = false)
        {
            if (!PresetStore.LightingEnabled || currentMode < 0 || currentMode == lastLightingMode)
                return false;
            int observed = currentMode;
            var preset = presets.FirstOrDefault(p => p.Name == PresetStore.ModePresets[observed]);
            if (preset == null)
            {
                lastLightingMode = observed;
                return false;
            }
            var colors = preset.Colors.ToArray();
            var levels = preset.Brightness.ToArray();
            string name = preset.Name;
            try
            {
                msg.ForeColor = SystemColors.ControlText;
                msg.Text = T("正在应用模式灯光…", "Applying mode lighting…");
                await Task.Run(() => HpBios.SetZones(colors, levels));
                lastLightingMode = observed;
                msg.Text = T("灯光已写入，正在回读确认…", "Lighting written; checking readback…");
                await RefreshKeyboard(updateEditor);
                Ok(T("模式灯光已应用：", "Mode lighting applied: ") + name);
                return true;
            }
            catch (Exception e)
            {
                Warn(T("模式请求已接受，灯光联动失败：",
                       "Mode request accepted; lighting link failed: ") +
                     e.Message);
                return false;
            }
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
            language.English = english;
            minimizeToTray.Text = T("最小化到托盘", "Minimize to tray");
            RenderTray();
            RenderHotkey();
            lastLabel.Text = T("记录模式", "Recorded");
            detailsTip.SetToolTip(
                lastLabel,
                T("最后一次被 BIOS 接受的模式请求，不是硬件回读。重启或其他软件可能改变实际模式。",
                  "Last mode request accepted by BIOS, not hardware readback. Restarting or other software may change the actual mode."));
            detailsTip.SetToolTip(mode, detailsTip.GetToolTip(lastLabel));
            refresh.Text = T("刷新状态", "Refresh");
            modeBox.Text = T("BIOS 性能策略", "BIOS Performance Policy");
            def.Text = T("默认", "Balanced");
            perf.Text = T("狂暴", "Performance");
            cool.Text = T("酷冷", "Comfort");
            lightingLink.Text = T("灯光联动", "Link lighting to mode");
            ReloadLightingSettings();
            kb.Text = T("四分区键盘灯", "4-Zone Keyboard Lighting");
            detailsTip.SetToolTip(
                keyboard,
                T("点击选区，双击选色", "Click to select a zone; double-click to choose a color."));
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
            ShowRecordedMode();
        }

        void SwitchLanguage()
        {
            try
            {
                ConfigStore.Set("language", english ? "zh" : "en");
                english = !english;
                ApplyLanguage();
            }
            catch (Exception e)
            {
                language.English = english;
                RenderHotkey();
                Fail(e);
            }
        }

        string ModeName(string id)
        {
            if (id == "balanced" || id == "默认模式")
                return T("默认模式", "Balanced");
            if (id == "performance" || id == "狂暴模式")
                return T("狂暴模式", "Performance");
            if (id == "comfort" || id == "酷冷模式")
                return T("酷冷模式", "Comfort");
            return T("无记录", "No record");
        }

        bool BeginHardwareOperation()
        {
            if (hardwareBusy)
                return false;
            hardwareBusy = true;
            hotkeyGeneration++;
            if (hotkey != null)
                hotkey.Blocked = true;
            operationTime.Restart();
            SetHardwareControls(false);
            return true;
        }

        void SetHardwareControls(bool enabled)
        {
            def.Enabled = perf.Enabled = cool.Enabled = apply.Enabled = refresh.Enabled = enabled;
            lightingLink.Enabled = enabled;
            RenderTray();
            foreach (var choice in modePresets)
                choice.Enabled = enabled && PresetStore.LightingEnabled;
        }

        async void EndHardwareOperation()
        {
            // One-second minimum between user operations, not after every internal read.
            int remaining = (int)Math.Max(0, 1000 - operationTime.ElapsedMilliseconds);
            if (remaining > 0)
                await Task.Delay(remaining);
            hotkeyGeneration++;
            if (hotkey != null)
                hotkey.Blocked = false;
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
            ShowRecordedMode();
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
            if (presetList.Items.Count > 0)
                presetList.Invalidate(presetList.GetItemRectangle(0));
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
                loadingPresets = true;
                try
                {
                    presetList.SelectedItem = biosPreset;
                }
                finally
                {
                    loadingPresets = false;
                }
                savePreset.Enabled = deletePreset.Enabled = false;
            }
            if (updateEditor || presetList.SelectedItem == biosPreset)
                LoadPresetColors(biosPreset);
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

        void ShowRecordedMode()
        {
            int saved;
            if (!Int32.TryParse(ConfigStore.Get("lastRequestedMode", "-1"), out saved) ||
                saved < 0 || saved > 2)
                saved = -1;
            SelectMode(saved);
            mode.Text = ModeName(saved == 0   ? "balanced"
                                 : saved == 1 ? "performance"
                                 : saved == 2 ? "comfort"
                                              : "unknown");
            mode.ForeColor = saved < 0 ? Color.DarkOrange : SystemColors.ControlText;
        }

        async void Mode(byte value)
        {
            if (!BeginHardwareOperation())
                return;
            try
            {
                await SwitchModeCore(value);
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

        async Task SwitchModeCore(byte value)
        {
            msg.ForeColor = SystemColors.ControlText;
            msg.Text = T("正在切换模式…", "Switching mode…");
            await Task.Run(() => HpBios.SetMode(value));
            // Persist only after the WMI request returned success. Invalidate stale state on a save
            // failure.
            try
            {
                ConfigStore.Set("lastRequestedMode", value.ToString());
            }
            catch (Exception e)
            {
                SelectMode(-1);
                mode.Text = T("记录未保存", "Record not saved");
                Warn(T("BIOS 已接受请求，但记录保存失败：",
                       "BIOS accepted the request, but saving failed: ") +
                     e.Message);
                return;
            }
            ShowRecordedMode();
            Ok(T("BIOS 已接受请求，模式记录已保存。",
                 "BIOS accepted the request; mode record saved."));
            lastLightingMode = -1;
            try
            {
                await ApplyModeLighting();
            }
            finally
            {
                SyncModeIcon();
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
            ReloadLightingSettings();
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
                var bindings =
                    PresetStore.ModePresets.Select(n => n == selected.Name ? name : n).ToArray();
                PresetStore.Save(next, bindings);
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
                string output;
                try
                {
                    string root = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
                    output = Path.GetFullPath(Path.Combine(root, args[1]));
                    if (!output.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        return 1;
                }
                catch
                {
                    return 1;
                }
                try
                {
                    int value;
                    if (!Int32.TryParse(ConfigStore.Get("lastRequestedMode", "-1"), out value) ||
                        value < 0 || value > 2)
                        value = -1;
                    string name = value == 0   ? "balanced"
                                  : value == 1 ? "performance"
                                  : value == 2 ? "comfort"
                                               : "unknown";
                    File.WriteAllText(
                        output, "source=last-request-record" + Environment.NewLine + "mode=" + name,
                        Encoding.UTF8);
                    return value < 0 ? 2 : 0;
                }
                catch (Exception e)
                {
                    File.WriteAllText(output, "mode=unknown" + Environment.NewLine + e.ToString(),
                                      Encoding.UTF8);
                    return 1;
                }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                using (var instance = new Mutex(false, @"Local\OMENLiteControl.UI"))
                {
                    bool owns;
                    try
                    {
                        owns = instance.WaitOne(0);
                    }
                    catch (AbandonedMutexException)
                    {
                        owns = true;
                    }
                    if (!owns)
                    {
                        MessageBox.Show("程序已在运行。 / The app is already running.",
                                        "OMEN Lite Control");
                        return 0;
                    }
                    try
                    {
                        Application.Run(new MainForm());
                    }
                    finally
                    {
                        instance.ReleaseMutex();
                    }
                }
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
