using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.PreviewStage
{
    /// Reusable helpers to DRAW and EDIT a <see cref="PreviewBackground"/> inside any tool's preview viewport, and to
    /// save/recall them as assets. Pure static IMGUI — no dependency beyond UnityEditor, so any Laubrary editor can
    /// drop a shared test backdrop into its preview.
    public static class PreviewStageGUI
    {
        const string Folder = "Assets/PreviewBackgrounds";

        // ── draw ──────────────────────────────────────────────────────────────────
        /// Draw the placed sprites of one layer into `view`, centred and scaled by `zoom`. Call inside a Repaint.
        /// `foreground` false = the behind-the-subject backdrop layer (also paints the fill); true = the in-front
        /// decoration layer (drawn over the subject). Call once with false BEFORE the subject and once with true after.
        public static void Draw(Rect view, PreviewBackground bg, float zoom, bool foreground)
        {
            if (bg == null) return;
            if (!foreground && bg.fill.a > 0f) EditorGUI.DrawRect(view, bg.fill);
            Vector2 c = view.center;
            var prev = GUI.color;
            for (int i = 0; i < bg.sprites.Count; i++)
            {
                var s = bg.sprites[i];
                if (s == null || s.sprite == null || s.front != foreground) continue;
                DrawSprite(new Vector2(c.x + s.position.x * zoom, c.y + s.position.y * zoom), s.sprite, s.scale * zoom, s.tint);
            }
            GUI.color = prev;
        }

        static void DrawSprite(Vector2 centre, Sprite sp, float scale, Color tint)
        {
            var tex = sp.texture; if (tex == null) return;
            Rect r = sp.textureRect;
            Rect tc = new Rect(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height);
            float w = r.width * scale, h = r.height * scale;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(new Rect(centre.x - w * 0.5f, centre.y - h * 0.5f, w, h), tex, tc, true);
        }

        // ── edit (drag the selected sprite in the viewport) ─────────────────────────
        /// Click-select + drag-move placed sprites in `view`. Returns true if anything changed. `sel` = selected
        /// index (−1 none), `dragging` = drag latch (keep both across frames on the caller).
        public static bool Edit(Rect view, PreviewBackground bg, float zoom, ref int sel, ref bool dragging)
        {
            if (bg == null) return false;
            var e = Event.current;
            bool changed = false;
            Vector2 c = view.center;

            if (e.type == EventType.MouseDown && e.button == 0 && view.Contains(e.mousePosition))
            {
                int hit = -1;
                for (int i = bg.sprites.Count - 1; i >= 0; i--)   // topmost first
                {
                    if (SpriteRect(bg.sprites[i], c, zoom, out Rect dst) && dst.Contains(e.mousePosition)) { hit = i; break; }
                }
                if (hit >= 0) { sel = hit; dragging = true; e.Use(); }
            }
            if (dragging && sel >= 0 && sel < bg.sprites.Count)
            {
                if (e.type == EventType.MouseDrag) { bg.sprites[sel].position += e.delta / Mathf.Max(0.01f, zoom); changed = true; e.Use(); }
                else if (e.type == EventType.MouseUp) { dragging = false; e.Use(); }
            }

            if (e.type == EventType.Repaint && sel >= 0 && sel < bg.sprites.Count && SpriteRect(bg.sprites[sel], c, zoom, out Rect outline))
            {
                Color col = new Color(0.4f, 0.8f, 1f, 0.9f);
                EditorGUI.DrawRect(new Rect(outline.x, outline.y, outline.width, 1f), col);
                EditorGUI.DrawRect(new Rect(outline.x, outline.yMax - 1f, outline.width, 1f), col);
                EditorGUI.DrawRect(new Rect(outline.x, outline.y, 1f, outline.height), col);
                EditorGUI.DrawRect(new Rect(outline.xMax - 1f, outline.y, 1f, outline.height), col);
            }
            return changed;
        }

        static bool SpriteRect(StageSprite s, Vector2 centre, float zoom, out Rect dst)
        {
            dst = default;
            if (s == null || s.sprite == null) return false;
            Rect r = s.sprite.textureRect;
            float w = r.width * s.scale * zoom, h = r.height * s.scale * zoom;
            dst = new Rect(centre.x + s.position.x * zoom - w * 0.5f, centre.y + s.position.y * zoom - h * 0.5f, w, h);
            return true;
        }

        // ── save / recall ───────────────────────────────────────────────────────────
        /// Persist `bg` to an asset. If it's already an asset, just saves; otherwise creates one under
        /// Assets/PreviewBackgrounds/<name>.asset (no dialog) and re-points `bg` to the saved asset.
        public static void Save(ref PreviewBackground bg, string name)
        {
            if (bg == null) return;
            string path = AssetDatabase.GetAssetPath(bg);
            if (string.IsNullOrEmpty(path))
            {
                if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "PreviewBackgrounds");
                path = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{(string.IsNullOrWhiteSpace(name) ? "PreviewBg" : name)}.asset");
                AssetDatabase.CreateAsset(bg, path);
            }
            EditorUtility.SetDirty(bg);
            AssetDatabase.SaveAssets();
        }

        /// A popup listing every saved PreviewBackground; calls onPick with the chosen one.
        public static void ShowRecall(Rect activator, Action<PreviewBackground> onPick)
            => PopupWindow.Show(activator, new RecallPopup(onPick));

        class RecallPopup : PopupWindowContent
        {
            readonly Action<PreviewBackground> onPick;
            readonly string[] guids;
            Vector2 scroll;
            public RecallPopup(Action<PreviewBackground> onPick) { this.onPick = onPick; guids = AssetDatabase.FindAssets("t:PreviewBackground"); }
            public override Vector2 GetWindowSize() => new Vector2(240f, 30f + Mathf.Clamp(guids.Length, 1, 10) * 20f);
            public override void OnGUI(Rect rect)
            {
                EditorGUILayout.LabelField("Saved backgrounds", EditorStyles.boldLabel);
                if (guids.Length == 0) { EditorGUILayout.HelpBox("None saved yet — Save one first.", MessageType.Info); return; }
                scroll = EditorGUILayout.BeginScrollView(scroll);
                foreach (var g in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(g);
                    if (GUILayout.Button(System.IO.Path.GetFileNameWithoutExtension(path), EditorStyles.miniButton))
                    {
                        onPick?.Invoke(AssetDatabase.LoadAssetAtPath<PreviewBackground>(path));
                        editorWindow.Close();
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }
    }
}
