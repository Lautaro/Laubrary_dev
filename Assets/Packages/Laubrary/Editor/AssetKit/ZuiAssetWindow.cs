using System;
using System.Collections.Generic;
using System.IO;
using Laubrary.Zui;
using UnityEditor;
using UnityEditor.UIElements;   // ObjectField — the asset field the toolbar sizes to its own name (T-0309)
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.AssetKit.Editor
{
    /// <summary>
    /// UI Toolkit counterpart of <see cref="LaubraryAssetWindow{T}"/> — the base for an editor window
    /// that edits ONE ScriptableObject type <typeparamref name="T"/>, providing the same asset chrome:
    ///   • a toolbar — assign, New (inline name prompt), Duplicate, Rename, Delete, Browse;
    ///   • a thumbnail-grid browser of every <typeparamref name="T"/> in the project;
    ///   • the browser shown automatically whenever no asset is selected.
    /// Subclasses implement <see cref="BuildAsset"/> (retained-mode; rebuilt on asset change and on
    /// undo/redo) instead of the IMGUI base's per-frame DrawAsset. Both bases stay available while
    /// tools migrate — they share <see cref="AssetLibrary{T}"/> for all actual asset CRUD.
    /// </summary>
    public abstract class ZuiAssetWindow<T> : ZuiWindow where T : ScriptableObject
    {
        [SerializeField] protected T asset;      // the asset currently being edited (null → browser)
        [SerializeField] bool browsing;          // browser explicitly toggled on (also shown when asset == null)

        // transient inline-prompt state
        bool creating; string createText = "";
        bool renaming; string renameText = "";

        // browser state
        List<T> _browse;
        readonly Dictionary<T, Texture2D> _thumbs = new Dictionary<T, Texture2D>();
        readonly List<Image> _thumbImages = new List<Image>();
        // Off by default — a browser full of animatable assets (e.g. 20 Pyres) shouldn't all animate at
        // once: overwhelming to look at, and expensive (a tool's UpdateAnimatedThumbnail/an asset's own
        // IVisualPreview.UpdateAnimatedPreview can be a real re-render). Off = static, hover to preview.
        [SerializeField] bool _animateAllPreviews;
        T _hoveredThumb;

        // ── override points (same contract as the IMGUI base) ───────────────────────
        /// Build the per-asset editor into <paramref name="root"/>. Only called when an asset is
        /// selected (never null). Rebuilt from scratch on asset change and undo/redo.
        protected abstract void BuildAsset(VisualElement root, T asset);
        protected virtual string TypeLabel => typeof(T).Name;
        protected virtual string DefaultFolder => "Assets";
        protected virtual string NewAssetName => "New " + typeof(T).Name;
        /// Return a FRESH preview texture for the browser (this base owns and destroys it). Null → Unity asset icon.
        protected virtual Texture2D RenderThumbnail(T item) => null;
        protected virtual bool AnimateThumbnails => false;
        protected virtual void UpdateAnimatedThumbnail(T item, Texture2D tex, double time) { }
        /// Called whenever the edited asset changes (assign / create / duplicate / browse pick).
        protected virtual void OnAssetChanged() { }
        /// Seed a freshly-created asset with tool-specific default content.
        protected virtual void InitializeNewAsset(T item) { }
        protected virtual float CellSize => 104f;
        protected virtual float ThumbSize => 92f;

        protected T Current => asset;
        protected bool IsBrowsing => browsing;

        /// The Tags section built by BuildUI for the current asset (null when no asset is selected/saved).
        /// Rebuilt every BuildUI call; BuildUI sets this BEFORE calling BuildAsset, so a subclass's own
        /// BuildAsset override can read it — e.g. to include it in its own ZuiSectionToggleBar.
        protected ZuiSection TagsSection { get; private set; }

        /// True (default) — the base places the built Tags section into root itself, right after the
        /// toolbar/create/rename rows, exactly as every current subclass (Pyre included) already relies on.
        /// Override false when a subclass's own chrome needs Tags placed somewhere else in its layout (e.g.
        /// below a toggle bar, so folding Tags never moves the bar above it — T-0187, ShaperWindow); the
        /// base still BUILDS the section into TagsSection either way, it just stops adding it to root, and
        /// the subclass becomes responsible for adding TagsSection into its own layout during BuildAsset.
        protected virtual bool AutoInsertTagsSection => true;

        protected void SetAsset(T next)
        {
            if (ReferenceEquals(asset, next)) return;
            asset = next; creating = false; renaming = false;
            OnAssetChanged();
            Rebuild();
        }

        protected void RefreshBrowse()
        {
            ClearThumbs();
            _browse = AssetLibrary<T>.Enumerate();
        }

        // ── lifecycle ───────────────────────────────────────────────────────────────
        const double ThumbAnimateInterval = 1.0 / 12.0;
        double _lastThumbTick;
        Action _unwatchInvalidation;

        protected virtual void OnEnable()
        {
            EditorApplication.projectChanged += OnProjectChanged;
            if (AnimateThumbnails) EditorApplication.update += TickThumbAnimation;
            // So an edited asset's browser thumbnail refreshes immediately instead of showing a stale
            // render until the window is reopened — see LauAssetGridGUI.WatchInvalidation.
            _unwatchInvalidation = LauAssetGridGUI.WatchInvalidation(_thumbs, Rebuild);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            EditorApplication.projectChanged -= OnProjectChanged;
            if (AnimateThumbnails) EditorApplication.update -= TickThumbAnimation;
            _unwatchInvalidation?.Invoke();
            ClearThumbs();
        }

        void OnProjectChanged() { RefreshBrowse(); Rebuild(); }

        void TickThumbAnimation()
        {
            if (!AnimateThumbnails || !(asset == null || browsing) || _thumbs.Count == 0) return;
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastThumbTick < ThumbAnimateInterval) return;
            _lastThumbTick = now;
            foreach (var kv in _thumbs)
            {
                if (kv.Value == null) continue;
                if (!_animateAllPreviews && !EqualityComparer<T>.Default.Equals(kv.Key, _hoveredThumb)) continue;
                UpdateAnimatedThumbnail(kv.Key, kv.Value, now);
            }
            foreach (var img in _thumbImages) img?.MarkDirtyRepaint();
        }

        // ── window build ────────────────────────────────────────────────────────────
        protected sealed override void BuildUI(VisualElement root)
        {
            root.Add(BuildToolbar());
            if (creating) root.Add(BuildCreateRow());
            if (renaming && !string.IsNullOrEmpty(AssetLibrary<T>.PathOf(asset))) root.Add(BuildRenameRow());

            // Tags for the selected, saved asset — a single IMGUI island (LauTagField/LauTagPicker are IMGUI-only;
            // there is no UITK tag control), placed once here in the base so EVERY ZuiAssetWindow subclass surfaces
            // tags automatically — restoring the parity the IMGUI LaubraryAssetWindow base had (it drew LauTagField
            // in its toolbar). Same LauTagLibrary GUID side-table, so assets tag/filter identically.
            // T-0065: wrapped in a normal ZuiSection (green header, foldable) instead of a bare island, so it
            // reads as one of the tool's sections and — via the TagsSection field below — can be included in a
            // subclass's own ZuiSectionToggleBar right alongside its other sections.
            TagsSection = null;
            if (asset != null && !string.IsNullOrEmpty(AssetLibrary<T>.PathOf(asset)))
            {
                // Says only what is true HERE. The tags go into the shared LauTagLibrary side-table, and
                // LauAssetBrowser (the IMGUI browser) does filter by them through LauTagFilter — but THIS
                // window's own grid (BuildBrowser below) enumerates every asset and has no tag filter at all,
                // so the old "filterable in the browser" pointed a reader at an affordance this browser does
                // not have. Measured live: tagging round-trips, and no control in this window mentions tags
                // or filtering outside this section.
                var section = Z.Section("Tags",
                    "Tags for this asset, shared with the rest of Laubrary. This window's own browser lists "
                    + "every asset and does not filter by them.");
                var tagIsland = new IMGUIContainer(() => LauTagField.Draw(asset));
                tagIsland.style.flexShrink = 0f;
                section.Add(tagIsland);
                if (AutoInsertTagsSection) root.Add(section);
                TagsSection = section;
            }

            if (asset == null || browsing) root.Add(BuildBrowser());
            else
            {
                var host = new VisualElement();
                host.style.flexGrow = 1f;
                host.style.minHeight = 0f;   // flexbox: without this, content height becomes a floor and overflows the window
                BuildAsset(host, asset);
                root.Add(host);
            }
        }

        protected override void OnBeforeRebuild() => _thumbImages.Clear();

        // ── unsaved state (T-0258) ──────────────────────────────────────────────────
        //
        // Every ZuiAssetWindow edits a ScriptableObject through Undo + EditorUtility.SetDirty, and NONE of them
        // had a way to save it or a way to see that it needed saving: persistence depended entirely on Unity's
        // own Ctrl+S or the quit prompt, which the window never surfaced, never labelled and never showed the
        // state of. An author who closed the editor after an hour of dialling had no on-screen reason to think
        // anything was pending. This is in the SHARED base rather than in one tool because it is the same
        // omission in every one of them — Pyre, Shaper, Choreographer and the rest all gain it at once.
        //
        // Both halves are PERMANENTLY present and only change what they SAY (the dot flips `visibility`, which
        // keeps its layout space, and the button flips enabled): a toolbar that grew a control the moment the
        // asset went dirty would reflow the row under the pointer, which is exactly what "Stable workspace"
        // forbids.
        VisualElement _dirtyDot;
        Button _saveButton;
        ObjectField _assetField;

        bool AssetIsDirty => asset != null && EditorUtility.IsDirty(asset);

        void RefreshSaveAffordance()
        {
            if (_dirtyDot != null)
            {
                _dirtyDot.style.visibility = AssetIsDirty ? Visibility.Visible : Visibility.Hidden;
                _dirtyDot.tooltip = $"This {TypeLabel} has unsaved edits. Press Save to write them to disk.";
            }
            if (_saveButton != null)
            {
                _saveButton.SetEnabled(AssetIsDirty);
                _saveButton.tooltip = asset == null
                    ? $"No {TypeLabel} is open, so there is nothing to save."
                    : AssetIsDirty
                        ? $"Write this {TypeLabel}'s edits to disk now."
                        : $"This {TypeLabel} matches what is on disk — nothing to save.";
            }
            RefreshAssetFieldName();
        }

        /// T-0309 — the asset field showed its name CLIPPED in every Laubrary asset window: Unity draws an
        /// ObjectField as "Name (Type)", and `ShaperDemoDoc (Shaper Document)` measured 210.2px against the
        /// 161.8px the 200px field leaves its label, so anything past ~14 characters was cut off. Two halves,
        /// both measured rather than guessed, and neither buys width from the buttons beside it:
        ///
        ///   • The "(Type)" suffix is dropped. Every asset this window can hold is a <typeparamref name="T"/>
        ///     — the window itself is the type label — so the parenthetical is 110px of redundancy in the one
        ///     place the row has none to spare. Unity rewrites the display label on every value change, so it
        ///     is re-stamped here, off the 250ms poll the dirty dot already runs.
        ///   • The field then SIZES TO ITS CONTENT instead of sitting at a fixed 200. The row already ends in
        ///     a Z.Flexible spacer holding 197.3px of slack at the declared 820px minimum window (measured,
        ///     T-0307/T-0312), so growing the field takes space from the spacer, never from Delete: flex-grow
        ///     shares that spare, and a max-width of exactly what the name needs stops the field from growing
        ///     past its own content on a wide window. `minWidth` keeps the empty state at the old 200px.
        ///
        /// The max-width write is gated by the same 1.5px absolute tolerance StampFieldHeight uses, and for
        /// the same reason: Yoga rounds to the device-pixel grid, so a write that MOVES the row can change the
        /// measurement it came from, and a relative epsilon re-writes forever (T-0304's layout-struggle loop).
        void RefreshAssetFieldName()
        {
            if (_assetField == null) return;
            var label = _assetField.Q<Label>(className: "unity-object-field-display__label");
            if (label == null) return;

            string want = asset == null ? $"None ({TypeLabel})" : asset.name;
            if (label.text != want) label.text = want;

            float need = label.MeasureTextSize(want, 0f, VisualElement.MeasureMode.Undefined,
                                                     0f, VisualElement.MeasureMode.Undefined).x;
            float have = label.contentRect.width;
            if (float.IsNaN(need) || float.IsNaN(have) || have <= 0f) return;
            // Everything in the field that is not the name: the type icon, the picker button, padding.
            float chrome = _assetField.resolvedStyle.width - have;
            if (float.IsNaN(chrome) || chrome < 0f) return;
            float px = Mathf.Max(AssetFieldMinWidth, need + chrome + 2f);
            var cur = _assetField.style.maxWidth;
            if (cur.keyword == StyleKeyword.Undefined && Mathf.Abs(cur.value.value - px) <= 1.5f) return;
            _assetField.style.maxWidth = px;
        }

        const float AssetFieldMinWidth = 200f;

        VisualElement BuildToolbar()
        {
            // Sits immediately after the asset field, so "which asset" and "is it saved" read as one statement.
            _dirtyDot = Z.Text("●", ZuiText.Body, $"This {TypeLabel} has unsaved edits.");
            _dirtyDot.style.width = 12f;
            _dirtyDot.style.flexShrink = 0f;
            // T-0307 — the default Label padding (0.89 left + 2.22 right at this UI scale) left 8.89px of
            // content for an 11.11px glyph, so the dot was drawn clipped in every asset window. Zeroing the
            // padding rather than widening the slot: the toolbar row is NoWrap and has only 10.2px of slack
            // at the declared 820px minimum window, so buying width here would push Delete off the edge.
            _dirtyDot.style.paddingLeft = 0f;
            _dirtyDot.style.paddingRight = 0f;
            _dirtyDot.style.unityTextAlign = TextAnchor.MiddleCenter;

            _saveButton = Z.Button("Save", $"Write this {TypeLabel}'s edits to disk now.", () =>
            {
                if (asset == null) return;
                AssetDatabase.SaveAssetIfDirty(asset);
                RefreshSaveAffordance();
            });
            _saveButton.style.width = 52f;

            _assetField = Z.Object<T>(asset,
                $"The {TypeLabel} asset being edited — assign one directly, or use Browse.",
                v => SetAsset(v), AssetFieldMinWidth);
            // See RefreshAssetFieldName: the field grows into the row's own flexible spare width (and no
            // further than its content needs), instead of clipping every name past ~14 characters at 200px.
            _assetField.style.flexGrow = 1f;
            _assetField.style.minWidth = AssetFieldMinWidth;

            var row = Z.Row(
                _assetField,
                _dirtyDot,
                _saveButton,
                Z.Button("New", $"Create a brand new {TypeLabel} asset (undoable).",
                    () => { creating = true; renaming = false; createText = NewAssetName; Rebuild(); }),
                Z.Button(browsing ? "Close browser" : "Browse",
                    $"Toggle the thumbnail browser of every {TypeLabel} in the project.",
                    () => { browsing = !browsing; creating = false; renaming = false; if (browsing) RefreshBrowse(); Rebuild(); }));

            string p = AssetLibrary<T>.PathOf(asset);
            if (!string.IsNullOrEmpty(p))
            {
                row.Add(Z.Button("Duplicate", $"Create a copy of this {TypeLabel} next to it and switch to editing the copy (undoable).", () =>
                {
                    var d = AssetLibrary<T>.Duplicate(asset);
                    if (d != null)
                    {
                        Undo.RegisterCreatedObjectUndo(d, "Duplicate " + TypeLabel);
                        SetAsset(d);
                        if (browsing) { RefreshBrowse(); Rebuild(); }
                    }
                }));
                row.Add(Z.Button("Rename", "Rename this asset's file.", () =>
                {
                    renaming = !renaming; creating = false;
                    renameText = Path.GetFileNameWithoutExtension(AssetLibrary<T>.PathOf(asset));
                    Rebuild();
                }));
                row.Add(Z.Button("Delete", "Delete this asset's file (asks first — file deletion cannot be undone).",
                    DeleteCurrent));
            }
            // T-0309 — the trailing Z.Flexible() that used to end this row is gone. It existed only to hold
            // the row's tail, and a Row already leaves its leftover space at the end (justify-content is
            // flex-start), so it changed nothing visually — but it took an equal share of the spare width
            // with the asset field, which halved how much of a long name the field could show at the 820px
            // minimum (measured: a 49-character name got 260.4px of 307.1 with the spacer competing, 309.3
            // without). The field is now the row's only flexible item and is capped at its own content, so
            // it can never grow past the name it is showing and can never push Delete off the edge.

            // Dirtiness is set from wherever an edit happens (a dial callback, a nested control's own hook, an
            // Undo), not from a place this base can hook, so the state is POLLED rather than pushed. 4/second is
            // below the threshold at which a change reads as delayed and far above what an EditorWindow costs.
            RefreshSaveAffordance();
            row.schedule.Execute(RefreshSaveAffordance).Every(250);
            return row;
        }

        VisualElement BuildCreateRow()
        {
            // T-0321 — say WHERE. FolderForNew() puts the new asset beside the one currently open and falls back
            // to DefaultFolder only when nothing is open, so the destination moves with whatever the author last
            // browsed to — and nothing in this row said so. Measured live: with the shipped ShaperDemo document
            // open, pressing New wrote the new document into Assets/Demos/ShaperDemo, a package demo folder,
            // silently. The folder is the operand of this row, not an explanation of it, so it belongs in the
            // tooltips of the field you type into and the button that acts.
            // T-0322 — and say it for the state it is IN. "Beside the one currently open" is false on the empty
            // state, which is where New is pressed most: nothing is open, so the destination is the default
            // folder. Measured live in Cartographer with 0 Levels in the project, where the row promised a
            // folder "beside the Level that is currently open" with no Level open at all.
            string folder = FolderForNew();
            bool beside = !string.IsNullOrEmpty(AssetLibrary<T>.PathOf(asset));
            string where = beside
                ? $"Creates {folder}/<name>.asset — beside the {TypeLabel} that is currently open."
                : $"Creates {folder}/<name>.asset — this tool's default folder, since no {TypeLabel} is open.";
            TextField nameField = Z.TextInput(createText, "File name for the new asset. " + where,
                v => createText = v, 200f);
            void Confirm()
            {
                var created = AssetLibrary<T>.Create(createText, FolderForNew());
                creating = false;
                if (created != null)
                {
                    InitializeNewAsset(created);
                    EditorUtility.SetDirty(created);
                    // T-0276 — flush THIS asset, never the project. AssetDatabase.SaveAssets() writes every
                    // dirty asset in the project, so pressing New here also published whatever unsaved edits
                    // another window happened to be holding — measured as a real incident during T-0265,
                    // where the Shaper demo document was written to disk by a task that never touched it.
                    AssetDatabase.SaveAssetIfDirty(created);
                    Undo.RegisterCreatedObjectUndo(created, "Create " + TypeLabel);
                    browsing = false;
                    SetAsset(created);
                }
                else Rebuild();
            }
            nameField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Confirm(); e.StopPropagation(); }
            });
            nameField.schedule.Execute(() => nameField.Focus());

            return Z.Row(
                Z.Text("Asset name", ZuiText.Body, "File name for the new asset. " + where),
                nameField,
                Z.Button("Create", "Create the asset with this name. " + where, Confirm),
                Z.Button("Cancel", "Abandon creating a new asset.", () => { creating = false; Rebuild(); }));
        }

        VisualElement BuildRenameRow()
        {
            TextField nameField = Z.TextInput(renameText, "New file name for this asset.", v => renameText = v, 200f);
            void Confirm()
            {
                AssetLibrary<T>.Rename(asset, renameText);
                renaming = false;
                if (browsing) RefreshBrowse();
                Rebuild();
            }
            nameField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Confirm(); e.StopPropagation(); }
            });
            nameField.schedule.Execute(() => nameField.Focus());

            return Z.Row(
                Z.Text("New name", ZuiText.Body, "New file name for this asset."),
                nameField,
                Z.Button("OK", "Apply the rename.", Confirm),
                Z.Button("Cancel", "Keep the current name.", () => { renaming = false; Rebuild(); }));
        }

        void DeleteCurrent()
        {
            string path = AssetLibrary<T>.PathOf(asset);
            if (string.IsNullOrEmpty(path)) return;
            if (!EditorUtility.DisplayDialog($"Delete {TypeLabel}",
                    $"Delete '{Path.GetFileName(path)}'? This cannot be undone.", "Delete", "Cancel")) return;
            if (AssetLibrary<T>.Delete(asset)) { SetAsset(null); if (browsing) RefreshBrowse(); Rebuild(); }
        }

        string FolderForNew()
        {
            string cur = AssetLibrary<T>.PathOf(asset);
            return !string.IsNullOrEmpty(cur) ? Path.GetDirectoryName(cur).Replace('\\', '/') : DefaultFolder;
        }

        // ── browser ─────────────────────────────────────────────────────────────────
        VisualElement BuildBrowser()
        {
            if (_browse == null) RefreshBrowse();

            var col = new VisualElement();
            col.style.flexGrow = 1f;

            var headerRow = Z.Row(
                Z.Text($"{TypeLabel} library ({_browse.Count})", ZuiText.Section,
                    $"Every {TypeLabel} asset found in the project."),
                Z.Flexible());
            if (AnimateThumbnails)
                headerRow.Add(Z.Toggle("▶ Animate all",
                    "On: every animated preview plays at once. Off: previews stay static — hover one to preview it.",
                    _animateAllPreviews, v => { _animateAllPreviews = v; if (!v) _hoveredThumb = default; }));
            headerRow.Add(Z.Button("Refresh", "Re-scan the project for assets.", () => { RefreshBrowse(); Rebuild(); }));
            col.Add(headerRow);

            if (_browse.Count == 0)
                col.Add(Z.Text($"No {TypeLabel} assets yet — hit New to make one.", ZuiText.Subtle));

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            scroll.Add(grid);
            // "A LauAsset that is a VISUAL asset must guarantee a thumbnail; a LauAsset that is not must not
            // reserve space for one" — an empty square takes the width, adds nothing, and reads as "this has a
            // picture and it failed to load". Whether this TYPE has a picture at all is a property of the type,
            // not of one asset, so it is decided ONCE per browser: if nothing in the library resolves a
            // thumbnail there is no picture to show and the grid is a list of names; if some do, every cell
            // keeps its slot so the one that failed still reads as a failure rather than silently shrinking.
            bool anyThumb = false;
            foreach (var item in _browse)
                if (item != null && Thumb(item) != null) { anyThumb = true; break; }
            foreach (var item in _browse)
                if (item != null) grid.Add(BuildCell(item, anyThumb));
            col.Add(scroll);
            return col;
        }

        VisualElement BuildCell(T item, bool reserveThumb)
        {
            var cell = new VisualElement();
            cell.AddToClassList("zui-cell");
            // With a picture the cell is a fixed square so the grid reads as a grid; without one it is its own
            // NAME and sizes to it, which is also what stops a 19-character name being clipped to 100px by a
            // width that only existed to line thumbnails up.
            if (reserveThumb) cell.style.width = CellSize;
            else cell.AddToClassList("zui-cell--nothumb");
            bool selected = ReferenceEquals(asset, item);
            if (selected) cell.AddToClassList("zui-cell--selected");
            // Says what the two clicks actually DO. A single click already binds this asset to the window
            // (SetAsset below) — there is no select-without-open state to promise — and the only thing the
            // second click adds is closing the browser, which the old wording ("click to select, double-click
            // to open") described as the difference between looking and opening.
            cell.tooltip = $"{item.name} — click to open it here, double-click to open it and close the browser.";

            if (reserveThumb)
            {
                var thumbBox = new VisualElement();
                thumbBox.AddToClassList("zui-cell__thumb");
                thumbBox.style.width = ThumbSize;
                thumbBox.style.height = ThumbSize;

                var tex = Thumb(item);
                if (tex != null)
                {
                    var img = new Image { image = tex, scaleMode = ScaleMode.ScaleToFit };
                    img.style.width = ThumbSize - 6f;
                    img.style.height = ThumbSize - 6f;
                    thumbBox.Add(img);
                    if (_thumbs.ContainsKey(item)) _thumbImages.Add(img);
                    img.RegisterCallback<PointerEnterEvent>(_ => SetHoveredThumb(item));
                    img.RegisterCallback<PointerLeaveEvent>(_ =>
                    {
                        if (EqualityComparer<T>.Default.Equals(_hoveredThumb, item)) SetHoveredThumb(null);
                    });
                }
                cell.Add(thumbBox);
            }

            var name = new Label(item.name);
            name.AddToClassList("zui-cell__name");
            if (reserveThumb) name.style.maxWidth = CellSize - 4f;
            cell.Add(name);

            cell.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                bool open = e.clickCount == 2;
                if (open) browsing = false;
                if (ReferenceEquals(asset, item)) Rebuild(); else SetAsset(item);
                e.StopPropagation();
            });
            return cell;
        }

        Texture2D Thumb(T item)
        {
            if (_thumbs.TryGetValue(item, out var t) && t != null) return t;

            // A subclass's own RenderThumbnail wins, then the asset's own IVisualPreview, then Unity's icon.
            //
            // The IVisualPreview step was missing here while LaubraryAssetWindow (the older browser base) had
            // it, so every ZuiAssetWindow-based browser showed blank cards for assets that could perfectly
            // well draw themselves — Zoe, WeaponDef and AmmoDef all implement the interface and all rendered
            // nothing. Asking the asset is the whole point of the interface: it exists so ANY consumer can
            // show a preview without a per-window override.
            var tex = RenderThumbnail(item);
            if (tex == null && item is Laubrary.PreviewKit.IVisualPreview vis) tex = vis.RenderPreviewTexture();
            if (tex != null) { _thumbs[item] = tex; return tex; }

            return AssetPreview.GetAssetPreview(item);   // Unity-owned — never cached or destroyed here
        }

        // Called on pointer enter/leave in hover-to-preview mode (ignored entirely while _animateAllPreviews
        // is on). Leaving an item resets its cached texture's PIXELS back to a fresh static frame — the
        // Image element keeps referencing the same Texture2D instance, so no rebuild is needed, matching
        // the "mutate in place" contract UpdateAnimatedThumbnail already uses.
        void SetHoveredThumb(T item)
        {
            if (EqualityComparer<T>.Default.Equals(_hoveredThumb, item)) return;
            var outgoing = _hoveredThumb;
            _hoveredThumb = item;
            if (_animateAllPreviews || outgoing == null) return;
            if (_thumbs.TryGetValue(outgoing, out var tex) && tex != null) ResetThumbToStatic(outgoing, tex);
        }

        void ResetThumbToStatic(T item, Texture2D tex)
        {
            Texture2D fresh = RenderThumbnail(item);
            if (fresh == null && item is Laubrary.PreviewKit.IVisualPreview vis) fresh = vis.RenderPreviewTexture();
            if (fresh == null) return;
            if (fresh.width != tex.width || fresh.height != tex.height) tex.Reinitialize(fresh.width, fresh.height);
            tex.SetPixels32(fresh.GetPixels32());
            tex.Apply();
            DestroyImmediate(fresh);
        }

        void ClearThumbs()
        {
            foreach (var t in _thumbs.Values) if (t != null) DestroyImmediate(t);
            _thumbs.Clear();
            _thumbImages.Clear();
            _hoveredThumb = default;
        }
    }
}
