using System;
using System.ComponentModel;
using System.IO;
using System.Management;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OmenLiteControl
{
    sealed class HotkeySettings
    {
        public bool Enabled;
        public Keys Key = Keys.None; // HP OMEN WMI button event.

        public static bool IsValid(Keys key)
        {
            if (key == Keys.None)
                return true;
            Keys code = key & Keys.KeyCode;
            Keys modifiers = key & Keys.Modifiers;
            return (modifiers & (Keys.Control | Keys.Alt)) != 0 &&
                   (modifiers & ~(Keys.Control | Keys.Alt | Keys.Shift)) == 0 &&
                   ((code >= Keys.A && code <= Keys.Z) || (code >= Keys.D0 && code <= Keys.D9) ||
                    (code >= Keys.F1 && code <= Keys.F11));
        }

        public static string Display(Keys key)
        {
            if (key == Keys.None)
                return "OMEN";
            Keys code = key & Keys.KeyCode;
            return ((key & Keys.Control) != 0 ? "Ctrl+" : "") +
                   ((key & Keys.Alt) != 0 ? "Alt+" : "") +
                   ((key & Keys.Shift) != 0 ? "Shift+" : "") +
                   (code >= Keys.D0 && code <= Keys.D9 ? ((int)code - (int)Keys.D0).ToString()
                                                       : code.ToString());
        }

        public static HotkeySettings Load()
        {
            return ConfigStore.GetHotkey();
        }

        public void Save()
        {
            ConfigStore.SetHotkey(this);
        }
    }

    // Windows delivers only the registered combination; no keyboard hook or keystroke log.
    sealed class ModeHotkey : NativeWindow, IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnregisterHotKey(IntPtr window, int id);
        public event EventHandler Pressed;
        int registeredId;
        Keys registeredKey;
        ManagementEventWatcher omenWatcher;
        volatile bool blocked;
        long acceptAfterUtc;
        int acceptAfterTick;
        [DllImport("user32.dll")]
        static extern int GetMessageTime();
        public bool Blocked
        {
            set {
                Interlocked.Exchange(ref acceptAfterUtc, DateTime.UtcNow.ToFileTimeUtc());
                acceptAfterTick = Environment.TickCount;
                blocked = value;
            }
        }
        readonly Stopwatch sinceOmen = new Stopwatch();

        void StopOmen()
        {
            var watcher = omenWatcher;
            omenWatcher = null;
            if (watcher == null)
                return;
            try
            {
                watcher.Stop();
            }
            finally
            {
                watcher.Dispose();
            }
        }

        public ModeHotkey()
        {
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) });
        }

        public void Configure(bool enabled, Keys key)
        {
            if (!enabled)
            {
                StopOmen();
                if (registeredId != 0 && !UnregisterHotKey(Handle, registeredId))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                registeredId = 0;
                return;
            }
            if (!HotkeySettings.IsValid(key))
                throw new ArgumentException("Invalid hotkey.");
            if (key == Keys.None)
            {
                if (omenWatcher != null)
                    return;
                var watcher = new ManagementEventWatcher(
                    @"root\wmi", "SELECT * FROM hpqBEvnt WHERE EventID = 4 AND EventData = 0");
                watcher.EventArrived += (sender, args) =>
                {
                    using (args.NewEvent)
                    {
                        lock (sinceOmen)
                        {
                            object created = args.NewEvent["TIME_CREATED"];
                            if (blocked ||
                                (created != null &&
                                 Convert.ToInt64(created) < Interlocked.Read(ref acceptAfterUtc)) ||
                                omenWatcher != watcher ||
                                (sinceOmen.IsRunning && sinceOmen.ElapsedMilliseconds < 250))
                                return;
                            sinceOmen.Restart();
                        }
                        var handler = Pressed;
                        if (handler != null)
                            handler(this, EventArgs.Empty);
                    }
                };
                try
                {
                    watcher.Start();
                }
                catch
                {
                    watcher.Dispose();
                    throw;
                }
                if (registeredId != 0 && !UnregisterHotKey(Handle, registeredId))
                {
                    int error = Marshal.GetLastWin32Error();
                    watcher.Stop();
                    watcher.Dispose();
                    throw new Win32Exception(error);
                }
                registeredId = 0;
                sinceOmen.Reset();
                omenWatcher = watcher;
                return;
            }
            if (registeredId != 0 && registeredKey == key)
                return;
            uint modifiers = 0x4000; // MOD_NOREPEAT: holding a key must not cycle repeatedly.
            if ((key & Keys.Alt) != 0)
                modifiers |= 1;
            if ((key & Keys.Control) != 0)
                modifiers |= 2;
            if ((key & Keys.Shift) != 0)
                modifiers |= 4;
            int nextId = registeredId == 1 ? 2 : 1;
            if (!RegisterHotKey(Handle, nextId, modifiers, (uint)(key & Keys.KeyCode)))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (registeredId != 0 && !UnregisterHotKey(Handle, registeredId))
            {
                int error = Marshal.GetLastWin32Error();
                UnregisterHotKey(Handle, nextId);
                throw new Win32Exception(error);
            }
            registeredId = nextId;
            registeredKey = key;
            StopOmen();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0312 && !blocked &&
                unchecked(GetMessageTime() - acceptAfterTick) >= 0 && registeredId != 0 &&
                message.WParam.ToInt32() == registeredId &&
                ((message.LParam.ToInt64() >> 16) & 0xFFFF) == (int)(registeredKey & Keys.KeyCode))
            {
                var handler = Pressed;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }
            base.WndProc(ref message);
        }

        public void Dispose()
        {
            try
            {
                StopOmen();
            }
            catch
            {
            }
            if (registeredId != 0)
                UnregisterHotKey(Handle, registeredId);
            registeredId = 0;
            if (Handle != IntPtr.Zero)
                DestroyHandle();
        }
    }

}
