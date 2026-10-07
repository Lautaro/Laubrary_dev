using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// <summary>A discrete column's picture. UV is normalized in the supplied texture.</summary>
    public readonly struct ZuiFramePicture
    {
        public readonly Texture Texture;
        public readonly Rect UV;
        public readonly string Tooltip;
        public ZuiFramePicture(Texture texture, Rect uv, string tooltip)
        { Texture = texture; UV = uv; Tooltip = tooltip; }
    }

    /// <summary>An inclusive span of discrete columns. Meaning and mutation belong to the host.</summary>
    public readonly struct ZuiFrameSpan
    {
        public readonly int First, Last;
        public readonly string Label, Tooltip;
        public readonly Color Color;
        public readonly bool Resizable;
        public ZuiFrameSpan(int first, int last, string label, Color color, string tooltip = null, bool resizable = false)
        { First = first; Last = last; Label = label; Color = color; Tooltip = tooltip; Resizable = resizable; }
    }

    /// <summary>A named row sharing the frame ruler. Disjoint occupancy uses several spans in one row.</summary>
    public sealed class ZuiFrameLane
    {
        public string Label, Tooltip;
        public bool Dim;
        public readonly List<ZuiFrameSpan> Spans = new List<ZuiFrameSpan>();
        public ZuiFrameLane(string label, string tooltip, bool dim = false)
        { Label = label; Tooltip = tooltip; Dim = dim; }
    }

    public sealed partial class ZuiLanes
    {
        // An opt-in presentation of the SAME control used by Chunks. Seconds mode is untouched unless
        // SetFrames is called. Both modes share the named gutter, clipped bar, ruler and pointer capture.
        bool _frameMode;
        readonly List<ZuiFramePicture> _framePictures = new List<ZuiFramePicture>();
        readonly List<ZuiFrameLane> _frameLanes = new List<ZuiFrameLane>();
        readonly HashSet<int> _frameSelection = new HashSet<int>();
        int _currentFrame, _framePress = -1, _frameHover = -1;
        int _frameRangeLane = -1, _frameRangeSpan = -1;
        bool _frameRangeStart;
        Vector2 _frameDownPosition;
        const float PictureHeight = 42f, FrameRowHeight = 18f;

        /// Select a frame. The two booleans mean additive (Ctrl/Cmd) and range (Shift).
        public Action<int, bool, bool> OnFrameSelected;
        /// Move one frame to the destination index. Raised once, on drop; the host owns Undo.
        public Action<int, int> OnFrameMoved;
        /// Context menu requested for a frame index; coordinates are owned by the host UI.
        public Action<int> OnFrameContext;
        /// Resize a span (lane index, span index, first, last), once on release. Boundaries are inclusive.
        public Action<int, int, int, int> OnFrameRangeChanged;
        /// A row is selected independently from changing its underlying data.
        public Action<int> OnFrameLaneSelected;

        public void SetFrames(IReadOnlyList<ZuiFramePicture> pictures, IReadOnlyList<ZuiFrameLane> lanes)
        {
            _frameMode = true;
            _framePictures.Clear();
            if (pictures != null) for (int i = 0; i < pictures.Count; i++) _framePictures.Add(pictures[i]);
            _frameLanes.Clear();
            if (lanes != null) for (int i = 0; i < lanes.Count; i++) _frameLanes.Add(lanes[i]);
            _gutter.style.width = _gutterWidth;
            _rulerPad.style.width = _gutterWidth;
            RecomputeFrames();
        }

        public void SetFrameSelection(IEnumerable<int> selection)
        {
            _frameSelection.Clear();
            if (selection != null) foreach (int i in selection) _frameSelection.Add(i);
            _bar.MarkDirtyRepaint();
        }

        /// Playback only: never re-enters the selection callback or stops the host's transport.
        public void SetFrameWithoutNotify(int frame)
        {
            _currentFrame = Mathf.Clamp(frame, 0, Mathf.Max(0, _framePictures.Count - 1));
            _seconds = _currentFrame;
            _bar.MarkDirtyRepaint();
        }

        void RecomputeFrames()
        {
            _span = Mathf.Max(1, _framePictures.Count);
            float height = PictureHeight + _frameLanes.Count * FrameRowHeight;
            _body.style.height = height;
            _bar.style.height = height;
            _gutter.style.height = height;
            style.height = height + RulerHeight;
            style.maxHeight = height + RulerHeight;
            style.flexGrow = 0;
            style.flexShrink = 0;
            LayoutFrames();
        }

        float FrameWidth => BarWidth / Mathf.Max(1, _framePictures.Count);
        int FrameAt(Vector2 panelPosition) => FrameIndexAt(LocalX(panelPosition), BarWidth, _framePictures.Count);
        static int FrameIndexAt(float x, float width, int count) => count == 0 ? -1
            : Mathf.Clamp(Mathf.FloorToInt(x / Mathf.Max(.1f, width / count)), 0, count - 1);

        void LayoutFrames()
        {
            _bar.Clear(); _gutter.Clear(); _rulerLane.Clear();
            if (BarWidth <= 1) return;
            var pictureLabel = FrameLabel("Frames", "Click a picture to select and preview it. Drag a picture to reorder; Ctrl adds and Shift selects a range.");
            pictureLabel.style.height = PictureHeight;
            _gutter.Add(pictureLabel);
            float width = FrameWidth;
            for (int i = 0; i < _framePictures.Count; i++)
            {
                var picture = _framePictures[i];
                if (picture.Texture != null)
                {
                    var img = new Image { image = picture.Texture, uv = picture.UV, scaleMode = ScaleMode.ScaleToFit,
                        tooltip = picture.Tooltip, pickingMode = PickingMode.Position };
                    PlaceFrameElement(img, i * width + 2, 2, Mathf.Max(1, width - 4), PictureHeight - 4);
                    _bar.Add(img);
                }
                var number = FrameLabel((i + 1).ToString(), "Click to select and preview frame " + (i + 1) + ".");
                PlaceFrameElement(number, i * width, 0, width, RulerHeight);
                _rulerLane.Add(number);
            }
            for (int row = 0; row < _frameLanes.Count; row++)
            {
                var lane = _frameLanes[row];
                int laneIndex = row;
                var label = FrameLabel(lane.Label, lane.Tooltip);
                label.style.height = FrameRowHeight;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                label.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; e.StopPropagation(); OnFrameLaneSelected?.Invoke(laneIndex); });
                _gutter.Add(label);
                foreach (var span in lane.Spans)
                {
                    int first = Mathf.Max(0, span.First), last = Mathf.Min(_framePictures.Count - 1, span.Last);
                    if (last < first) continue;
                    var value = FrameLabel(span.Label, span.Tooltip);
                    float sw = (last - first + 1) * width;
                    PlaceFrameElement(value, first * width + 1, PictureHeight + row * FrameRowHeight + 1, Mathf.Max(1, sw - 2), FrameRowHeight - 2);
                    value.style.opacity = lane.Dim ? .45f : 1f;
                    _bar.Add(value);
                }
            }
            _bar.MarkDirtyRepaint();
        }

        static Label FrameLabel(string text, string tip)
        {
            var label = new Label(text) { tooltip = tip };
            label.style.fontSize = 10;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            return label;
        }

        static void PlaceFrameElement(VisualElement element, float x, float y, float width, float height)
        {
            element.style.position = Position.Absolute;
            element.style.left = x; element.style.top = y;
            element.style.width = width; element.style.height = height;
        }

        void FrameDown(PointerDownEvent e)
        {
            if (_framePictures.Count == 0 || LocalX(e.position) < 0) return;
            int frame = FrameAt(e.position);
            if (e.button == 1) { OnFrameContext?.Invoke(frame); e.StopPropagation(); return; }
            if (e.button != 0) return;
            _framePress = _frameHover = frame;
            _frameDownPosition = e.position;
            _frameRangeLane = -1;
            Vector2 p = _bar.WorldToLocal(e.position);
            int row = Mathf.FloorToInt((p.y - PictureHeight) / FrameRowHeight);
            if (row >= 0 && row < _frameLanes.Count)
            {
                var spans = _frameLanes[row].Spans;
                for (int s = 0; s < spans.Count; s++)
                {
                    var span = spans[s];
                    if (!span.Resizable) continue;
                    float left = span.First * FrameWidth, right = (span.Last + 1) * FrameWidth;
                    if (Mathf.Abs(p.x - left) <= 6 || Mathf.Abs(p.x - right) <= 6)
                    { _frameRangeLane = row; _frameRangeSpan = s; _frameRangeStart = Mathf.Abs(p.x - left) < Mathf.Abs(p.x - right); break; }
                }
            }
            this.CapturePointer(e.pointerId);
            // Defer selection until release: a host may rebuild its inspector after selection, and that
            // must never detach the element while it owns a drag capture.
            e.StopPropagation();
        }

        void FrameMove(PointerMoveEvent e)
        {
            if (_framePress < 0) return;
            _frameHover = FrameAt(e.position);
            _bar.MarkDirtyRepaint();
            e.StopPropagation();
        }

        void FrameUp(PointerUpEvent e)
        {
            if (_framePress < 0) return;
            int to = FrameAt(e.position);
            bool moved = Vector2.Distance(_frameDownPosition, (Vector2)e.position) > 4;
            bool pictureDrag = _bar.WorldToLocal(_frameDownPosition).y < PictureHeight;
            // Release after copying the gesture state: releasing capture delivers OnCaptureOut synchronously.
            int from = _framePress, row = _frameRangeLane, spanIndex = _frameRangeSpan;
            bool firstEdge = _frameRangeStart;
            _framePress = -1; _frameRangeLane = -1;
            if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
            e.StopPropagation();
            CompleteFrameGesture(from, to, row, spanIndex, firstEdge, moved, pictureDrag, e.ctrlKey || e.commandKey, e.shiftKey);
            _bar.MarkDirtyRepaint();
        }

        void CompleteFrameGesture(int from, int to, int row, int spanIndex, bool firstEdge, bool moved,
            bool pictureDrag, bool additive, bool range)
        {
            if (moved && row >= 0)
            {
                var span = _frameLanes[row].Spans[spanIndex];
                int first = firstEdge ? Mathf.Min(to, span.Last) : span.First;
                int last = firstEdge ? span.Last : Mathf.Max(to, span.First);
                OnFrameRangeChanged?.Invoke(row, spanIndex, first, last);
            }
            else if (moved && pictureDrag && from != to && !additive && !range)
                OnFrameMoved?.Invoke(from, to);
            else OnFrameSelected?.Invoke(to, additive, range);
        }

        void PaintFrames(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;
            float width = FrameWidth, height = _bar.contentRect.height;
            if (BarWidth <= 1 || height <= 1) return;
            Fill(p, 0, 0, BarWidth, height, TrackColor);
            for (int row = 0; row < _frameLanes.Count; row++)
            {
                var lane = _frameLanes[row];
                foreach (var span in lane.Spans)
                {
                    int first = Mathf.Max(0, span.First), last = Mathf.Min(_framePictures.Count - 1, span.Last);
                    if (last < first) continue;
                    Color color = span.Color; color.a *= lane.Dim ? .2f : .5f;
                    Fill(p, first * width + 1, PictureHeight + row * FrameRowHeight + 1, (last - first + 1) * width - 2, FrameRowHeight - 2, color);
                    if (span.Resizable)
                    {
                        color.a = .9f;
                        Fill(p, first * width + 1, PictureHeight + row * FrameRowHeight + 2, 3, FrameRowHeight - 4, color);
                        Fill(p, (last + 1) * width - 4, PictureHeight + row * FrameRowHeight + 2, 3, FrameRowHeight - 4, color);
                    }
                }
            }
            for (int i = 0; i < _framePictures.Count; i++)
            {
                Fill(p, i * width, 0, 1, height, new Color(0, 0, 0, .5f));
                if (_frameSelection.Contains(i)) Fill(p, i * width, 0, width, 3, new Color(1, .85f, .1f));
            }
            if (_framePictures.Count > 0)
            {
                float x = (_currentFrame + .5f) * width;
                Fill(p, x - 1, PictureHeight, 2, height - PictureHeight, PlayheadCore);
            }
            if (_framePress >= 0 && _frameHover >= 0)
                Fill(p, _frameHover * width, 0, 2, height, new Color(1, .85f, .1f));
        }
    }
}
