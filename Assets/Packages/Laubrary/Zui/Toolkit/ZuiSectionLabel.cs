// ZuiSectionLabel — the heading that Z.Text(.., ZuiText.Section, ..) returns.
//
// To a call site it is still just a Label, so no tool had to change. What it adds is the behaviour a
// heading should have had all along: clicking it folds away the controls it names.
//
// The overwhelmingly common shape in these windows is a heading followed by a run of sibling controls
// with no container around them:
//
//     root.Add(Z.Text("Layers", ZuiText.Section, ".."));
//     root.Add(theList);
//     root.Add(theButtons);
//     root.Add(Z.Text("Blast", ZuiText.Section, ".."));      <- the next block starts here
//
// There is no element that owns "the Layers block", so the heading works out its own extent: on click
// it hides every following sibling up to the next section heading. ZuiSection remains the explicit,
// structural form (it owns a real body) and is what to reach for in new code — this exists so the
// forty-odd headings already written across Pyre, Choreographer, Launimator and Rulesets all became
// collapsible at once, rather than each needing its block re-parented by hand.
//
// A heading sitting in a ROW is left alone: its siblings are the rest of the row, not the block below
// it, so folding them would hide unrelated controls.
//
// Fold state is static and keyed by title+tooltip, so it survives the window rebuilds that every dial
// edit triggers. Title alone is not enough — "Gradient" heads three different blocks in Pyre.
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiSectionLabel : Label
    {
        static readonly Dictionary<string, bool> s_open = new();

        readonly string _title;
        readonly string _key;
        // What this heading hid, and the display each element had before — restoring the recorded
        // value (rather than blanket-setting Flex) keeps a control the tool itself had hidden hidden.
        readonly List<(VisualElement el, StyleEnum<DisplayStyle> prev)> _hidden = new();

        public bool IsOpen
        {
            get => !s_open.TryGetValue(_key, out bool open) || open;   // default: open
            set { s_open[_key] = value; Apply(); }
        }

        public ZuiSectionLabel(string title, string tooltip)
        {
            _title = title ?? string.Empty;
            _key = _title + "" + (tooltip ?? string.Empty);
            text = _title;
            if (!string.IsNullOrEmpty(tooltip)) this.tooltip = tooltip;
            AddToClassList("zui-text--section");

            // Clickable, not a raw PointerDownEvent: a bare RegisterCallback<PointerDownEvent> did not
            // fire for headings inside a ScrollView (verified live 2026-07-23 — the fold machinery
            // worked when driven directly but no real click ever reached it). Clickable owns the
            // down/up pair and the capture between them, exactly as Button does.
            this.AddManipulator(new Clickable(() => { if (Foldable) IsOpen = !IsOpen; }));

            // Siblings do not exist yet while the panel is still being built, so the first Apply has
            // to wait for the build to finish. This is also what restores a fold after a rebuild.
            schedule.Execute(Apply);
        }

        bool Foldable => parent != null && parent.resolvedStyle.flexDirection != FlexDirection.Row;

        void Apply()
        {
            if (!Foldable) { text = _title; return; }

            bool open = IsOpen;
            text = (open ? "▾ " : "▸ ") + _title;

            if (open)
            {
                foreach (var (el, prev) in _hidden) el.style.display = prev;
                _hidden.Clear();
                return;
            }

            if (_hidden.Count > 0) return;   // already folded — don't re-record hidden-as-previous

            var p = parent;
            int i = p.hierarchy.IndexOf(this);
            for (int k = i + 1; k < p.hierarchy.childCount; k++)
            {
                var el = p.hierarchy.ElementAt(k);
                if (el is ZuiSectionLabel || el is ZuiSection) break;   // the next block starts here
                _hidden.Add((el, el.style.display));
                el.style.display = DisplayStyle.None;
            }
        }
    }
}
