// ChunkWindow.Timing — the recipe's clock, drawn as one lane per timed capability.
//
// The section exists only when there is something to be timed AGAINST: two or more capabilities competing for
// the clock, or a cue that has to be placed on it. One capability owns the whole clock and has nothing to
// compare itself to, so it gets no ruler and no Delay dial — the same rule, asked once, in ChunkClock.
//
// It sits BELOW the preview and its lane count never changes on an on/off toggle, so switching a capability
// off to look at the rest cannot move the picture the user is looking at.
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        ZuiLanes lanes;
        // Which capability each lane draws, by lane index, as of the last FillLanes — how a band drag finds the
        // card it belongs to. Ids, not references, so a stale entry can only miss, never write elsewhere.
        readonly List<string> laneCapIds = new List<string>();

        // Lanes are coloured by their place in the stack rather than by kind: what a reader needs from the
        // colour is "which card is this", and two Pyre Blasts in one recipe is the normal case.
        static readonly Color[] LaneColors =
        {
            new Color(0.36f, 0.62f, 0.92f), new Color(0.95f, 0.62f, 0.25f),
            new Color(0.45f, 0.83f, 0.52f), new Color(0.86f, 0.45f, 0.72f),
            new Color(0.85f, 0.82f, 0.36f), new Color(0.55f, 0.55f, 0.95f),
        };

        void BuildTimingSection(VisualElement parent, ChunkSpec c)
        {
            timingSection = null;
            lanes = null;
            if (!ChunkClock.NeedsTimingSurface(c)) return;

            timingSection = Z.Section("Timing",
                "When each capability fires and how long its output lasts, on the recipe's one clock.",
                "Chunks.timing", "clock");

            lanes = Z.Lanes(ChunkClock.Length(c),
                "Every timed capability on one clock. Drag a band to change when that capability fires; " +
                "click or drag on the ruler or an empty stretch to move the playhead — it is the same instant " +
                "the transport shows.",
                v => { previewTime = v; playing = false; UpdatePlayButton(); SyncTransport(); },
                gutterWidth: 120f,
                onLaneMoved: OnLaneMoved);
            timingSection.Add(lanes);
            FillLanes(c);
            parent.Add(timingSection);
        }

        void FillLanes(ChunkSpec c)
        {
            if (lanes == null || c == null) return;

            // The LENGTH is computed over every timed capability, switched off ones included (ChunkClock says
            // so), which is what stops a lane switched off from rescaling the ruler and sliding every other
            // band sideways under the cursor.
            lanes.SetLength(ChunkClock.Length(c));

            var bands = new List<ZuiLane>();
            var marks = new List<ZuiLaneMarker>();
            laneCapIds.Clear();
            var stack = c.capabilities;
            int colour = 0;
            if (stack != null)
                for (int i = 0; i < stack.Count; i++)
                {
                    var cap = stack[i];
                    if (cap == null) continue;

                    if (cap.OccupiesTime)
                    {
                        float start = Mathf.Max(0f, cap.delay);
                        float end = start + Mathf.Max(0f, cap.DurationSeconds(c));
                        bands.Add(new ZuiLane(cap.Title, start, end, LaneColors[colour % LaneColors.Length],
                            dim: !cap.enabled,
                            tooltip: $"{cap.KindName} — fires at {start:0.00}s and is gone by {end:0.00}s." +
                                     (cap.enabled ? "" : " Switched off, so it puts nothing on screen.")));
                        laneCapIds.Add(cap.EnsureId());
                        colour++;
                    }

                    if (cap is Cues cues && cues.cues != null && cap.enabled)
                        for (int m = 0; m < cues.cues.Count; m++)
                        {
                            var cue = cues.cues[m];
                            if (cue == null || cue.IsEmpty) continue;
                            marks.Add(new ZuiLaneMarker(cue.time, cue.DisplayName,
                                $"{cue.DisplayName} at {cue.time:0.00}s."));
                        }
                }

            lanes.SetLanes(bands);
            lanes.SetMarkers(marks);
            lanes.SetTime(previewTime);
        }

        /// A band dragged on the lanes IS an edit of that capability's Delay: it goes through Dial like the
        /// card's own field (the lanes collapse the whole drag into one Undo step), the card's field is moved
        /// without re-raising its callback, and Dial's SyncTiming pushes the lanes and the stage — no card and
        /// no window is rebuilt per move.
        void OnLaneMoved(int laneIndex, float start)
        {
            var c = Current;
            if (c == null || laneIndex < 0 || laneIndex >= laneCapIds.Count) return;
            string id = laneCapIds[laneIndex];
            var cap = Find(c, id);
            if (cap == null || !cap.enabled) return;

            float delay = Mathf.Max(0f, start);
            if (Mathf.Approximately(cap.delay, delay)) return;
            Dial("Edit Delay", () => cap.delay = delay);
            if (delayFields.TryGetValue(id, out var field) && field != null) field.SetValueWithoutNotify(delay);
        }

        /// Keep the clock surface agreeing with the recipe after any edit. A change that makes the surface
        /// APPEAR or DISAPPEAR is a change to which sections the window has, so it rebuilds the window; a
        /// change to what the lanes say is pushed into the live control, which keeps the playhead and any
        /// drag in progress.
        internal void SyncTiming()
        {
            var c = Current;
            if (c == null) return;
            bool need = ChunkClock.NeedsTimingSurface(c);
            if (need != (timingSection != null)) { Rebuild(); return; }
            FillLanes(c);
            SyncTransport();
        }
    }
}
