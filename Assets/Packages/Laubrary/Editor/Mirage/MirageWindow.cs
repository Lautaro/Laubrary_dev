using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Laubrary.AssetKit.Editor;
using Laubrary.Zoetrope;
using Laubrary.ZoetropeLaunimator;
using Laubrary.Launimator;
using Laubrary.Choreographer;
using Laubrary.BackSplash.Editor;
using Laubrary.Zui;

namespace Laubrary.Mirage.Editor
{
    /// <summary>
    /// Browse/create/duplicate/rename/delete <see cref="MirageView"/> assets (free from
    /// <see cref="ZuiAssetWindow{T}"/>, same base Pyre/Choreographer/Larder use). Edits write straight
    /// to the asset — no clone, no discard, matching Pyre's own persistence model exactly. Works in Edit
    /// mode: a MirageRig in the scene shows/repositions previewables live as you edit here too (see
    /// MirageActiveView / MirageRig.Update's position sync) — MirageHud in the Game view remains the only
    /// place for click-to-place/drag, since only the real preview camera shows the true pixel-perfect result.
    ///
    /// UI TOOLKIT PORT (ZUI → UI Toolkit migration): every control surface is Laubrary.Zui (Z.*) retained-mode
    /// controls, built once and rebuilt in one targeted place (<see cref="RebuildBody"/>) whenever the SET of
    /// controls changes (an entry added/removed/selected, a content type swapped, Target Practice toggled).
    /// The ONE deliberate IMGUI island is the embedded BackSplash editor: <c>BackSplashGUI.DrawInline</c> is a
    /// shared viewport-drawing helper (backdrop swatch + drag-pad + sprite thumbnail) that Pyre's own
    /// still-IMGUI window also calls, so its public API is untouched and it is hosted verbatim inside an
    /// <see cref="IMGUIContainer"/>.
    ///
    /// LIVE-UPDATE CONTRACT (see the mirage-instant-update-protocol memory): nothing here subscribes to
    /// <c>Laubrary.Caching.AssetCacheInvalidation</c> — that subscription lives on the scene-side
    /// <c>MirageSubject</c>/<c>BlastPlayer</c>, fed by <c>AssetCacheInvalidationBridge</c> off Unity's own
    /// ObjectChangeEvents stream. This window's job is only to (a) keep routing every mutation through
    /// <c>Undo.RecordObject</c> + <c>EditorUtility.SetDirty</c>, which is what makes that stream fire, and
    /// (b) keep calling <c>MirageActiveView.Set</c> from <see cref="OnAssetChanged"/>. A retained-mode rebuild
    /// therefore cannot drop or stack a live-preview subscription — there is none in the rebuilt subtree. The
    /// one direction that DID need work is the reverse flow: MirageHud's Game-view drag writes
    /// <c>entry.position</c> behind this window's back, which an immediate-mode window picked up for free
    /// every repaint — <see cref="TickLiveSync"/> (an EditorApplication.update hook owned by the window, not
    /// by any rebuilt element) pushes those external writes into the retained position controls and row labels.
    /// </summary>
    public class MirageWindow : ZuiAssetWindow<MirageView>
    {
        const string ActiveViewGuidKey = "Laubrary.Mirage.ActiveViewGuid";

        /// Width of the previewable list's name column — wide enough for a full asset name plus its
        /// position readout, and the same on every row so the Ping/Remove buttons form real columns.
        const float RowLabelWidth = 300f;

        [MenuItem("Laubrary/Mirage")]
        public static void Open() => GetWindow<MirageWindow>("Mirage");

        /// Open the window focused directly on a specific view — same entry-point shape as PyreWindow.OpenFor,
        /// used by LauAssetEditors' Open registration so a MirageView picked elsewhere jumps straight in here.
        public static void OpenFor(MirageView view)
        {
            var w = GetWindow<MirageWindow>("Mirage");
            if (view != null) w.SetAsset(view);
        }

        protected override string TypeLabel => "Mirage View";
        protected override string NewAssetName => "MirageView";
        protected override string DefaultFolder => "Assets/Mirage";

        PreviewableEntry _selected;
        [SerializeField] bool _choreographyExpanded = true;

        // Thumbnail cache shared by the Content and Choreography asset rows (LauAssetGridGUI owns the
        // textures it creates; cleared on disable so repeated open/close doesn't leak them).
        readonly Dictionary<Object, Texture2D> _entryThumbs = new Dictionary<Object, Texture2D>();

        // ── retained element references ─────────────────────────────────────────────────────
        VisualElement _bodyHost;                                   // scroll content — target of RebuildBody
        readonly List<(PreviewableEntry entry, Button button)> _rowButtons = new List<(PreviewableEntry, Button)>();
        ZuiPad _posPad;
        FloatField _posXField, _posYField;
        Vector2 _lastSyncedPos;
        bool _rebuildQueued;
        double _lastSync;

        protected override void OnAssetChanged()
        {
            _selected = null;
            var path = Current != null ? AssetDatabase.GetAssetPath(Current) : "";
            EditorPrefs.SetString(ActiveViewGuidKey, string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path));
            MirageActiveView.Set(Current);
        }

        protected override Texture2D RenderThumbnail(MirageView item) =>
            item.thumbnail != null ? Object.Instantiate(item.thumbnail) : null;

