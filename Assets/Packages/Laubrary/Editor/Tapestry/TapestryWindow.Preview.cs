// TapestryWindow.Preview — the flat 2D preview canvas. Unlike Lathe/PyrePlus's 3D PreviewRenderUtility
// orbit cameras, Tapestry's output is a flat texture, so the preview is a plain IMGUI-drawn quad:
// GUI.DrawTextureWithTexCoords samples UV outside [0,1] (wrapping, since the baked texture's own wrapMode is
// Repeat) to get the "Tiled" 3x3 view for free, with no extra geometry or tiled Image elements needed.
using UnityEditor;
using UnityEngine;

namespace Laubrary.Tapestry.Editor
{
    public partial class TapestryWindow
    {
        void DrawPreview(TapestrySpec s)
        {
            var rect = GUILayoutUtility.GetRect(0, 0, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            var evt = Event.current;
            if (evt.type == EventType.ScrollWheel && rect.Contains(evt.mousePosition))
            {
                previewZoom = Mathf.Clamp(previewZoom - evt.delta.y * 0.05f, 0.25f, 4f);
                evt.Use();
                preview?.MarkDirtyRepaint();
            }

            EditorGUI.DrawRect(rect, s.previewBackground);
            var tex = GetBaked(s);
            if (tex == null || evt.type != EventType.Repaint) return;

            Vector2 center = rect.center;
            float size = Mathf.Min(rect.width, rect.height) * 0.85f * previewZoom;
            var drawRect = new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);

            var savedMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(previewRotation, center);
            float tiles = tiledPreview ? 3f : 1f;
            float off = tiledPreview ? -1f : 0f;
            GUI.DrawTextureWithTexCoords(drawRect, tex, new Rect(off, off, tiles, tiles));
            GUI.matrix = savedMatrix;
        }
    }
}
