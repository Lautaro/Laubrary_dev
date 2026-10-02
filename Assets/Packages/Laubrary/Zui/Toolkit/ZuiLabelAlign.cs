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
            scope.Query<Label>(className: "zui-field__label").ForEach(l =>
            {
                if (l.ClassListContains("zui-component-label") || NearestScope(l) != scope) return;
                mine.Add(l);
                float w = l.MeasureTextSize(l.text ?? string.Empty, 0f, VisualElement.MeasureMode.Undefined,
                                            0f, VisualElement.MeasureMode.Undefined).x;
                if (!float.IsNaN(w) && w > max) max = w;
            });
            if (mine.Count == 0) return;

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

        // Walk up from a label to the first element carrying `zui-align-scope`.
        static VisualElement NearestScope(VisualElement label)
        {
            for (var p = label.parent; p != null; p = p.parent)
                if (p.ClassListContains("zui-align-scope")) return p;
            return null;
        }
    }
}
