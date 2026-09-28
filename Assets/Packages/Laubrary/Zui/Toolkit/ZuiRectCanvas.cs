using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// A UI Toolkit canvas for views drawn as filled rectangles (added for the Zounds UI Toolkit port, T-0467).
    ///
    /// The paint callback receives the canvas's local rect and a <c>fill(rect, colour)</c> function — the same shape as
    /// IMGUI's <c>EditorGUI.DrawRect</c> — so one paint routine can serve an IMGUI view and its UI Toolkit twin, giving
    /// both exactly the same geometry. The rectangles are turned into one mesh (chunked to stay under 16-bit indices),
    /// which keeps thousands of cells per frame — a scrolling spectrogram — cheap. Call <c>MarkDirtyRepaint()</c> to redraw.
    public class ZuiRectCanvas : VisualElement
    {
        public Action<Rect, Action<Rect, Color>> paint;

        readonly List<Rect> _rects = new List<Rect>(1024);
        readonly List<Color32> _colours = new List<Color32>(1024);
        const int QuadsPerChunk = 16000;

        public ZuiRectCanvas(Action<Rect, Action<Rect, Color>> paint = null)
        {
            this.paint = paint;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Generate;
        }

        void Add(Rect r, Color c)
        {
            if (r.width <= 0f || r.height <= 0f || c.a <= 0f) return;
            _rects.Add(r); _colours.Add(c);
        }

        void Generate(MeshGenerationContext ctx)
        {
            _rects.Clear(); _colours.Clear();
            var area = contentRect;
            if (paint == null || area.width <= 0f || area.height <= 0f) return;
            paint(area, Add);
            for (int start = 0; start < _rects.Count; start += QuadsPerChunk)
            {
                int n = Mathf.Min(QuadsPerChunk, _rects.Count - start);
                var mesh = ctx.Allocate(n * 4, n * 6);
                for (int q = 0; q < n; q++)
                {
                    var r = _rects[start + q];
                    var c = _colours[start + q];
                    mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMin, r.yMin, Vertex.nearZ), tint = c });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMax, r.yMin, Vertex.nearZ), tint = c });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMax, r.yMax, Vertex.nearZ), tint = c });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMin, r.yMax, Vertex.nearZ), tint = c });
                    ushort b = (ushort)(q * 4);
                    mesh.SetNextIndex(b); mesh.SetNextIndex((ushort)(b + 1)); mesh.SetNextIndex((ushort)(b + 2));
                    mesh.SetNextIndex(b); mesh.SetNextIndex((ushort)(b + 2)); mesh.SetNextIndex((ushort)(b + 3));
                }
            }
        }
    }
}