        // A recapture overwrites item.thumbnail's pixels in place (MirageView.SetThumbnail) rather than
        // creating a new asset — that alone doesn't reliably fire EditorApplication.projectChanged, so the
        // base's cached browser copy (made once by RenderThumbnail above) would otherwise only pick up a
        // recapture on the next full RefreshBrowse (which is what made this look like it only updated on
        // Play/Stop — those happen to force a refresh, nothing here specifically waits for them). Opting
        // into the base's animate-thumbnail path re-samples the cached copy's pixels from the live
        // item.thumbnail every tick instead, independent of any refresh event firing.
        protected override bool AnimateThumbnails => true;
        protected override void UpdateAnimatedThumbnail(MirageView item, Texture2D tex, double time)
        {
            if (item.thumbnail == null) return;
            if (tex.width != item.thumbnail.width || tex.height != item.thumbnail.height) return;
            tex.SetPixels32(item.thumbnail.GetPixels32());
            tex.Apply();
        }

        // ── lifecycle ───────────────────────────────────────────────────────────────────────
        protected override void OnEnable()
        {
            base.OnEnable();
            EditorApplication.update += TickLiveSync;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            EditorApplication.update -= TickLiveSync;
            LauAssetGridGUI.ClearCache(_entryThumbs);
        }

        /// The retained-mode replacement for "an immediate-mode window re-reads the model every repaint":
        /// MirageHud's Game-view click-to-place/drag writes entry.position directly, so poll for changes and
        /// push them into the controls that display them. Owned by the WINDOW (subscribed once in OnEnable),
        /// never by a rebuilt element, so a RebuildBody can neither drop it nor stack a second copy.
        void TickLiveSync()
        {
            if (Current == null || IsBrowsing) return;
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastSync < 0.15) return;
            _lastSync = now;

            foreach (var (entry, button) in _rowButtons)
            {
                if (entry == null || button == null) continue;
                string text = EntryLabel(entry);
                if (button.text != text) button.text = text;
            }

