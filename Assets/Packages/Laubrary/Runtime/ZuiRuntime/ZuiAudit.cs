// ZuiAudit — the bridge that makes immediate-mode UI lintable. IMGUI has no retained tree to walk,
// so instead ZuiRuntime *records* what it draws (one frame's worth) when recording is on, and the
// UIAudit IMGUI section reads that record. Each record carries the MEASURED content size vs the rect
// it was drawn into, computed here inside OnGUI where GUIStyle measurement is valid — so the section
// just compares numbers, no GUI calls at audit time.
//
// Double-buffered by frame: draws append to a "current" list; the first Repaint of a new frame
// promotes current → last. LastFrame therefore always holds one complete, consistent frame. Cost
// when Recording is false is a single bool test per draw.

using System.Collections.Generic;
using UnityEngine;

namespace ZuiRuntime
{
    public struct ZuiDrawRecord
    {
        public string Kind;          // panel / label / button / toggle / slider / menuitem
        public Rect Rect;            // screen-space rect it was drawn into
        public string Text;          // text it carries (null for non-text draws)
        public int FontPx;           // effective (already scaled) font size, 0 if none
        public float NeededWidth;    // measured content width, 0 if n/a
        public float NeededHeight;   // measured content height at Rect.width, 0 if n/a
        public bool Wrap;
        public bool Interactive;     // buttons/toggles/sliders/menu items — relevant to off-screen
        public bool Clipped;         // drawn inside a scroll view — skip the off-screen check
    }

    public static class ZuiAudit
    {
        /// <summary>When true, ZuiRuntime draws append records. Turn on, let a frame render, then read LastFrame.</summary>
        public static bool Recording;

        static List<ZuiDrawRecord> _current = new List<ZuiDrawRecord>();
        static List<ZuiDrawRecord> _last = new List<ZuiDrawRecord>();
        static int _frame = -1;

        /// <summary>The most recent fully-recorded frame's draws.</summary>
        public static IReadOnlyList<ZuiDrawRecord> LastFrame => _last;

        public static void Record(in ZuiDrawRecord r)
        {
            if (!Recording) return;
            var e = Event.current;
            if (e == null || e.type != EventType.Repaint) return; // one authoritative pass per frame
            int frame = Time.frameCount;
            if (frame != _frame)
            {
                var tmp = _last; _last = _current; _current = tmp; // promote, then reuse the old buffer
                _current.Clear();
                _frame = frame;
            }
            _current.Add(r);
        }

        /// <summary>Forget everything (used when a scripted audit run starts fresh).</summary>
        public static void Reset()
        {
            _current.Clear();
            _last.Clear();
            _frame = -1;
        }
    }
}
