using System.Collections.Generic;
using System.Linq;
using Laubrary.Launimator;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Launimator.Editor
{
    /// <summary>
    /// The reel browser. Lists every reel under <see cref="ReelRepo.Root"/>, lets you pick a
    /// version (the editable <c>draft</c> or an immutable snapshot <c>v1..vN</c>), browse and preview that
    /// version's animations, and manage reel lifecycle (New / Duplicate / Rename / Delete) plus commit
    /// the draft into a new immutable version. This is the workflow's starting point: create or pick a
    /// reel, add a named animation, then <b>Edit</b> it in the Animation Builder (which saves back here).
    /// It also lists standalone "orphaned" animations and can include them into a reel's draft.
    ///
    /// UI TOOLKIT PORT: every control is a Z.* control; the reel list and the animation rows are retained
    /// elements rebuilt whenever their data changes. The playback PREVIEW stays an IMGUIContainer — it draws
    /// baked atlas frames through <see cref="FramePreview"/> (pivot-anchored sub-rect blits), the one shared
    /// frame visualiser this tool must not fork.
    /// </summary>
    public class ReelBrowserWindow : ZuiWindow
    {
        private List<Reel> _reels = new List<Reel>();
        private List<AnimationAsset> _orphans = new List<AnimationAsset>();
        private Reel _selected;
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
        private Reel _lastReel;       // last real reel selected (default include target)
        private Reel _orphanIncludeTarget; // reel a row's → includes into, while browsing orphans

        // preview — driven by the shared AnimationPlayback (the ONE player), same as the game & Animation Builder
        private bool _playing = true;
        private double _lastStep;
        private AnimationDef _previewing;
        private readonly AnimationPlayback _pb = new AnimationPlayback();
        private Texture2D _orphanPreviewTex; // owned in-memory atlas for the previewed orphan (orphans aren't baked)

        // retained elements
        private VisualElement _listHost, _detailHost;
        private IMGUIContainer _previewBox;
        private Label _previewLabel;
        private Button _playButton;

        [MenuItem("Laubrary/Reel Browser")]
        public static void Open()
        {
            var w = GetWindow<ReelBrowserWindow>("Reel Browser");
            w.minSize = new Vector2(640, 520);
            w.Refresh();
            w.Show();
        }

        private void OnEnable() { _lastStep = EditorApplication.timeSinceStartup; EditorApplication.update += Tick; Refresh(); }
        protected override void OnDisable() { base.OnDisable(); EditorApplication.update -= Tick; DestroyOrphanPreview(); }
        private void OnFocus() { Refresh(); if (rootVisualElement.childCount > 0) Rebuild(); }

        /// <summary>Re-read reels from disk and repaint. Called by the Animation Builder after a save so
        /// this browser reflects the new/updated animation immediately.</summary>
        public void ExternalRefresh()
        {
            // A save in the Animation Builder rebuilds the draft and REPLACES the AnimationDef instances, so the
            // preview's cached reference goes stale. Re-bind it by name to the freshly rebuilt animation.
            string previewedName = _previewing != null ? _previewing.name : null;
            Refresh();
            RebindPreview(previewedName);
            if (rootVisualElement.childCount > 0) Rebuild();
        }

        private void RebindPreview(string name)
        {
            _previewing = null;
            if (_selected == null) { _animSel = -1; return; }
            var version = ReelRepo.LoadVersion(_selected, _versionSel);
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

            if (_pb.Anim != _previewing) { _pb.Play(_previewing, true); _previewBox?.MarkDirtyRepaint(); }
            if (_playing) { _pb.Tick(dt, 1f); _previewBox?.MarkDirtyRepaint(); }
        }

        private void Refresh()
        {
            _reels = ReelRepo.EnumerateReels();
            _orphans = AnimationLibrary.Enumerate();
            if (_selected != null && !_reels.Contains(_selected)) _selected = null;
        }

        /// <summary>Select a reel, jump to its latest version, and auto-play its first animation.</summary>
        private void SelectReel(Reel c)
        {
            _showingOrphans = false;
            DestroyOrphanPreview();
            _selected = c;
            if (c != null) _lastReel = c;
            _renameBuffer = c != null ? c.reelName : "";
            _versionSel = c != null && c.latestVersion > 0 ? c.latestVersion : 0;
            AutoPlayFirstAnimation();
            Rebuild();
        }

        /// <summary>Select the "Orphaned" pseudo-entry: the detail panel lists all orphaned animations using the
        /// same row UI as a reel's animations.</summary>
        private void SelectOrphaned()
        {
            _showingOrphans = true;
            _selected = null;
            _renamingAnim = -1; _renamingOrphan = -1;
            _animSel = -1; _previewing = null;
            DestroyOrphanPreview();
            if (_orphanIncludeTarget == null || !_reels.Contains(_orphanIncludeTarget))
                _orphanIncludeTarget = _lastReel != null && _reels.Contains(_lastReel)
                    ? _lastReel
                    : (_reels.Count > 0 ? _reels[0] : null);
            Rebuild();
        }

        private void AutoPlayFirstAnimation()
        {
            _renamingAnim = -1;
            _animSel = -1; _previewing = null;
            if (_selected == null) return;
            var version = ReelRepo.LoadVersion(_selected, _versionSel);
            if (version != null && version.animations.Count > 0)
            {
                _animSel = 0;
                _previewing = version.animations[0];
            }
        }

        // ── window build ─────────────────────────────────────────────────────
        protected override void OnBeforeRebuild()
        {
            _listHost = null; _detailHost = null; _previewBox = null; _previewLabel = null; _playButton = null;
        }

        protected override void BuildUI(VisualElement root)
        {
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;

            var nameField = Z.TextInput(_newName, "Name for a brand-new reel.", v => _newName = v, 140f);
            root.Add(Z.Row(
                Z.Button("Refresh", "Re-scan the project for reels and orphaned animations.",
                    () => { Refresh(); Rebuild(); }).W(64f),
                Z.Flexible(),
                nameField,
                Z.Button("New reel", "Create a reel with the typed name and select it.", () =>
                {
                    var created = ReelRepo.CreateReel(_newName);
                    Refresh();
                    SelectReel(created);
                }).W(100f)));

            var split = new VisualElement();
            split.style.flexDirection = FlexDirection.Row;
            split.style.flexGrow = 1f;
            split.style.minHeight = 0f;
            root.Add(split);

            var left = new VisualElement();
            left.style.width = 240f;
            left.style.flexShrink = 0f;
            left.style.minHeight = 0f;
            _listHost = left;
            BuildReelList(left);
            split.Add(left);

            var right = new VisualElement();
            right.style.flexGrow = 1f;
            right.style.minWidth = 0f;
            right.style.minHeight = 0f;
            right.style.marginLeft = 4f;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            _detailHost = scroll.contentContainer;
            BuildReelDetail(_detailHost);
            right.Add(scroll);
            split.Add(right);
        }

        // ── left: reel list + lifecycle ─────────────────────────────────
        private void BuildReelList(VisualElement root)
        {
            root.Add(Z.Text("Reels", ZuiText.Section, "Every reel found under the Launimator reel root."));

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.height = 170f;
            root.Add(scroll);
            var list = scroll.contentContainer;

            if (_reels.Count == 0)
                list.Add(Z.Text("None yet. Click 'New reel', or author an animation in the Animation Builder.",
                    ZuiText.Subtle, "No reels exist yet."));

            // "Orphaned" pseudo-entry — selecting it lists all orphaned animations in the detail panel,
            // using the same row UI as reel animations (so there's one browser, not two).
            var orphanRow = Z.Button($"Orphaned   ({_orphans.Count})",
                "Standalone animations that aren't part of any reel yet.", SelectOrphaned);
            if (_showingOrphans) orphanRow.AddToClassList("zui-radio__on");
            list.Add(orphanRow);

            foreach (var c in _reels)
            {
                var reel = c;
                var b = Z.Button($"{c.reelName}   (latest v{c.latestVersion})",
                    "Select this reel to browse its versions and animations.", () => SelectReel(reel));
                if (!_showingOrphans && c == _selected) b.AddToClassList("zui-radio__on");
                list.Add(b);
            }

            var dup = Z.Button("Duplicate", "Create a copy of the selected reel (draft only).", () =>
            {
                var d = ReelRepo.Duplicate(_selected, _selected.reelName + " Copy");
                Refresh(); _selected = d; _versionSel = 0; _animSel = -1; Rebuild();
            });
            dup.SetEnabled(_selected != null);
            root.Add(dup);

            var renameField = Z.TextInput(_renameBuffer, "New name for the selected reel.", v => _renameBuffer = v, 170f);
            var renameButton = Z.Button("Rename", "Rename the selected reel.", () =>
            {
                if (_selected == null) return;
                ReelRepo.Rename(_selected, _renameBuffer); Refresh(); Rebuild();
            }).W(64f);
            var renameRow = Z.Row(renameField, renameButton);
            renameRow.SetEnabled(_selected != null);
            root.Add(renameRow);

            var del = Z.Button("Delete…", "Delete the selected reel and every one of its versions (asks first).", () =>
            {
                if (_selected == null) return;
                if (EditorUtility.DisplayDialog("Delete reel",
                    $"Delete '{_selected.reelName}' and ALL its versions (draft + v1..v{_selected.latestVersion})?\n\n" +
                    "This cannot be undone.", "Delete", "Cancel"))
                {
                    ReelRepo.Delete(_selected);
                    _selected = null; _animSel = -1; _previewing = null; Refresh(); Rebuild();
                }
            });
            del.SetEnabled(_selected != null);
            root.Add(del);
        }

        private void IncludeOrphanInto(AnimationAsset o, Reel c)
        {
            if (o == null || c == null) return;
            ReelRepo.SaveAnimationToDraft(c, ReelRepo.CopyAnimation(o.animation));
            Refresh();
            Rebuild();
        }

        /// <summary>Preview an orphaned animation: orphans store only a recipe (not baked frames), so bake the
        /// recipe IN MEMORY (same path as reels/the game) into a throwaway def the shared player can run.
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
                    Rebuild();
                    return;
                }
            }
            // Empty recipe or bake failure: select it but there's nothing to play.
            _previewing = new AnimationDef { name = def.name, fps = def.fps };
            Rebuild();
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
            Undo.RecordObject(o, "Set animation fps");
            o.animation.fps = fps;
            EditorUtility.SetDirty(o);
            AssetDatabase.SaveAssets();
            Refresh();
            Rebuild();
        }

        // Standalone animations not in any reel — shown in the SAME detail panel as a reel's animations.
        // Author one with "New animation", Edit opens it in the Animation Builder, → includes it into a reel.
        private void BuildOrphanDetail(VisualElement root)
        {
            root.Add(Z.Text("Orphaned animations", ZuiText.Section,
                "Standalone animations not yet part of any reel."));

            root.Add(Z.Row(
                Z.TextInput(_newOrphanName, "Name for a brand-new standalone animation.", v => _newOrphanName = v, 200f),
                Z.Button("New animation", "Author a standalone animation and open it in the Animation Builder.", () =>
                {
                    var a = AnimationLibrary.Create(_newOrphanName);
                    Refresh();
                    AnimationBuilderWindow.OpenForOrphan(a);
                    Rebuild();
                }).W(110f)));

            if (_reels.Count > 0)
            {
                int ti = Mathf.Max(0, _reels.IndexOf(_orphanIncludeTarget));
                root.Add(Z.Field("Include into", "Which reel's draft the → button on each row includes that animation into.",
                    Z.Dropdown(ti, _reels.Select(c => c.reelName).ToList(),
                        "Which reel's draft the → button on each row includes that animation into.",
                        v => { _orphanIncludeTarget = _reels[Mathf.Clamp(v, 0, _reels.Count - 1)]; }, 160f)));
            }

            root.Add(Z.Text($"Animations ({_orphans.Count})", ZuiText.Section, "Every orphaned animation in the project."));

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.height = 160f;
            root.Add(scroll);
            var list = scroll.contentContainer;

            if (_orphans.Count == 0)
                list.Add(Z.Text("None yet. 'New animation' authors a standalone one.", ZuiText.Subtle,
                    "No orphaned animations exist."));

            for (int i = 0; i < _orphans.Count; i++)
                list.Add(BuildOrphanRow(i, _orphans[i]));

            BuildPreview(root);
        }

        private VisualElement BuildOrphanRow(int i, AnimationAsset o)
        {
            var def = o.animation;
            var row = Z.Row();
            if (i == _animSel) row.style.backgroundColor = new Color(0.35f, 0.55f, 0.95f, 0.18f);

            if (_renamingOrphan == i)
            {
                row.Add(Z.TextInput(_orphanRenameBuffer, "New name for this orphaned animation.",
                    v => _orphanRenameBuffer = v, 160f));
                row.Add(Z.Button("OK", "Apply the rename.", () => CommitOrphanRename(o)).W(36f));
                row.Add(Z.Button("Cancel", "Keep the current name.", () => { _renamingOrphan = -1; Rebuild(); }).W(56f));
                return row;
            }

            int index = i;
            var pick = Z.Button(def.name, "Preview this orphaned animation.", () => SelectOrphanForPreview(index, o));
            pick.style.flexGrow = 1f;
            pick.style.minWidth = 60f;
            pick.AddToClassList("zui-audit-allow-stretch");
            row.Add(pick);

            int fcount = def.frames.Count > 0 ? def.frames.Count : (def.recipe?.Count ?? 0);
            row.Add(Z.Text($"{fcount}f", ZuiText.Small, "How many frames this animation has.").W(28f));

            // Commit-on-blur/Enter matters here: a live per-keystroke commit would re-save the asset mid-edit.
            var fpsField = Z.Float(def.fps, "Playback speed in frames per second.", null, 42f);
            fpsField.isDelayed = true;
            fpsField.RegisterValueChangedCallback(e => SetOrphanFps(o, e.newValue));
            row.Add(fpsField);
            row.Add(Z.Text("fps", ZuiText.Small, "Frames per second.").W(22f));

            row.Add(Z.Button("Edit", "Open this orphaned animation in the Animation Builder.",
                () => AnimationBuilderWindow.OpenForOrphan(o)).W(44f));
            row.Add(Z.Button("Rename", "Rename this orphaned animation.",
                () => { _renamingOrphan = index; _orphanRenameBuffer = def.name; Rebuild(); }).W(60f));

            var include = Z.Button("→",
                _orphanIncludeTarget != null ? $"Include into '{_orphanIncludeTarget.reelName}' draft" : "No reel to include into",
                () => IncludeOrphanInto(o, _orphanIncludeTarget)).W(24f);
            include.SetEnabled(_orphanIncludeTarget != null);
            row.Add(include);

            row.Add(Z.Button("X", "Delete this orphaned animation.", () =>
            {
                if (EditorUtility.DisplayDialog("Delete orphan", $"Delete orphaned animation '{def.name}'?", "Delete", "Cancel"))
                { AnimationLibrary.Delete(o); Refresh(); Rebuild(); }
            }).W(24f));
            return row;
        }

        private void CreateEmptyAnimation()
        {
            if (_selected == null || string.IsNullOrWhiteSpace(_newAnimName)) return;

            // The name field doesn't clear itself after a successful create, so a second stray click (or one
            // left over from renaming/inspecting a different animation) silently overwrites an existing
            // animation of the same name with a blank one — SaveAnimationToDraft matches by name and replaces
            // in place. That's real, unrecoverable-in-practice data loss (Undo only survives until the next
            // edit), so guard it the same way the rest of the toolset confirms before an edit Undo can't
            // reliably cover.
            bool exists = ReelRepo.EnsureDraft(_selected).animations.Exists(
                a => a != null && string.Equals(a.name, _newAnimName, System.StringComparison.OrdinalIgnoreCase));
            if (exists && !EditorUtility.DisplayDialog("Overwrite animation?",
                    $"'{_newAnimName}' already exists on this reel's draft. Creating a new (empty) animation with " +
                    "the same name will replace it — its frames and meta-layers will be gone.",
                    "Overwrite", "Cancel"))
                return;

            ReelRepo.SaveAnimationToDraft(_selected, new AnimationDef { name = _newAnimName });
            _versionSel = 0;
            _newAnimName = ""; // clear so a follow-up click can't silently repeat the same collision
            Refresh();
            Rebuild();
        }

        // ── right: versions + animations + preview ───────────────────────────
        private void BuildReelDetail(VisualElement root)
        {
            if (_showingOrphans) { BuildOrphanDetail(root); return; }

            if (_selected == null)
            {
                root.Add(Z.Help("Select a reel or 'Orphaned', or create one. Animations are authored in the Animation Builder."));
                root.Add(Z.Button("Open Animation Builder", "Open the Animation Builder window.",
                    () => AnimationBuilderWindow.Open()).W(180f));
                return;
            }

            root.Add(Z.Text(_selected.reelName, ZuiText.Section, "The selected reel."));
            root.Add(Z.Text($"id {_selected.reelId}", ZuiText.Small, "This reel's stable identity — safe across renames."));

            // version selector
            var options = new List<string> { "draft" };
            var committed = ReelRepo.ListCommittedVersions(_selected);
            options.AddRange(committed.Select(n => "v" + n));
            int curIdx = _versionSel == 0 ? 0 : committed.IndexOf(_versionSel) + 1;
            if (curIdx < 0) curIdx = 0;

            var commitButton = Z.Button("Save draft as new version",
                "Snapshot the draft into a new immutable version.", TryCommit).W(180f);
            commitButton.SetEnabled(_versionSel == 0);
            root.Add(Z.Row(
                Z.Field("Version", "Which version of this reel to browse — the editable draft, or a committed snapshot.",
                    Z.Dropdown(curIdx, options,
                        "Which version of this reel to browse — the editable draft, or a committed snapshot.",
                        v => { _versionSel = v == 0 ? 0 : committed[v - 1]; AutoPlayFirstAnimation(); Rebuild(); }, 120f)),
                Z.Flexible(),
                commitButton));

            var version = ReelRepo.LoadVersion(_selected, _versionSel);
            if (version == null) { root.Add(Z.Help("Version not found.", HelpBoxMessageType.Warning)); return; }
            bool isDraft = _versionSel == 0;

            root.Add(Z.Text($"Animations ({version.animations.Count})", ZuiText.Section,
                "Every animation in the selected version."));

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.height = 160f;
            root.Add(scroll);
            for (int i = 0; i < version.animations.Count; i++)
                scroll.contentContainer.Add(BuildAnimRow(i, version.animations[i], isDraft));

            if (isDraft)
                root.Add(Z.Row(
                    Z.TextInput(_newAnimName, "Name for a new (empty) animation on this reel's draft.",
                        v => _newAnimName = v, 200f),
                    Z.Button("New animation", "Add an empty animation with this name to the draft.",
                        CreateEmptyAnimation).W(110f)));
            else
                root.Add(Z.Text("Committed versions are read-only. Edit copies an animation into the draft.",
                    ZuiText.Subtle, "Why the per-animation edit tools are hidden on a committed version."));

            BuildPreview(root);
        }

        private VisualElement BuildAnimRow(int i, AnimationDef def, bool isDraft)
        {
            var row = Z.Row();
            if (i == _animSel) row.style.backgroundColor = new Color(0.35f, 0.55f, 0.95f, 0.18f);

            if (isDraft && _renamingAnim == i)
            {
                row.Add(Z.TextInput(_animRenameBuffer, "New name for this animation.", v => _animRenameBuffer = v, 160f));
                row.Add(Z.Button("OK", "Apply the rename.", () => CommitRename(def)).W(36f));
                row.Add(Z.Button("Cancel", "Keep the current name.", () => { _renamingAnim = -1; Rebuild(); }).W(56f));
                return row;
            }

            int index = i;
            row.Add(Z.Button(def.name, "Preview this animation.",
                () => { _animSel = index; _previewing = def; Rebuild(); }).W(160f));
            row.Add(Z.Text($"{def.frames.Count}f", ZuiText.Small, "How many baked frames this animation has.").W(28f));

            if (isDraft)
            {
                // Commit-on-blur/Enter matters here: the draft is rebuilt on commit, so a per-keystroke
                // commit would re-write the reel on every character.
                var fpsField = Z.Float(def.fps, "Playback speed in frames per second.", null, 42f);
                fpsField.isDelayed = true;
                fpsField.RegisterValueChangedCallback(e => SetAnimFps(def, e.newValue));
                row.Add(fpsField);
                row.Add(Z.Text("fps", ZuiText.Small, "Frames per second.").W(22f));
            }
            else
                row.Add(Z.Text($"@ {def.fps:0}fps", ZuiText.Small, "This committed animation's playback speed.").W(64f));

            row.Add(Z.Flexible());
            row.Add(Z.Button("Edit", "Open this animation in the Animation Builder.",
                () => EditAnimation(def, isDraft)).W(44f));
            if (isDraft)
            {
                row.Add(Z.Button("Rename", "Rename this animation.",
                    () => { _renamingAnim = index; _animRenameBuffer = def.name; Rebuild(); }).W(60f));
                row.Add(Z.Button("Dup", "Duplicate this animation.", () => DuplicateAnim(def)).W(40f));
                row.Add(Z.Button("X", "Delete this animation from the draft.", () => DeleteAnim(def)).W(24f));
            }
            return row;
        }

        // ── preview (the ONE deliberate IMGUI island — FramePreview's pivot-anchored atlas blits) ──
        private void BuildPreview(VisualElement root)
        {
            _playButton = Z.Button(_playing ? "❚❚" : "▶", "Play or pause the looping preview.", () =>
            {
                _playing = !_playing;
                _playButton.text = _playing ? "❚❚" : "▶";
            }).W(36f);
            _previewLabel = Z.Text(_previewing != null ? $"Preview: {_previewing.name}" : "Select an animation to preview",
                ZuiText.Subtle, "Which animation the box below is playing.");
            root.Add(Z.Row(_playButton, _previewLabel));

            _previewBox = new IMGUIContainer(DrawPreviewGUI)
            {
                tooltip = "The selected animation, played by the same AnimationPlayback the game uses."
            };
            _previewBox.style.height = 160f;
            _previewBox.style.flexShrink = 0f;
            root.Add(_previewBox);
        }

        private void DrawPreviewGUI()
        {
            if (_previewBox == null) return;
            var box = new Rect(0f, 0f, _previewBox.layout.width, _previewBox.layout.height);
            if (!(box.width > 4f) || !(box.height > 4f)) return;
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
                ReelRepo.SaveAnimationToDraft(_selected, ReelRepo.CopyAnimation(def));
                _versionSel = 0;
                Refresh();
            }
            AnimationBuilderWindow.OpenForEdit(_selected, def.name);
            Rebuild();
        }

        private void SetAnimFps(AnimationDef def, float fps)
        {
            try { ReelRepo.SetDraftAnimationFps(_selected, def.name, fps); Refresh(); RebindPreview(def.name); }
            catch (System.Exception ex) { EditorUtility.DisplayDialog("Set FPS failed", ex.Message, "OK"); }
            Rebuild();
        }

        // ── draft animation CRUD ─────────────────────────────────────────────
        private void CommitRename(AnimationDef def)
        {
            string newName = (_animRenameBuffer ?? "").Trim();
            _renamingAnim = -1;
            if (!string.IsNullOrEmpty(newName) && !string.Equals(newName, def.name, System.StringComparison.OrdinalIgnoreCase))
            {
                try { ReelRepo.RenameDraftAnimation(_selected, def.name, newName); Refresh(); RebindPreview(newName); }
                catch (System.Exception ex) { EditorUtility.DisplayDialog("Rename failed", ex.Message, "OK"); }
            }
            Rebuild();
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
            Rebuild();
        }

        private void DuplicateAnim(AnimationDef def)
        {
            string created = ReelRepo.DuplicateDraftAnimation(_selected, def.name);
            if (created != null) { Refresh(); RebindPreview(created); }
            Rebuild();
        }

        private void DeleteAnim(AnimationDef def)
        {
            if (!EditorUtility.DisplayDialog("Delete animation",
                    $"Delete '{def.name}' from the draft? This can't be undone.", "Delete", "Cancel"))
                return;
            ReelRepo.RemoveAnimationFromDraft(_selected, def.name);
            _animSel = -1; _previewing = null;
            Refresh(); RebindPreview(null);
            Rebuild();
        }

        private void TryCommit()
        {
            try
            {
                int n = ReelRepo.CommitNewVersion(_selected);
                Refresh();
                _versionSel = n; // jump to the freshly committed version
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Commit failed", ex.Message, "OK");
            }
            Rebuild();
        }
    }
}
