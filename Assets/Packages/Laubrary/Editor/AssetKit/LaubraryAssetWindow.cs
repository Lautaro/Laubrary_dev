using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

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
        readonly Dictionary<T, Texture2D> _thumbs = new Dictionary<T, Texture2D>();
        Vector2 _browseScroll;

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
        protected sealed override void OnZUI()
        {
            DrawToolbar();
            if (asset == null || browsing) DrawBrowser();
            else DrawAsset(asset);
        }

        protected virtual void OnDisable() => ClearThumbs();

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
                    if (row.Button("Duplicate")) { var d = AssetLibrary<T>.Duplicate(asset); if (d != null) { SetAsset(d); if (browsing) RefreshBrowse(); } }
                    if (row.Button("Rename")) { renaming = !renaming; creating = false; renameText = Path.GetFileNameWithoutExtension(p); }
                    if (row.Button("Delete")) DeleteCurrent();
                }
                row.Flexible();
            }

            if (creating) DrawCreateRow();
            if (renaming && !string.IsNullOrEmpty(AssetLibrary<T>.PathOf(asset))) DrawRenameRow();
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
        void DrawBrowser()
        {
            if (_browse == null) RefreshBrowse();

            using (var row = ZUI.HRow())
            {
                Label($"{TypeLabel} library ({_browse.Count})", ZUI.ZTextStyle.SectionHeader);
                row.Flexible();
                if (row.Button("Refresh")) RefreshBrowse();
            }

            if (_browse.Count == 0)
                Label($"No {TypeLabel} assets yet — hit New to make one.", ZUI.ZTextStyle.Subtle);

            using (ScrollView(ref _browseScroll))
            {
                float cell = CellSize, thumb = ThumbSize;
                int cols = Mathf.Max(1, Mathf.FloorToInt((position.width - 24f) / cell));
                int i = 0;
                while (i < _browse.Count)
                {
                    using (ZUI.HRow())
                    {
                        for (int c = 0; c < cols && i < _browse.Count; c++, i++)
                            if (_browse[i] != null) DrawCell(_browse[i], cell, thumb);
                        GUILayout.FlexibleSpace();
                    }
                }
            }
        }

        void DrawCell(T item, float cell, float thumb)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(cell));
            bool selected = ReferenceEquals(asset, item);

            Rect tr = GUILayoutUtility.GetRect(thumb, thumb, GUILayout.Width(thumb), GUILayout.Height(thumb));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(tr, selected ? new Color(0.35f, 0.55f, 0.95f, 0.35f) : new Color(0.11f, 0.12f, 0.15f));
                var tex = Thumb(item);
                if (tex != null)
                {
                    float sc = Mathf.Min((thumb - 6f) / Mathf.Max(1, tex.width), (thumb - 6f) / Mathf.Max(1, tex.height));
                    float w = tex.width * sc, h = tex.height * sc;
                    GUI.DrawTexture(new Rect(tr.x + (thumb - w) * 0.5f, tr.y + (thumb - h) * 0.5f, w, h), tex, ScaleMode.StretchToFill, true);
                }
                if (selected)
                {
                    var b = new Color(0.4f, 0.8f, 1f, 0.9f);
                    EditorGUI.DrawRect(new Rect(tr.x, tr.y, tr.width, 1f), b);
                    EditorGUI.DrawRect(new Rect(tr.x, tr.yMax - 1f, tr.width, 1f), b);
                    EditorGUI.DrawRect(new Rect(tr.x, tr.y, 1f, tr.height), b);
                    EditorGUI.DrawRect(new Rect(tr.xMax - 1f, tr.y, 1f, tr.height), b);
                }
            }
            EditorGUIUtility.AddCursorRect(tr, MouseCursor.Link);
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && tr.Contains(e.mousePosition))
            {
                bool open = e.clickCount == 2;
                SetAsset(item);
                if (open) browsing = false;
                e.Use(); Repaint();
            }
            GUILayout.Label(new GUIContent(item.name, item.name), EditorStyles.miniLabel, GUILayout.Width(thumb));
            EditorGUILayout.EndVertical();
        }

        Texture2D Thumb(T item)
        {
            if (_thumbs.TryGetValue(item, out var t) && t != null) return t;
            var tex = RenderThumbnail(item);
            if (tex != null) { _thumbs[item] = tex; return tex; }
            return AssetPreview.GetAssetPreview(item);   // Unity-owned — never cached or destroyed here
        }

        void ClearThumbs()
        {
            foreach (var t in _thumbs.Values) if (t != null) DestroyImmediate(t);
            _thumbs.Clear();
        }
    }
}
