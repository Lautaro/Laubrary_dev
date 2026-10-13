using Unity.Mathematics;

namespace Laubrary.ZTracker.Engine
{
    public enum TrackerPlaybackDirection { Forward = 1, Reverse = -1 }
    public enum TrackerRowReversePhase { Idle, Slowing, Reversing }
    public enum TrackerRowReverseResult { None, Accepted, Completed, Cancelled, Busy, NotPlaying, AtPatternStart, InvalidDuration }

    /// <summary>Reverse row traversal, independent of rendered-mix tape rewind. Frame counts exclude pauses.</summary>
    public struct TrackerRowReverseStatus
    {
        public bool active;
        public TrackerRowReversePhase phase;
        public TrackerRowReverseResult result;
        public int order, startRow, turnRow, currentRow, elapsedFrames, totalFrames, slowFrames, sampleRate;
        public long completed, refused, cancelled;
        public float Progress => totalFrames > 0 ? math.saturate(elapsedFrames / (float)totalFrames) : 0;
        public TrackerPlaybackDirection Direction => active && phase == TrackerRowReversePhase.Reversing ? TrackerPlaybackDirection.Reverse : TrackerPlaybackDirection.Forward;
    }

    public struct TrackerRowReverseState
    {
        public bool active;
        public TrackerRowReversePhase phase;
        public TrackerRowReverseResult result;
        public int order, startRow, turnRow, elapsed, total, slowFrames, rampFrames;
        public double forwardRowFrames;
        public long completed, refused, cancelled;
    }

    public unsafe partial struct TrackerRealtime
    {
        public TrackerRowReverseStatus RowReverseStatus() => new TrackerRowReverseStatus
        {
            active = state.rowReverse.active, phase = state.rowReverse.phase, result = state.rowReverse.result,
            order = state.rowReverse.order, startRow = state.rowReverse.startRow, turnRow = state.rowReverse.turnRow, currentRow = state.row,
            elapsedFrames = state.rowReverse.elapsed, totalFrames = state.rowReverse.total, slowFrames = state.rowReverse.slowFrames, sampleRate = state.sampleRate,
            completed = state.rowReverse.completed, refused = state.rowReverse.refused, cancelled = state.rowReverse.cancelled
        };

        void CancelRowReverse()
        {
            if (!state.rowReverse.active) return;
            state.rowReverse.active = false;
            state.rowReverse.phase = TrackerRowReversePhase.Idle;
            state.rowReverse.result = TrackerRowReverseResult.Cancelled;
            state.rowReverse.cancelled++;
        }

        void RequestReverseRows(float seconds)
        {
            var result = TrackerRowReverseResult.Accepted;
            int total = math.isfinite(seconds) && seconds >= .1f && seconds <= 10 ? (int)math.round(seconds * state.sampleRate) : 0;
            if (state.rowReverse.active || state.rewind.phase != TrackerRewindPhase.Idle) result = TrackerRowReverseResult.Busy;
            else if (!state.playing) result = TrackerRowReverseResult.NotPlaying;
            else if (state.row <= 0) result = TrackerRowReverseResult.AtPatternStart;
            // The fastest part of the normalized schedule must still leave at least one frame per row.
            else if (total < 4 * state.patterns[state.sequence[state.sequenceIndex].pattern].lineCount) result = TrackerRowReverseResult.InvalidDuration;
            state.rowReverse.result = result;
            if (result != TrackerRowReverseResult.Accepted)
            {
                state.rowReverse.refused++;
                Emit(TrackerEventKind.RowReverseRefused, payload: (int)result);
                return;
            }
            state.rowReverse.active = true;
            state.rowReverse.phase = TrackerRowReversePhase.Slowing;
            state.rowReverse.order = state.sequenceIndex; state.rowReverse.startRow = state.row;
            state.rowReverse.turnRow = state.row;
            state.rowReverse.elapsed = 0; state.rowReverse.total = total;
            state.rowReverse.slowFrames = math.min(3 * state.sampleRate / 4, total / 4);
            state.rowReverse.rampFrames = math.min(3 * state.sampleRate / 20, (total - state.rowReverse.slowFrames) / 4);
            state.rowReverse.forwardRowFrames = state.sampleRate * 60d / (state.bpm * state.linesPerBeat);
            // Discard pending forward triggers and held voices, but retain instrument/command/effect memory.
            // There is no reconstruction of what those values were when these rows played previously.
            ClearVoices();
            for (int i = 0; i < state.columnCount; i++)
            {
                var c = state.columns[i]; c.cohort = 0; c.pendingCell = -1; c.due = -1;
                state.columns[i] = c;
            }
            ClearRewind(false); // reverse sequencing is not a complete forward pass for tape history
            state.runtimeTempoApplied = false;
            state.rowStart = 0; state.clockCompensation = 0; state.transportOrigin = state.samplePosition;
            state.holdRemaining = 0; state.held = false; state.rowPending = true;
            Emit(TrackerEventKind.RowReverseStarted);
        }

