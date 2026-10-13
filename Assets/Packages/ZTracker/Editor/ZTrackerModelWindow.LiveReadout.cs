using System;
using Laubrary.ZTracker.Engine;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        /// <summary>Responsive-editor target, after native publication; excludes output latency/editor stalls.</summary>
        public const double LiveReadoutLagTargetMs = 100;
        public long ObservedLiveBlock { get; private set; }
        public double LiveReadoutObservedAt { get; private set; }
        public double LiveReadoutMaxPollGapMs { get; private set; }
        double lastLivePoll;
        bool observedLive;
        TrackerSapGenerator observedHost;

        void RefreshLiveReadout()
        {
            if (status == null) return;
            var external = ZTrackerPlayback.Current;
            if (external == null || !external.IsPlaying || external.SourceSong != song) external = null;
            // This window owns only its preview. A game-owned stream is observed without letting
            // the preview buttons silently start a second stream or pretend to stop the game's music.
            PreviewControl("play", external == null, "Start the song from its first row. Space toggles preview play/stop.");
            PreviewControl("play-cursor", external == null, "Start the preview at the selected sequence slot and row.");
            PreviewControl("stop", external == null, "Stop the editor preview and release its owned audio buffers.");
            var source = external != null ? external.Generator : host;
            double now = EditorApplication.timeSinceStartup;
            if (source != observedHost) { observedHost = source; lastLivePoll = 0; LiveReadoutMaxPollGapMs = 0; }
            if (source != null && lastLivePoll > 0) LiveReadoutMaxPollGapMs = Math.Max(LiveReadoutMaxPollGapMs, (now - lastLivePoll) * 1000);
            lastLivePoll = source == null ? 0 : now;
            if (source == null || !source.TryReadClockHead(out var head)) {
                status.text = lastError != null ? "⚠ Pending: Stop/Play" : "Idle";
                status.tooltip = lastError ?? "No matching song playback. Play starts an editor preview.";
                if (observedLive) { observedLive = false; playingRow = -1; foreach (var pair in labels) pair.Value.EnableInClassList("tracker-playing", false); RefreshTrackReadouts(null); }
                return;
            }
            observedLive = true; ObservedLiveBlock = head.blocks; LiveReadoutObservedAt = now;
            bool tape = head.rewind.IsRewinding;
            string mode = head.paused != 0 ? "Paused" : tape ? "Tape " + head.rewind.Progress.ToString("P0") : head.rowReverse.active ? (head.rowReverse.phase == TrackerRowReversePhase.Slowing ? "Slowing " : "Reverse ") + head.rowReverse.Progress.ToString("P0") : head.playing != 0 ? "Playing" : "Stopped";
            int shownOrder = head.sequence, shownRow = head.row;
            double shownBpm = head.bpm;
            if (external != null && !tape && external.Clock.TryGetHeard(out var heard)) { shownOrder = heard.Order; shownRow = heard.Row; shownBpm = heard.Bpm; }
            string pending = head.tempoPending ? " → " + (head.requestedTempo > 0 ? head.requestedTempo.ToString("0.#") : "authored") + " next row" : "";
            if (head.pendingParameters > 0) pending += " · " + head.pendingParameters + " queued";
            string detail = "";
            if (source.TryReadTrack(track, out var selected)) detail = " · " + PitchReadout(selected) + (selected.gated ? " gated" : "") + (selected.muted ? " muted" : "");
            status.text = (external != null ? "Game · " : "") + mode + " · " + shownOrder + ":" + shownRow + " · " + shownBpm.ToString("0.#") + " BPM" + pending + detail;
            status.tooltip = lastError ?? (status.text + ". Track pitch is the additive game offset; authored notes and instrument tuning remain unchanged. Activity counts live voices, including release tails. Tape plays recorded mix; track voices do not describe that recorded audio.");
            if (!tape && head.playing != 0 && pane == 0) {
                if (follow && shownOrder != order && Data != null && shownOrder >= 0 && shownOrder < Data.sequence.Count) { order = shownOrder; BuildPane(); }
                if (shownOrder == order && shownRow != playingRow) {
                    if (follow) HighlightPlaying(shownRow);
                    else { playingRow = shownRow; foreach (var pair in labels) pair.Value.EnableInClassList("tracker-playing", pair.Key.x == shownRow); }
                }
            } else if (playingRow != -1) { playingRow = -1; foreach (var pair in labels) pair.Value.EnableInClassList("tracker-playing", false); }
            RefreshTrackReadouts(source);
        }
        void PreviewControl(string name, bool enabled, string tooltip)
        {
            var control = rootVisualElement.Q<VisualElement>(name);
            if (control == null) return;
            control.SetEnabled(enabled);
            control.tooltip = enabled ? tooltip : "The game owns this song's playback. Use the game's transport controls; this window follows it live.";
        }
        static string PitchReadout(TrackerTrackReadout value) => value.transpose.ToString("+0;-0;0") + "st " + value.detune.ToString("+0.#;-0.#;0") + "c";
        void RefreshTrackReadouts(TrackerSapGenerator source)
        {
            if (Data == null || gridCanvas == null || pane != 0) return;
            for (int t = 0; t < Data.tracks.Count; t++) {
                var title = gridCanvas.Q<Label>("pattern-track-header-" + t);
                if (title == null) continue;
                var authored = Data.tracks[t];
                if (source == null || !source.TryReadTrack(t, out var value)) { title.text = authored.name; title.tooltip = "Select this track; drag it sideways to reorder the tracks."; continue; }
                string flags = (value.muted ? " M" : "") + (value.gated ? " G" : "");
                string pitch = value.transpose != 0 || value.detune != 0 ? " " + PitchReadout(value) : "";
                title.text = (value.activeVoices > 0 ? "● " : "○ ") + authored.name + flags + pitch;
                title.style.textOverflow = TextOverflow.Ellipsis;
                title.style.whiteSpace = WhiteSpace.NoWrap;
                title.style.overflow = Overflow.Hidden;
                title.tooltip = authored.name + ": " + value.activeVoices + " active voices; " + PitchReadout(value) + "; " + (value.gated ? "pattern triggers gated" : "pattern triggers enabled") + "; " + (value.muted ? "output muted" : "output enabled") + ". Select this track; drag sideways to reorder.";
            }
        }
    }
}
