using System;
using System.Diagnostics;
using System.Management;
using System.Threading;

namespace OmenLiteControl
{
    internal static class HardwareAccess
    {
        static readonly object Sync = new object();
        static readonly Stopwatch SinceCompletion = new Stopwatch();

        static readonly Stopwatch SinceWrite = new Stopwatch();
        static bool failed;

        internal static T Run<T>(Func<T> action, bool write = false)
        {
            lock (Sync)
            {
                long gap = failed ? 1000 : 50;
                long wait =
                    SinceCompletion.IsRunning ? gap - SinceCompletion.ElapsedMilliseconds : 0;
                if (write && SinceWrite.IsRunning)
                    wait = Math.Max(wait, 1000 - SinceWrite.ElapsedMilliseconds);
                if (wait > 0)
                    Thread.Sleep((int)wait);
                try
                {
                    var result = action();
                    failed = false;
                    return result;
                }
                catch
                {
                    failed = true;
                    throw;
                }
                finally
                {
                    if (write)
                        SinceWrite.Restart();
                    SinceCompletion.Restart();
                } // Errors also need recovery time.
            }
        }
    }

    internal static class HardwarePlatform
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
    }
}