        TrackerPlaybackDirection RowDirection => state.rowReverse.active && state.rowReverse.phase == TrackerRowReversePhase.Reversing ? TrackerPlaybackDirection.Reverse : TrackerPlaybackDirection.Forward;

        // Forward speed falls linearly from 1 to zero. Integrate it, then invert for row boundaries.
        // Stay inside the request's pattern; the unfinished final row is cut at the turn.
        int ForwardRowBoundary(int visited)
        {
            double slow = state.rowReverse.slowFrames;
            double distance = visited * state.rowReverse.forwardRowFrames;
            int last = state.patterns[state.sequence[state.sequenceIndex].pattern].lineCount - 1;
            if (state.row >= last || distance >= .5 * slow) return state.rowReverse.slowFrames;
            return (int)math.round(2 * distance / (1 + math.sqrt(1 - 2 * distance / slow)));
        }

        // Reverse speed starts at zero, rises linearly over rampFrames, then stays constant.
        // Normalize its integral to traverse every remaining row and land exactly at total.
        int ReverseRowBoundary(int visited)
        {
            if (visited >= state.rowReverse.turnRow) return state.rowReverse.total;
            double ramp = state.rowReverse.rampFrames;
            double distance = visited * (state.rowReverse.total - state.rowReverse.slowFrames - .5 * ramp) / state.rowReverse.turnRow;
            double frame = distance < .5 * ramp ? math.sqrt(2 * ramp * distance) : distance + .5 * ramp;
            return state.rowReverse.slowFrames + (int)math.round(frame);
        }

        int RowReverseSliceLimit(int frames) => !state.rowReverse.active ? frames : math.min(frames,
            (state.rowReverse.phase == TrackerRowReversePhase.Slowing ? state.rowReverse.slowFrames : state.rowReverse.total) - state.rowReverse.elapsed);

        double RowReversePitchScale(int frame)
        {
            if (!state.rowReverse.active) return 1;
            if (state.rowReverse.phase == TrackerRowReversePhase.Slowing)
                return math.max(.02, 1d - frame / (double)state.rowReverse.slowFrames);
            return math.max(.02, math.min(1, (frame - state.rowReverse.slowFrames) / (double)state.rowReverse.rampFrames));
        }

        void TurnRowReverse()
        {
            if (!state.rowReverse.active || state.rowReverse.phase != TrackerRowReversePhase.Slowing || state.rowReverse.elapsed < state.rowReverse.slowFrames) return;
            state.rowReverse.phase = TrackerRowReversePhase.Reversing;
            state.rowReverse.turnRow = state.row;
            ClearVoices();
            for (int i = 0; i < state.columnCount; i++) { var c = state.columns[i]; c.cohort = 0; c.pendingCell = -1; c.due = -1; state.columns[i] = c; }
            state.rowStart = state.rowReverse.slowFrames; state.clockCompensation = 0;
            state.holdRemaining = 0; state.held = false; state.rowPending = true;
            EnterRow();
        }

        void RowReverseGain(int offset, int frames)
        {
            if (!state.rowReverse.active) return;
            for (int i = 0; i < frames; i++)
            {
                float gain = 1 - .5f * math.saturate((state.rowReverse.elapsed + i) / (float)math.max(1, state.rowReverse.slowFrames - 1));
                state.outputLeft[offset + i] *= gain; state.outputRight[offset + i] *= gain;
            }
            state.rowReverse.elapsed += frames;
        }

        void CompleteRowReverse()
        {
            if (!state.rowReverse.active || state.rowReverse.elapsed < state.rowReverse.total) return;
            state.rowReverse.active = false; state.rowReverse.result = TrackerRowReverseResult.Completed;
            state.rowReverse.phase = TrackerRowReversePhase.Idle;
            state.rowReverse.completed++;
            // Direct landing avoids replaying earlier orders/flow commands. Native effect/voice/control defaults
            // are restored, while game tempo/pitch/gates, event ring, row anchors and transport stay intact.
            ClearVoices(); ResetChains(); ResetColumns(); ResetDefaults(true); ResetCommandState(); ClearRewind(false);
            state.sequenceIndex = state.rowReverse.order; state.row = 0; state.tick = 0; state.breakRow = -1;
            state.rowStart = 0; state.clockCompensation = 0; state.transportOrigin = state.samplePosition;
            state.rowPending = true; state.playing = true;
            Emit(TrackerEventKind.RowReverseCompleted);
            EnterRow(); // row zero occurs once, at the endpoint even when it is exactly a block boundary
        }
    }
}
