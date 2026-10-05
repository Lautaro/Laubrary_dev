using System;
using System.Diagnostics;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Laubrary.Audio {
    /// <summary>The host owns the ticket, including after the realtime object has disposed its own buffers.</summary>
    public static class SapRenderTicket {
        public static NativeArray<long> Create(Allocator allocator) => new NativeArray<long>(3, allocator, NativeArrayOptions.ClearMemory);
        public static void Enter(NativeArray<long> ticket) { if (ticket.IsCreated) ticket[0] = ticket[0] + 1; }
        public static void Exit(NativeArray<long> ticket, int frames, bool finished) {
            if (!ticket.IsCreated) return;
            if (ticket.Length > 1) ticket[1] = ticket[1] + frames;
            if (ticket.Length > 2 && finished && ticket[2] == 0) ticket[2] = ticket[1] > 0 ? ticket[1] : 1;
            ticket[0] = ticket[0] + 1;
        }
        /// <summary>Main-thread observation; does not take NativeArray safety locks while the renderer owns it.</summary>
        public static unsafe long Read(NativeArray<long> ticket) => ticket.IsCreated ? Volatile.Read(ref ((long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(ticket))[0]) : 0L;
    }

    /// <summary>Host-driven stable-even observation. Movement or an odd ticket restarts the full settle window.</summary>
    public struct SapQuietWindow {
        public long lastSeen;
        public double quietSince;
        public bool started, observingQuiet;

        public bool Observe(long ticket, double now, double settleSeconds) {
            bool still = started && ticket == lastSeen && (ticket & 1L) == 0L;
            lastSeen = ticket; started = true;
            if ((ticket & 1L) != 0L || (observingQuiet && !still)) observingQuiet = false;
            if (!observingQuiet && (ticket & 1L) == 0L) { quietSince = now; observingQuiet = true; }
            return observingQuiet && now - quietSince >= settleSeconds;
        }
    }

    public static class SapLifetime {
        public static double DefaultSettleSeconds(int bufferFrames, int sampleRate) {
            double block = sampleRate > 0 && bufferFrames > 0 ? (double)bufferFrames / sampleRate : 0.021d;
            return Math.Max(0.005d, Math.Min(0.060d, block * 2.2d));
        }

        /// <summary>Main-thread only. Readers may use native tickets or a host facade. Timeout means retain memory.</summary>
        public static bool WaitUntilQuiet(int count, Func<int, long> readTicket, double settleSeconds, double timeoutSeconds) {
            if (count <= 0) return true;
            var windows = new SapQuietWindow[count];
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < count; i++) windows[i].Observe(readTicket(i), 0d, settleSeconds);
            while (true) {
                double now = clock.Elapsed.TotalSeconds;
                bool allQuiet = true;
                for (int i = 0; i < count; i++) if (!windows[i].Observe(readTicket(i), now, settleSeconds)) allQuiet = false;
                if (allQuiet) return true;
                if (now >= timeoutSeconds) return false;
                Thread.Sleep(1);
            }
        }
    }

    /// <summary>Host-driven quit gate. Begin after stop/silence; reject new renders while draining, keep frame updates alive, then quit.</summary>
    public struct SapQuitDrain {
        public bool draining, complete;
        public double startedAt;
        public int frames;
        public bool RefuseNewRendering => draining || complete;
        public void Begin(double now) { if (draining || complete) return; draining = true; startedAt = now; frames = 0; }
        public bool Tick(double now) {
            if (!draining || complete) return false;
            frames++;
            if (frames < 2 || now - startedAt < 0.3d) return false;
            complete = true;
            return true;
        }
    }
}
