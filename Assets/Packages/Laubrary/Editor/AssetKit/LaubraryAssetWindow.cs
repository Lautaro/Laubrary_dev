using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Laubrary.PreviewKit;
using Object = UnityEngine.Object;

namespace Laubrary.AssetKit.Editor
{
    /// <summary>
    /// Base for a ZUI editor window that edits ONE ScriptableObject type <typeparamref name="T"/>. It provides,
    /// for free, the asset-management chrome every such tool wants:
    ///   • a toolbar — assign (ObjectField), New (inline name prompt), Duplicate, Rename, Delete, Browse;
    ///   • a thumbnail-grid browser of every <typeparamref name="T"/> in the project;
    ///   • and it shows that browser automatically whenever no asset is selected.
    /// Subclasses implement <see cref="DrawAsset"/> (the per-asset editor body they used to put in OnZUI) and may
    /// override <see cref="RenderThumbnail"/> to give the browser real previews instead of Unity's asset icon.
    /// Extracted from Pyre, which hand-rolled all of this; adopted by Larder, Choreographer, SpriteCatalog, Lazor…
    /// </summary>
    public abstract class LaubraryAssetWindow<T> : ZUIWindow where T : ScriptableObject
    {
        [SerializeField] protected T asset;      // the asset currently being edited (null → browser)
        [SerializeField] bool browsing;          // browser explicitly toggled on (also shown when asset == null)

        // transient inline-prompt state
        bool creating; string createText = ""; bool focusNew;
        bool renaming; string renameText = "";

        // browser state
        List<T> _browse;
        readonly Dictionary<Object, Texture2D> _thumbs = new Dictionary<Object, Texture2D>();
        Vector2 _browseScroll;
        Vector2 _assetScroll;
        // Off by default — a browser full of animatable assets (e.g. 20 Pyres) shouldn't all animate at
        // once, which is both overwhelming to look at and expensive (each opts-in tool's own
        // UpdateAnimatedThumbnail can be a real re-render, not a cheap sprite-sheet flip). Off = every
        // thumbnail sits on its static frame except whichever one the mouse is over.
        [SerializeField] bool _animateAllPreviews;
        Object _hoveredThumb;

        // ── override points ─────────────────────────────────────────────────────────────
        /// Draw the per-asset editor. Only called when an asset is selected (never null).
        protected abstract void DrawAsset(T asset);
        /// Heading label for the type in the toolbar/browser. Defaults to the type name.
        protected virtual string TypeLabel => typeof(T).Name;
        /// Folder a brand-new asset lands in when there's no current asset to sit beside.
        protected virtual string DefaultFolder => "Assets";
        /// Default name offered in the New prompt.
        protected virtual string NewAssetName => "New " + typeof(T).Name;
        /// Return a FRESH preview texture for the browser (this base owns and destroys it). Null → Unity asset icon.
        protected virtual Texture2D RenderThumbnail(T item) => null;
        /// When true, this tool's browser thumbnails refresh periodically (via <see cref="UpdateAnimatedThumbnail"/>)
        /// instead of being cached forever after the first <see cref="RenderThumbnail"/> call. Default false — zero
        /// behaviour change for every other <see cref="LaubraryAssetWindow{T}"/> subclass; only a tool that opts in
        /// pays the cost of periodic re-rendering.
        protected virtual bool AnimateThumbnails => false;
        /// Called periodically (while the browser is visible) for a thumbnail this tool opted into animating —
        /// update <paramref name="tex"/>'s pixels IN PLACE however the tool likes (e.g. SetPixels32 + Apply for a
        /// different baked frame each call). <paramref name="time"/> is EditorApplication.timeSinceStartup, so the
        /// tool can derive its own frame index without tracking a timer itself. Only invoked when
        /// <see cref="AnimateThumbnails"/> is true; no-op by default.
        protected virtual void UpdateAnimatedThumbnail(T item, Texture2D tex, double time) { }
        /// Called whenever the edited asset changes (assign / create / duplicate / browse pick). Reset caches here.
        protected virtual void OnAssetChanged() { }
        /// Seed a freshly-created asset with tool-specific default content (e.g. example layers). Called once, right
        /// after New creates the asset and before it's saved.
        protected virtual void InitializeNewAsset(T item) { }
        protected virtual float CellSize => 104f;
        protected virtual float ThumbSize => 92f;

        /// The asset currently being edited (read-only for subclasses; change it via <see cref="SetAsset"/>).
        protected T Current => asset;
        protected bool IsBrowsing => browsing;

        /// Switch the edited asset (routes through OnAssetChanged + repaint). Pass null to clear → browser.
        protected void SetAsset(T next)
        {
            if (ReferenceEquals(asset, next)) return;
            asset = next; creating = false; renaming = false;
            OnAssetChanged();
            Repaint();
        }

