using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// <summary>Resolves the envelope's paint and hit-test presentation from USS without changing its data or gestures.</summary>
    internal sealed class ZuiEnvelopePresentation
    {
        internal struct Handle
        {
            internal float radius, hoverRadius, borderWidth;
            internal Color fillColor, hoverFillColor, borderColor;
        }

        internal struct Values
        {
            internal Color background, borderColor, gridColor, curveColor, selectedColor;
            internal float borderTop, borderRight, borderBottom, borderLeft;
            internal float paddingTop, paddingRight, paddingBottom, paddingLeft;
            internal float curveThickness, curveHoverThickness, hitRadiusExtra;
            internal float gridThickness, selectedStrokeThickness, selectionBoxFillAlpha;
            internal float hoverSaturationScale, hoverValueScale;
            internal float uncertaintyFillAlpha, uncertaintyStrokeAlpha, uncertaintyStrokeThickness;
            internal float dottedWhiteMix, dottedMinWidth, ghostOpacity;
            internal int gridRows;
            internal Handle editable, xEditable, yEditable, notEditable;

            internal Handle GetHandle(ZUIEnvelopeEditState state)
            {
                switch (state)
                {
                    case ZUIEnvelopeEditState.XEditable: return xEditable;
                    case ZUIEnvelopeEditState.YEditable: return yEditable;
                    case ZUIEnvelopeEditState.NotEditable: return notEditable;
                    default: return editable;
                }
            }

            internal float MaxHandleRadius => Mathf.Max(editable.radius, Mathf.Max(xEditable.radius, Mathf.Max(yEditable.radius, notEditable.radius)));
        }

        static readonly CustomStyleProperty<Color> Background = new CustomStyleProperty<Color>("--zui-envelope-background");
        static readonly CustomStyleProperty<Color> BorderColor = new CustomStyleProperty<Color>("--zui-envelope-border-color");
        static readonly CustomStyleProperty<Color> GridColor = new CustomStyleProperty<Color>("--zui-envelope-grid-color");
        static readonly CustomStyleProperty<Color> CurveColor = new CustomStyleProperty<Color>("--zui-envelope-curve-color");
        static readonly CustomStyleProperty<Color> SelectedColor = new CustomStyleProperty<Color>("--zui-envelope-selected-color");
        static readonly CustomStyleProperty<float> BorderTop = new CustomStyleProperty<float>("--zui-envelope-border-top");
        static readonly CustomStyleProperty<float> BorderRight = new CustomStyleProperty<float>("--zui-envelope-border-right");
        static readonly CustomStyleProperty<float> BorderBottom = new CustomStyleProperty<float>("--zui-envelope-border-bottom");
        static readonly CustomStyleProperty<float> BorderLeft = new CustomStyleProperty<float>("--zui-envelope-border-left");
        static readonly CustomStyleProperty<float> PaddingTop = new CustomStyleProperty<float>("--zui-envelope-padding-top");
        static readonly CustomStyleProperty<float> PaddingRight = new CustomStyleProperty<float>("--zui-envelope-padding-right");
        static readonly CustomStyleProperty<float> PaddingBottom = new CustomStyleProperty<float>("--zui-envelope-padding-bottom");
        static readonly CustomStyleProperty<float> PaddingLeft = new CustomStyleProperty<float>("--zui-envelope-padding-left");
        static readonly CustomStyleProperty<float> CurveThickness = new CustomStyleProperty<float>("--zui-envelope-curve-thickness");
        static readonly CustomStyleProperty<float> CurveHoverThickness = new CustomStyleProperty<float>("--zui-envelope-curve-hover-thickness");
        static readonly CustomStyleProperty<float> HitRadiusExtra = new CustomStyleProperty<float>("--zui-envelope-hit-radius-extra");
        static readonly CustomStyleProperty<float> GridRows = new CustomStyleProperty<float>("--zui-envelope-grid-rows");
        static readonly CustomStyleProperty<float> GridThickness = new CustomStyleProperty<float>("--zui-envelope-grid-thickness");
        static readonly CustomStyleProperty<float> SelectedStrokeThickness = new CustomStyleProperty<float>("--zui-envelope-selected-stroke-thickness");
        static readonly CustomStyleProperty<float> SelectionBoxFillAlpha = new CustomStyleProperty<float>("--zui-envelope-selection-box-fill-alpha");
        static readonly CustomStyleProperty<float> HoverSaturationScale = new CustomStyleProperty<float>("--zui-envelope-hover-saturation-scale");
        static readonly CustomStyleProperty<float> HoverValueScale = new CustomStyleProperty<float>("--zui-envelope-hover-value-scale");
        static readonly CustomStyleProperty<float> UncertaintyFillAlpha = new CustomStyleProperty<float>("--zui-envelope-uncertainty-fill-alpha");
        static readonly CustomStyleProperty<float> UncertaintyStrokeAlpha = new CustomStyleProperty<float>("--zui-envelope-uncertainty-stroke-alpha");
        static readonly CustomStyleProperty<float> UncertaintyStrokeThickness = new CustomStyleProperty<float>("--zui-envelope-uncertainty-stroke-thickness");
        static readonly CustomStyleProperty<float> DottedWhiteMix = new CustomStyleProperty<float>("--zui-envelope-dotted-white-mix");
        static readonly CustomStyleProperty<float> DottedMinWidth = new CustomStyleProperty<float>("--zui-envelope-dotted-min-width");
        static readonly CustomStyleProperty<float> GhostOpacity = new CustomStyleProperty<float>("--zui-envelope-ghost-opacity");

        internal Values Current;
        bool _hasStyle;
        readonly Dictionary<string, float> _floats = new Dictionary<string, float>();
        readonly Dictionary<string, Color> _colors = new Dictionary<string, Color>();

        internal void Resolve(CustomStyleResolvedEvent style, ZUIEnvelopeDef def, Color curveColor)
        {
            _floats.Clear(); _colors.Clear();
            Set(style, "--zui-envelope-background", Background); Set(style, "--zui-envelope-border-color", BorderColor); Set(style, "--zui-envelope-grid-color", GridColor); Set(style, "--zui-envelope-curve-color", CurveColor); Set(style, "--zui-envelope-selected-color", SelectedColor);
            Set(style, "--zui-envelope-border-top", BorderTop); Set(style, "--zui-envelope-border-right", BorderRight); Set(style, "--zui-envelope-border-bottom", BorderBottom); Set(style, "--zui-envelope-border-left", BorderLeft);
            Set(style, "--zui-envelope-padding-top", PaddingTop); Set(style, "--zui-envelope-padding-right", PaddingRight); Set(style, "--zui-envelope-padding-bottom", PaddingBottom); Set(style, "--zui-envelope-padding-left", PaddingLeft);
            Set(style, "--zui-envelope-curve-thickness", CurveThickness); Set(style, "--zui-envelope-curve-hover-thickness", CurveHoverThickness); Set(style, "--zui-envelope-hit-radius-extra", HitRadiusExtra); Set(style, "--zui-envelope-grid-rows", GridRows);
            Set(style, "--zui-envelope-grid-thickness", GridThickness); Set(style, "--zui-envelope-selected-stroke-thickness", SelectedStrokeThickness); Set(style, "--zui-envelope-selection-box-fill-alpha", SelectionBoxFillAlpha); Set(style, "--zui-envelope-hover-saturation-scale", HoverSaturationScale); Set(style, "--zui-envelope-hover-value-scale", HoverValueScale); Set(style, "--zui-envelope-uncertainty-fill-alpha", UncertaintyFillAlpha); Set(style, "--zui-envelope-uncertainty-stroke-alpha", UncertaintyStrokeAlpha); Set(style, "--zui-envelope-uncertainty-stroke-thickness", UncertaintyStrokeThickness); Set(style, "--zui-envelope-dotted-white-mix", DottedWhiteMix); Set(style, "--zui-envelope-dotted-min-width", DottedMinWidth); Set(style, "--zui-envelope-ghost-opacity", GhostOpacity);
            SetHandle(style, "editable"); SetHandle(style, "x-editable"); SetHandle(style, "y-editable"); SetHandle(style, "not-editable");
            _hasStyle = true;
            RefreshFallbacks(def, curveColor);
        }

        internal void RefreshFallbacks(ZUIEnvelopeDef def, Color curveColor)
        {
            Current = FromDefinition(def, curveColor);
            if (!_hasStyle) return;
            Current.background = ColorOf("--zui-envelope-background", Current.background); Current.borderColor = ColorOf("--zui-envelope-border-color", Current.borderColor); Current.gridColor = ColorOf("--zui-envelope-grid-color", Current.gridColor); Current.curveColor = ColorOf("--zui-envelope-curve-color", Current.curveColor); Current.selectedColor = ColorOf("--zui-envelope-selected-color", Current.selectedColor);
            Current.borderTop = FloatOf("--zui-envelope-border-top", Current.borderTop); Current.borderRight = FloatOf("--zui-envelope-border-right", Current.borderRight); Current.borderBottom = FloatOf("--zui-envelope-border-bottom", Current.borderBottom); Current.borderLeft = FloatOf("--zui-envelope-border-left", Current.borderLeft);
            Current.paddingTop = FloatOf("--zui-envelope-padding-top", Current.paddingTop); Current.paddingRight = FloatOf("--zui-envelope-padding-right", Current.paddingRight); Current.paddingBottom = FloatOf("--zui-envelope-padding-bottom", Current.paddingBottom); Current.paddingLeft = FloatOf("--zui-envelope-padding-left", Current.paddingLeft);
            Current.curveThickness = FloatOf("--zui-envelope-curve-thickness", Current.curveThickness); Current.curveHoverThickness = FloatOf("--zui-envelope-curve-hover-thickness", Current.curveHoverThickness); Current.hitRadiusExtra = FloatOf("--zui-envelope-hit-radius-extra", Current.hitRadiusExtra);
            Current.gridRows = Mathf.RoundToInt(Mathf.Min(256f, NonNegative(FloatOf("--zui-envelope-grid-rows", Current.gridRows))));
            Current.gridThickness = FloatOf("--zui-envelope-grid-thickness", Current.gridThickness); Current.selectedStrokeThickness = FloatOf("--zui-envelope-selected-stroke-thickness", Current.selectedStrokeThickness); Current.selectionBoxFillAlpha = FloatOf("--zui-envelope-selection-box-fill-alpha", Current.selectionBoxFillAlpha); Current.hoverSaturationScale = FloatOf("--zui-envelope-hover-saturation-scale", Current.hoverSaturationScale); Current.hoverValueScale = FloatOf("--zui-envelope-hover-value-scale", Current.hoverValueScale); Current.uncertaintyFillAlpha = FloatOf("--zui-envelope-uncertainty-fill-alpha", Current.uncertaintyFillAlpha); Current.uncertaintyStrokeAlpha = FloatOf("--zui-envelope-uncertainty-stroke-alpha", Current.uncertaintyStrokeAlpha); Current.uncertaintyStrokeThickness = FloatOf("--zui-envelope-uncertainty-stroke-thickness", Current.uncertaintyStrokeThickness); Current.dottedWhiteMix = FloatOf("--zui-envelope-dotted-white-mix", Current.dottedWhiteMix); Current.dottedMinWidth = FloatOf("--zui-envelope-dotted-min-width", Current.dottedMinWidth); Current.ghostOpacity = FloatOf("--zui-envelope-ghost-opacity", Current.ghostOpacity);
            Current.editable = SanitizeHandle(HandleOf("editable", Current.editable)); Current.xEditable = SanitizeHandle(HandleOf("x-editable", Current.xEditable)); Current.yEditable = SanitizeHandle(HandleOf("y-editable", Current.yEditable)); Current.notEditable = SanitizeHandle(HandleOf("not-editable", Current.notEditable));
            Current.borderTop = NonNegative(Current.borderTop); Current.borderRight = NonNegative(Current.borderRight); Current.borderBottom = NonNegative(Current.borderBottom); Current.borderLeft = NonNegative(Current.borderLeft); Current.paddingTop = NonNegative(Current.paddingTop); Current.paddingRight = NonNegative(Current.paddingRight); Current.paddingBottom = NonNegative(Current.paddingBottom); Current.paddingLeft = NonNegative(Current.paddingLeft); Current.curveThickness = NonNegative(Current.curveThickness); Current.curveHoverThickness = NonNegative(Current.curveHoverThickness); Current.hitRadiusExtra = NonNegative(Current.hitRadiusExtra); Current.gridThickness = NonNegative(Current.gridThickness); Current.selectedStrokeThickness = NonNegative(Current.selectedStrokeThickness); Current.uncertaintyStrokeThickness = NonNegative(Current.uncertaintyStrokeThickness); Current.gridRows = Mathf.Clamp(Current.gridRows, 0, 256); Current.selectionBoxFillAlpha = Unit(Current.selectionBoxFillAlpha); Current.dottedWhiteMix = Unit(Current.dottedWhiteMix); Current.ghostOpacity = Unit(Current.ghostOpacity); Current.uncertaintyFillAlpha = Unit(Current.uncertaintyFillAlpha); Current.uncertaintyStrokeAlpha = Unit(Current.uncertaintyStrokeAlpha); Current.hoverSaturationScale = NonNegative(Current.hoverSaturationScale); Current.hoverValueScale = NonNegative(Current.hoverValueScale);
        }

        Values FromDefinition(ZUIEnvelopeDef def, Color curveColor)
        {
            if (def == null) return Defaults(curveColor);
            var sheet = def.ownerSheet;
            var edge = def.border != null ? def.border.edgeWidth : null;
            return new Values
            {
                background = def.background != null ? def.background.GetColorA(sheet) : Color.clear,
                borderColor = def.border != null ? def.border.color.GetColorA(sheet) : Color.clear,
                gridColor = def.gridColor.Resolve(sheet), curveColor = curveColor, selectedColor = def.selectedColor.Resolve(sheet),
                borderTop = edge != null ? edge.Top : 0f, borderRight = edge != null ? edge.Right : 0f, borderBottom = edge != null ? edge.Bottom : 0f, borderLeft = edge != null ? edge.Left : 0f,
                paddingTop = def.paddingTop, paddingRight = def.paddingRight, paddingBottom = def.paddingBottom, paddingLeft = def.paddingLeft,
                curveThickness = def.curveThickness, curveHoverThickness = def.curveHoverThickness, hitRadiusExtra = def.hitRadiusExtra, gridRows = def.gridRows, gridThickness = 1f, selectedStrokeThickness = 1f, selectionBoxFillAlpha = 0.1f, hoverSaturationScale = 0.8f, hoverValueScale = 1.4f, uncertaintyFillAlpha = 0.14f, uncertaintyStrokeAlpha = 0.75f, uncertaintyStrokeThickness = 1f, dottedWhiteMix = 0.45f, dottedMinWidth = 1.5f, ghostOpacity = 0.5f,
                editable = HandleFrom(def.editable, sheet), xEditable = HandleFrom(def.xEditable, sheet), yEditable = HandleFrom(def.yEditable, sheet), notEditable = HandleFrom(def.notEditable, sheet)
            };
        }

        static Values Defaults(Color curveColor) => new Values { curveColor = curveColor, curveThickness = 1.5f, curveHoverThickness = 3f, hitRadiusExtra = 4f, gridThickness = 1f, selectedStrokeThickness = 1f, selectionBoxFillAlpha = 0.1f, hoverSaturationScale = 0.8f, hoverValueScale = 1.4f, uncertaintyFillAlpha = 0.14f, uncertaintyStrokeAlpha = 0.75f, uncertaintyStrokeThickness = 1f, dottedWhiteMix = 0.45f, dottedMinWidth = 1.5f, ghostOpacity = 0.5f };

        static Handle HandleFrom(ZUIEnvelopeHandleDef source, ZUIStyleSheetAsset sheet)
        {
            if (source == null) return default;
            return new Handle { radius = source.radius, hoverRadius = source.hoverRadius, borderWidth = source.borderWidth, fillColor = source.fillColor.Resolve(sheet), hoverFillColor = source.hoverFillColor.Resolve(sheet), borderColor = source.borderColor.Resolve(sheet) };
        }

        Handle HandleOf(string state, Handle fallback)
        {
            fallback.radius = FloatOf("--zui-envelope-" + state + "-radius", fallback.radius);
            fallback.hoverRadius = FloatOf("--zui-envelope-" + state + "-hover-radius", fallback.hoverRadius);
            fallback.borderWidth = FloatOf("--zui-envelope-" + state + "-border-width", fallback.borderWidth);
            fallback.fillColor = ColorOf("--zui-envelope-" + state + "-fill", fallback.fillColor);
            fallback.hoverFillColor = ColorOf("--zui-envelope-" + state + "-hover-fill", fallback.hoverFillColor);
            fallback.borderColor = ColorOf("--zui-envelope-" + state + "-border-color", fallback.borderColor);
            return fallback;
        }

        static Handle SanitizeHandle(Handle value)
        {
            value.radius = NonNegative(value.radius); value.hoverRadius = NonNegative(value.hoverRadius); value.borderWidth = NonNegative(value.borderWidth);
            return value;
        }

        void Set(CustomStyleResolvedEvent style, string name, CustomStyleProperty<float> property)
        {
            if (style.customStyle.TryGetValue(property, out float value)) _floats[name] = value;
        }

        void Set(CustomStyleResolvedEvent style, string name, CustomStyleProperty<Color> property)
        {
            if (style.customStyle.TryGetValue(property, out Color value)) _colors[name] = value;
        }

        void SetHandle(CustomStyleResolvedEvent style, string state)
        {
            string prefix = "--zui-envelope-" + state + "-";
            Set(style, prefix + "radius", new CustomStyleProperty<float>(prefix + "radius")); Set(style, prefix + "hover-radius", new CustomStyleProperty<float>(prefix + "hover-radius")); Set(style, prefix + "border-width", new CustomStyleProperty<float>(prefix + "border-width"));
            Set(style, prefix + "fill", new CustomStyleProperty<Color>(prefix + "fill")); Set(style, prefix + "hover-fill", new CustomStyleProperty<Color>(prefix + "hover-fill")); Set(style, prefix + "border-color", new CustomStyleProperty<Color>(prefix + "border-color"));
        }

        float FloatOf(string name, float fallback) => _floats.TryGetValue(name, out float value) ? value : fallback;
        Color ColorOf(string name, Color fallback) => _colors.TryGetValue(name, out Color value) ? value : fallback;
        static float NonNegative(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
        static float Unit(float value) => Mathf.Clamp01(NonNegative(value));
    }
}
