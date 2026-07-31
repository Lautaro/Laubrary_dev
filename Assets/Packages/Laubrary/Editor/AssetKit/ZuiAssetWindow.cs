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

        protected virtual void OnEnable()
        {
            EditorApplication.projectChanged += OnProjectChanged;
            if (AnimateThumbnails) EditorApplication.update += TickThumbAnimation;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            EditorApplication.projectChanged -= OnProjectChanged;
            if (AnimateThumbnails) EditorApplication.update -= TickThumbAnimation;
            ClearThumbs();
        }

        void OnProjectChanged() { RefreshBrowse(); Rebuild(); }

        void TickThumbAnimation()
        {
            if (!AnimateThumbnails || !(asset == null || browsing) || _thumbs.Count == 0) return;
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastThumbTick < ThumbAnimateInterval) return;
            _lastThumbTick = now;
            foreach (var kv in _thumbs) if (kv.Value != null) UpdateAnimatedThumbnail(kv.Key, kv.Value, now);
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
            if (asset != null && !string.IsNullOrEmpty(AssetLibrary<T>.PathOf(asset)))
            {
                var tagIsland = new IMGUIContainer(() => LauTagField.Draw(asset));
                tagIsland.style.flexShrink = 0f;
                root.Add(tagIsland);
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

            col.Add(Z.Row(
                Z.Text($"{TypeLabel} library ({_browse.Count})", ZuiText.Section,
                    $"Every {TypeLabel} asset found in the project."),
                Z.Flexible(),
                Z.Button("Refresh", "Re-scan the project for assets.", () => { RefreshBrowse(); Rebuild(); })));

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

        void ClearThumbs()
        {
            foreach (var t in _thumbs.Values) if (t != null) DestroyImmediate(t);
            _thumbs.Clear();
            _thumbImages.Clear();
        }
    }
}