        /// Force the browser to re-scan the project (call after external asset changes if needed).
        protected void RefreshBrowse()
        {
            ClearThumbs();
            _browse = AssetLibrary<T>.Enumerate();
        }

        // ── window loop ─────────────────────────────────────────────────────────────────
        [System.NonSerialized] bool _hookedProjectChange;
        [System.NonSerialized] bool _hookedThumbAnimation;
        [System.NonSerialized] double _lastThumbTick;
        [System.NonSerialized] System.Action _unwatchInvalidation;
        const double ThumbAnimateInterval = 1.0 / 12.0;   // a common baked-preview fps; smooth enough, cheap enough

        protected sealed override void OnZUI()
        {
            // Keep the browser in sync with the project: re-scan whenever assets are added/removed/renamed/imported
            // ANYWHERE (Project window, external CRUD), not just via this window's own buttons. Hooked lazily here so
            // it works regardless of whether a subclass overrides OnZUIEnable without calling base.
            if (!_hookedProjectChange) { EditorApplication.projectChanged += OnProjectChanged; _hookedProjectChange = true; }
            // So an edited asset's browser thumbnail refreshes immediately instead of showing a stale
            // render until the window is reopened — see LauAssetGridGUI.WatchInvalidation.
            _unwatchInvalidation ??= LauAssetGridGUI.WatchInvalidation(_thumbs, Repaint);
            // Same lazy-hook pattern for thumbnail animation — only subscribed at all when a subclass opts in via
            // AnimateThumbnails, so every other tool pays zero cost (not even an extra delegate on the update event).
            if (AnimateThumbnails && !_hookedThumbAnimation) { EditorApplication.update += TickThumbAnimation; _hookedThumbAnimation = true; }

            DrawToolbar();
            if (asset == null || browsing) DrawBrowser();
            else
            {
                // The per-asset editor body had no scroll container at all — content taller than the window
                // (routine for a Zoe with several expanded [SerializeReference] sections) just clipped with no
                // way to reach it, no scrollbar shown. Same ScrollView wrapper the browser above already uses.
                using (ScrollView(ref _assetScroll))
                    DrawAsset(asset);
            }
        }

        void OnProjectChanged() { RefreshBrowse(); Repaint(); }

        // Advances every cached thumbnail in place (mutating the SAME Texture2D DrawCell already draws — no
        // change needed there) while the browser is actually visible, throttled to ThumbAnimateInterval so this
        // doesn't try to out-run the editor's own repaint rate for no visual benefit.
        void TickThumbAnimation()
        {
            if (!AnimateThumbnails || !(asset == null || browsing) || _thumbs.Count == 0) return;
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastThumbTick < ThumbAnimateInterval) return;
            _lastThumbTick = now;
            foreach (var kv in _thumbs)
            {
                if (kv.Value == null) continue;
                if (!_animateAllPreviews && !ReferenceEquals(kv.Key, _hoveredThumb)) continue;
                if (kv.Key is T typed) UpdateAnimatedThumbnail(typed, kv.Value, now);
                if (kv.Key is IVisualPreview vp && vp.CanAnimatePreview) vp.UpdateAnimatedPreview(kv.Value, now);
            }
            Repaint();
        }

        protected virtual void OnDisable()
        {
            if (_hookedProjectChange) { EditorApplication.projectChanged -= OnProjectChanged; _hookedProjectChange = false; }
            if (_hookedThumbAnimation) { EditorApplication.update -= TickThumbAnimation; _hookedThumbAnimation = false; }
            _unwatchInvalidation?.Invoke();
            _unwatchInvalidation = null;
            ClearThumbs();
        }

        // This chrome is fully ZUI: ZUI.HRow rows, this.Button/this.Label controls, this.ObjectField/TextField/
        // ScrollView fields. The only raw IMGUI is the hand-painted thumbnail grid (legitimately canvas).
        void DrawToolbar()
        {
            using (var row = ZUI.HRow())
            {
                EditorGUI.BeginChangeCheck();
                var picked = ObjectField(asset, false, 200f);
                if (EditorGUI.EndChangeCheck()) SetAsset(picked);

                if (row.Button("New")) { creating = true; renaming = false; createText = NewAssetName; focusNew = true; }
                if (row.Button(browsing ? "Close browser" : "Browse"))
                { browsing = !browsing; creating = false; renaming = false; if (browsing) RefreshBrowse(); }

                string p = AssetLibrary<T>.PathOf(asset);
                if (!string.IsNullOrEmpty(p))
                {
                    if (row.Button("Duplicate")) { var d = AssetLibrary<T>.Duplicate(asset); if (d != null) { Undo.RegisterCreatedObjectUndo(d, "Duplicate " + TypeLabel); SetAsset(d); if (browsing) RefreshBrowse(); } }
                    if (row.Button("Rename")) { renaming = !renaming; creating = false; renameText = Path.GetFileNameWithoutExtension(p); }
                    if (row.Button("Delete")) DeleteCurrent();
                }
                row.Flexible();
            }

            if (creating) DrawCreateRow();
            if (renaming && !string.IsNullOrEmpty(AssetLibrary<T>.PathOf(asset))) DrawRenameRow();
            if (asset != null && !string.IsNullOrEmpty(AssetLibrary<T>.PathOf(asset))) LauTagField.Draw(asset);
        }

