using System;
using System.Collections.Generic;
using Laubrary.Launimator;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Launimator.Editor
{
    /// <summary>
    /// Top-area "Reel Animation browser" for the Animation Builder. When bound to a reel, the top of
    /// the window splits: left = the existing Sheet section (UI.1), right = a compact list of that reel's
    /// draft animations, each with a quick Edit button that switches the builder to it. When editing a
    /// standalone orphan (or authoring one with orphans on disk), the right side lists all orphaned animations
    /// instead. Kept in its own partial so the core window file stays focused.
    /// </summary>
    public partial class AnimationBuilderWindow
    {
        private Vector2 _browserScroll;
        private bool _leftCollapsed; // hide the Sheet + canvas (identify-sprites) area for more room on #4/#5 + the list

        /// <summary>A one-line toggle to collapse the sheet + canvas area (more room for the sprites/animation/list).</summary>
        private void DrawCollapseToggleRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (Button(_leftCollapsed
                        ? "▶ Show sheet & canvas"
                        : "◀ Hide sheet & canvas — more room for sprites, animation & the list",
                        ZUI.Style.Default, GUILayout.Width(360)))
                    _leftCollapsed = !_leftCollapsed;
                GUILayout.FlexibleSpace();
            }
        }

        /// <summary>The top row: Sheet section alone, or Sheet + a reel/orphan animation browser. When the
        /// sheet/canvas is collapsed, the Sheet/canvas fold away and only the animation quicklist remains.</summary>
        private void DrawTopArea()
        {
            bool charMode = _boundReel != null;
            List<AnimationAsset> orphans = charMode ? null : AnimationLibrary.Enumerate();
            bool showBrowser = charMode || _orphanAsset != null || (orphans != null && orphans.Count > 0);

            if (_leftCollapsed)
            {
                // Sheet + canvas (UI.1/2/3) are fully folded away; show only the animation quicklist.
                if (charMode) DrawReelAnimBrowser();
                else if (showBrowser) DrawOrphanBrowser(orphans);
                return;
            }

            if (!showBrowser) { DrawSheetSection(); return; }

            using (new EditorGUILayout.HorizontalScope())
            {
                float leftW = Mathf.Max(420f, position.width * 0.58f);
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(leftW)))
                    DrawSheetSection();
                using (new EditorGUILayout.VerticalScope())
                {
                    if (charMode) DrawReelAnimBrowser();
                    else DrawOrphanBrowser(orphans);
                }
            }
        }

        /// <summary>Lay out animations as wrapping horizontal "chips" (name buttons) to save vertical space —
        /// screens are wider than tall. The current one is highlighted; clicking a chip switches to it.</summary>
        private void DrawAnimChips(List<(string label, string tip, bool current)> items, Action<int> onClick)
        {
            if (items.Count == 0) { EditorGUILayout.LabelField("None yet.", EditorStyles.miniLabel); return; }

            // Available width = this browser column: full window when collapsed, else the right part of the split.
            float leftW = _leftCollapsed ? 0f : Mathf.Max(420f, position.width * 0.58f);
            float avail = Mathf.Max(140f, position.width - leftW - 44f);
            var style = EditorStyles.miniButton;

            _browserScroll = EditorGUILayout.BeginScrollView(_browserScroll, GUILayout.Height(52));
            float x = 0f; bool rowOpen = false;
            for (int i = 0; i < items.Count; i++)
            {
                var c = new GUIContent(items[i].label, items[i].tip);
                float w = Mathf.Clamp(style.CalcSize(c).x + 6f, 44f, 240f);
                if (rowOpen && x + w + 3f > avail) { EditorGUILayout.EndHorizontal(); rowOpen = false; }
                if (!rowOpen) { EditorGUILayout.BeginHorizontal(); rowOpen = true; x = 0f; }
                var prev = GUI.backgroundColor;
                if (items[i].current) GUI.backgroundColor = new Color(0.40f, 0.60f, 1f);
                if (GUILayout.Button(c, style, GUILayout.Width(w), GUILayout.Height(20))) onClick(i);
                GUI.backgroundColor = prev;
                x += w + 3f;
            }
            if (rowOpen) EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();
        }

        private void DrawReelAnimBrowser()
        {
            var draft = ReelRepo.EnsureDraft(_boundReel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"▸ {_boundReel.reelName} — animations ({draft.animations.Count})", EditorStyles.miniBoldLabel);
                var names = draft.animations.ConvertAll(a => a.name); // snapshot — switching rebuilds the draft
                var items = new List<(string, string, bool)>();
                foreach (var n in names)
                {
                    var d = ReelRepo.GetDraftAnimation(_boundReel, n);
                    bool cur = NameEq(n, _boundAnimName);
                    string tip = d != null ? $"{d.recipe?.Count ?? 0}f @ {d.fps:0}fps — click to edit" : "click to edit";
                    items.Add(((cur ? "● " : "") + n, tip, cur));
                }
                DrawAnimChips(items, i => { SwitchToReelAnimation(names[i]); GUIUtility.ExitGUI(); });
            }
        }

        private void DrawOrphanBrowser(List<AnimationAsset> orphans)
        {
            var valid = new List<AnimationAsset>();
            foreach (var a in orphans) if (a != null && a.animation != null) valid.Add(a);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"▸ Orphaned animations ({valid.Count})", EditorStyles.miniBoldLabel);
                var items = new List<(string, string, bool)>();
                foreach (var a in valid)
                {
                    bool cur = _orphanAsset == a;
                    items.Add(((cur ? "● " : "") + a.animation.name, $"{a.animation.recipe?.Count ?? 0}f — click to edit", cur));
                }
                DrawAnimChips(items, i => { SwitchToOrphan(valid[i]); GUIUtility.ExitGUI(); });
            }
        }

        /// <summary>Switch the builder to another animation of the bound reel (loads its saved state).
        /// Does NOT auto-save the current animation — use Save first if you have unsaved edits.</summary>
        private void SwitchToReelAnimation(string name)
        {
            if (_boundReel == null) return;
            var def = ReelRepo.GetDraftAnimation(_boundReel, name);
            if (def == null) { _status = $"'{name}' is no longer in the draft."; return; }
            _boundAnimName = name;
            _orphanAsset = null;
            LoadAnimationIntoSequence(def);
        }

        private void SwitchToOrphan(AnimationAsset a)
        {
            if (a == null || a.animation == null) return;
            _orphanAsset = a;
            _boundReel = null; _boundAnimName = null;
            LoadAnimationIntoSequence(a.animation);
        }

        private static bool NameEq(string a, string b)
            => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
