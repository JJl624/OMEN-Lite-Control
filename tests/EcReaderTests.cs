using System;
using System.Collections.Generic;
using System.IO;

namespace OmenModeSwitcher
{
    internal static class EcReaderTests
    {
        sealed class Ports : IEcPorts
        {
            internal readonly Queue<string> Expected = new Queue<string>();
            internal readonly Queue<byte> Values = new Queue<byte>();
            internal void Byte(byte register, byte value)
            {
                foreach (string step in new[] { "R66", "W66:80", "R66", "W62:" + register.ToString("X2"), "R66", "R66", "R62" })
                    Expected.Enqueue(step);
                foreach (byte b in new byte[] { 0, 0, 0, 1, value }) Values.Enqueue(b);
            }
            void Check(string operation)
            {
                Assert(Expected.Count > 0 && Expected.Dequeue() == operation, "Unexpected I/O: " + operation);
            }
            public byte Read(ushort port) { Check("R" + port.ToString("X2")); return Values.Dequeue(); }
            public void Write(ushort port, byte value) { Check("W" + port.ToString("X2") + ":" + value.ToString("X2")); }
            public void Dispose() { }
        }
        sealed class Busy : IEcPorts
        {
            public byte Read(ushort port) { return 2; }
            public void Write(ushort port, byte value) { throw new Exception("Wrote to busy controller"); }
            public void Dispose() { }
        }
        sealed class Failed : IEcPorts
        {
            public byte Read(ushort port) { throw new IOException("Driver error"); }
            public void Write(ushort port, byte value) { throw new Exception("Unexpected write"); }
            public void Dispose() { }
        }
        static int checks;
        static void Assert(bool condition, string name) { checks++; if (!condition) throw new Exception(name); }
        static void Throws<T>(Action action) where T : Exception
        {
            try { action(); } catch (T) { checks++; return; }
            throw new Exception("Expected " + typeof(T).Name);
        }
        static void Pair(Ports p, byte f8, byte ec) { p.Byte(0xF8, f8); p.Byte(0xEC, ec); }
        static int Main()
        {
            // All unrelated bits may vary without changing these two mode flags.
            for (int f8 = 0; f8 < 256; f8++)
                for (int ec = 0; ec < 256; ec++)
                {
                    int expected = (f8 & 2) == 0 ? ((ec & 1) == 0 ? 0 : 2) : ((ec & 1) == 0 ? 1 : -1);
                    Assert(new PerformanceState((byte)f8, (byte)ec).Mode == expected, "Flag decode");
                }
            var ports = new Ports();
            Pair(ports, 8, 0); Pair(ports, 10, 0); // In-flight transition: must retry.
            Pair(ports, 10, 0); Pair(ports, 10, 0);
            Assert(new EcReader(ports).ReadPerformance().Mode == 1, "Stable resample");
            Assert(ports.Expected.Count == 0 && ports.Values.Count == 0, "Full transaction consumed");
            ports = new Ports();
            for (int i = 0; i < 7; i++) Pair(ports, (byte)(i % 2 == 0 ? 8 : 10), 0);
            Throws<InvalidDataException>(() => new EcReader(ports).ReadPerformance());
            ports = new Ports();
            Pair(ports, 0x90, 0x90); Pair(ports, 8, 0); Pair(ports, 8, 0); Pair(ports, 8, 0);
            var stable = new EcReader(ports).ReadPerformance();
            Assert(stable.F8 == 8 && stable.EC == 0, "Reject transient bytes even with matching mode bits");
            Throws<TimeoutException>(() => new EcReader(new Busy()).ReadByte(0xF8));
            Throws<IOException>(() => new EcReader(new Failed()).ReadPerformance());
            Console.WriteLine("PASS: " + checks + " checks (decoding, protocol, transitions, timeout, driver failure)");
            return 0;
        }
    }
}
