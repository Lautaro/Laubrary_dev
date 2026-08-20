// PlusFrameFill — renders the frames of one spec on every core, byte-identical to rendering them one by one.
//
// The renderer's contract makes frames independent (PYREPLUS_DESIGN.md "Determinism contract"): RenderFrame is a
// pure function of (spec, frameIndex), so N frames can run on N threads and the bytes cannot differ — the same
// managed code runs in each thread. What a worker must NOT share with another is scratch: the forms keep planes and
// Prepare'd dials on their instances, modifiers keep Prepare'd params on theirs, so every worker gets its OWN deep
// clone of the whole spec (Object.Instantiate on the main thread — a serialization round trip, i.e. exactly the
// state a cold load produces, which the cold-vs-warm checks already prove renders identically). The renderer's own
// per-thread state is [ThreadStatic]; the shared pre-pass caches are thread-safe and linked clone → origin so a
// Plasma fit solved once is reused by every worker (PlusPrepassCache / PlusForm.SharePrepassWith).
//
// Shape: K dedicated background threads (not the ThreadPool — a heavy frame is hundreds of ms, and the pool would
// queue behind Unity's own work), each pulling the next frame index from a shared counter in the requested order,
// pushing a Color32[] result onto a queue the MAIN thread drains with TryTake and uploads to textures (Texture2D is
// main-thread-only, so no worker ever touches one). Cancel flips a flag checked between frames: an in-flight frame
// finishes and is simply never taken. Dispose waits for the workers and destroys the clones — main thread only.
//
// Specs that are not parallel-safe (PyrePlusRenderer.IsParallelSafe: stateful sims, Text, Sprite, Playback3D) are
// the caller's problem: they keep the serial fill. This class does not check — it is the caller's choice.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace Laubrary.PyrePlus
{
    public sealed class PlusFrameFill : IDisposable
    {
        public readonly struct Result
        {
            public readonly int frameIndex;
            public readonly Color32[] pixels;     // null when that frame's render threw — see FirstError
            public readonly double renderMs;      // that frame's own render time on its worker
            public Result(int f, Color32[] px, double ms) { frameIndex = f; pixels = px; renderMs = ms; }
        }

        /// Every core but one, so the editor's main thread keeps breathing while a fill runs.
        public static int DefaultWorkers => Math.Max(1, Environment.ProcessorCount - 1);

        readonly int[] _frames;
        readonly PyrePlusSpec[] _clones;
        readonly Thread[] _threads;
        readonly ConcurrentQueue<Result> _results = new ConcurrentQueue<Result>();
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        int _next = -1;            // the last frame slot handed out (Interlocked)
        int _running;              // workers still inside their loop
        long _renderTicks;         // sum of every produced frame's render time (Interlocked) — the serial-equivalent cost
        Exception _firstError;
        bool _disposed;

        public int Workers => _threads.Length;
        public int FrameCount => _frames.Length;
        /// Sum of the produced frames' individual render times — what the same frames would have cost serially.
        public double RenderMsTotal => Interlocked.Read(ref _renderTicks) * 1000.0 / Stopwatch.Frequency;
        /// True once every worker has left its loop (all frames done, or cancelled and the in-flight ones finished).
        public bool WorkersIdle => Volatile.Read(ref _running) == 0;
        public bool IsCancelled => _cts.IsCancellationRequested;
        /// The first exception a worker hit (its frame's Result carries null pixels); null when none.
        public Exception FirstError => Volatile.Read(ref _firstError);

        /// Start rendering `frames` of `spec` on `workers` threads, in the given order. Main thread only (clones the
        /// spec). `spec` itself is never read by a worker after this returns — the caller may keep editing it.
        public PlusFrameFill(PyrePlusSpec spec, IReadOnlyList<int> frames, int workers = 0)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            _frames = new int[frames.Count];
            for (int i = 0; i < _frames.Length; i++) _frames[i] = frames[i];
            workers = Math.Max(1, Math.Min(workers <= 0 ? DefaultWorkers : workers, Math.Max(1, _frames.Length)));
            _clones = new PyrePlusSpec[workers];
            _threads = new Thread[workers];
            for (int w = 0; w < workers; w++) _clones[w] = CloneForWorker(spec);
            _running = workers;
            for (int w = 0; w < workers; w++)
            {
                var clone = _clones[w];
                _threads[w] = new Thread(() => Run(clone)) { IsBackground = true, Name = "PyrePlus fill " + w };
            }
            for (int w = 0; w < workers; w++) _threads[w].Start();
        }

        /// A deep copy of the spec for one worker, with every plug-in form linked to its origin so the pre-pass caches
        /// are shared (not re-solved per worker). Instantiate goes through serialization, which is what makes the
        /// copy complete by construction — any render input a future field adds is copied without this code knowing.
        static PyrePlusSpec CloneForWorker(PyrePlusSpec spec)
        {
            var c = UnityEngine.Object.Instantiate(spec);
            c.hideFlags = HideFlags.HideAndDontSave;
            if (spec.layers != null && c.layers != null)
                for (int i = 0; i < spec.layers.Count && i < c.layers.Count; i++)
                {
                    var src = spec.layers[i]?.form; var dst = c.layers[i]?.form;
                    if (src != null && dst != null) dst.SharePrepassWith(src);
                }
            return c;
        }

        void Run(PyrePlusSpec clone)
        {
            try
            {
                var token = _cts.Token;
                while (!token.IsCancellationRequested)
                {
                    int slot = Interlocked.Increment(ref _next);
                    if (slot >= _frames.Length) break;
                    int f = _frames[slot];
                    long t0 = Stopwatch.GetTimestamp();
                    Color32[] px = null;
                    try { px = PyrePlusRenderer.RenderFrame(clone, f); }
                    catch (Exception e) { Interlocked.CompareExchange(ref _firstError, e, null); }
                    long dt = Stopwatch.GetTimestamp() - t0;
                    Interlocked.Add(ref _renderTicks, dt);
                    _results.Enqueue(new Result(f, px, dt * 1000.0 / Stopwatch.Frequency));
                }
            }
            finally { Interlocked.Decrement(ref _running); }
        }

        /// Take one finished frame, if any. Results arrive roughly in request order but not strictly (a fast frame
        /// overtakes a slow one) — the caller places them by frameIndex.
        public bool TryTake(out Result r) => _results.TryDequeue(out r);

        /// Stop handing out frames. In-flight frames finish (they cannot be interrupted) and stay in the queue,
        /// where a caller that has moved on simply never takes them.
        public void Cancel() => _cts.Cancel();

        /// Cancel, wait for the workers to leave their loops (bounded by one frame's render time each), destroy the
        /// clones. Main thread only. A fill whose frames all landed returns immediately.
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            foreach (var t in _threads) t.Join();
            foreach (var c in _clones) if (c != null) UnityEngine.Object.DestroyImmediate(c);
            _cts.Dispose();
        }

        /// Blocking convenience: every frame 0..frameCount-1 of `spec`, rendered in parallel, as an array indexed by
        /// frame. The byte-identity tests compare this against RenderFrame in a loop; an editor path drains TryTake
        /// per tick instead of blocking.
        public static Color32[][] RenderAll(PyrePlusSpec spec, int frameCount, int workers = 0)
        {
            var order = new int[frameCount];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            var outp = new Color32[frameCount][];
            using (var fill = new PlusFrameFill(spec, order, workers))
            {
                int got = 0;
                while (got < frameCount)
                {
                    if (fill.TryTake(out var r)) { outp[r.frameIndex] = r.pixels; got++; }
                    else if (fill.WorkersIdle && fill._results.IsEmpty) break;   // a worker died without a result — FirstError says why
                    else Thread.Sleep(1);
                }
                if (fill.FirstError != null) throw new InvalidOperationException("A frame failed to render on a worker thread.", fill.FirstError);
            }
            return outp;
        }
    }
}
