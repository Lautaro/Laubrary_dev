using System.Collections.Generic;
using System.Linq;
using Laubrary.Zoetrope;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// The zoe browser. Lists every zoe under <see cref="ZoeRepo.Root"/>, lets you pick a
    /// version (the editable <c>draft</c> or an immutable snapshot <c>v1..vN</c>), browse and preview that
    /// version's animations, and manage zoe lifecycle (New / Duplicate / Rename / Delete) plus commit
    /// the draft into a new immutable version. This is the workflow's starting point: create or pick a
    /// zoe, add a named animation, then <b>Edit</b> it in the Animation Builder (which saves back here).
    /// It also lists standalone "orphaned" animations and can include them into a zoe's draft.
    /// </summary>
    public class ZoeBrowserWindow : EditorWindow
    {
        private List<Zoe> _zoes = new List<Zoe>();
        private List<AnimationAsset> _orphans = new List<AnimationAsset>();
        private Zoe _selected;
        private int _versionSel;            // 0 = draft, else committed version number
        private int _animSel = -1;

        private string _newName = "Hero";
        private string _renameBuffer = "";
        private string _newAnimName = "Idle";
        private string _newOrphanName = "Clip";

        private int _renamingAnim = -1;      // index of the draft animation being inline-renamed, else -1
        private string _animRenameBuffer = "";
        private int _renamingOrphan = -1;    // index of the orphaned animation being inline-renamed, else -1
        private string _orphanRenameBuffer = "";
        private bool _showingOrphans;        // the "Orphaned" pseudo-entry is selected (detail shows orphans)
        private Zoe _lastZoe;       // last real zoe selected (default include target)
        private Zoe _orphanIncludeTarget; // zoe a row's → includes into, while browsing orphans

        // preview — driven by the shared AnimationPlayback (the ONE player), same as the game & Animation Builder
        private bool _playing = true;
        private double _lastStep;
        private AnimationDef _previewing;
        private readonly AnimationPlayback _pb = new AnimationPlayback();
        private Texture2D _orphanPreviewTex; // owned in-memory atlas for the previewed orphan (orphans aren't baked)

        private Vector2 _charScroll, _animScroll;

        [MenuItem("Tools/Laubrary/Zoetrope/Zoe Browser")]
        public static void Open()
        {
            var w = GetWindow<ZoeBrowserWindow>("Zoe Browser");
            w.minSize = new Vector2(640, 520);
            w.Refresh();
            w.Show();
        }

        private void OnEnable() { _lastStep = EditorApplication.timeSinceStartup; EditorApplication.update += Tick; Refresh(); }
        private void OnDisable() { EditorApplication.update -= Tick; DestroyOrphanPreview(); }
        private void OnFocus() { Refresh(); Repaint(); }

        /// <summary>Re-read zoes from disk and repaint. Called by the Animation Builder after a save so
        /// this browser reflects the new/updated animation immediately.</summary>
        public void ExternalRefresh()
        {
            // A save in the Animation Builder rebuilds the draft and REPLACES the AnimationDef instances, so the
            // preview's cached reference goes stale. Re-bind it by name to the freshly rebuilt animation.
            string previewedName = _previewing != null ? _previewing.name : null;
            Refresh();
            RebindPreview(previewedName);
            Repaint();
        }

        private void RebindPreview(string name)
        {
            _previewing = null;
            if (_selected == null) { _animSel = -1; return; }
            var version = ZoeRepo.LoadVersion(_selected, _versionSel);
            if (version == null || version.animations.Count == 0) { _animSel = -1; return; }
            int idx = name != null
                ? version.animations.FindIndex(a => string.Equals(a.name, name, System.StringComparison.OrdinalIgnoreCase))
                : -1;
            if (idx < 0) idx = Mathf.Clamp(_animSel, 0, version.animations.Count - 1);
            _animSel = idx;
            _previewing = version.animations[idx];
        }

        private void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - _lastStep);
            _lastStep = now;
            if (_previewing == null || _previewing.frames.Count == 0) return;

            if (_pb.Anim != _previewing) { _pb.Play(_previewing, true); Repaint(); }
            if (_playing) { _pb.Tick(dt, 1f); Repaint(); }
        }

        private void Refresh()
        {
            _zoes = ZoeRepo.EnumerateZoes();
            _orphans = AnimationLibrary.Enumerate();
            if (_selected != null && !_zoes.Contains(_selected)) _selected = null;
        }

        /// <summary>Select a zoe, jump to its latest version, and auto-play its first animation.</summary>
        private void SelectZoe(Zoe c)
        {
            _showingOrphans = false;
            DestroyOrphanPreview();
            _selected = c;
            if (c != null) _lastZoe = c;
            _renameBuffer = c != null ? c.zoeName : "";
            _versionSel = c != null && c.latestVersion > 0 ? c.latestVersion : 0;
            AutoPlayFirstAnimation();
        }

        /// <summary>Select the "Orphaned" pseudo-entry: the detail panel lists all orphaned animations using the
        /// same row UI as a zoe's animations.</summary>
        private void SelectOrphaned()
        {
            _showingOrphans = true;
            _selected = null;
            _renamingAnim = -1; _renamingOrphan = -1;
            _animSel = -1; _previewing = null;
            DestroyOrphanPreview();
            if (_orphanIncludeTarget == null || !_zoes.Contains(_orphanIncludeTarget))
                _orphanIncludeTarget = _lastZoe != null && _zoes.Contains(_lastZoe)
                    ? _lastZoe
                    : (_zoes.Count > 0 ? _zoes[0] : null);
        }

        private void AutoPlayFirstAnimation()
        {
            _renamingAnim = -1;
            _animSel = -1; _previewing = null;
            if (_selected == null) return;
            var version = ZoeRepo.LoadVersion(_selected, _versionSel);
            if (version != null && version.animations.Count > 0)
            {
                _animSel = 0;
                _previewing = version.animations[0];
            }
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(64))) Refresh();
                GUILayout.FlexibleSpace();
                _newName = EditorGUILayout.TextField(_newName, EditorStyles.toolbarTextField, GUILayout.Width(140));
                if (GUILayout.Button("New zoe", EditorStyles.toolbarButton, GUILayout.Width(100)))
                {
                    var created = ZoeRepo.CreateZoe(_newName);
                    Refresh();
                    SelectZoe(created);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawZoeList();
                DrawZoeDetail();
            }
        }

        // ── left: zoe list + lifecycle ─────────────────────────────────
        private void DrawZoeList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(240)))
            {
                GUILayout.Label("Zoes", EditorStyles.boldLabel);
                _charScroll = EditorGUILayout.BeginScrollView(_charScroll, "box", GUILayout.Height(170));
                if (_zoes.Count == 0)
                    GUILayout.Label("None yet. Click 'New zoe', or author an animation in the Animation Builder.",
                        EditorStyles.wordWrappedMiniLabel);
                var rowStyle = new GUIStyle(EditorStyles.miniButton) { alignment = TextAnchor.MiddleLeft };

                // "Orphaned" pseudo-entry — selecting it lists all orphaned animations in the detail panel,
                // using the same row UI as zoe animations (so there's one browser, not two).
                {
                    var bg = GUI.backgroundColor;
                    if (_showingOrphans) GUI.backgroundColor = new Color(0.40f, 0.60f, 1f);
                    if (GUILayout.Button($"Orphaned   ({_orphans.Count})", rowStyle))
                        SelectOrphaned();
                    GUI.backgroundColor = bg;
                }

                foreach (var c in _zoes)
                {
                    var bg = GUI.backgroundColor;
                    if (!_showingOrphans && c == _selected) GUI.backgroundColor = new Color(0.40f, 0.60f, 1f);
                    if (GUILayout.Button($"{c.zoeName}   (latest v{c.latestVersion})", rowStyle))
                        SelectZoe(c);
                    GUI.backgroundColor = bg;
                }
                EditorGUILayout.EndScrollView();

                using (new EditorGUI.DisabledScope(_selected == null))
                {
                    if (GUILayout.Button("Duplicate"))
                    {
                        var dup = ZoeRepo.Duplicate(_selected, _selected.zoeName + " Copy");
                        Refresh(); _selected = dup; _versionSel = 0; _animSel = -1;
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _renameBuffer = EditorGUILayout.TextField(_renameBuffer);
                        if (GUILayout.Button("Rename", GUILayout.Width(64)) && _selected != null)
                        { ZoeRepo.Rename(_selected, _renameBuffer); Refresh(); }
                    }
                    if (GUILayout.Button("Delete…") && _selected != null)
                    {
                        if (EditorUtility.DisplayDialog("Delete zoe",
                            $"Delete '{_selected.zoeName}' and ALL its versions (draft + v1..v{_selected.latestVersion})?\n\n" +
                            "This cannot be undone.", "Delete", "Cancel"))
                        {
                            ZoeRepo.Delete(_selected);
                            _selected = null; _animSel = -1; _previewing = null; Refresh();
                        }
                    }
                }
            }
        }

        private void IncludeOrphanInto(AnimationAsset o, Zoe c)
        {
            if (o == null || c == null) return;
            ZoeRepo.SaveAnimationToDraft(c, ZoeRepo.CopyAnimation(o.animation));
            Refresh();
            GUIUtility.ExitGUI();
        }

        /// <summary>Preview an orphaned animation: orphans store only a recipe (not baked frames), so bake the
        /// recipe IN MEMORY (same path as zoes/the game) into a throwaway def the shared player can run.
        /// The owned atlas texture is freed when the next one is selected or the window closes.</summary>
        private void SelectOrphanForPreview(int i, AnimationAsset o)
        {
            _animSel = i;
            DestroyOrphanPreview();

            var def = o.animation;
            if (def.recipe != null && def.recipe.Count > 0)
            {
                var key = new RegionSlicer.ColorKey { enabled = def.bgKeyEnabled, color = def.bgKey, tolerance = def.bgKeyTolerance };
                var box = new AtlasBaker.FrameBox { fixedSize = def.fixedFrame, w = def.frameWidth, h = def.frameHeight, pivot = def.framePivot };
                var frames = AtlasBaker.BakeInMemory(def.recipe, 16f, out string err, out Texture2D atlas, key, box);
                if (frames != null)
                {
                    _orphanPreviewTex = atlas;
                    // A throwaway def carrying just what the player/preview reads — never saved, never mutates the asset.
                    _previewing = new AnimationDef { name = def.name, fps = def.fps, frames = frames };
                    return;
                }
            }
            // Empty recipe or bake failure: select it but there's nothing to play.
            _previewing = new AnimationDef { name = def.name, fps = def.fps };
        }

        private void DestroyOrphanPreview()
        {
            if (_orphanPreviewTex != null) { DestroyImmediate(_orphanPreviewTex); _orphanPreviewTex = null; }
        }

        private void SetOrphanFps(AnimationAsset o, float fps)
        {
            if (o == null || o.animation == null) return;
            fps = Mathf.Clamp(fps, 0.1f, 120f);
            if (Mathf.Approximately(o.animation.fps, fps)) return;
            o.animation.fps = fps;
            EditorUtility.SetDirty(o);
            AssetDatabase.SaveAssets();
            Refresh();
            GUIUtility.ExitGUI();
        }

        // Standalone animations not in any zoe — shown in the SAME detail panel as a zoe's animations.
        // Author one with "New animation", Edit opens it in the Animation Builder, → includes it into a zoe.
        private void DrawOrphanDetail()
        {
            EditorGUILayout.LabelField("Orphaned animations", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                _newOrphanName = EditorGUILayout.TextField(_newOrphanName);
                if (GUILayout.Button("New animation", GUILayout.Width(110)))
                {
                    var a = AnimationLibrary.Create(_newOrphanName);
                    Refresh();
                    AnimationBuilderWindow.OpenForOrphan(a);
                }
            }

            if (_zoes.Count > 0)
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(new GUIContent("Include into", "The → on each row adds that orphan into this zoe's draft."), GUILayout.Width(80));
                    int ti = Mathf.Max(0, _zoes.IndexOf(_orphanIncludeTarget));
                    int nti = EditorGUILayout.Popup(ti, _zoes.Select(c => c.zoeName).ToArray(), GUILayout.Width(160));
                    _orphanIncludeTarget = _zoes[Mathf.Clamp(nti, 0, _zoes.Count - 1)];
                    GUILayout.FlexibleSpace();
                }

            EditorGUILayout.LabelField($"Animations ({_orphans.Count})", EditorStyles.boldLabel);
            _animScroll = EditorGUILayout.BeginScrollView(_animScroll, GUILayout.Height(160));
            if (_orphans.Count == 0)
                EditorGUILayout.LabelField("None yet. 'New animation' authors a standalone one.", EditorStyles.miniLabel);

            for (int i = 0; i < _orphans.Count; i++)
            {
                var o = _orphans[i];
                var def = o.animation;
                using (new EditorGUILayout.HorizontalScope(i == _animSel ? EditorStyles.helpBox : GUIStyle.none))
                {
                    if (_renamingOrphan == i)
                    {
                        _orphanRenameBuffer = EditorGUILayout.TextField(_orphanRenameBuffer, GUILayout.Width(160));
                        if (GUILayout.Button("OK", GUILayout.Width(36))) CommitOrphanRename(o);
                        if (GUILayout.Button("Cancel", GUILayout.Width(56))) _renamingOrphan = -1;
                        GUILayout.FlexibleSpace();
                    }
                    else
                    {
                        if (GUILayout.Button($"{def.name}", EditorStyles.label, GUILayout.ExpandWidth(true)))
                            SelectOrphanForPreview(i, o);
                        int fcount = def.frames.Count > 0 ? def.frames.Count : (def.recipe?.Count ?? 0);
                        GUILayout.Label($"{fcount}f", EditorStyles.miniLabel, GUILayout.Width(28));
                        EditorGUI.BeginChangeCheck();
                        float nf = EditorGUILayout.DelayedFloatField(def.fps, GUILayout.Width(42));
                        GUILayout.Label("fps", EditorStyles.miniLabel, GUILayout.Width(22));
                        if (EditorGUI.EndChangeCheck()) SetOrphanFps(o, nf);
                        if (GUILayout.Button(new GUIContent("Edit", "Open this orphaned animation in the Animation Builder."), GUILayout.Width(44)))
                            AnimationBuilderWindow.OpenForOrphan(o);
                        if (GUILayout.Button(new GUIContent("Rename", "Rename this orphaned animation."), GUILayout.Width(60)))
                        { _renamingOrphan = i; _orphanRenameBuffer = def.name; }
                        using (new EditorGUI.DisabledScope(_orphanIncludeTarget == null))
                            if (GUILayout.Button(new GUIContent("→", _orphanIncludeTarget != null ? $"Include into '{_orphanIncludeTarget.zoeName}' draft" : "No zoe to include into"), GUILayout.Width(24)))
                                IncludeOrphanInto(o, _orphanIncludeTarget);
                        if (GUILayout.Button(new GUIContent("✕", "Delete this orphaned animation."), GUILayout.Width(24)))
                            if (EditorUtility.DisplayDialog("Delete orphan", $"Delete orphaned animation '{def.name}'?", "Delete", "Cancel"))
                            { AnimationLibrary.Delete(o); Refresh(); GUIUtility.ExitGUI(); }
                    }
                }
            }
            EditorGUILayout.EndScrollView();

            DrawPreview();
        }

        private void CreateEmptyAnimation()
        {
            if (_selected == null || string.IsNullOrWhiteSpace(_newAnimName)) return;
            ZoeRepo.SaveAnimationToDraft(_selected, new AnimationDef { name = _newAnimName });
            _versionSel = 0;
            Refresh();
        }

        // ── right: versions + animations + preview ───────────────────────────
        private void DrawZoeDetail()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                if (_showingOrphans) { DrawOrphanDetail(); return; }

                if (_selected == null)
                {
                    EditorGUILayout.HelpBox("Select a zoe or 'Orphaned', or create one. Animations are authored in the Animation Builder.", MessageType.Info);
                    if (GUILayout.Button("Open Animation Builder", GUILayout.Width(180))) AnimationBuilderWindow.Open();
                    return;
                }

                EditorGUILayout.LabelField(_selected.zoeName, EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"id {_selected.zoeId}", EditorStyles.miniLabel);

                // version selector
                var options = new List<string> { "draft" };
                var committed = ZoeRepo.ListCommittedVersions(_selected);
                options.AddRange(committed.Select(n => "v" + n));
                int curIdx = _versionSel == 0 ? 0 : committed.IndexOf(_versionSel) + 1;
                if (curIdx < 0) curIdx = 0;
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Version", GUILayout.Width(56));
                    int newIdx = EditorGUILayout.Popup(curIdx, options.ToArray(), GUILayout.Width(120));
                    if (newIdx != curIdx) { _versionSel = newIdx == 0 ? 0 : committed[newIdx - 1]; AutoPlayFirstAnimation(); }
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(_versionSel != 0))
                        if (GUILayout.Button("Save draft as new version", GUILayout.Width(180)))
                            TryCommit();
                }

                var version = ZoeRepo.LoadVersion(_selected, _versionSel);
                if (version == null) { EditorGUILayout.HelpBox("Version not found.", MessageType.Warning); return; }
                bool isDraft = _versionSel == 0;

                EditorGUILayout.LabelField($"Animations ({version.animations.Count})", EditorStyles.boldLabel);
                _animScroll = EditorGUILayout.BeginScrollView(_animScroll, GUILayout.Height(160));
                for (int i = 0; i < version.animations.Count; i++)
                {
                    var def = version.animations[i];
                    using (new EditorGUILayout.HorizontalScope(i == _animSel ? EditorStyles.helpBox : GUIStyle.none))
                    {
                        if (isDraft && _renamingAnim == i)
                        {
                            _animRenameBuffer = EditorGUILayout.TextField(_animRenameBuffer, GUILayout.Width(160));
                            if (GUILayout.Button("OK", GUILayout.Width(36))) CommitRename(def);
                            if (GUILayout.Button("Cancel", GUILayout.Width(56))) _renamingAnim = -1;
                            GUILayout.FlexibleSpace();
                        }
                        else
                        {
                            if (GUILayout.Button($"{def.name}", EditorStyles.label, GUILayout.Width(160)))
                            { _animSel = i; _previewing = def; }
                            GUILayout.Label($"{def.frames.Count}f", EditorStyles.miniLabel, GUILayout.Width(28));
                            if (isDraft)
                            {
                                EditorGUI.BeginChangeCheck();
                                float nf = EditorGUILayout.DelayedFloatField(def.fps, GUILayout.Width(42));
                                GUILayout.Label("fps", EditorStyles.miniLabel, GUILayout.Width(22));
                                if (EditorGUI.EndChangeCheck()) SetAnimFps(def, nf);
                            }
                            else
                                GUILayout.Label($"@ {def.fps:0}fps", EditorStyles.miniLabel, GUILayout.Width(64));
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button(new GUIContent("Edit", "Open this animation in the Animation Builder."), GUILayout.Width(44)))
                                EditAnimation(def, isDraft);
                            if (isDraft)
                            {
                                if (GUILayout.Button(new GUIContent("Rename", "Rename this animation."), GUILayout.Width(60)))
                                { _renamingAnim = i; _animRenameBuffer = def.name; }
                                if (GUILayout.Button(new GUIContent("Dup", "Duplicate this animation."), GUILayout.Width(40)))
                                    DuplicateAnim(def);
                                if (GUILayout.Button(new GUIContent("✕", "Delete this animation from the draft."), GUILayout.Width(24)))
                                    DeleteAnim(def);
                            }
                        }
                    }
                }
                EditorGUILayout.EndScrollView();

                if (isDraft)
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _newAnimName = EditorGUILayout.TextField(_newAnimName);
                        if (GUILayout.Button("New animation", GUILayout.Width(110))) CreateEmptyAnimation();
                    }
                else
                    EditorGUILayout.LabelField("Committed versions are read-only. Edit copies an animation into the draft.",
                        EditorStyles.miniLabel);

                DrawPreview();
            }
        }

        private void DrawPreview()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _playing = GUILayout.Toggle(_playing, _playing ? "❚❚" : "▶", "Button", GUILayout.Width(36));
                GUILayout.Label(_previewing != null ? $"Preview: {_previewing.name}" : "Select an animation to preview", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
            }
            var box = GUILayoutUtility.GetRect(10, 160, GUILayout.ExpandWidth(true), GUILayout.Height(160));
            EditorGUI.DrawRect(box, new Color(0.12f, 0.12f, 0.12f));
            if (_previewing != null && _previewing.frames.Count > 0)
            {
                int frame = _pb.Anim == _previewing ? _pb.Frame : 0;
                FramePreview.DrawClip(box, _previewing.frames, frame, 0.5f, 0.6f);
                GUI.Label(new Rect(box.x + 4, box.yMax - 16, 200, 16), $"Frame {frame + 1}/{_previewing.frames.Count}", EditorStyles.whiteMiniLabel);
            }
        }

        private void EditAnimation(AnimationDef def, bool isDraft)
        {
            if (!isDraft)
            {
                // Editing targets the draft: copy this committed animation's recipe into the draft first.
                ZoeRepo.SaveAnimationToDraft(_selected, ZoeRepo.CopyAnimation(def));
                _versionSel = 0;
                Refresh();
            }
            AnimationBuilderWindow.OpenForEdit(_selected, def.name);
        }

        private void SetAnimFps(AnimationDef def, float fps)
        {
            try { ZoeRepo.SetDraftAnimationFps(_selected, def.name, fps); Refresh(); RebindPreview(def.name); }
            catch (System.Exception ex) { EditorUtility.DisplayDialog("Set FPS failed", ex.Message, "OK"); }
            Repaint();
            GUIUtility.ExitGUI(); // the draft rebuilt mid-OnGUI; abort this pass cleanly
        }

        // ── draft animation CRUD ─────────────────────────────────────────────
        private void CommitRename(AnimationDef def)
        {
            string newName = (_animRenameBuffer ?? "").Trim();
            _renamingAnim = -1;
            if (!string.IsNullOrEmpty(newName) && !string.Equals(newName, def.name, System.StringComparison.OrdinalIgnoreCase))
            {
                try { ZoeRepo.RenameDraftAnimation(_selected, def.name, newName); Refresh(); RebindPreview(newName); }
                catch (System.Exception ex) { EditorUtility.DisplayDialog("Rename failed", ex.Message, "OK"); }
            }
            Repaint();
            GUIUtility.ExitGUI(); // the animations list changed mid-OnGUI; abort this pass cleanly
        }

        private void CommitOrphanRename(AnimationAsset o)
        {
            string newName = (_orphanRenameBuffer ?? "").Trim();
            _renamingOrphan = -1;
            if (!string.IsNullOrEmpty(newName) && o != null && !string.Equals(newName, o.animation.name, System.StringComparison.OrdinalIgnoreCase))
            {
                try { AnimationLibrary.Rename(o, newName); Refresh(); }
                catch (System.Exception ex) { EditorUtility.DisplayDialog("Rename failed", ex.Message, "OK"); }
            }
            Repaint();
            GUIUtility.ExitGUI(); // the orphan list/asset changed mid-OnGUI; abort this pass cleanly
        }

        private void DuplicateAnim(AnimationDef def)
        {
            string created = ZoeRepo.DuplicateDraftAnimation(_selected, def.name);
            if (created != null) { Refresh(); RebindPreview(created); }
            Repaint();
            GUIUtility.ExitGUI();
        }

        private void DeleteAnim(AnimationDef def)
        {
            if (!EditorUtility.DisplayDialog("Delete animation",
                    $"Delete '{def.name}' from the draft? This can't be undone.", "Delete", "Cancel"))
                return;
            ZoeRepo.RemoveAnimationFromDraft(_selected, def.name);
            _animSel = -1; _previewing = null;
            Refresh(); RebindPreview(null);
            Repaint();
            GUIUtility.ExitGUI();
        }

        private void TryCommit()
        {
            try
            {
                int n = ZoeRepo.CommitNewVersion(_selected);
                Refresh();
                _versionSel = n; // jump to the freshly committed version
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Commit failed", ex.Message, "OK");
            }
        }

    }
}
