// ZuiLabelAlign — makes every field label within a scope share the width of the WIDEST one, so the
// controls beside them line up in a clean column instead of starting at ragged x-positions. This is
// the UI-Toolkit counterpart of the old IMGUI ZUIForm auto-label-width; readability, applied once per
// container. Labels are only ever as wide as the widest actually needs — nothing is padded to a fixed
// guess.
//
// Scope & nesting: a scope marks itself with `zui-align-scope`. A label belongs to the NEAREST such
// ancestor, so a label inside a nested box aligns to that box's column, not the outer section's — each
// container gets its own tidy label column and they never fight over one width. ZuiSection and ZuiBox
// call Align(this) in their constructors; consumers add rows afterward and the GeometryChangedEvent
// re-measures as content appears.
//
// Loop safety: setting a label's width fires another GeometryChangedEvent, but MeasureTextSize measures
// the TEXT (independent of the width we set), so the max is stable and the guard (compare to the last
// applied max) makes the second pass a no-op.
//
// A label belongs to a COLUMN only when nothing sits to its left on the same line. Padding a label that
// is packed mid-row buys no alignment — there is no column there — and the padding simply shoves the
// control away from the label it belongs to, so the eye reads it as belonging to whatever came before.
// Such labels are therefore left at their natural width and excluded from the widest-label measurement.
// The test is over LAID-OUT GEOMETRY, never `resolvedStyle.flexDirection`: an inline managed reference's
// children report a Column parent while visibly laying out in a row, so the style cannot be trusted and
// the geometry cannot disagree with what is on screen.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public static class ZuiLabelAlign
    {
        const float ScopeClassPad = 2f;   // a hair of breathing room past the widest glyphs
        static readonly Dictionary<VisualElement, float> s_lastMax = new Dictionary<VisualElement, float>();

        /// Align every `.zui-field__label` whose nearest align-scope ancestor is `scope` to the widest
        /// among them. Idempotent; safe to call in a container's constructor before its body exists.
        public static void Align(VisualElement scope)
        {
            if (scope == null) return;
            scope.AddToClassList("zui-align-scope");
            scope.RegisterCallback<GeometryChangedEvent>(_ => Apply(scope));
        }

        static void Apply(VisualElement scope)
        {
            if (scope.panel == null) return;

            float max = 0f;
            var mine = new List<Label>();
            var packed = new List<Label>();
            scope.Query<Label>(className: "zui-field__label").ForEach(l =>
            {
                if (NearestScope(l) != scope) return;   // belongs to a nested scope — that one handles it
                if (HasSomethingToItsLeft(l, scope)) { packed.Add(l); return; }
                mine.Add(l);
                float w = l.MeasureTextSize(l.text ?? string.Empty, 0f, VisualElement.MeasureMode.Undefined,
                                            0f, VisualElement.MeasureMode.Undefined).x;
                if (!float.IsNaN(w) && w > max) max = w;
            });

            // A label that was in a column and is now packed mid-row (or the reverse) must lose the width a
            // previous pass gave it, so the release is unconditional and runs before the early-out below.
            foreach (var l in packed)
            {
                l.style.width = StyleKeyword.Auto;
                l.style.minWidth = StyleKeyword.Auto;
            }
            if (mine.Count == 0) { s_lastMax.Remove(scope); return; }

            float target = Mathf.Ceil(max) + ScopeClassPad;
            if (s_lastMax.TryGetValue(scope, out float prev) && Mathf.Abs(prev - target) < 0.5f) return;
            s_lastMax[scope] = target;

            foreach (var l in mine)
            {
                l.style.width = target;
                l.style.minWidth = target;
                l.style.flexShrink = 0f;
            }
        }

        /// Is anything laid out to the LEFT of this label on the same line, inside the same scope? Walks the
        /// ancestor chain up to the scope and looks only at each level's EARLIER hierarchy siblings, which is
        /// the whole of "what came before me on this row" and costs depth × siblings rather than a full scan.
        static bool HasSomethingToItsLeft(Label label, VisualElement scope)
        {
            var lb = label.worldBound;
            if (float.IsNaN(lb.x) || lb.height <= 0f) return false;   // not laid out yet — decide next pass

            for (var node = (VisualElement)label; node != null && node != scope; node = node.hierarchy.parent)
            {
                var parent = node.hierarchy.parent;
                if (parent == null) break;
                for (int i = 0; i < parent.hierarchy.childCount; i++)
                {
                    var sib = parent.hierarchy[i];
                    if (sib == node) break;              // only what precedes me in this parent
                    var sb = sib.worldBound;
                    if (sib.resolvedStyle.display == DisplayStyle.None) continue;
                    if (float.IsNaN(sb.x) || sb.width <= 1f || sb.height <= 0f) continue;
                    if (sb.xMax > lb.x + 0.5f) continue;                  // not to my left
                    if (sb.yMax <= lb.y + 2f || sb.y >= lb.yMax - 2f) continue;   // not on my line
                    return true;
                }
            }
            return false;
        }

        // Walk up from a label to the first element carrying `zui-align-scope`.
        static VisualElement NearestScope(VisualElement label)
        {
            for (var p = label.parent; p != null; p = p.parent)
                if (p.ClassListContains("zui-align-scope")) return p;
            return null;
        }
    }
}
