// ZuiAudit — the UI Toolkit-native audit (migration plan §10/§11.5). Because a VisualElement tree
// is a real, persistent tree, this is a plain walker over a live window — none of the IMGUI
// audit's "toggle recording, force a Repaint pass" dance. Public API, no menu item: call it from
// tooling/tests/scripts (e.g. Coplay execute_script) against any open EditorWindow.
//
// Checks (the mechanically-detectable slice of ui-layout-rules.md):
//   • tooltip-missing — an interactive control with no tooltip on itself or any ancestor;
//   • off-screen     — a control whose right edge extends past the window (the horizontal-
//                      scrollbar smell / IMGUI audit's OffScreen check);
//   • stretch        — a BaseField-derived control (or, T-0192, a ZuiMicroSlider/ZuiMicroMinMax) that
//                      resolved to flex-grow > 0 (the "fills whatever's left" failure class
//                      ZuiToolkit.uss globally forbids);
//   • over-width     — a plain field control wider than a generous cap (600px), excluding
//                      containers/plots, per the no-infinite-width-controls rule;
//   • clipped-input  — a text/numeric field printing a string wider than the box it prints it in, so an
//                      authored value is silently cut (the truncated-text rule, applied to inputs).
// Row-packing waste, redundant titles, and explanatory labels stay a human's job — same as always.
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public static class ZuiAudit
    {
        public class Finding
        {
            public string check;      // tooltip-missing | off-screen | stretch | over-width | native-toggle
            public string element;    // type + classes + name/text hint
            public string detail;
            public override string ToString() => $"[{check}] {element} — {detail}";
        }

        static readonly System.Type[] InteractiveTypes =
        {
            typeof(Button), typeof(Toggle), typeof(TextField), typeof(FloatField), typeof(IntegerField),
            typeof(Slider), typeof(SliderInt), typeof(MinMaxSlider), typeof(DropdownField), typeof(EnumField),
            typeof(Foldout),
            // A PropertyField is a control, not a container, even though a list/nested-class one CONTAINS a
            // Foldout. It was invisible to every check here until the Chunks sprite list turned up spanning
            // the whole window with a clean audit.
            typeof(PropertyField),
            // T-0192 — a bare Z.MicroSlider/Z.MicroMinMax is a real input control (drag/click to set a
            // value) but was invisible to every check here, which is exactly why a solo one growing to fill
            // its row (ZuiValueControl's Static/MinMax body, or any future bare-MicroSlider misuse) passed a
            // clean audit while visibly stretching (PM by-eye, shaper_3col.png). Neither is a BaseField, so
            // they also need the dedicated stretch check below — being interactive alone only buys them the
            // tooltip/over-width/off-screen checks.
            typeof(ZuiMicroSlider), typeof(ZuiMicroMinMax),
        };

        const float OverWidthCap = 600f;

        public static List<Finding> Audit(EditorWindow window) => Audit(window, out _);

        /// `foldedSkipped` counts subtrees this pass could not see because they were collapsed. Since every
        /// section heading and box title became foldable, a window sitting with half its blocks closed can
        /// return zero findings while most of it was never looked at — a clean result is only meaningful
        /// when this is 0. Callers that want real coverage should expand first, or report the number.
        public static List<Finding> Audit(EditorWindow window, out int foldedSkipped)
        {
            var findings = new List<Finding>();
            var root = window.rootVisualElement;
            float windowRight = root.worldBound.xMax + 1f;   // +1: float slop
            foldedSkipped = 0;
            Walk(root, root, windowRight, findings, ref foldedSkipped);
            return findings;
        }

        /// Expand every ZuiSection / ZuiBox in a window so an audit can actually see it. Returns how many
        /// it opened, so a caller can put them back if it cares.
        public static int ExpandAll(EditorWindow window)
        {
            int opened = 0;
            foreach (var s in window.rootVisualElement.Query<ZuiSection>().ToList())
                if (!s.IsOpen) { s.IsOpen = true; opened++; }
            foreach (var b in window.rootVisualElement.Query<ZuiBox>().ToList())
                if (!b.IsOpen) { b.IsOpen = true; opened++; }
            foreach (var l in window.rootVisualElement.Query<ZuiSectionLabel>().ToList())
                if (!l.IsOpen) { l.IsOpen = true; opened++; }
            return opened;
        }

        static void Walk(VisualElement ve, VisualElement root, float windowRight, List<Finding> findings,
            ref int foldedSkipped)
        {
            if (ve.resolvedStyle.display == DisplayStyle.None) { foldedSkipped++; return; }

            bool interactive = IsInteractive(ve);
            if (interactive)
            {
                if (!HasTooltipInChain(ve))
                    findings.Add(New("tooltip-missing", ve, "no tooltip on the control or any ancestor"));

                // Native checkbox: a bool is a Z.ToggleButton EVERYWHERE (rule upgraded 2026-08-02 from
                // menus-only to all surfaces) — the UITK Toggle's checkmark box reads as a bare OS
                // checkbox. A Foldout's internal Toggle is its fold header, not a checkbox; the
                // "zui-audit-allow-toggle" class is the sanctioned opt-out for fold/chrome toggles.
                if (ve is Toggle && ve.GetFirstAncestorOfType<Foldout>() == null
                    && !ve.ClassListContains("zui-audit-allow-toggle"))
                    findings.Add(New("native-toggle", ve, "checkbox Toggle — use Z.ToggleButton"));

                // "zui-audit-allow-stretch" is the sanctioned opt-out for the rulebook's own exception:
                // a name/path field that legitimately fills the space between fixed row-mates.
                if (ve is not Foldout && ve.resolvedStyle.flexGrow > 0f && ve.ClassListContains("unity-base-field")
                    && !ve.ClassListContains("zui-audit-allow-stretch"))
                    findings.Add(New("stretch", ve, $"BaseField resolved flex-grow {ve.resolvedStyle.flexGrow:0.##}"));

                // T-0192 — ZuiMicroSlider/ZuiMicroMinMax carry neither "unity-base-field" nor a BaseField
                // base type, so the check above cannot see them stretch. They also aren't the cross-axis
                // (align-self) case the toggle/chip checks cover below — a growing MicroSlider resolves a
                // real flex-grow on the MAIN axis (it sits in a horizontal zui-row), same mechanism as a
                // BaseField, just a different base class. Same opt-out class applies.
                if ((ve is ZuiMicroSlider || ve is ZuiMicroMinMax) && ve.resolvedStyle.flexGrow > 0f
                    && !ve.ClassListContains("zui-audit-allow-stretch"))
                    findings.Add(New("stretch", ve, $"MicroSlider resolved flex-grow {ve.resolvedStyle.flexGrow:0.##}"));

                // A ZuiToggleButton is a Button, so it carries neither "unity-base-field" nor a flex-grow —
                // it stretches across the CROSS axis instead, which the check above cannot see. That blind
                // spot is why every Z.Toggle sitting in a column body silently filled the whole pane while
                // this audit reported clean. Catch the cross-axis case explicitly.
                // align-self:auto resolves to Auto, NOT to the parent's value, so testing for Stretch alone would
                // never fire — inheriting the parent's align-items IS the failing case. Test both, and confirm
                // against the laid-out geometry so the finding can't disagree with what is on screen.
                if (ve.ClassListContains("zui-togglebutton") && !ve.ClassListContains("zui-audit-allow-stretch")
                    && ve.hierarchy.parent is { } tbParent
                    && tbParent.resolvedStyle.flexDirection is FlexDirection.Column or FlexDirection.ColumnReverse)
                {
                    var self = ve.resolvedStyle.alignSelf;
                    bool stretches = self == Align.Stretch
                                     || (self == Align.Auto && tbParent.resolvedStyle.alignItems == Align.Stretch);
                    if (stretches && ve.worldBound.width > tbParent.worldBound.width - 4f)
                        findings.Add(New("stretch", ve,
                            $"toggle fills its column ({ve.worldBound.width:0}px) — needs align-self, not a hard width"));
                }

                // Foldouts are exempt as CONTAINERS — a foldout legitimately spans its content. But a
                // PropertyField renders AS a Foldout whenever the property is a list or a nested class, and
                // that is a control, not a container: unbounded, it stretches the whole window and strands
                // its size field at the far edge (caught by eye in the Chunks sprite list, which this check
                // had passed). So exempt a bare Foldout, never a PropertyField.
                float w = ve.worldBound.width;
                if ((ve is not Foldout || ve is PropertyField) && w > OverWidthCap)
                    findings.Add(New("over-width", ve, $"{w:0}px wide (cap {OverWidthCap})"));
            }

            // A text/numeric INPUT printing more than it can show. This was a blind spot that hid a real
            // defect for fourteen passes (T-0324): the usual "does this string fit its box" test compares a
            // TextElement's needed width against its OWN content box, and a UITK text field's inner text
            // element GROWS to its text — so it always fits itself and no check ever fires, while the input
            // box around it clips the overflow. Compare against the INPUT's box, which is what can be read.
            // The input element is neither a BaseField nor anything in InteractiveTypes, so this sits
            // outside the interactive block on purpose.
            if (ve.ClassListContains("unity-base-text-field__input"))
            {
                var te = ve.Q<TextElement>();
                if (te != null && !string.IsNullOrEmpty(te.text))
                {
                    float need = te.MeasureTextSize(te.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).x;
                    float have = ve.contentRect.width;
                    if (!float.IsNaN(need) && !float.IsNaN(have) && have > 0f && need > have + 1.5f)
                        findings.Add(New("clipped-input", ve,
                            $"\"{Truncate(te.text)}\" needs {need:0.#}px, the field shows {have:0.#}px"));
                }
            }

            if ((interactive || ve is Label) && ve.worldBound.width > 0f && ve.worldBound.xMax > windowRight
                && !InsideScroller(ve))
                findings.Add(New("off-screen", ve, $"right edge {ve.worldBound.xMax:0} past window {windowRight:0}"));

            foreach (var child in ve.Children())
                Walk(child, root, windowRight, findings, ref foldedSkipped);
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
