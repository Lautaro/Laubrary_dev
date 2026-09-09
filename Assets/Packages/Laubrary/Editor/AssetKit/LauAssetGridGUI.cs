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
            if (tex == null && item is IVisualPreview vp)
            {
                tex = vp.RenderPreviewTexture();
                if (tex != null && IsBlank(tex) && !SeekVisibleFrame(vp, tex))
                {
                    // A fully transparent preview is the one thing the thumbnail rule forbids outright: the
                    // slot claims its width and promises a picture, so an empty one reads as "this has a
                    // picture and it failed to load" — a lie. The null guard above cannot catch it, because
                    // the asset returned a perfectly valid texture with nothing in it. Measured live: the
                    // Pyre blast "Proper Blast" renders 0 opaque pixels out of 4096 at its first frame while
                    // "Old School Explo 2 Plus" renders 3607, so two chips side by side disagreed about
                    // whether a blast has a picture at all. Drop it and let the fallback decide.
                    Object.DestroyImmediate(tex);
                    tex = null;
                }
            }
            if (tex != null) { cache[item] = tex; return tex; }
            return AssetPreview.GetAssetPreview(item);   // Unity-owned — never cached or destroyed here
        }

        /// True when nothing in `tex` would be visible. Cheap enough to run once per asset because the result
        /// is cached by the caller, and it early-outs on the first pixel that shows.
        static bool IsBlank(Texture2D tex)
        {
            if (tex == null || tex.width == 0 || tex.height == 0) return true;
            Color[] px;
            try { px = tex.GetPixels(); }
            catch (UnityException) { return false; }   // unreadable (Unity-owned) — assume it has content
            for (int i = 0; i < px.Length; i++)
                if (px[i].a > 0.02f) return false;
            return true;
        }

        /// An animated preview whose FIRST frame is empty is not an empty preview — it is a preview sampled at
        /// the wrong moment, which is the common case for an explosion that starts from nothing. Walk its own
        /// timeline for a frame that actually shows something and keep that, mutating `tex` in place (which is
        /// the contract UpdateAnimatedPreview already has). Returns false if the whole thing really is empty.
        static bool SeekVisibleFrame(IVisualPreview vp, Texture2D tex)
        {
            if (!vp.CanAnimatePreview) return false;
            float fps = vp.PreviewFps > 0.01f ? vp.PreviewFps : 12f;
            const int framesToTry = 24;
            for (int f = 1; f <= framesToTry; f++)
            {
                vp.UpdateAnimatedPreview(tex, f / fps);
                if (!IsBlank(tex)) return true;
            }
            vp.UpdateAnimatedPreview(tex, 0d);   // put it back where it started rather than on a stray frame
            return false;
        }

        /// Advance cached IVisualPreview thumbnails, throttled. When `animateAll` is true every animatable
        /// cached thumbnail advances (everything plays at once); when false, only `hovered` advances — the
        /// default "static unless you're pointing at it" mode, so a grid of 10s of animated assets doesn't
        /// all animate simultaneously. Call from an owner's EditorApplication.update hook while its grid is
        /// actually visible; returns true if anything changed (so the caller knows to Repaint).
        public static bool TickAnimatedPreviews(Dictionary<Object, Texture2D> cache, ref double lastTick,
            bool animateAll, Object hovered, double interval = 1.0 / 12.0)
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - lastTick < interval) return false;
            lastTick = now;
            bool any = false;
            foreach (var kv in cache)
                if (kv.Key is IVisualPreview vp && vp.CanAnimatePreview && kv.Value != null
                    && (animateAll || ReferenceEquals(kv.Key, hovered)))
                { vp.UpdateAnimatedPreview(kv.Value, now); any = true; }
            return any;
        }

        /// Call whenever the hovered item changes (including to/from null), only in hover-only mode (not
        /// while `animateAll` is on) — destroys the OUTGOING item's cached texture so the next GetThumbnail
        /// regenerates a fresh static frame instead of leaving it stuck on whatever frame it animated to.
        public static void OnHoverChanged(Dictionary<Object, Texture2D> cache, Object previous, Object current)
        {
            if (ReferenceEquals(previous, current) || previous == null) return;
            if (previous is IVisualPreview vp && vp.CanAnimatePreview
                && cache.TryGetValue(previous, out var tex) && tex != null)
            {
                Object.DestroyImmediate(tex);
                cache.Remove(previous);
            }
        }

        public static void ClearCache(Dictionary<Object, Texture2D> cache)
        {
            foreach (var t in cache.Values) if (t != null) Object.DestroyImmediate(t);
            cache.Clear();
        }

        /// Subscribes `cache` to Laubrary.Caching.AssetCacheInvalidation so an edited asset's cached
        /// thumbnail is dropped (and, via `repaint`, redrawn) the moment the edit happens — instead of
        /// showing a stale render until the cache is cleared some other way (window reopen, Refresh).
        /// AssetCacheInvalidation itself fires generically for ANY ScriptableObject edited through ANY means
        /// (a ZUI window, the plain Inspector, Undo/Redo — see AssetCacheInvalidationBridge), so every
        /// browsing surface gets live updates for free just by calling this once, typically from OnEnable/
        /// OnOpen. Store and invoke the returned action from OnDisable/OnClose to unsubscribe.
        public static Action WatchInvalidation<TKey>(Dictionary<TKey, Texture2D> cache, Action repaint = null) where TKey : Object
        {
            void Handler(Object asset)
            {
                if (!(asset is TKey key) || !cache.TryGetValue(key, out var tex) || tex == null) return;
                Object.DestroyImmediate(tex);
                cache.Remove(key);
                repaint?.Invoke();
            }
            Laubrary.Caching.AssetCacheInvalidation.Invalidated += Handler;
            return () => Laubrary.Caching.AssetCacheInvalidation.Invalidated -= Handler;
        }

        /// Draws a GUILayout-flow thumbnail grid (fits inside a window body OR a PopupWindowContent.OnGUI —
        /// both are GUILayout contexts). onPick receives the clicked item and the click count, so a caller can
        /// tell single-click-to-select apart from double-click-to-commit (LaubraryAssetWindow's browser closes
        /// on double-click; a Recall popup commits and closes on any click) however it likes. Returns whichever
        /// item the mouse is currently over (or null), so a caller can feed it into TickAnimatedPreviews/
        /// OnHoverChanged for hover-to-preview mode.
        public static Object DrawGrid(float availableWidth, IList<Object> items, Object selected, Action<Object, int> onPick,
            Dictionary<Object, Texture2D> thumbCache, Func<Object, Texture2D> customThumb = null,
            float cellSize = 104f, float thumbSize = 92f)
        {
            Object hovered = null;
            int cols = Mathf.Max(1, Mathf.FloorToInt((availableWidth - 24f) / cellSize));
            int i = 0;
            while (i < items.Count)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int c = 0; c < cols && i < items.Count; c++, i++)
                        if (items[i] != null && DrawCell(items[i], selected, onPick, thumbCache, customThumb, cellSize, thumbSize))
                            hovered = items[i];
                    GUILayout.FlexibleSpace();
                }
            }
            return hovered;
        }

        /// Returns true if the mouse is over this cell's thumbnail.
        static bool DrawCell(Object item, Object selected, Action<Object, int> onPick, Dictionary<Object, Texture2D> cache,
            Func<Object, Texture2D> customThumb, float cell, float thumb)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(cell));
            bool isSel = ReferenceEquals(selected, item);

            Rect tr = GUILayoutUtility.GetRect(thumb, thumb, GUILayout.Width(thumb), GUILayout.Height(thumb));
            bool isHover = tr.Contains(Event.current.mousePosition);
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
            // The name gets the whole CELL, not just the thumbnail's width — the surrounding vertical group is
            // already `cell` wide, so those extra pixels were reserved and unused — and it is elided rather
            // than left to IMGUI's hard clip. Measured on the browser Mirage's "Add Previewable" opens
            // (T-0324, the first pass to see it): 16 of 47 names were wider than the label, cut mid-word with
            // nothing to say they continued. The full name stays on the tooltip.
            GUILayout.Label(new GUIContent(Elide(item.name, EditorStyles.miniLabel, cell), item.name),
                EditorStyles.miniLabel, GUILayout.Width(cell));
            EditorGUILayout.EndVertical();
            return isHover;
        }

        /// Shorten a string to an ellipsis that FITS the given width in the given style. IMGUI has no
        /// text-overflow, so a fixed-width Label just stops drawing mid-glyph; the layout rules ask for the
        /// string to be truncated by hand instead, exactly as `.zui-chip__label` does in the retained-mode half.
        ///
        /// The ellipsis goes in the MIDDLE, because in a PICKER the point of the name is to tell one row from
        /// the next, and this project names assets family-first with the variant at the END. Measured over
        /// every ScriptableObject name in the project (421 names, 29 of them too wide for the 104px cell):
        /// cutting the tail collapsed 9 of those onto a name another asset already showed — three assets all
        /// reading "Directional Grenade…", four reading "New Universal Ren…"/"UniversalRenderPip…" — while
        /// keeping both ends leaves 2, and those two differ only past the room a cell has at any cut. Six
        /// further duplicates are assets that genuinely share a full name, which no truncation can fix.
        static string Elide(string s, GUIStyle style, float width)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var probe = new GUIContent(s);
            if (style.CalcSize(probe).x <= width) return s;
            for (int keep = s.Length - 1; keep >= 2; keep--)
            {
                int head = (keep + 1) / 2;                       // odd budget favours the head, which usually names the family
                probe.text = s.Substring(0, head) + "…" + s.Substring(s.Length - (keep - head));
                if (style.CalcSize(probe).x <= width) return probe.text;
            }
            return "…";
        }
    }
}
