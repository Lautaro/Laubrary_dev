using UnityEditor;
using UnityEngine;
using Laubrary.PreviewKit;
using Laubrary.AssetKit.Editor;

namespace Laubrary.Chunks.Editor
{
    /// A CustomEditor (not a ZUIWindow — plain EditorGUILayout, per authoring.md's sheet-scoping rule) that
    /// augments ChunkSpec's default inspector with a live preview of its optional animationSource, plus a jump
    /// straight into that animation's own authoring tool (Pyre, the Animation Builder, ...) when one is
    /// registered via LauAssetEditors — Chunks itself never references those tools directly.
    [CustomEditor(typeof(ChunkSpec))]
    public class ChunkSpecEditor : UnityEditor.Editor
    {
        Texture2D previewTex;
        Object lastSource;
        double lastTick;
        const double AnimateInterval = 1.0 / 12.0;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var spec = (ChunkSpec)target;
            var source = spec.animationSource;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Animation preview", EditorStyles.boldLabel);

            if (source == null)
            {
                EditorGUILayout.HelpBox("No animation source — chunks use a static/procedural sprite.", MessageType.None);
                return;
            }

            if (source != lastSource)
            {
                if (previewTex != null) DestroyImmediate(previewTex);
                previewTex = null;
                lastSource = source;
            }

            if (source is IVisualPreview preview)
            {
                previewTex ??= preview.RenderPreviewTexture();

                if (preview.CanAnimatePreview)
                {
                    double now = EditorApplication.timeSinceStartup;
                    if (now - lastTick >= AnimateInterval)
                    {
                        lastTick = now;
                        if (previewTex != null) preview.UpdateAnimatedPreview(previewTex, now);
                        Repaint();
                    }
                }

                DrawPreviewBox(previewTex);
            }
            else
            {
                EditorGUILayout.HelpBox($"{source.GetType().Name} does not implement IVisualPreview — no preview available.", MessageType.Warning);
            }

            if (LauAssetEditors.CanOpen(source))
            {
                if (GUILayout.Button($"Edit '{source.name}'…"))
                    LauAssetEditors.Open(source);
            }
        }

        static void DrawPreviewBox(Texture2D tex)
        {
            const float box = 96f;
            Rect r = GUILayoutUtility.GetRect(box, box, GUILayout.Width(box), GUILayout.Height(box));
            if (Event.current.type != EventType.Repaint) return;

            EditorGUI.DrawRect(r, new Color(0.11f, 0.12f, 0.15f));
            if (tex == null) return;

            float sc = Mathf.Min((box - 6f) / Mathf.Max(1, tex.width), (box - 6f) / Mathf.Max(1, tex.height));
            float w = tex.width * sc, h = tex.height * sc;
            GUI.DrawTexture(new Rect(r.x + (box - w) * 0.5f, r.y + (box - h) * 0.5f, w, h), tex, ScaleMode.StretchToFill, true);
        }

        void OnDisable()
        {
            if (previewTex != null) DestroyImmediate(previewTex);
        }
    }
}
