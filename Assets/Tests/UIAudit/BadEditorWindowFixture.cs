// BadEditorWindowFixture — a deliberately-bad ZUIWindow, the edit-mode sibling of ImguiFixtureHud
// (which does the same for runtime IMGUI HUDs). Two purposes:
//
//  1. Ground truth for EditorWindowAuditSection (Editor/UIAudit/) — every mechanically-detectable
//     issue kind it produces (OverWidth, OffScreen, TinyText, TextOverflow, Crowded, Overlap) appears
//     here at least once, spatially separated so each fires independently.
//  2. A cold-start benchmark for Claude itself: this window is intentionally bad in ways the
//     standard UI docs (the laubrary skill's zui.md / authoring.md / ui-layout-rules.md) already
//     cover — row-packing, redundant box titles, explanatory title text, missing tooltips — on top
//     of the mechanically-audited ones. Point a fresh session at it with no extra instructions beyond
//     what every Claude session doing Laubrary UI work already gets, and see how much it catches and
//     fixes unprompted.
//
// DO NOT "FIX" THIS FILE. It is a frozen benchmark, not a real tool — improving it in place destroys
// its value as a repeatable cold-start test. If the audit tooling or the doc set changes and this
// fixture needs new/different bad patterns to stay representative, that's a deliberate edit to the
// fixture's DESIGN, not a cleanup pass. Work on a COPY when actually practicing/testing a fix.
//
// NOT part of the Laubrary package (dev-host-only, like ImguiFixtureHud) — a fixture for testing
// UIAudit and Claude's own UI judgment, not a demo or a shipped tool.

using UnityEditor;
using UnityEngine;
using ZuiRuntime;
using Laubrary.UIAudit;

namespace Laubrary.UIAudit.Tests
{
    public class BadEditorWindowFixture : ZUIWindow
    {
        [MenuItem("Laubrary/Audit Bad-UI Fixture (Test)")]
        static void Open() => GetWindow<BadEditorWindowFixture>("Bad UI Fixture");

        float amount = 0.5f;
        float speed = 1f, radius = 8f, life = 2f;
        bool flagA, flagB;

        protected override void OnZUI()
        {
            // ── Bug 1: a slider left to GUILayout's default "fill whatever's left" width instead of
            // an explicit cap (see ui-layout-rules.md's "no infinite-width controls" section — this
            // is the single most-reported real complaint this fixture exists to catch). Stretches to
            // the window's full width; catches OverWidth.
            amount = ZUI.Slider(amount, 0f, 1f, "Amount");

            GUILayout.Space(8);

            // ── Bug 2: a redundant box title — "Amount" the box title duplicating "Amount" the
            // field's own label, on a box holding exactly one field. See authoring.md rule #12 /
            // ui-layout-rules.md's "never title a box that only holds one field" guidance.
            using (ZUI.Box("Amount"))
            {
                GUILayout.Label("Amount");
            }

            GUILayout.Space(8);

            // ── Bug 3: one-field-per-row waste — three short, unrelated-but-conceptually-grouped
            // numeric fields, each claiming a full-width row instead of sharing one via ZUI.Form/Row.
            using (ZUI.Box("Emitter"))
            {
                speed = ZUI.MicroSlider(speed, 0f, 10f, "Speed");
                radius = ZUI.MicroSlider(radius, 0f, 32f, "Radius");
                life = ZUI.MicroSlider(life, 0f, 5f, "Life");
            }

            GUILayout.Space(8);

            // ── Bug 4: explanatory text baked into a title instead of a tooltip (see
            // ui-layout-rules.md's "explanatory text belongs in a tooltip, not the UI").
            using (ZUI.Box("Live preview subject (not baked — preview-time wiring only)"))
            {
                GUILayout.Label("Subject: none");
            }

            GUILayout.Space(8);

            // ── Bug 5: Crowded — two toggles with ~1px gap, under the 2px MinControlGap default.
            using (new EditorGUILayout.HorizontalScope())
            {
                var r1 = GUILayoutUtility.GetRect(60, 20, GUILayout.Width(60));
                flagA = ZUI.Toggle(r1, flagA, "A");
                var r2 = new Rect(r1.xMax + 1f, r1.y, 60, 20);
                GUILayout.Space(61f);
                flagB = ZUI.Toggle(r2, flagB, "B");
            }

            GUILayout.Space(8);

            // ── Bug 6: TinyText — well under the 12px legibility threshold. Recorded by hand (like
            // ImguiFixtureHud's RecordButton) since this is a raw GUILayout.Label, not ZUI.Label —
            // also demonstrates the audit's real limitation: only ZUI.Editor-drawn text is seen at
            // all, same caveat as the runtime IMGUI section has for raw GUI.Label/GUI.Button calls.
            const string tinyText = "Fine print nobody can read";
            var tinyStyle = new GUIStyle(EditorStyles.label) { fontSize = 7 };
            GUILayout.Label(tinyText, tinyStyle);
            if (Event.current.type == EventType.Repaint)
            {
                EditorZuiAudit.Record(new ZuiDrawRecord
                {
                    Kind = "label", Rect = GUILayoutUtility.GetLastRect(), Text = tinyText, FontPx = tinyStyle.fontSize,
                });
            }

            GUILayout.Space(8);

            // ── Bug 7: Overlap — two buttons whose rects genuinely intersect.
            var bA = GUILayoutUtility.GetRect(0, 0);
            var rectA = new Rect(20, bA.y, 80, 24);
            var rectB = new Rect(60, bA.y, 80, 24);
            GUILayout.Space(28);
            ZUI.Button(rectA, "C");
            ZUI.Button(rectB, "D");
        }
    }
}
