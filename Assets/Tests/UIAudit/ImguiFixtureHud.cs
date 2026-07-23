// ImguiFixtureHud — deliberately-bad vs. correctly-built IMGUI layouts, used as ground truth by
// UIAuditImguiFixtureTests. NOT part of the Laubrary package (dev-host-only, see
// ZUI_API_AND_RUNTIME_ROADMAP.md Task 3) — this is a fixture for testing UIAudit itself, not a demo.
//
// The "bad" frame deliberately bypasses ZuiStack's own safe helpers where needed (a stack can't produce
// overlap or crowding on its own — it lays rows out sequentially with a fixed gap) to reproduce the kind
// of mistake a careless caller would actually make: hand-placed rects with no gap, a too-small LabelIn
// rect, an unreadable font size, a rect that drifts off-screen.

using UnityEngine;
using ZuiRuntime;

namespace Laubrary.UIAudit.Tests
{
    public class ImguiFixtureHud : MonoBehaviour
    {
        public bool DrawBad = true;

        void OnGUI()
        {
            if (DrawBad) DrawBadFrame();
            else DrawFixedFrame();
        }

        // One instance of each of the 5 IMGUI-coverable issue kinds (NeedsScrollView is uGUI-only today —
        // see UIAuditCore.cs), spatially separated so each fires independently with no cross-triggering.
        void DrawBadFrame()
        {
            var stack = Zui.BeginStack(new Rect(20, 20, 300, 400), gapPts: 6f);

            // TinyText — pts=3 guarantees FontPx < the 12px threshold even at UIScale's 3x ceiling (3*3=9).
            stack.Label("Too small to read", pts: 3f);

            // TextOverflow — LabelIn's explicit rect is far narrower than the (no-wrap) text needs.
            stack.LabelIn(new Rect(30, 70, 20, 18), "This text needs way more room than 20px", pts: 15f, wrap: false);

            var style = Zui.ButtonStyle(15f);

            // Crowded — two interactive rects with a 1px gap, under the 2px MinControlGap default.
            var r1 = new Rect(30, 110, 60, 24);
            var r2 = new Rect(r1.xMax + 1f, 110, 60, 24);
            RecordButton(style, r1, "A");
            RecordButton(style, r2, "B");

            // Overlap — two interactive rects that genuinely intersect.
            var r3 = new Rect(30, 150, 80, 24);
            var r4 = new Rect(70, 150, 80, 24);
            RecordButton(style, r3, "C");
            RecordButton(style, r4, "D");

            // OffScreen — placed well past the right edge of the screen.
            var rOff = new Rect(Screen.width + 100, 40, 80, 24);
            RecordButton(style, rOff, "Off");
        }

        void DrawFixedFrame()
        {
            var stack = Zui.BeginStack(new Rect(20, 20, 300, 400), gapPts: 10f);
            stack.Label("Readable label", pts: 15f);
            stack.Label("This text needs way more room and now wraps to fit its rect", pts: 15f);
            stack.Buttons(15f, "A", "B");
            stack.Buttons(15f, "C", "D");
            stack.Button("On-screen");
        }

        static void RecordButton(GUIStyle style, Rect rect, string label)
        {
            GUI.Button(rect, label, style);
            if (!ZuiAudit.Recording) return;
            ZuiAudit.Record(new ZuiDrawRecord { Kind = "button", Rect = rect, Text = label, Interactive = true });
        }
    }
}
