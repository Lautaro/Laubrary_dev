using System;
using System.Diagnostics;
using UnityEditor;

namespace Laubrary.Shaper.Editor
{
    /// <summary>
    /// T-0115 -- design B9 requirement 2's non-blocking background pre-bake, driven by
    /// <c>EditorApplication.update</c>: bakes at most one frame per tick (or stops early inside a tick if a
    /// caller-set time budget is exceeded), so the editor's own UI thread is never blocked waiting for a whole
    /// animation to finish -- Unity keeps pumping input/repaint/other updates between ticks either way. This is
    /// the ONE place in the whole cache architecture that touches <c>UnityEditor</c>: <see cref="ShaperFrameCache"/>
    /// itself (Runtime/Shaper) has zero editor dependency, so a future runtime consumer (a coroutine-driven
    /// pre-bake at play time) can drive the exact same <see cref="ShaperFrameCache.ComputeFrame"/> without this
    /// class at all.
    ///
    /// <b>What "non-blocking" is, and is not, verified to mean here (SPEC.md Part 5, honest about the limit).</b>
    /// Measured: each <c>Tick()</c> call itself returns quickly (bounded by <see cref="MaxMillisecondsPerTick"/>)
    /// and the whole bake completes over many ticks rather than one. NOT measured, because it needs a live,
    /// interactive editor session this task's own Unity-CLI-driven audit cannot drive: whether the Unity main
    /// thread's frame pacing / input responsiveness is subjectively smooth WHILE this runs across real wall-clock
    /// time in an open editor window. The architecture is built for it (bounded per-tick work, resumable next
    /// tick); a human walking a real animated document while this bakes in the background is the verification
    /// this task could not perform itself.
    /// </summary>
    public sealed class ShaperFramePrebaker
    {
        readonly ShaperFrameCache frameCache;
        int cursor;
        bool running;

        /// <summary>Wall-clock budget per <c>EditorApplication.update</c> tick. Frames vary in cost with grid
        /// size and tree depth, so this is a TIME budget, not a frame-count budget -- a cheap document bakes
        /// many frames per tick, an expensive one backs off to one or even stops mid-frame-boundary (a whole
        /// frame is always finished once started; this only gates whether ANOTHER frame starts this tick).</summary>
        public float MaxMillisecondsPerTick = 8f;

        public bool IsRunning => running;
        public int FramesCachedSoFar => frameCache.CountCachedFrames();
        public int TotalFrames => frameCache.frameCount;

        /// <summary>Fires once after every tick that made progress (bakes>=0 frames), so a caller (a future
        /// window) can repaint a per-frame indicator strip without polling every frame.</summary>
        public event Action<ShaperFramePrebaker> Progressed;
        /// <summary>Fires once when every frame is resident.</summary>
        public event Action<ShaperFramePrebaker> Completed;

        public ShaperFramePrebaker(ShaperFrameCache frameCache)
        {
            this.frameCache = frameCache ?? throw new ArgumentNullException(nameof(frameCache));
        }

        public void Start()
        {
            if (running) return;
            running = true;
            cursor = 0;
            EditorApplication.update += Tick;
        }

        public void Stop()
        {
            if (!running) return;
            running = false;
            EditorApplication.update -= Tick;
        }

        void Tick()
        {
            var sw = Stopwatch.StartNew();
            bool progressed = false;
            while (sw.Elapsed.TotalMilliseconds < MaxMillisecondsPerTick)
            {
                int next = frameCache.NextUncachedFrame(cursor);
                if (next < 0)
                {
                    Stop();
                    Completed?.Invoke(this);
                    if (progressed) Progressed?.Invoke(this);
                    return;
                }
                frameCache.ComputeFrame(next);
                cursor = (next + 1) % Math.Max(1, frameCache.frameCount);
                progressed = true;
            }
            if (progressed) Progressed?.Invoke(this);
        }
    }
}
