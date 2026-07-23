using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Laubrary.PreviewKit;
using Object = UnityEngine.Object;

namespace Laubrary.AssetKit.Editor
{
    /// The one thumbnail-grid renderer behind BOTH LaubraryAssetWindow&lt;T&gt;'s own browse panel and every
    /// "pick a LauAsset" popup — was hand-rolled separately in each (LaubraryAssetWindow.DrawCell,
    /// BackSplashPicker.DrawCell) before this. Thumbnail resolution always tries IVisualPreview before falling
    /// back to Unity's own AssetPreview icon, so every LauAsset type gets a real preview for free the moment
    /// it implements the interface — no per-window RenderThumbnail override needed anymore.
    public static class LauAssetGridGUI
    {
        public static Texture2D GetThumbnail(Object item, Func<Object, Texture2D> custom, Dictionary<Object, Texture2D> cache)
        {
            if (item == null) return null;
            if (cache.TryGetValue(item, out var cached) && cached != null) return cached;

            Texture2D tex = custom?.Invoke(item);
            if (tex == null && item is IVisualPreview vp) tex = vp.RenderPreviewTexture();
            if (tex != null) { cache[item] = tex; return tex; }
            return AssetPreview.GetAssetPreview(item);   // Unity-owned — never cached or destroyed here
        }

        /// Advance every cached IVisualPreview thumbnail that opts into animation, throttled. Call from an
        /// owner's EditorApplication.update hook while its grid is actually visible; returns true if anything
        /// changed (so the caller knows to Repaint).
        public static bool TickAnimatedPreviews(Dictionary<Object, Texture2D> cache, ref double lastTick, double interval = 1.0 / 12.0)
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - lastTick < interval) return false;
            lastTick = now;
            bool any = false;
            foreach (var kv in cache)
                if (kv.Key is IVisualPreview vp && vp.CanAnimatePreview && kv.Value != null)
                { vp.UpdateAnimatedPreview(kv.Value, now); any = true; }
            return any;
        }

        public static void ClearCache(Dictionary<Object, Texture2D> cache)
        {
            foreach (var t in cache.Values) if (t != null) Object.DestroyImmediate(t);
            cache.Clear();
        }

        /// Draws a GUILayout-flow thumbnail grid (fits inside a window body OR a PopupWindowContent.OnGUI —
        /// both are GUILayout contexts). onPick receives the clicked item and the click count, so a caller can
        /// tell single-click-to-select apart from double-click-to-commit (LaubraryAssetWindow's browser closes
        /// on double-click; a Recall popup commits and closes on any click) however it likes.
        public static void DrawGrid(float availableWidth, IList<Object> items, Object selected, Action<Object, int> onPick,
            Dictionary<Object, Texture2D> thumbCache, Func<Object, Texture2D> customThumb = null,
            float cellSize = 104f, float thumbSize = 92f)
        {
            int cols = Mathf.Max(1, Mathf.FloorToInt((availableWidth - 24f) / cellSize));
            int i = 0;
            while (i < items.Count)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int c = 0; c < cols && i < items.Count; c++, i++)
                        if (items[i] != null) DrawCell(items[i], selected, onPick, thumbCache, customThumb, cellSize, thumbSize);
                    GUILayout.FlexibleSpace();
                }
            }
        }

        static void DrawCell(Object item, Object selected, Action<Object, int> onPick, Dictionary<Object, Texture2D> cache,
            Func<Object, Texture2D> customThumb, float cell, float thumb)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(cell));
            bool isSel = ReferenceEquals(selected, item);

            Rect tr = GUILayoutUtility.GetRect(thumb, thumb, GUILayout.Width(thumb), GUILayout.Height(thumb));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(tr, isSel ? new Color(0.35f, 0.55f, 0.95f, 0.35f) : new Color(0.11f, 0.12f, 0.15f));
                var tex = GetThumbnail(item, customThumb, cache);
                if (tex != null)
                {
                    float sc = Mathf.Min((thumb - 6f) / Mathf.Max(1, tex.width), (thumb - 6f) / Mathf.Max(1, tex.height));
                    float w = tex.width * sc, h = tex.height * sc;
                    GUI.DrawTexture(new Rect(tr.x + (thumb - w) * 0.5f, tr.y + (thumb - h) * 0.5f, w, h), tex, ScaleMode.StretchToFill, true);
                }
                if (isSel)
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
                onPick?.Invoke(item, e.clickCount);
                e.Use();
            }
            GUILayout.Label(new GUIContent(item.name, item.name), EditorStyles.miniLabel, GUILayout.Width(thumb));
            EditorGUILayout.EndVertical();
        }
    }
}
