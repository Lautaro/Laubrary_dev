// PlusLayerFill — runs render jobs for one spec on every core, each job against that worker's own spec clone.
//
// The per-layer preview cache hands out two kinds of job: "render layer L of frame F on its own" and "compose
// frame F from these layer buffers". Both are pure functions of (spec, inputs) and so run anywhere; what a worker
// must not share is scratch (forms, modifiers and sims keep Prepare'd state on their instances), so — exactly as
// PlusFrameFill does for whole frames — every worker owns a deep clone of the spec (Object.Instantiate on the main
// thread, plug-in forms linked to their origin so the pre-pass caches are shared). A job is a delegate taking the
// worker's clone and returning its payload; the main thread enqueues jobs at any time (composes jump the queue —
// they are cheap and make a frame visible) and drains results with TryTake. Cancel flips a flag checked between
// jobs; an in-flight job finishes and its result is simply never taken. Dispose waits for the workers and destroys
// the clones — main thread only.
//
// Specs that are not parallel-safe (PyrePlusRenderer.IsParallelSafe) must not be given to this class; the caller
// runs the same delegates on the main thread against the real spec instead.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace Laubrary.PyrePlus
{
    public sealed class PlusLayerFill : IDisposable
    {
        public readonly struct Result
        {
            public readonly object tag;          // whatever Enqueue was given — the caller's job identity
            public readonly object payload;      // the job's return value; null when it threw (see FirstError)
            public readonly double ms;           // that job's own wall time on its worker
            public Result(object tag, object payload, double ms) { this.tag = tag; this.payload = payload; this.ms = ms; }
        }

        struct Job { public object tag; public Func<PyrePlusSpec, object> work; }

        public static int DefaultWorkers => Math.Max(1, Environment.ProcessorCount - 1);

        readonly PyrePlusSpec[] _clones;
        readonly Thread[] _threads;
        readonly LinkedList<Job> _queue = new LinkedList<Job>();     // urgent jobs at the front
        readonly object _gate = new object();
        readonly ConcurrentQueue<Result> _results = new ConcurrentQueue<Result>();
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        int _running;          // workers still inside their loop
        int _pending;          // jobs enqueued and not yet finished (queued + in flight)
        long _workTicks;
        Exception _firstError;
        bool _disposed;

        public int Workers => _threads.Length;
        /// Jobs queued or in flight.
        public int Pending => Volatile.Read(ref _pending);
        /// Sum of every finished job's own wall time — the serial-equivalent cost.
        public double WorkMsTotal => Interlocked.Read(ref _workTicks) * 1000.0 / Stopwatch.Frequency;
        public bool WorkersIdle => Volatile.Read(ref _running) == 0;
        public bool IsCancelled => _cts.IsCancellationRequested;
        public Exception FirstError => Volatile.Read(ref _firstError);

        /// Start `workers` threads, each with its own clone of `spec`. Main thread only; `spec` itself is never read
        /// by a worker after this returns — the caller may keep editing it.
        public PlusLayerFill(PyrePlusSpec spec, int workers = 0)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            workers = Math.Max(1, workers <= 0 ? DefaultWorkers : workers);
            _clones = new PyrePlusSpec[workers];
            _threads = new Thread[workers];
            for (int w = 0; w < workers; w++) _clones[w] = PlusFrameFill.CloneForWorker(spec);
            _running = workers;
            for (int w = 0; w < workers; w++)
            {
                var clone = _clones[w];
                _threads[w] = new Thread(() => Run(clone)) { IsBackground = true, Name = "PyrePlus layer fill " + w };
            }
            for (int w = 0; w < workers; w++) _threads[w].Start();
        }

        /// Queue a job. `urgent` puts it ahead of everything waiting (a compose, or the frame on screen).
        public void Enqueue(object tag, Func<PyrePlusSpec, object> work, bool urgent = false)
        {
            if (_disposed || _cts.IsCancellationRequested) return;
            Interlocked.Increment(ref _pending);
            lock (_gate)
            {
                var job = new Job { tag = tag, work = work };
                if (urgent) _queue.AddFirst(job); else _queue.AddLast(job);
                Monitor.Pulse(_gate);
            }
        }

        void Run(PyrePlusSpec clone)
        {
            try
            {
                var token = _cts.Token;
                while (true)
                {
                    Job job;
                    lock (_gate)
                    {
                        while (_queue.Count == 0 && !token.IsCancellationRequested) Monitor.Wait(_gate);
                        if (token.IsCancellationRequested) break;
                        job = _queue.First.Value;
                        _queue.RemoveFirst();
                    }
                    long t0 = Stopwatch.GetTimestamp();
                    object payload = null;
                    try { payload = job.work(clone); }
                    catch (Exception e) { Interlocked.CompareExchange(ref _firstError, e, null); }
                    long dt = Stopwatch.GetTimestamp() - t0;
                    Interlocked.Add(ref _workTicks, dt);
                    _results.Enqueue(new Result(job.tag, payload, dt * 1000.0 / Stopwatch.Frequency));
                    Interlocked.Decrement(ref _pending);
                }
            }
            finally { Interlocked.Decrement(ref _running); }
        }

        /// Take one finished job, if any. Results arrive roughly in queue order but not strictly.
        public bool TryTake(out Result r) => _results.TryDequeue(out r);

        /// Stop handing out jobs and wake the workers so they leave. In-flight jobs finish and stay in the queue.
        public void Cancel()
        {
            _cts.Cancel();
            lock (_gate) Monitor.PulseAll(_gate);
        }

        /// Cancel, wait for the workers (bounded by one job each), destroy the clones. Main thread only.
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Cancel();
            foreach (var t in _threads) t.Join();
            foreach (var c in _clones) if (c != null) UnityEngine.Object.DestroyImmediate(c);
            _cts.Dispose();
        }
    }
}
