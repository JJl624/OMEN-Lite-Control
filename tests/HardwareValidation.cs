using System;
using System.IO;
using System.Text;

namespace OmenModeSwitcher
{
    // Explicitly opt-in hardware test. Switches all modes, then restores the initial one.
    internal static class HardwareValidation
    {
        static int Main(string[] args)
        {
            if (args.Length != 1) return 2;
            string log = args[0];
            Action<string> record = s => File.AppendAllText(log, DateTime.UtcNow.ToString("o") + " " + s + Environment.NewLine, Encoding.UTF8);
            try
            {
                PerformanceState original = HardwareStatus.Read();
                record("Initial " + original.Id + " " + original.Raw);
                if (original.Mode < 0) throw new InvalidOperationException("Cannot safely restore an unknown initial mode.");
                try
                {
                    foreach (byte request in new byte[] { 1, 2, 0 })
                    {
                        HpBios.SetMode(request);
                        PerformanceState state = HardwareStatus.WaitForMode(request);
                        record("Requested " + request + "; read " + state.Id + " " + state.Raw);
                        if (state.Mode != request) throw new InvalidOperationException("Hardware mode mismatch.");
                    }
                }
                finally
                {
                    HpBios.SetMode((byte)original.Mode);
                    PerformanceState restored = HardwareStatus.WaitForMode((byte)original.Mode);
                    record("Restored " + restored.Id + " " + restored.Raw);
                    if (restored.Mode != original.Mode) throw new InvalidOperationException("Original mode restoration could not be verified.");
                }
                record("PASS");
                return 0;
            }
            catch (Exception ex) { record("FAIL " + ex); return 1; }
        }
    }
}
