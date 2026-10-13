using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Laubrary.ZTracker.Engine
{
    public enum TrackerRewindPhase { Idle, Slowing, Reversing }
    public enum TrackerRewindResult { None, Accepted, Completed, Cancelled, Disabled, EmptyHistory, HistoryExhausted, Busy, NotPlaying, InvalidDuration }

    /// <summary>One coherent render-published observation. Elapsed time excludes pause and output-device latency.</summary>
    public struct TrackerRewindStatus
    {
        public TrackerRewindPhase phase;
        public TrackerRewindResult result;
        public int historyFrames, capacityFrames, elapsedFrames, totalFrames, sampleRate, order;
        public long completed, refused;
        public bool completeHistory;
        public bool IsRewinding => phase != TrackerRewindPhase.Idle;
        public bool CanRewind => !IsRewinding && completeHistory && historyFrames >= 2;
        public float Progress => totalFrames > 0 ? math.saturate(elapsedFrames / (float)totalFrames) : 0;
        public double HistorySeconds => sampleRate > 0 ? historyFrames / (double)sampleRate : 0;
    }

    /// <summary>Fixed native storage owned by the prepared song; only the renderer changes its scalar state.</summary>
    public struct TrackerRewindState
    {
        public NativeArray<float2> history;
        public TrackerRewindPhase phase;
        public TrackerRewindResult result;
        public int count, order, elapsed, total, slowFrames;
        public double cursor, reverseStart;
        public long completed, refused;
        public bool started, exhausted;
    }

    public sealed unsafe partial class TrackerPreparedSong
    {
        /// <summary>Opt in before publication. Allocates stereo float history on the control thread (0 disables,
        /// otherwise 1..120 seconds). At 48 kHz, 30 seconds uses 11.52 MB. History never grows on the audio thread.</summary>
        public void EnableRewindHistory(float seconds = 30f)
        {
            if (Disposed || Published) throw new InvalidOperationException("Configure rewind before publishing the prepared song");
            if (!math.isfinite(seconds) || seconds != 0 && (seconds < 1 || seconds > 120)) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (state.rewind.history.IsCreated) state.rewind.history.Dispose();
            state.rewind = default;
            if (seconds > 0) state.rewind.history = new NativeArray<float2>((int)Math.Ceiling(seconds * state.sampleRate), Allocator.Persistent);
        }
    }

    public unsafe partial struct TrackerRealtime
    {
        public TrackerRewindStatus RewindStatus() => new TrackerRewindStatus
        {
            phase = state.rewind.phase, result = state.rewind.result,
            historyFrames = state.rewind.count, capacityFrames = state.rewind.history.IsCreated ? state.rewind.history.Length : 0,
            elapsedFrames = state.rewind.elapsed, totalFrames = state.rewind.total, sampleRate = state.sampleRate,
            order = state.rewind.order, completed = state.rewind.completed, refused = state.rewind.refused,
            completeHistory = state.rewind.started && !state.rewind.exhausted && state.playing
        };

        void ClearRewind(bool cancel = true)
        {
            var history = state.rewind.history;
            long completed = state.rewind.completed, refused = state.rewind.refused;
            var result = cancel && state.rewind.phase != TrackerRewindPhase.Idle ? TrackerRewindResult.Cancelled : state.rewind.result;
            state.rewind = new TrackerRewindState { history = history, completed = completed, refused = refused, result = result };
        }

        void BeginPatternHistory()
        {
            if (!state.rewind.history.IsCreated || state.rewind.phase != TrackerRewindPhase.Idle || state.rowReverse.active || state.row != 0) return;
            state.rewind.count = 0; state.rewind.started = true; state.rewind.exhausted = false;
            state.rewind.order = state.sequenceIndex;
        }

        void RequestRewind(float seconds)
        {
            var result = TrackerRewindResult.Accepted;
            int total = math.isfinite(seconds) && seconds >= .1f && seconds <= 10 ? (int)math.round(seconds * state.sampleRate) : 0;
            int slow = math.min(state.sampleRate / 2, total / 4);
            if (total == 0) result = TrackerRewindResult.InvalidDuration;
            else if (state.rewind.phase != TrackerRewindPhase.Idle || state.rowReverse.active) result = TrackerRewindResult.Busy;
            else if (!state.rewind.history.IsCreated) result = TrackerRewindResult.Disabled;
            else if (!state.playing) result = TrackerRewindResult.NotPlaying;
            else if (!state.rewind.started || state.rewind.count < 2) result = TrackerRewindResult.EmptyHistory;
            else if (state.rewind.exhausted || state.rewind.count > state.rewind.history.Length - slow) result = TrackerRewindResult.HistoryExhausted;
            state.rewind.result = result;
            if (result != TrackerRewindResult.Accepted)
            {
                state.rewind.refused++;
                Emit(TrackerEventKind.RewindRefused, payload: (int)result);
                return;
            }
            state.rewind.phase = TrackerRewindPhase.Slowing;
            state.rewind.total = total; state.rewind.slowFrames = slow; state.rewind.elapsed = 0;
            state.rewind.cursor = state.rewind.count;
            Emit(TrackerEventKind.RewindStarted);
        }

        int RewindSliceLimit(int frames) => state.rewind.phase == TrackerRewindPhase.Slowing ? math.min(frames, state.rewind.slowFrames - state.rewind.elapsed) : frames;

        // Called immediately after the master mix, before any tape resampling. Thus reverb, sends, live notes and
        // game changes are preserved exactly as rendered, not reconstructed from authored cells or reversed voices.
        void CaptureMix(int offset, int frames)
        {
            if (!state.rewind.history.IsCreated || !state.rewind.started || state.rewind.exhausted) return;
            for (int i = 0; i < frames; i++)
            {
                if (state.rewind.count == state.rewind.history.Length) { state.rewind.exhausted = true; break; }
                state.rewind.history[state.rewind.count++] = new float2(state.outputLeft[offset + i], state.outputRight[offset + i]);
            }
        }

        float2 ReadHistory(double position)
        {
            int a = (int)math.floor(position);
            a = math.clamp(a, 0, state.rewind.count - 1);
            int b = math.min(a + 1, state.rewind.count - 1);
            return math.lerp(state.rewind.history[a], state.rewind.history[b], (float)math.saturate(position - a));
        }

        void SlowMix(int offset, int frames)
        {
            if (state.rewind.phase != TrackerRewindPhase.Slowing) return;
            for (int i = 0; i < frames; i++)
            {
                float t = state.rewind.elapsed / (float)math.max(1, state.rewind.slowFrames - 1);
                float2 sample = ReadHistory(state.rewind.cursor) * (1 - .5f * t);
                state.outputLeft[offset + i] = sample.x; state.outputRight[offset + i] = sample.y;
                state.rewind.cursor += 1 - .5 * t;
                state.rewind.elapsed++;
            }
            if (state.rewind.elapsed == state.rewind.slowFrames)
            {
                state.rewind.phase = TrackerRewindPhase.Reversing;
                state.rewind.reverseStart = math.min(state.rewind.cursor, state.rewind.count - 1);
            }
        }

        int ReverseMix(int offset, int frames)
        {
            int count = math.min(frames, state.rewind.total - state.rewind.elapsed);
            int reverseFrames = state.rewind.total - state.rewind.slowFrames;
            for (int i = 0; i < count; i++)
            {
                int at = state.rewind.elapsed - state.rewind.slowFrames;
                double position = state.rewind.reverseStart * (1 - at / (double)math.max(1, reverseFrames - 1));
                float2 sample = ReadHistory(position) * .5f;
                state.outputLeft[offset + i] = sample.x; state.outputRight[offset + i] = sample.y;
                state.rewind.cursor = position; state.rewind.elapsed++;
            }
            state.samplePosition += count;
            if (state.rewind.elapsed == state.rewind.total)
            {
                int order = state.rewind.order, total = state.rewind.total;
                long completed = state.rewind.completed + 1;
                ClearRewind(false);
                state.rewind.completed = completed; state.rewind.elapsed = state.rewind.total = total;
                state.rewind.result = TrackerRewindResult.Completed;
                Seek(order, 0);
                state.playing = true;
                Emit(TrackerEventKind.RewindCompleted);
            }
            return count;
        }
    }
}
