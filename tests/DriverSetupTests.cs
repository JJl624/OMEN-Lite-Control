using System;
using System.IO;
namespace OmenModeSwitcher
{
    static class DriverSetupTests
    {
        static void Check(bool ok, string text) { if (!ok) throw new Exception(text); }
        static void Main(string[] args)
        {
            Check(DriverSetup.Classify(true, 0x20200, 0, true) == DriverState.Available, "Reuse 2.2");
            Check(DriverSetup.Classify(true, 0x30000, 0, true) == DriverState.Available, "Never downgrade newer driver");
            Check(DriverSetup.Classify(true, 0x20100, 0, true) == DriverState.UpdateRequired, "Old version");
            Check(DriverSetup.Classify(false, 0, 2, false) == DriverState.Missing, "Missing driver");
            Check(DriverSetup.Classify(false, 0, 2, true) == DriverState.Unavailable, "Do not reinstall stopped/pending driver");
            Check(DriverSetup.Classify(false, 0, 5, false) == DriverState.Unavailable, "Access denied is not missing");
            using (var stream = File.OpenRead(args[0])) Check(DriverSetup.MatchesInstaller(stream), "Original installer");
            byte[] tampered = File.ReadAllBytes(args[0]); tampered[tampered.Length - 1] ^= 1;
            using (var stream = new MemoryStream(tampered)) Check(!DriverSetup.MatchesInstaller(stream), "Reject changed installer");
            Console.WriteLine("PASS: driver detection and installer integrity (no driver installation performed)");
        }
    }
}
