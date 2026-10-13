using Unity.Mathematics;

namespace Laubrary.ZTracker.Engine
{
    public enum TrackerPlaybackDirection { Forward = 1, Reverse = -1 }
    public enum TrackerRowReverseResult { None, Accepted, Completed, Cancelled, Busy, NotPlaying, AtPatternStart, InvalidDuration }

    /// <summary>Reverse row traversal, independent of rendered-mix tape rewind. Frame counts exclude pauses.</summary>
    public struct TrackerRowReverseStatus
    {
        public bool active;
        public TrackerRowReverseResult result;
        public int order, startRow, currentRow, elapsedFrames, totalFrames, sampleRate;
        public long completed, refused, cancelled;
        public float Progress => totalFrames > 0 ? math.saturate(elapsedFrames / (float)totalFrames) : 0;
        public TrackerPlaybackDirection Direction => active ? TrackerPlaybackDirection.Reverse : TrackerPlaybackDirection.Forward;
    }

    public struct TrackerRowReverseState
    {
        public bool active;
        public TrackerRowReverseResult result;
        public int order, startRow, elapsed, total, slowFrames;
        public long completed, refused, cancelled;
    }

    public unsafe partial struct TrackerRealtime
    {
        public TrackerRowReverseStatus RowReverseStatus() => new TrackerRowReverseStatus
        {
            active = state.rowReverse.active, result = state.rowReverse.result,
            order = state.rowReverse.order, startRow = state.rowReverse.startRow, currentRow = state.row,
            elapsedFrames = state.rowReverse.elapsed, totalFrames = state.rowReverse.total, sampleRate = state.sampleRate,
            completed = state.rowReverse.completed, refused = state.rowReverse.refused, cancelled = state.rowReverse.cancelled
        };

        void CancelRowReverse()
        {
            if (!state.rowReverse.active) return;
            state.rowReverse.active = false;
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
            else if (total < 2 * state.row) result = TrackerRowReverseResult.InvalidDuration;
            state.rowReverse.result = result;
            if (result != TrackerRowReverseResult.Accepted)
            {
                state.rowReverse.refused++;
                Emit(TrackerEventKind.RowReverseRefused, payload: (int)result);
                return;
            }
            state.rowReverse.active = true;
            state.rowReverse.order = state.sequenceIndex; state.rowReverse.startRow = state.row;
            state.rowReverse.elapsed = 0; state.rowReverse.total = total;
            state.rowReverse.slowFrames = math.min(state.sampleRate / 2, total / 4);
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

        // Inverse of the integral of rate(t): 1 -> .5 during the first slowFrames, then .5.
        // Equal traversal distances give longer later rows; the final boundary is exactly total frames.
        int ReverseRowBoundary(int visited)
        {
            if (visited >= state.rowReverse.startRow) return state.rowReverse.total;
            double slow = state.rowReverse.slowFrames;
            double distance = visited * (.5 * state.rowReverse.total + .25 * slow) / state.rowReverse.startRow;
            double frame = distance <= .75 * slow ? 2 * distance / (1 + math.sqrt(1 - distance / slow)) : 2 * distance - .5 * slow;
            return (int)math.round(frame);
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
