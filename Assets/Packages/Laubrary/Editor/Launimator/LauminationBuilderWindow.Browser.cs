using System;
using System.Collections.Generic;
using Laubrary.Launimator;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Launimator.Editor
{
    /// <summary>
    /// Top-area "Lauminary Animation browser" for the Laumination Builder. When bound to a lauminary, the top of
    /// the window splits: left = the existing Sheet section (UI.1), right = a compact list of that lauminary's
    /// draft animations, each with a quick Edit button that switches the builder to it. When editing a
    /// standalone orphan (or authoring one with orphans on disk), the right side lists all orphaned animations
    /// instead. Kept in its own partial so the core window file stays focused.
    ///
    /// UI TOOLKIT PORT: fully native. The hand-computed chip-wrapping budget is gone — a flex-wrap row does it.
    /// </summary>
    public partial class LauminationBuilderWindow
    {
        private bool _leftCollapsed; // hide the Sheet + canvas (identify-sprites) area for more room on #4/#5 + the list

        /// <summary>The collapse toggle, the Sheet section, and the lauminary/orphan animation quicklist. When the
        /// sheet/canvas is collapsed, the Sheet/canvas fold away and only the animation quicklist remains.</summary>
        private void BuildTopSection(VisualElement root)
        {
            // Short label; the "why" lives in the tooltip (ui-layout-rules: a title names, it doesn't explain).
            root.Add(Z.Row(Z.Button(
                _leftCollapsed ? "▶ Show sheet & canvas" : "◀ Hide sheet & canvas",
                "Fold the sheet/canvas half away so the sprite palette, the animation and the quicklist get the whole window.",
                () => { _leftCollapsed = !_leftCollapsed; Rebuild(); }).W(220f)));

            bool charMode = _boundLauminary != null;
            List<AnimationAsset> orphans = charMode ? null : AnimationLibrary.Enumerate();
            bool showBrowser = charMode || _orphanAsset != null || (orphans != null && orphans.Count > 0);

            if (_leftCollapsed)
            {
                // Sheet + canvas (UI.1/2/3) are fully folded away; show only the animation quicklist.
                if (charMode) BuildLauminaryAnimBrowser(root);
                else if (showBrowser) BuildOrphanBrowser(root, orphans);
                return;
            }

            if (!showBrowser) { BuildSheetSection(root); return; }

            var row = new VisualElement();
            row.AddToClassList("lau-tool-shell__row-wrap");

            var left = new VisualElement();
            left.AddToClassList("lau-animation-builder__sheet-controls");
            BuildSheetSection(left);
            row.Add(left);

            var right = new VisualElement();
            right.AddToClassList("lau-tool-shell__tools");
            if (charMode) BuildLauminaryAnimBrowser(right);
            else BuildOrphanBrowser(right, orphans);
            row.Add(right);

            root.Add(row);
        }

        /// <summary>Lay animations out as wrapping horizontal "chips" (name buttons) to save vertical space —
        /// screens are wider than tall. The current one is highlighted; clicking a chip switches to it.</summary>
        private void BuildAnimChips(VisualElement root, List<(string label, string tip, bool current)> items, Action<int> onClick)
        {
            if (items.Count == 0)
            {
                root.Add(Z.Text("None yet.", ZuiText.Small, "No animations to switch between."));
                return;
            }

            // flex-wrap replaces the old hand-computed per-row width budget.
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("lau-animation-builder__animation-list");
            var wrap = scroll.contentContainer;
            wrap.AddToClassList("lau-tool-shell__row-wrap");

            for (int i = 0; i < items.Count; i++)
            {
                int idx = i;
                var b = Z.Button(items[i].label, items[i].tip, () => onClick(idx));
                b.AddToClassList("lau-animation-builder__animation-choice");
                if (items[i].current) b.AddToClassList("zui-radio__on");
                wrap.Add(b);
            }
            root.Add(scroll);
        }

        private void BuildLauminaryAnimBrowser(VisualElement root)
        {
            var draft = LauminaryRepo.EnsureDraft(_boundLauminary);
            var box = Z.Box($"{_boundLauminary.lauminaryName} — animations ({draft.animations.Count})",
                "Every animation on this lauminary's draft — click one to switch the builder to it (unsaved edits are NOT auto-saved).");
            var names = draft.animations.ConvertAll(a => a.name); // snapshot — switching rebuilds the draft
            var items = new List<(string, string, bool)>();
            foreach (var n in names)
            {
                var d = LauminaryRepo.GetDraftAnimation(_boundLauminary, n);
                bool cur = NameEq(n, _boundAnimName);
                string tip = d != null ? $"{d.recipe?.Count ?? 0}f @ {d.fps:0}fps — click to edit" : "click to edit";
                items.Add(((cur ? "● " : "") + n, tip, cur));
            }
            BuildAnimChips(box, items, i => SwitchToLauminaryAnimation(names[i]));
            root.Add(box);
        }

        private void BuildOrphanBrowser(VisualElement root, List<AnimationAsset> orphans)
        {
            var valid = new List<AnimationAsset>();
            foreach (var a in orphans) if (a != null && a.animation != null) valid.Add(a);
            var box = Z.Box($"Orphaned animations ({valid.Count})",
                "Standalone animations on disk — click one to switch the builder to it.");
            var items = new List<(string, string, bool)>();
            foreach (var a in valid)
            {
                bool cur = _orphanAsset == a;
                items.Add(((cur ? "● " : "") + a.animation.name, $"{a.animation.recipe?.Count ?? 0}f — click to edit", cur));
            }
            BuildAnimChips(box, items, i => SwitchToOrphan(valid[i]));
            root.Add(box);
        }

        /// <summary>Switch the builder to another animation of the bound lauminary (loads its saved state).
        /// Does NOT auto-save the current animation — use Save first if you have unsaved edits.</summary>
        private void SwitchToLauminaryAnimation(string name)
        {
            if (_boundLauminary == null) return;
            var def = LauminaryRepo.GetDraftAnimation(_boundLauminary, name);
            if (def == null) { SetStatus($"'{name}' is no longer in the draft."); return; }
            _boundAnimName = name;
            _orphanAsset = null;
            LoadAnimationIntoSequence(def);
        }

        private void SwitchToOrphan(AnimationAsset a)
        {
            if (a == null || a.animation == null) return;
            _orphanAsset = a;
            _boundLauminary = null; _boundAnimName = null;
            LoadAnimationIntoSequence(a.animation);
        }

        private static bool NameEq(string a, string b)
            => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
