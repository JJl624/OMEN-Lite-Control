using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace OmenModeSwitcher
{
    static class HardwareAccessTests
    {
        static int Main()
        {
            var clock = Stopwatch.StartNew();
            var starts = new List<long>();
            var ends = new List<long>();
            int active = 0, failures = 0;
            Action<bool> request = fail => {
                try {
                    HardwareAccess.Run(() => {
                        if (Interlocked.Increment(ref active) != 1) throw new Exception("Overlapping hardware requests");
                        starts.Add(clock.ElapsedMilliseconds);
                        try {
                            Thread.Sleep(80);
                            if (fail) throw new InvalidOperationException("Simulated I/O failure");
                            return 42;
                        } finally {
                            ends.Add(clock.ElapsedMilliseconds);
                            Interlocked.Decrement(ref active);
                        }
                    });
                } catch (InvalidOperationException) { Interlocked.Increment(ref failures); }
            };
            try {
                request(true);
                Task.WaitAll(Task.Run(() => request(false)), Task.Run(() => request(false)), Task.Run(() => request(false)));
                if (failures != 1 || starts.Count != 4 || ends.Count != 4) throw new Exception("Lost request or error");
                for (int i = 1; i < starts.Count; i++) {
                    long gap = starts[i] - ends[i - 1];
                    if (gap < 1000) throw new Exception("Recovery gap too short: " + gap);
                    Console.WriteLine("Completion-to-start gap: " + gap + " ms");
                }
                Console.WriteLine("PASS: concurrent calls serialized; success and failure cooldown enforced.");
                return 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
