// ZuiAudit — the UI Toolkit-native audit (migration plan §10/§11.5). Because a VisualElement tree
// is a real, persistent tree, this is a plain walker over a live window — none of the IMGUI
// audit's "toggle recording, force a Repaint pass" dance. Public API, no menu item: call it from
// tooling/tests/scripts (e.g. Coplay execute_script) against any open EditorWindow.
//
// Checks (the mechanically-detectable slice of ui-layout-rules.md):
//   • tooltip-missing — an interactive control with no tooltip on itself or any ancestor;
//   • off-screen     — a control whose right edge extends past the window (the horizontal-
//                      scrollbar smell / IMGUI audit's OffScreen check);
//   • stretch        — a BaseField-derived control that resolved to flex-grow > 0 (the "fills
//                      whatever's left" failure class ZuiToolkit.uss globally forbids);
//   • over-width     — a plain field control wider than a generous cap (600px), excluding
//                      containers/plots, per the no-infinite-width-controls rule.
// Row-packing waste, redundant titles, and explanatory labels stay a human's job — same as always.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public static class ZuiAudit
    {
        public class Finding
        {
            public string check;      // tooltip-missing | off-screen | stretch | over-width
            public string element;    // type + classes + name/text hint
            public string detail;
            public override string ToString() => $"[{check}] {element} — {detail}";
        }

        static readonly System.Type[] InteractiveTypes =
        {
            typeof(Button), typeof(Toggle), typeof(TextField), typeof(FloatField), typeof(IntegerField),
            typeof(Slider), typeof(SliderInt), typeof(MinMaxSlider), typeof(DropdownField), typeof(EnumField),
            typeof(Foldout),
        };

        const float OverWidthCap = 600f;

        public static List<Finding> Audit(EditorWindow window)
        {
            var findings = new List<Finding>();
            var root = window.rootVisualElement;
            float windowRight = root.worldBound.xMax + 1f;   // +1: float slop
            Walk(root, root, windowRight, findings);
            return findings;
        }

        static void Walk(VisualElement ve, VisualElement root, float windowRight, List<Finding> findings)
        {
            if (ve.resolvedStyle.display == DisplayStyle.None) return;   // hidden branches don't count

            bool interactive = IsInteractive(ve);
            if (interactive)
            {
                if (!HasTooltipInChain(ve))
                    findings.Add(New("tooltip-missing", ve, "no tooltip on the control or any ancestor"));

                if (ve is not Foldout && ve.resolvedStyle.flexGrow > 0f && ve.ClassListContains("unity-base-field"))
                    findings.Add(New("stretch", ve, $"BaseField resolved flex-grow {ve.resolvedStyle.flexGrow:0.##}"));

                float w = ve.worldBound.width;
                if (ve is not Foldout && w > OverWidthCap)
                    findings.Add(New("over-width", ve, $"{w:0}px wide (cap {OverWidthCap})"));
            }

            if ((interactive || ve is Label) && ve.worldBound.width > 0f && ve.worldBound.xMax > windowRight
                && !InsideScroller(ve))
                findings.Add(New("off-screen", ve, $"right edge {ve.worldBound.xMax:0} past window {windowRight:0}"));

            foreach (var child in ve.Children())
                Walk(child, root, windowRight, findings);
        }

        static bool IsInteractive(VisualElement ve)
        {
            var t = ve.GetType();
            foreach (var it in InteractiveTypes)
                if (it.IsAssignableFrom(t)) return true;
            // Custom Zui controls that take input
            return ve is ZuiPad || ve is ZuiEnvelope;
        }

        static bool HasTooltipInChain(VisualElement ve)
        {
            for (var v = ve; v != null; v = v.parent)
                if (!string.IsNullOrEmpty(v.tooltip)) return true;
            return false;
        }

        // Content inside a ScrollView legitimately extends past the window — the scroller handles it.
        static bool InsideScroller(VisualElement ve)
        {
            for (var v = ve.parent; v != null; v = v.parent)
                if (v is ScrollView) return true;
            return false;
        }

        static Finding New(string check, VisualElement ve, string detail)
        {
            string hint = ve switch
            {
                Button b when !string.IsNullOrEmpty(b.text) => $" \"{b.text}\"",
                Label l when !string.IsNullOrEmpty(l.text) => $" \"{Truncate(l.text)}\"",
                Foldout f when !string.IsNullOrEmpty(f.text) => $" \"{f.text}\"",
                TextElement te when !string.IsNullOrEmpty(te.text) => $" \"{Truncate(te.text)}\"",
                _ => string.IsNullOrEmpty(ve.name) ? "" : $" #{ve.name}",
            };
            return new Finding
            {
                check = check,
                element = ve.GetType().Name + hint + " [" + string.Join(",", ve.GetClasses()) + "]",
                detail = detail,
            };
        }

        static string Truncate(string s) => s.Length > 30 ? s.Substring(0, 30) + "…" : s;
    }
}
