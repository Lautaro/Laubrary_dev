using System;
using UnityEngine;
using Laubrary.Zui;

namespace Laubrary.Zounds.Uitk
{
    /// <summary>Audio-domain adapter for the shared retained-mode envelope canvas.</summary>
    public class EnvelopeTK : ZuiSkinEnvelope
    {
        public Envelope envelope;
        public Color mainColor;
        public float thickness = 1.5f;
        public Action onBegin;
        public Action onChanged;

        static ZoundsProject.ProjectSettings.EditorStyle Style => ZoundsProject.Instance.projectSettings.editorStyle;

        public EnvelopeTK(Envelope envelope, Color mainColor)
            : base(envelope?.GetPointsList(), mainColor, new ZUIEnvelopeDef(), new ZUIEnvelopeRuntime())
        {
            this.envelope = envelope; this.mainColor = mainColor;
            AddToClassList("zui-envelope--audio");
            tooltip = "Drag points to shape the curve; double-click to add or remove a point. Shift-drag a segment to move it, or Shift-right-drag to bend it.";
            configuration.showReadout = false;
            configuration.pointState = PointState;
            configuration.lineHitDistance = 4f;
            // The audio curve overlays its host surface; USS can override this compatibility fallback.
            def.background.colorA = new ZUIColorRef(Color.clear);
            def.border = null;
            def.paddingTop = def.paddingRight = def.paddingBottom = def.paddingLeft = 0f;
            def.gridRows = 0;
            rt.onDragStarted = () => onBegin?.Invoke();
            rt.onDragUpdated = rt.onMutated = () => onChanged?.Invoke();
            Prepare();
        }

        ZUIEnvelopeEditState PointState(int index)
        {
            var state = points[index].editState;
            if (state == ZUIEnvelopeEditState.NotEditable) return state;
            if (index == 0 || (Envelope.requiresEndPoint && index == points.Count - 1))
                return state == ZUIEnvelopeEditState.XEditable ? ZUIEnvelopeEditState.NotEditable : ZUIEnvelopeEditState.YEditable;
            return state;
        }

        protected override void Prepare()
        {
            points = envelope?.GetPointsList();
            if (envelope == null) return;
            rt.xMin = envelope.xMin; rt.xMax = envelope.xMax;
            rt.yMin = envelope.yMin; rt.yMax = envelope.yMax;
            curveColor = mainColor;
            def.curveThickness = thickness;
            float radius = Style.envelopeHandleSize;
            def.editable.radius = def.editable.hoverRadius = radius;
            def.yEditable.radius = def.yEditable.hoverRadius = radius;
            def.editable.fillColor = def.yEditable.fillColor = new ZUIColorRef(mainColor);
            Color.RGBToHSV(mainColor, out float h, out float s, out float v);
            def.editable.hoverFillColor = def.yEditable.hoverFillColor = new ZUIColorRef(Color.HSVToRGB(h, s * 0.8f, Mathf.Min(v * 1.5f, 1f)));
            def.selectedColor = new ZUIColorRef(Style.selectedEnvelopeHandleColor);
            def.curveHoverThickness = thickness;
        }

        public void Refresh() => Repaint();
    }
}
