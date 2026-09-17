using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace OmenModeSwitcher
{
    // Share one process-wide lane for WMI commands and complete EC snapshots.
    // Throttle snapshots, never the individual port I/O of an EC handshake.
    internal static class HardwareAccess
    {
        static readonly object Sync = new object();
        static readonly Stopwatch SinceCompletion = new Stopwatch();

        internal static T Run<T>(Func<T> action)
        {
            lock (Sync)
            {
                while (SinceCompletion.IsRunning && SinceCompletion.ElapsedMilliseconds < 1000)
                    Thread.Sleep((int)Math.Max(1, 1000 - SinceCompletion.ElapsedMilliseconds));
                try
                {
                    return action();
                }
                finally
                {
                    SinceCompletion.Restart();
                } // Errors also need recovery time.
            }
        }
    }

    // Own transport implementation of the documented PawnIO device ABI.
    // The embedded, signed LpcACPIEC module permits only ports 62/66.
    internal interface IEcPorts : IDisposable
    {
        byte Read(ushort port);
        void Write(ushort port, byte value);
    }

    internal sealed class PawnEcPorts : IEcPorts
    {
        const uint LoadModule = (41394u << 16) | (0x821u << 2);
        const uint Execute = (41394u << 16) | (0x841u << 2);
        readonly SafeFileHandle handle;

        // Read-only device/version probe. Never installs, starts or upgrades a driver.
        internal static bool Probe(out uint version, out int error)
        {
            version = 0;
            error = 0;
            using (var device = CreateFile(@"\\.\GLOBALROOT\Device\PawnIO", 0xC0000000, 7,
                                           IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (device.IsInvalid)
                {
                    error = Marshal.GetLastWin32Error();
                    return false;
                }
                byte[] output = new byte[4];
                uint returned;
                if (!DeviceIoControl(device, (41394u << 16) | (0x861u << 2), new byte[0], 0, output,
                                     4, out returned, IntPtr.Zero))
                {
                    error = Marshal.GetLastWin32Error();
                    return false;
                }
                if (returned != 4)
                {
                    error = 13;
                    return false;
                }
                version = BitConverter.ToUInt32(output, 0);
                return true;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share,
                                                IntPtr security, uint creation, uint flags,
                                                IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return:MarshalAs(UnmanagedType.Bool)]
        static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input,
                                           uint inputSize, byte[] output, uint outputSize,
                                           out uint returned, IntPtr overlapped);

        internal PawnEcPorts()
        {
            handle = CreateFile(@"\\.\GLOBALROOT\Device\PawnIO", 0xC0000000, 7, IntPtr.Zero, 3, 0,
                                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(
                    error,
                    "EC: PawnIO unavailable. Install signed PawnIO and run as administrator.");
            }
            try
            {
                byte[] module;
                using (var resource =
                           typeof(PawnEcPorts)
                               .Assembly.GetManifestResourceStream("OmenLiteControl.LpcACPIEC.bin"))
                {
                    if (resource == null)
                        throw new InvalidDataException("Embedded EC module is missing.");
                    using (var data = new MemoryStream())
                    {
                        resource.CopyTo(data);
                        module = data.ToArray();
                    }
                }
                if (module.Length == 0 || module.Length > 65536)
                    throw new InvalidDataException("Invalid EC module size.");
                Call(LoadModule, module, 0);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        byte[] Call(uint code, byte[] input, int outputLength)
        {
            byte[] output = new byte[outputLength];
            uint returned;
            if (!DeviceIoControl(handle, code, input, (uint)input.Length, output,
                                 (uint)output.Length, out returned, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "EC driver request failed.");
            if (returned != outputLength)
                throw new InvalidDataException("EC driver returned an incomplete response.");
            return output;
        }

        byte[] Invoke(string function, params ulong[] values)
        {
            byte[] input = new byte[32 + values.Length * 8];
            Encoding.ASCII.GetBytes(function).CopyTo(input, 0);
            for (int i = 0; i < values.Length; i++)
                BitConverter.GetBytes(values[i]).CopyTo(input, 32 + i * 8);
            return Call(Execute, input, function == "ioctl_pio_read" ? 8 : 0);
        }

        public byte Read(ushort port)
        {
            ulong value = BitConverter.ToUInt64(Invoke("ioctl_pio_read", port), 0);
            if (value > 255)
                throw new InvalidDataException("Invalid EC port byte.");
            return (byte)value;
        }

        public void Write(ushort port, byte value)
        {
            Invoke("ioctl_pio_write", port, value);
        }

        public void Dispose()
        {
            handle.Dispose();
        }
    }

    // ACPI RD_EC transaction only. There is deliberately no EC-register write API.
    internal sealed class EcReader
    {
        readonly IEcPorts ports;

        internal EcReader(IEcPorts ports)
        {
            this.ports = ports;
        }

        void Wait(byte mask, byte expected)
        {
            var timer = Stopwatch.StartNew();
            byte last = 0;
            for (int attempt = 0; attempt < 4096 && timer.ElapsedMilliseconds < 50; attempt++)
            {
                last = ports.Read(0x66);
                if ((last & mask) == expected)
                    return;
                Thread.SpinWait(32);
            }
            throw new TimeoutException("EC handshake timed out (status=" + last.ToString("X2") +
                                       ", mask=" + mask + ", expected=" + expected + ").");
        }

        // Caller must hold Global\Access_EC for the complete transaction/snapshot.
        internal byte ReadByte(byte register)
        {
            // Do not consume another client's pending data to force an idle state.
            Wait(3, 0);
            ports.Write(0x66, 0x80);
            Wait(2, 0);
            ports.Write(0x62, register);
            Wait(2, 0);
            Wait(1, 1);
            return ports.Read(0x62);
        }

        internal PerformanceState ReadPerformance()
        {
            // 84DB/F.19 DSDT: GM1A writes NVPM=EC[F8].bit1, COLM=EC[EC].bit0.
            // EC[95] is MMST/DMST on this board, NOT OmenMon's newer HPCM layout.
            byte previousF8 = 0, previousEc = 0;
            int matches = 0;
            for (int attempt = 0; attempt < 7; attempt++)
            {
                byte f8 = ReadByte(0xF8), ec = ReadByte(0xEC);
                matches = attempt > 0 && f8 == previousF8 && ec == previousEc ? matches + 1 : 1;
                // Compare complete raw bytes, not just mode bits: unrelated bytes from
                // a competing firmware transaction must not look like a stable mode.
                if (matches == 3)
                    return new PerformanceState(f8, ec);
                previousF8 = f8;
                previousEc = ec;
                Thread.Sleep(5);
            }
            throw new InvalidDataException("EC mode changed while reading; refresh again.");
        }
    }

    internal sealed class PerformanceState
    {
        internal readonly byte F8, EC;
        internal readonly DateTime ReadAtUtc = DateTime.UtcNow;

        internal PerformanceState(byte f8, byte ec)
        {
            F8 = f8;
            EC = ec;
        }

        internal int Mode
        {
            get {
                bool performance = (F8 & 2) != 0, comfort = (EC & 1) != 0;
                return performance && comfort ? -1 : performance ? 1 : comfort ? 2 : 0;
            }
        }
        internal string Id
        {
            get {
                return Mode == 0   ? "balanced"
                       : Mode == 1 ? "performance"
                       : Mode == 2 ? "comfort"
                                   : "unknown";
            }
        }
        internal string Raw
        {
            get {
                return "EC[F8]=0x" + F8.ToString("X2") + ", EC[EC]=0x" + EC.ToString("X2");
            }
        }
    }

    internal static class HardwareStatus
    {
        static bool supported;

        internal static void RequireSupportedBoard()
        {
            if (supported)
                return;
            bool board = false, bios = false;
            using (var search = new ManagementObjectSearcher("SELECT Manufacturer,Product FROM Win32_BaseBoard")) using (
                var rows = search.Get()) foreach (ManagementObject row in rows) using (row) board |=
                String.Equals(Convert.ToString(row["Product"]).Trim(), "84DB",
                              StringComparison.OrdinalIgnoreCase) &&
                String.Equals(Convert.ToString(row["Manufacturer"]).Trim(), "HP",
                              StringComparison.OrdinalIgnoreCase);
            using (var search = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion FROM Win32_BIOS")) using (
                var rows = search.Get()) foreach (ManagementObject row in rows) using (row) bios |=
                String.Equals(Convert.ToString(row["SMBIOSBIOSVersion"]).Trim(), "F.19",
                              StringComparison.OrdinalIgnoreCase);
            if (!board || !bios)
                throw new NotSupportedException(
                    "Verified hardware mapping requires HP 84DB BIOS F.19.");
            supported = true;
        }

        internal static PerformanceState Read()
        {
            return HardwareAccess.Run(ReadWithRetries);
        }

        static PerformanceState ReadWithRetries()
        {
            for (int attempt = 0;; attempt++)
            {
                try
                {
                    return ReadOnce();
                }
                catch (TimeoutException)
                {
                    // ACPI/firmware can briefly own the EC, especially during a mode change.
                    // Release our mutex, then retry a whole snapshot, never a fabricated byte.
                    if (attempt == 2)
                        throw;
                    Thread.Sleep(50);
                }
            }
        }

        static PerformanceState ReadOnce()
        {
            RequireSupportedBoard();
            using (var ports = new PawnEcPorts()) using (var mutex =
                                                             new Mutex(false, @"Global\Access_EC"))
            {
                bool held = false;
                try
                {
                    try
                    {
                        held = mutex.WaitOne(300);
                    }
                    catch (AbandonedMutexException)
                    {
                        held = true;
                        throw new InvalidOperationException(
                            "Previous EC client exited during access; refresh again.");
                    }
                    if (!held)
                        throw new TimeoutException("EC is busy with another application.");
                    return new EcReader(ports).ReadPerformance();
                }
                finally
                {
                    if (held)
                        mutex.ReleaseMutex();
                }
            }
        }

        internal static PerformanceState WaitForMode(byte expected)
        {
            Thread.Sleep(250);
            var timer = Stopwatch.StartNew();
            PerformanceState state = null;
            Exception lastError = null;
            do
            {
                Thread.Sleep(80);
                try
                {
                    state = Read();
                    lastError = null;
                    if (state.Mode == expected)
                        return state;
                }
                catch (TimeoutException ex)
                {
                    lastError = ex;
                }
            } while (timer.ElapsedMilliseconds < 1500);
            if (lastError != null)
                throw lastError;
            return state;
        }
    }
}