            if (_selected == null || _posPad == null || _selected.position == _lastSyncedPos) return;
            _lastSyncedPos = _selected.position;
            _posPad.Value = _lastSyncedPos;
            _posXField?.SetValueWithoutNotify(_lastSyncedPos.x);
            _posYField?.SetValueWithoutNotify(_lastSyncedPos.y);
        }

        // ── mutation helpers (the Undo contract every control routes through) ────────────────
        /// Record + apply + dirty in one step. SetDirty is what ultimately drives the scene-side live
        /// preview: it makes Unity publish a ChangeAssetObjectProperties event, which
        /// AssetCacheInvalidationBridge turns into an AssetCacheInvalidation.Invalidate for MirageSubject.
        void Dial(string undoLabel, System.Action apply)
        {
            var view = Current;
            if (view == null) return;
            Undo.RecordObject(view, undoLabel);
            apply();
            EditorUtility.SetDirty(view);
        }

        /// A structural change — the SET of controls differs, so rebuild the body rather than mutate one
        /// element. Deferred by one editor tick so a control can safely request the rebuild that destroys it
        /// from inside its own change callback; coalesced so a burst of edits rebuilds once.
        void RebuildBody()
        {
            if (_rebuildQueued) return;
            _rebuildQueued = true;
            EditorApplication.delayCall += () =>
            {
                _rebuildQueued = false;
                if (this == null || _bodyHost == null || Current == null) return;
                ResetBodyRefs();
                _bodyHost.Clear();
                BuildBody(_bodyHost, Current);
            };
        }

        void ResetBodyRefs()
        {
            _rowButtons.Clear();
            _posPad = null; _posXField = null; _posYField = null;
        }

        protected override void OnBeforeRebuild()
        {
            base.OnBeforeRebuild();
            _bodyHost = null;
            ResetBodyRefs();
        }

        // ── window build ────────────────────────────────────────────────────────────────────
        protected override void BuildAsset(VisualElement root, MirageView view)
        {
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;

            // The entry editor below (Weapon/Target Practice/Clips/Choreography) can grow well past window
            // height — a real scroll view, not window resizing, is the only fix for content overflow.
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            _bodyHost = scroll.contentContainer;
            BuildBody(_bodyHost, view);
            root.Add(scroll);
        }

        // Every block is a Z.Section, which OWNS its body. A bare Z.Text(.., ZuiText.Section, ..) heading
        // works out its fold extent from its following siblings, and this window's blocks don't have that
        // shape: the "Previewables" heading sits inside a row (so it would fold that row's own siblings),
        // and folding "View" swallowed everything down to "Entry", the whole previewable list included
        // (confirmed live, 9 siblings hidden). A section can't get its own extent wrong.
        void BuildBody(VisualElement root, MirageView view)
        {
            var viewSection = Z.Section("View", "Settings that apply to this whole preview arrangement.");
            BuildViewRow(viewSection, view);
            BuildBackSplashSection(viewSection, view);
            root.Add(viewSection);

            BuildPreviewableList(root, view);

            if (_selected != null && view.previewables.Contains(_selected))
                BuildEntry(root, view, _selected);
        }

        // ── view-level row ──────────────────────────────────────────────────────────────────
        void BuildViewRow(VisualElement root, MirageView view)
        {
            const string ppuTip = "Every previewable is scaled so its own source PPU maps to this — the " +
                "\"no mixels\" guarantee. A 16-PPU Zoe and a 64-PPU Pyre blast render at the same pixel size.";
            const string spriteTip = "Pick any Sprite in the project to add as a background previewable — " +
                "not a LauAsset-registered type, so it's kept out of the Add Previewable browser.";

            var addButton = Z.Button("Add Previewable",
                "Browse Zoes and Blasts — the LauAsset-registered types this view can preview.", null);
            addButton.clicked += () =>
            {
                var wb = addButton.worldBound;
                LauAssetPicker.Show(new Rect(wb.x, wb.y, wb.width, wb.height),
                    MirageAssetPicker.FindAll().ConvertAll(i => i.asset),
                    asset => { AddEntry(view, asset); RebuildBody(); }, null, pickHint: "Zoe / Blast");
            };

            root.Add(WrapRow(
                Z.Field("Display PPU", ppuTip, Z.Float(view.displayPixelsPerUnit, ppuTip,
                    v => Dial("Display PPU", () => view.displayPixelsPerUnit = Mathf.Max(1f, v)), 60f)),
                addButton,
                Z.Field("Add sprite", spriteTip, Z.Object<Sprite>(null, spriteTip,
                    v => { if (v != null) { AddEntry(view, v); RebuildBody(); } }, 150f)),
                Z.Flexible()));
        }

        // ── backsplash ───────────────────────────────────────────────────────────
        // Was an IMGUIContainer hosting BackSplashGUI.DrawInline, kept IMGUI only because Pyre called the
        // same helper. Pyre no longer does — both now use ONE Zui control (BackSplashZui), so this window
        // has no IMGUI island left at all.
        void BuildBackSplashSection(VisualElement root, MirageView view)
        {
            view.backSplash ??= new Laubrary.BackSplash.BackSplashSettings(); // guards assets saved before this field existed
            BackdropDomainExtent(out float domainHalfW, out float domainHalfH);

            const string tip = "A private backdrop copy for THIS view: camera colour + one zoomable, " +
                "positionable image. Recall copies values in from a shared preset; Save writes them out.";

            // Undo + SetDirty now happen INSIDE BackSplashZui.Build itself (owner: view) — every consumer gets
            // the same recorded-before-mutation guarantee for free instead of reimplementing it per host.
            root.Add(BackSplashZui.Build(view.backSplash, "Backsplash", tip,
                onChanged: Repaint,
                onStructureChanged: () => { Repaint(); RebuildBody(); },
                domainHalfWidth: domainHalfW, domainHalfHeight: domainHalfH, owner: view));
        }

        // The backdrop's imagePos is a real Transform.position read by MirageRig's orthographic previewCamera
        // (MirageRig.SyncBackSplash) — a WORLD-unit space, unlike Pyre's own IMGUI preview where imagePos is
        // screen pixels. So the position pad's "how far can you drag before it's off-screen" domain has to be
        // derived from that camera's actual visible half-extents, not a guessed constant. Falls back to this
        // project's own measured preview camera setup (orthographicSize 5, aspect 1.6) when no live rig is in
        // the open scene to ask (e.g. editing a MirageView asset with MirageStage not open).
        static void BackdropDomainExtent(out float halfWidth, out float halfHeight)
        {
            var rig = Object.FindFirstObjectByType<MirageRig>();
            var cam = rig != null ? rig.previewCamera : null;
            if (cam != null && cam.orthographic)
            {
                halfHeight = cam.orthographicSize;
                halfWidth = cam.orthographicSize * cam.aspect;
                return;
            }
            halfWidth = 8f;
            halfHeight = 5f;
        }

        // ── previewable list ────────────────────────────────────────────────────────────────
        static string EntryLabel(PreviewableEntry entry) =>
            (entry.content != null ? entry.content.name : "(missing)") +
            $"  @ ({entry.position.x:0.#}, {entry.position.y:0.#})";

        void BuildPreviewableList(VisualElement root, MirageView view)
        {
            // The count lives in the title, so the fold state needs an explicit key — the default
            // (title + tooltip) would forget the fold every time an entry is added or removed.
            var section = Z.Section($"Previewables ({view.previewables.Count})",
                "Everything this view places into the preview scene. A MirageRig in the open scene shows " +
                "them live and stays in sync with edits here, in Edit mode or Play mode. Click-to-place/drag " +
                "still needs the Mirage HUD in the Game view — only the real preview camera shows the true " +
                "pixel-perfect result.",
                stateKey: "Mirage.Previewables");

            foreach (var entry in view.previewables.ToArray())
            {
                var captured = entry;
                var row = new VisualElement();
                row.AddToClassList("zui-row");
                if (ReferenceEquals(_selected, entry)) row.style.backgroundColor = new Color(0.35f, 0.55f, 0.95f, 0.18f);

                // The label button carries an asset name plus a live position readout, so its natural width
                // differs per row — which left Ping/Remove at a different x on every row, reading as three
                // unrelated pairs of buttons instead of one list. Growing each label into the same bounded
                // column instead lines the action buttons up without measuring text (a measure-after-layout
                // pass is the one thing that can't be trusted on a freshly built tree — the panel's first
                // scheduled tick can still run before styles resolve). Ellipsis, not overflow, is what a
                // name too long for the column does.
                var select = Z.Button(EntryLabel(entry),
                    "Select this previewable to edit it below (click it again to deselect).",
                    () => { _selected = ReferenceEquals(_selected, captured) ? null : captured; RebuildBody(); });
                select.style.flexGrow = 1f;
                select.style.flexShrink = 1f;
                select.style.minWidth = 140f;
                select.style.maxWidth = RowLabelWidth;
                select.style.unityTextAlign = TextAnchor.MiddleLeft;
                select.style.whiteSpace = WhiteSpace.NoWrap;
                select.style.overflow = Overflow.Hidden;
                select.style.textOverflow = TextOverflow.Ellipsis;
                _rowButtons.Add((captured, select));
                row.Add(select);
                row.Add(Z.Button("Ping", "Flash this previewable in the Game view and select its live object.",
                    () => PingEntry(captured)).W(46f));
                row.Add(Z.Button("Remove", "Delete this previewable from the view.",
                    () => { RemoveEntry(view, captured); RebuildBody(); }).W(70f));
                section.Add(row);
            }

            if (view.previewables.Count == 0)
                section.Add(Z.Text("No previewables yet — Add Previewable above.", ZuiText.Subtle));

            root.Add(section);
        }

        // Flashes the entry's LIVE rendered sprite directly in the Game View (MirageHud draws a pulsing
        // outline around its actual on-screen bounds) — Unity's native Ping only flashes the Project window
        // / Hierarchy row, neither of which is where you're looking while composing a shot in Mirage, so
        // that alone isn't the fix here. Still also does the native select+ping as a free secondary cue
        // (useful if you DO have the Hierarchy visible), and falls back to pinging the underlying asset in
        // the Project window when there's no live rig showing this view at all.
        static void PingEntry(PreviewableEntry entry)
        {
            var rig = Object.FindFirstObjectByType<MirageRig>();
            var live = rig != null ? rig.GetLive(entry.id) : null;
            if (live != null)
            {
                Selection.activeGameObject = live;
                EditorGUIUtility.PingObject(live);
                // Ensure a MirageHud exists (and is correctly wired) BEFORE flashing -- it's the thing that
                // actually subscribes to MirageFlashSignal and draws the outline; Flash() is fire-and-forget,
                // so a missing/stale listener means the signal just goes nowhere, no error, nothing visible.
                MirageHud.EnsureExists(rig);
                MirageFlashSignal.Flash(entry.id);
            }
            else if (entry.content != null)
            {
                EditorGUIUtility.PingObject(entry.content);
            }
        }

        void AddEntry(MirageView view, Object content)
        {
            Undo.RecordObject(view, "Add Previewable");
            view.AddEntry(content, Vector2.zero);
            EditorUtility.SetDirty(view);
        }

        void RemoveEntry(MirageView view, PreviewableEntry entry)
        {
            Undo.RecordObject(view, "Remove Previewable");
            view.previewables.Remove(entry);
            if (_selected == entry) _selected = null;
            EditorUtility.SetDirty(view);
        }

        // ── the selected entry ──────────────────────────────────────────────────────────────
        void BuildEntry(VisualElement root, MirageView view, PreviewableEntry entry)
        {
            var entrySection = Z.Section("Entry", "The selected previewable's own settings.");
            entrySection.Add(BuildContentRow(view, entry));
            entrySection.Add(BuildPositionRow(view, entry));

            const string scaleTip = "Author-chosen multiplier ON TOP OF the view's Display PPU normalization, not instead of it.";
            entrySection.Add(Z.Field("Scale", scaleTip, Z.Float(entry.scale, scaleTip,
                v => Dial("Edit Previewable", () => entry.scale = Mathf.Max(0.01f, v)), 60f)));
            root.Add(entrySection);

            var zoe = entry.content as Zoe;
            if (zoe == null) return;

            var zoeSection = Z.Section("Zoe options", "Settings that only apply when this previewable is a Zoe.");

            // Mirage never equips a weapon the Zoe doesn't already have — this only SELECTS which of the
            // Zoe's own zoe.weapons slots to preview firing (index 0 = "Zoe's own default"), the same rule
            // as MirageSubject.weapon's own doc comment. A dropdown over the Zoe's actual list, not a general
            // asset browser over every WeaponDef in the project, makes that constraint the only thing
            // physically choosable, not just a documented convention.
            var zoeWeapons = (zoe.weapons ?? new List<ZoeWeaponSlot>())
                .Where(s => s != null && s.weapon != null).Select(s => s.weapon).ToList();
            if (zoeWeapons.Count == 0)
            {
                zoeSection.Add(Z.Text("This Zoe has no weapons configured (add one to Zoe.weapons, not here).", ZuiText.Subtle));
            }
            else
            {
                const string weaponTip = "Which of THIS ZOE'S OWN equipped weapons to make active for this preview.";
                var options = new List<string>(zoeWeapons.Count + 1) { "(Zoe's default)" };
                foreach (var w in zoeWeapons) options.Add(w != null ? w.name : "(missing)");
                int current = entry.weapon != null ? zoeWeapons.IndexOf(entry.weapon) + 1 : 0;
                zoeSection.Add(Z.Field("Weapon", weaponTip, Z.Dropdown(Mathf.Max(current, 0), options, weaponTip,
                    i => Dial("Edit Previewable",
                        () => entry.weapon = i <= 0 ? null : zoeWeapons[Mathf.Clamp(i - 1, 0, zoeWeapons.Count - 1)]),
                    200f)));
            }

            // Freeze onto one named, real game-state pose ("Idle N", "Moving E") instead of driving off
            // Mirage's own stand-in input — for a composite Zoe (independently-timed parts) there's no single
            // "current clip" to preview by name, so this offers the actual COMBINATION every part would show
            // for that game state. List is auto-derived, never typed: see MotionPoseCatalog's own doc for why.
            var poses = MotionPoseCatalog.Derive(zoe);
            if (poses.Count > 0)
            {
                const string poseTip = "Freeze this preview onto one named, real game-state pose (auto-derived " +
                    "from this Zoe's authored MotionPose rules + LauminationSets) instead of driving off live " +
                    "input. \"(live)\" = normal preview, unchanged.";
                var poseOptions = new List<string>(poses.Count + 1) { "(live)" };
                foreach (var p in poses) poseOptions.Add(p.label);
                int currentPose = string.IsNullOrEmpty(entry.previewPose) ? 0
                    : Mathf.Max(0, poseOptions.IndexOf(entry.previewPose));
                zoeSection.Add(Z.Field("Preview Pose", poseTip, Z.Dropdown(currentPose, poseOptions, poseTip,
                    i => Dial("Edit Previewable", () => entry.previewPose = i <= 0 ? "" : poseOptions[i]), 200f)));
            }

            var zonedView = zoe.view as ZonedLauminaryView;
            var lauminary = zonedView != null ? zonedView.version : null;
            string[] clipNames = lauminary != null && lauminary.animations != null
                ? lauminary.animations.Where(a => a != null && !string.IsNullOrEmpty(a.name)).Select(a => a.name).ToArray()
                : System.Array.Empty<string>();

            BuildTargetPractice(zoeSection, view, entry, zoe);
            root.Add(zoeSection);

            if (!entry.targetPractice)
            {
                entry.clips ??= new List<ClipStep>();
                var clipSection = BuildClips(entry, lauminary, clipNames, zoeWeapons.Count > 0);

                // Belongs with the clips, not after them: it only means anything once some step fires.
                const string aimTip = "Stands in for Combatant.aimDirection, which real gameplay (player input / AI) " +
                    "would normally drive — Mirage has neither, so firing needs an explicit direction to preview with.";
                var aimField = Z.Field("Fire direction", aimTip,
                    Z.Pad(entry.previewAimDirection, new Rect(-2f, -2f, 4f, 4f), aimTip,
                        v => Dial("Edit Previewable", () => entry.previewAimDirection = v), 60f));
                aimField.SetEnabled(entry.clips.Exists(s => s != null && s.fireWeapon));
                clipSection.Add(aimField);
                root.Add(clipSection);
            }

            root.Add(Z.VSpace());
            const string choreoTip = "Drives this entry's OWN motion — the same Choreography asset the Choreographer tool authors.";
            var choreoFold = Z.Foldout("Choreography", choreoTip, _choreographyExpanded, BuildChoreographyRow(view, entry));
            choreoFold.RegisterValueChangedCallback(e => { if (e.target == choreoFold) _choreographyExpanded = e.newValue; });
            root.Add(choreoFold);
        }

        // A world-space spatial position is a 2D-drag-target case, NOT a row-packing case (packing two
        // float fields fixes width but not the actual ergonomics — dragging two independent 1D fields to aim
        // ONE 2D value). The numeric fields sit BESIDE the pad, not instead of it, so an exact value is still
        // typeable. Both are also the write-back target of TickLiveSync when MirageHud moves the entry.
        VisualElement BuildPositionRow(MirageView view, PreviewableEntry entry)
        {
            const string tip = "Where this previewable sits, in world units. Drag the pad, type exact values, " +
                "or drag it directly in the Game view via the Mirage HUD.";
            _lastSyncedPos = entry.position;

            _posPad = Z.Pad(entry.position, new Rect(-10f, -10f, 20f, 20f), tip, v =>
            {
                Dial("Edit Previewable", () => entry.position = v);
                _lastSyncedPos = entry.position;
                _posXField?.SetValueWithoutNotify(entry.position.x);
                _posYField?.SetValueWithoutNotify(entry.position.y);
                RefreshRowLabel(entry);
            }, 80f);

            _posXField = Z.Float(entry.position.x, "Horizontal world position.", v =>
            {
                Dial("Edit Previewable", () => entry.position = new Vector2(v, entry.position.y));
                _lastSyncedPos = entry.position;
                if (_posPad != null) _posPad.Value = entry.position;
                RefreshRowLabel(entry);
            }, 68f);
            _posYField = Z.Float(entry.position.y, "Vertical world position.", v =>
            {
                Dial("Edit Previewable", () => entry.position = new Vector2(entry.position.x, v));
                _lastSyncedPos = entry.position;
                if (_posPad != null) _posPad.Value = entry.position;
                RefreshRowLabel(entry);
            }, 68f);

            return Z.Field("Position", tip, Z.Row(_posPad,
                Z.Field("X", "Horizontal world position.", _posXField),
                Z.Field("Y", "Vertical world position.", _posYField)));
        }

        void RefreshRowLabel(PreviewableEntry entry)
        {
            foreach (var (e, button) in _rowButtons)
                if (ReferenceEquals(e, entry) && button != null) button.text = EntryLabel(entry);
        }

        // ── content / choreography asset rows ───────────────────────────────────────────────
        /// The native counterpart of LauAssetField.Draw (an IMGUI-layout helper this window can no longer
        /// call): thumbnail swatch + name + Recall + New + Edit, composed from Z.* pieces. Kept as ONE helper
        /// so the Content row and the Choreography row can't drift apart — Content additionally gets a Sprite
        /// object field, since a previewable's content is a Zoe/Pyre/Sprite union with no shared type
        /// constraint a single browser could represent (see MirageAssetPicker's own doc comment).
        VisualElement BuildAssetRow(Object current, string nameTooltip,
            System.Action<Rect> showRecall, IList<System.Type> creatable,
            string suggestedName, string folder, System.Action<Object> onPick, VisualElement extra = null)
        {
            var row = WrapRow();

            var swatch = new VisualElement { tooltip = nameTooltip };
            swatch.style.width = 40f;
            swatch.style.height = 40f;
            swatch.style.flexShrink = 0f;
            swatch.style.backgroundColor = new Color(0.11f, 0.12f, 0.15f);
            swatch.style.justifyContent = Justify.Center;
            swatch.style.alignItems = Align.Center;
            FillSwatch(swatch, current);
            row.Add(swatch);

            var name = Z.Text(current != null ? current.name : "· none ·", ZuiText.Body,
                current != null ? current.GetType().Name : nameTooltip);
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.maxWidth = 160f;
            name.style.overflow = Overflow.Hidden;
            row.Add(name);

            var recall = Z.Button("Recall...", "Pick an existing asset from a thumbnail browser.", null);
            recall.clicked += () =>
            {
                var wb = recall.worldBound;
                showRecall(new Rect(wb.x, wb.y, wb.width, wb.height));
            };
            row.Add(recall);

            var create = Z.Button("New...", "Create a brand new asset and assign it here.", null);
            create.SetEnabled(creatable != null && creatable.Count > 0);
            create.clicked += () =>
            {
                var menu = Z.Menu(create);
                foreach (var t in creatable)
                {
                    var concrete = t;
                    menu.Item(concrete.Name, $"Create a new {concrete.Name} and assign it here.", () =>
                    {
                        var made = LauAssetEditors.Create(concrete, suggestedName, folder);
                        if (made == null) return;
                        onPick(made);
                        if (LauAssetEditors.CanOpen(made)) LauAssetEditors.Open(made);
                    });
                }
                menu.Show();
            };
            row.Add(create);

            if (extra != null) row.Add(extra);

            var edit = Z.Button("Edit", "Open this asset in its own editor.",
                () => { if (current != null && LauAssetEditors.CanOpen(current)) LauAssetEditors.Open(current); });
            edit.SetEnabled(current != null && LauAssetEditors.CanOpen(current));
            row.Add(edit);

            row.Add(Z.Flexible());
            return row;
        }

        /// Unity's AssetPreview.GetAssetPreview (LauAssetGridGUI's fallback for a type with no IVisualPreview)
        /// is ASYNCHRONOUS — the first call returns null while the preview renders. An immediate-mode window
        /// got a free second chance on every repaint; a retained one has to ask again explicitly, so retry on
        /// a short schedule and stop as soon as a texture arrives (or after a few seconds).
        void FillSwatch(VisualElement swatch, Object current)
        {
            if (current == null) return;
            var tex = LauAssetGridGUI.GetThumbnail(current, null, _entryThumbs);
            if (tex != null) { AddThumb(swatch, tex); return; }

            IVisualElementScheduledItem retry = null;
            retry = swatch.schedule.Execute(() =>
            {
                if (current == null) { retry.Pause(); return; }
                var later = LauAssetGridGUI.GetThumbnail(current, null, _entryThumbs);
                if (later == null) return;
                AddThumb(swatch, later);
                retry.Pause();
            }).Every(250);
            retry.ForDuration(4000);
        }

        static void AddThumb(VisualElement swatch, Texture2D tex)
        {
            var img = new Image { image = tex, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            img.style.width = 36f;
            img.style.height = 36f;
            swatch.Add(img);
        }

        VisualElement BuildContentRow(MirageView view, PreviewableEntry entry)
        {
            const string spriteTip = "Pick any Sprite in the project as a background — not a LauAsset-registered " +
                "type, so it's kept out of Recall.";
            var spriteField = Z.Field("Sprite", spriteTip, Z.Object<Sprite>(entry.content as Sprite, spriteTip,
                v => { if (v != null && !ReferenceEquals(v, entry.content)) SetContent(entry, v); }, 140f));

            // Scoped to MirageAssetPicker.SupportedTypes (Zoe/Pyre), not a typeof(Object) constraint —
            // that used to match every LauAssetEditors-registered type in the project (ChunkSpec, WareSpec…).
            var creatable = System.Array.FindAll(MirageAssetPicker.SupportedTypes, LauAssetEditors.CanCreate);

            return Z.Column(
                Z.Text("Content", ZuiText.Subtle, "A Zoe, a Blast, or a Sprite — the kind is read from this object's own type."),
                BuildAssetRow(entry.content,
                    "A Zoe, a Blast, or a Sprite — the kind is read from this object's own type.",
                    rect => LauAssetPicker.Show(rect, MirageAssetPicker.FindAll().ConvertAll(i => i.asset),
                        picked => SetContent(entry, picked), entry.content, pickHint: "Zoe / Blast"),
                    creatable, "Previewable", "Assets/Mirage/Content",
                    picked => SetContent(entry, picked), spriteField));
        }

        void SetContent(PreviewableEntry entry, Object picked)
        {
            if (picked == null || ReferenceEquals(picked, entry.content)) return;
            Dial("Edit Previewable", () => entry.content = picked);
            RebuildBody();   // a content-TYPE change swaps whole sections in/out (Zoe options, Clips…)
        }

        VisualElement BuildChoreographyRow(MirageView view, PreviewableEntry entry)
        {
            const string tip = "Drives this entry's OWN motion (matches MirageSubject's existing semantics).";
            var creatable = LauAssetEditors.RegisteredTypesFor(typeof(Choreography)).Where(LauAssetEditors.CanCreate).ToList();
            return BuildAssetRow(entry.choreography, tip,
                rect => LauAssetPicker.Show(rect, typeof(Choreography),
                    picked => SetChoreography(entry, picked), entry.choreography),
                creatable, "Choreography", "Assets/Choreographer",
                picked => SetChoreography(entry, picked));
        }

        void SetChoreography(PreviewableEntry entry, Object picked)
        {
            Dial("Edit Previewable", () => entry.choreography = picked as Choreography);
            RebuildBody();   // the swatch/name/Edit-enabled state all change together
        }

        // ── target practice ─────────────────────────────────────────────────────────────────
        // Target Practice is a Mirage-only testing convenience (PreviewableEntry.targetPractice/respawnDelay)
        // — deliberately NOT saved onto the Zoe (it lived there briefly during development; corrected after
        // review — "opt in to Target Practice in Mirage" meant a Mirage concept, like the Clips list it
        // replaces, not persistent Zoe data). It still exercises the SAME spawn path, Health, and
        // hit-reaction machinery a real level would (Zoe.hitReaction/HitReactionPlayer, untouched here —
        // those stay real, general Zoe data, edited on the Zoe's own editor, not from Mirage).
        void BuildTargetPractice(VisualElement root, MirageView view, PreviewableEntry entry, Zoe zoe)
        {
            // Explicit width + a row wrapper: a Toggle added straight to a COLUMN has no flex sibling to
            // stop it, so it stretches to the window (mechanically confirmed at 649px against the 600px cap
            // — the same failure the pre-port window fixed with GUILayout.Width(160f)).
            root.Add(Z.Row(
                Z.Toggle("Target Practice",
                    "A Mirage-only testing convenience: opt in to a self-contained Idle -> Hurt -> Death -> " +
                    "respawn loop for THIS preview, replacing the Clips list below. Uses the real Health system " +
                    "and the Zoe's own Hit Reaction, same as a real level would.",
                    entry.targetPractice,
                    v => { Dial("Toggle Target Practice", () => entry.targetPractice = v); RebuildBody(); }).W(160f),
                Z.Flexible()));

            if (!entry.targetPractice) return;

            // Boxed (not a bare row of fields) so the explanatory tooltip has a title row to live on, per
            // ui-layout-rules.md's "a hover icon explaining an area belongs on that area's own title row".
            // No Idle clip field here — deliberately removed (was redundant with the Zoe's own view.idleClip;
            // Target Practice always uses that directly now) after explicit user feedback.
            var box = Z.Box("Target Practice",
                "Idle always plays the Zoe's own view idle clip. Hurt/Death clips come from this Zoe's own " +
                "Hit/Death reactions, not from Mirage — edit them on the Zoe asset itself (same place as its " +
                "hit/death VFX).");

            const string respawnTip = "Seconds after the death reaction finishes (or immediately, if there's no " +
                "death clip) before reviving.";
            box.Add(Z.Field("Respawn delay (s)", respawnTip, Z.Float(entry.respawnDelay, respawnTip,
                v => Dial("Edit Target Practice", () => entry.respawnDelay = Mathf.Max(0.01f, v)), 60f)));

            bool hasHitReaction = zoe != null &&
                ((zoe.hit != null && !string.IsNullOrEmpty(zoe.hit.clip)) ||
                 (zoe.death != null && !string.IsNullOrEmpty(zoe.death.clip)));
            var infoRow = Z.Row(Z.Text(
                hasHitReaction ? "Hit/Death reaction clips are configured on this Zoe."
                               : "This Zoe has no Hit/Death reaction clips configured yet — hurt/death will have no clip.",
                ZuiText.Subtle,
                "Hurt/Death clips live on the Zoe asset, alongside its hit/death VFX — not here."), Z.Flexible());
            if (zoe != null && LauAssetEditors.CanOpen(zoe))
                infoRow.Add(Z.Button("Edit on Zoe",
                    "Open this Zoe's own editor to set (or check) its Hit/Death reaction clips.",
                    () => LauAssetEditors.Open(zoe)).W(100f));
            box.Add(infoRow);
            root.Add(box);
        }

        // ── clips ───────────────────────────────────────────────────────────────────────────
        // The ONLY animation playback control — a list of clips, cycled in order, forever ("Idle, Shoot,
        // repeat"); a one-item list just replays that one clip forever. Each step owns its OWN fire-weapon
        // trigger, scoped to THAT step's own clip's painted MetaLayers — not one entry-wide layer, which
        // couldn't tell "fire during THIS step" from "fire during that other one" once more than one clip
        // could be playing (the actual bug report this replaced).
        ZuiSection BuildClips(PreviewableEntry entry, LauminaryVersion lauminary, string[] clipNames, bool zoeHasWeapons)
        {
            var root = Z.Section("Clips", "Played in order, forever — a one-item list just loops that clip.");

            if (lauminary == null)
            {
                root.Add(Z.Text("This Zoe's view isn't a Zoned Launimator view — no clips to preview.", ZuiText.Subtle));
                return root;
            }
            if (clipNames.Length == 0)
            {
                root.Add(Z.Text("This Lauminary has no authored animations.", ZuiText.Subtle));
                return root;
            }
            if (!zoeHasWeapons)
                root.Add(Z.Text("This Zoe has no weapons configured — Fire Weapon toggles below are disabled.", ZuiText.Subtle));

            for (int i = 0; i < entry.clips.Count; i++)
            {
                var step = entry.clips[i];
                if (step == null) continue;
                int index = i;

                var stepAnim = lauminary.animations != null
                    ? lauminary.animations.FirstOrDefault(a => a != null && string.Equals(a.name, step.clip, System.StringComparison.OrdinalIgnoreCase))
                    : null;
                string[] stepLayerIds = stepAnim != null && stepAnim.metaLayers != null
                    ? stepAnim.metaLayers.Where(l => l != null && !string.IsNullOrEmpty(l.id))
                        .Select(l => l.id).Distinct().ToArray()
                    : System.Array.Empty<string>();
                // FrameEvents with an authored pixel (hasPosition) are the lighter "one point, one frame"
                // alternative to a painted MetaLayer — offered in the same Trigger dropdown below.
                string[] stepEventNames = stepAnim != null && stepAnim.events != null
                    ? stepAnim.events.Where(ev => ev != null && ev.hasPosition && !string.IsNullOrEmpty(ev.name))
                        .Select(ev => ev.name).Distinct().ToArray()
                    : System.Array.Empty<string>();

                var up = Z.Button("Up", "Move this step earlier in the list.", () =>
                {
                    Dial("Reorder Clips", () => (entry.clips[index - 1], entry.clips[index]) =
                        (entry.clips[index], entry.clips[index - 1]));
                    RebuildBody();
                }).W(34f);
                up.SetEnabled(index > 0);
                var stepLabel = Z.Text($"{index + 1}. {step.clip}", ZuiText.Body, "This step's animation clip.");
                stepLabel.style.minWidth = 120f;
                root.Add(Z.Row(
                    stepLabel, up,
                    Z.Button("X", "Remove this step from the list.", () =>
                    {
                        Dial("Remove Clip Step", () => entry.clips.RemoveAt(index));
                        RebuildBody();
                    }).W(24f),
                    Z.Flexible()));

                // A clip with no MetaLayer AND no positioned FrameEvent has nothing this row could ever
                // trigger on — don't paint a permanently-inert disabled row for it, just skip it.
                if (stepLayerIds.Length == 0 && stepEventNames.Length == 0) continue;

                var options = new List<string>(stepLayerIds.Length + stepEventNames.Length);
                foreach (var id in stepLayerIds) options.Add("Layer: " + id);
                foreach (var n in stepEventNames) options.Add("Event: " + n);

                string currentLabel = !string.IsNullOrEmpty(step.muzzleEventName) ? "Event: " + step.muzzleEventName
                    : !string.IsNullOrEmpty(step.muzzleLayerId) ? "Layer: " + step.muzzleLayerId : null;
                int current = currentLabel != null ? options.IndexOf(currentLabel) : -1;

                const string triggerTip = "Which painted MetaLayer or authored FrameEvent pixel on this step's clip triggers the shot.";
                var trigger = Z.Field("Trigger", triggerTip, Z.Dropdown(Mathf.Max(current, 0), options, triggerTip,
                    ci => Dial("Edit Clips", () => ApplyTrigger(step, options[Mathf.Clamp(ci, 0, options.Count - 1)])), 180f));
                trigger.SetEnabled(step.fireWeapon);

                var fire = Z.Toggle("Fire Weapon",
                    zoeHasWeapons ? "Fire the selected weapon when this step's clip reaches its trigger point."
                                  : "This Zoe has no weapons configured.",
                    step.fireWeapon, v =>
                    {
                        Dial("Edit Clips", () =>
                        {
                            step.fireWeapon = v;
                            // The pre-port window wrote the dropdown's current selection back every repaint,
                            // so arming a step implicitly defaulted its trigger to the first option. Retained
                            // controls only fire on real user change, so do that defaulting explicitly here.
                            if (v && current < 0 && options.Count > 0) ApplyTrigger(step, options[0]);
                        });
                        RebuildBody();
                    });
                fire.SetEnabled(zoeHasWeapons);

                root.Add(Z.Row(fire, trigger, Z.Flexible()));
                if (index < entry.clips.Count - 1) root.Add(Z.VSpace(3f));
            }

            root.Add(Z.VSpace());
            var add = Z.Button("Add clip", "Append another clip to the end of the list.", null);
            add.clicked += () =>
            {
                var menu = Z.Menu(add);
                foreach (var name in clipNames)
                {
                    var captured = name;
                    menu.Item(captured, "Append this clip as a new step.", () =>
                    {
                        Dial("Add Clip Step", () => entry.clips.Add(new ClipStep { clip = captured }));
                        RebuildBody();
                    });
                }
                menu.Show();
            };
            var clear = Z.Button("Clear", "Remove every clip step.", () =>
            {
                Dial("Clear Clips", () => entry.clips.Clear());
                RebuildBody();
            }).W(56f);
            clear.SetEnabled(entry.clips.Count > 0);
            root.Add(Z.Row(add, clear, Z.Flexible()));
            return root;
        }

        static void ApplyTrigger(ClipStep step, string picked)
        {
            if (string.IsNullOrEmpty(picked)) return;
            if (picked.StartsWith("Event: "))
            { step.muzzleEventName = picked.Substring("Event: ".Length); step.muzzleLayerId = ""; }
            else if (picked.StartsWith("Layer: "))
            { step.muzzleLayerId = picked.Substring("Layer: ".Length); step.muzzleEventName = ""; }
        }

        // ── shared layout helper ────────────────────────────────────────────────────────────
        /// A Z.Row that wraps instead of overflowing — the retained-mode answer to IMGUI's computed row
        /// budgets (same helper Pyre's port uses).
        static VisualElement WrapRow(params VisualElement[] children)
        {
            var row = Z.Row(children);
            row.style.flexWrap = Wrap.Wrap;
            return row;
        }
    }
}