        void DrawCreateRow()
        {
            using (var row = ZUI.HRow())
            {
                row.Label("Asset name", GUILayout.Width(72));
                bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;
                GUI.SetNextControlName("AssetKitNewField");
                createText = TextField(createText, 200f);
                if (focusNew && Event.current.type == EventType.Repaint) { EditorGUI.FocusTextInControl("AssetKitNewField"); focusNew = false; }
                if (row.Button("Create") || enter)
                {
                    var created = AssetLibrary<T>.Create(createText, FolderForNew());
                    creating = false;
                    if (created != null)
                    {
                        InitializeNewAsset(created);
                        EditorUtility.SetDirty(created);
                        AssetDatabase.SaveAssets();
                        Undo.RegisterCreatedObjectUndo(created, "Create " + TypeLabel);   // New is undoable (delete needs the confirm dialog)
                        browsing = false;
                        SetAsset(created);
                    }
                }
                if (row.Button("Cancel")) creating = false;
            }
        }

        void DrawRenameRow()
        {
            using (var row = ZUI.HRow())
            {
                row.Label("New name", GUILayout.Width(72));
                bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;
                renameText = TextField(renameText, 200f);
                if (row.Button("OK") || enter) { AssetLibrary<T>.Rename(asset, renameText); renaming = false; if (browsing) RefreshBrowse(); }
                if (row.Button("Cancel")) renaming = false;
            }
        }

        void DeleteCurrent()
        {
            string path = AssetLibrary<T>.PathOf(asset);
            if (string.IsNullOrEmpty(path)) return;
            if (!EditorUtility.DisplayDialog($"Delete {TypeLabel}",
                    $"Delete '{Path.GetFileName(path)}'? This cannot be undone.", "Delete", "Cancel")) return;
            if (AssetLibrary<T>.Delete(asset)) { SetAsset(null); if (browsing) RefreshBrowse(); }
        }

        string FolderForNew()
        {
            string cur = AssetLibrary<T>.PathOf(asset);
            return !string.IsNullOrEmpty(cur) ? Path.GetDirectoryName(cur).Replace('\\', '/') : DefaultFolder;
        }

        // ── browser ───────────────────────────────────────────────────────────────────
        // Delegates to LauAssetGridGUI — the same grid renderer every "pick a LauAsset" popup uses, so this
        // window's own browse panel and a Recall popup elsewhere look and behave identically, and both get
        // IVisualPreview support for free (RenderThumbnail still takes priority when a subclass overrides it).
        void DrawBrowser()
        {
            if (_browse == null) RefreshBrowse();

            using (var row = ZUI.HRow())
            {
                Label($"{TypeLabel} library ({_browse.Count})", ZUI.ZTextStyle.SectionHeader);
                row.Flexible();
                if (AnimateThumbnails)
                {
                    bool newAll = GUILayout.Toggle(_animateAllPreviews,
                        new GUIContent("▶ Animate all", "On: every animated preview plays at once. Off: previews "
                            + "stay static — hover one to preview it."), EditorStyles.toolbarButton, GUILayout.Width(90));
                    if (newAll != _animateAllPreviews) _animateAllPreviews = newAll;
                }
                if (row.Button("Refresh")) RefreshBrowse();
            }

            if (_browse.Count == 0)
                Label($"No {TypeLabel} assets yet — hit New to make one.", ZUI.ZTextStyle.Subtle);

            using (ScrollView(ref _browseScroll))
            {
                var hovered = LauAssetGridGUI.DrawGrid(position.width, _browse.ConvertAll(t => (Object)t), asset, (item, clickCount) =>
                {
                    bool open = clickCount == 2;
                    SetAsset((T)item);
                    if (open) browsing = false;
                    Repaint();
                }, _thumbs, item => RenderThumbnail((T)item), CellSize, ThumbSize);

                if (!_animateAllPreviews && !ReferenceEquals(hovered, _hoveredThumb)
                    && _hoveredThumb != null && _thumbs.TryGetValue(_hoveredThumb, out var outgoing) && outgoing != null)
                {
                    DestroyImmediate(outgoing);
                    _thumbs.Remove(_hoveredThumb);
                }
                _hoveredThumb = hovered;
            }
        }

        void ClearThumbs() => LauAssetGridGUI.ClearCache(_thumbs);
    }
}
