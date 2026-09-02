using System;
using System.Collections.Generic;
using System.IO;
using Laubrary.Zui;
using UnityEditor;
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
                var section = Z.Section("Tags", "Tags for this asset — filterable in the browser.");
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

        VisualElement BuildToolbar()
        {
            var row = Z.Row(
                Z.Object<T>(asset, $"The {TypeLabel} asset being edited — assign one directly, or use Browse.",
                    v => SetAsset(v), 200f),
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
            row.Add(Z.Flexible());
            return row;
        }

        VisualElement BuildCreateRow()
        {
            TextField nameField = Z.TextInput(createText, "File name for the new asset.", v => createText = v, 200f);
            void Confirm()
            {
                var created = AssetLibrary<T>.Create(createText, FolderForNew());
                creating = false;
                if (created != null)
                {
                    InitializeNewAsset(created);
                    EditorUtility.SetDirty(created);
                    AssetDatabase.SaveAssets();
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
                Z.Text("Asset name", ZuiText.Body, "File name for the new asset."),
                nameField,
                Z.Button("Create", "Create the asset with this name.", Confirm),
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
            foreach (var item in _browse)
                if (item != null) grid.Add(BuildCell(item));
            col.Add(scroll);
            return col;
        }

        VisualElement BuildCell(T item)
        {
            var cell = new VisualElement();
            cell.AddToClassList("zui-cell");
            cell.style.width = CellSize;
            bool selected = ReferenceEquals(asset, item);
            if (selected) cell.AddToClassList("zui-cell--selected");
            cell.tooltip = $"{item.name} — click to select, double-click to open.";

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

            var name = new Label(item.name);
            name.AddToClassList("zui-cell__name");
            name.style.maxWidth = CellSize - 4f;
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
