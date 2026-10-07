using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zui
{
    public sealed partial class ZuiLanes
    {
        /// <summary>Direct editor checks without opening a window or a test-runner modal. Throws on failure.
        /// Gesture dispatch is tested independently of native pointer delivery; visual/real-drag QA remains required.</summary>
        public static string[] VerifyFrameContracts()
        {
            var passed = new List<string>();
            void Check(bool condition, string name)
            { if (!condition) throw new InvalidOperationException(name); passed.Add(name); }

            var seconds = new ZuiLanes(2, "Clock compatibility");
            seconds.SetLanes(new ZuiLane("First", 0, 1), new ZuiLane("Second", .5f, 2));
            int calls = 0;
            seconds.OnTimeChanged = _ => calls++;
            seconds.SetTime(1.25f);
            Check(calls == 0 && Mathf.Approximately(seconds.Seconds, 1.25f) && seconds.LaneCount == 2, "Seconds clock retains silent playback and overlapping lanes");
            seconds.Seconds = 5;
            Check(calls == 1 && Mathf.Approximately(seconds.Seconds, 2), "Seconds clock clamps and notifies a user seek once");
            Check(FrameIndexAt(-10, 300, 3) == 0 && FrameIndexAt(100, 300, 3) == 1 && FrameIndexAt(300, 300, 3) == 2 && FrameIndexAt(0, 300, 0) == -1,
                "Discrete column boundaries are zero-based, clamped and empty-safe");

            var frames = new ZuiLanes(3, "Frame contracts");
            var lane = new ZuiFrameLane("Range", "Explicit endpoints");
            lane.Spans.Add(new ZuiFrameSpan(1, 2, "Phase", Color.blue, resizable: true));
            frames.SetFrames(new[] { default(ZuiFramePicture), default(ZuiFramePicture), default(ZuiFramePicture) }, new[] { lane });
            int selected = -1, moveFrom = -1, moveTo = -1, first = -1, last = -1;
            bool additive = false, range = false;
            frames.OnFrameSelected = (i, a, r) => { selected = i; additive = a; range = r; };
            frames.OnFrameMoved = (f, t) => { moveFrom = f; moveTo = t; };
            frames.OnFrameRangeChanged = (r, s, f, l) => { first = f; last = l; };
            frames.CompleteFrameGesture(0, 2, -1, -1, false, true, true, false, false);
            Check(moveFrom == 0 && moveTo == 2 && selected == -1, "Picture drag requests one reorder without an incidental selection");
            moveFrom = -1;
            frames.CompleteFrameGesture(0, 2, -1, -1, false, true, true, true, false);
            Check(moveFrom == -1 && selected == 2 && additive && !range, "Ctrl drag extends selection without reordering");
            frames.CompleteFrameGesture(0, 1, -1, -1, false, false, false, false, true);
            Check(selected == 1 && !additive && range, "Shift gesture preserves range selection intent");
            frames.CompleteFrameGesture(1, 0, 0, 0, true, true, false, false, false);
            Check(first == 0 && last == 2, "First edge drag keeps the opposite explicit endpoint");
            frames.CompleteFrameGesture(2, 0, 0, 0, false, true, false, false, false);
            Check(first == 1 && last == 1, "Last edge cannot invert a phase range");
            selected = -1; frames.SetFrameWithoutNotify(2);
            Check(selected == -1 && frames._currentFrame == 2, "Playback never re-enters frame selection");
            return passed.ToArray();
        }
    }
}
