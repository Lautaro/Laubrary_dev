using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    // A translucent finish over the USS-resolved face and border. The underlying native button
    // remains responsible for its palette, layout, focus, disabled state and interaction.
    internal sealed class ZuiButtonSurface
    {
        static readonly ConditionalWeakTable<Button, ZuiButtonSurface> Surfaces = new();
        static readonly CustomStyleProperty<float> Strength = new("--zui-button-metallic-strength");
        static readonly CustomStyleProperty<Color> FillTop = new("--zui-button-fill-top");
        static readonly CustomStyleProperty<Color> FillMiddle = new("--zui-button-fill-middle");
        static readonly CustomStyleProperty<Color> FillBottom = new("--zui-button-fill-bottom");
        static readonly CustomStyleProperty<Color> BorderTop = new("--zui-button-border-top");
        static readonly CustomStyleProperty<Color> BorderBottom = new("--zui-button-border-bottom");

        readonly Button _button;
        readonly Vector2[] _outer = new Vector2[32];
        readonly Vector2[] _inner = new Vector2[32];
        float _strength;
        Color _fillTop, _fillMiddle, _fillBottom, _borderTop, _borderBottom;

        internal static void Attach(Button button) => Surfaces.GetValue(button, b => new ZuiButtonSurface(b));

        ZuiButtonSurface(Button button)
        {
            _button = button;
            button.AddToClassList("zui-button-surface");
            button.RegisterCallback<CustomStyleResolvedEvent>(Resolve);
            // TextElement already registered its text painter in its constructor. Prepend our
            // surface so lettering and child icons remain above the finish, with their exact colors.
            button.generateVisualContent = (Action<MeshGenerationContext>)Paint + button.generateVisualContent;
        }

        void Resolve(CustomStyleResolvedEvent evt)
        {
            if (evt.target != _button) return;
            // Missing/removed sheets and scopes must restore defaults rather than retain an old root.
            _strength = 0f;
            _fillTop = _fillMiddle = _fillBottom = _borderTop = _borderBottom = Color.clear;
            var custom = evt.customStyle;
            if (custom.TryGetValue(Strength, out float strength)) _strength = Mathf.Clamp01(strength);
            custom.TryGetValue(FillTop, out _fillTop);
            custom.TryGetValue(FillMiddle, out _fillMiddle);
            custom.TryGetValue(FillBottom, out _fillBottom);
            custom.TryGetValue(BorderTop, out _borderTop);
            custom.TryGetValue(BorderBottom, out _borderBottom);
            _button.MarkDirtyRepaint();
        }

        void Paint(MeshGenerationContext context)
        {
            if (_strength <= 0f || _button.ClassListContains("zui-button-plain")) return;
            var style = _button.resolvedStyle;
            var outer = new Rect(0f, 0f, _button.layout.width, _button.layout.height);
            if (outer.width < 1f || outer.height < 1f) return;
            // An authored background image owns its face; it must not acquire an unexpected wash.
            bool paintFill = style.backgroundImage.Equals(default(Background));
            float cap = Mathf.Min(outer.width, outer.height) * 0.5f;
            var radii = new Vector4(Mathf.Clamp(style.borderTopLeftRadius, 0f, cap),
                Mathf.Clamp(style.borderTopRightRadius, 0f, cap),
                Mathf.Clamp(style.borderBottomRightRadius, 0f, cap),
                Mathf.Clamp(style.borderBottomLeftRadius, 0f, cap));
            float left = Mathf.Clamp(style.borderLeftWidth, 0f, outer.width * 0.5f);
            float right = Mathf.Clamp(style.borderRightWidth, 0f, outer.width * 0.5f);
            float top = Mathf.Clamp(style.borderTopWidth, 0f, outer.height * 0.5f);
            float bottom = Mathf.Clamp(style.borderBottomWidth, 0f, outer.height * 0.5f);
            var inner = new Rect(left, top, Mathf.Max(0f, outer.width - left - right),
                Mathf.Max(0f, outer.height - top - bottom));
            var innerRadii = new Vector4(Mathf.Max(0f, radii.x - Mathf.Max(left, top)),
                Mathf.Max(0f, radii.y - Mathf.Max(right, top)),
                Mathf.Max(0f, radii.z - Mathf.Max(right, bottom)),
                Mathf.Max(0f, radii.w - Mathf.Max(left, bottom)));
            float innerCap = Mathf.Min(inner.width, inner.height) * 0.5f;
            for (int i = 0; i < 4; i++) innerRadii[i] = Mathf.Min(innerRadii[i], innerCap);

            if (paintFill && style.backgroundColor.a > 0f && inner.width > 0f && inner.height > 0f)
                PaintFill(context, inner, innerRadii, outer.height, style.backgroundColor.a);

            if (left + right + top + bottom <= 0f) return;
            // Match each native border side's alpha; a transparent/zero-width side stays absent.
            Perimeter(outer, radii, _outer);
            Perimeter(inner, innerRadii, _inner);
            var mesh = context.Allocate(128, 192);
            for (int i = 0; i < 32; i++)
            {
                int next = (i + 1) % 32;
                float alpha = (i / 8) switch
                {
                    0 => style.borderTopColor.a,
                    1 => style.borderRightColor.a,
                    2 => style.borderBottomColor.a,
                    _ => style.borderLeftColor.a
                };
                alpha *= _strength;
                Quad(mesh, i * 4, _outer[i], _outer[next], _inner[next], _inner[i],
                    BorderColor(_outer[i].y, outer.height, alpha), BorderColor(_outer[next].y, outer.height, alpha));
            }
        }

        void PaintFill(MeshGenerationContext context, Rect rect, Vector4 radii, float height, float alpha)
        {
            int strips = Mathf.Clamp(Mathf.CeilToInt(rect.height), 2, 64);
            var mesh = context.Allocate(strips * 4, strips * 6);
            for (int i = 0; i < strips; i++)
            {
                float y0 = Mathf.Lerp(rect.yMin, rect.yMax, i / (float)strips);
                float y1 = Mathf.Lerp(rect.yMin, rect.yMax, (i + 1f) / strips);
                Edges(rect, radii, y0, out float l0, out float r0);
                Edges(rect, radii, y1, out float l1, out float r1);
                Quad(mesh, i * 4, new Vector2(l0, y0), new Vector2(r0, y0),
                    new Vector2(r1, y1), new Vector2(l1, y1),
                    FillColor(y0, height, alpha), FillColor(y1, height, alpha), true);
            }
        }

        Color FillColor(float y, float height, float alpha)
        {
            float t = Mathf.Clamp01(y / height);
            Color color = t <= 0.5f ? Color.Lerp(_fillTop, _fillMiddle, t * 2f)
                : Color.Lerp(_fillMiddle, _fillBottom, (t - 0.5f) * 2f);
            color.a *= alpha * _strength;
            return color;
        }

        Color BorderColor(float y, float height, float alpha)
        {
            var color = Color.Lerp(_borderTop, _borderBottom, Mathf.Clamp01(y / height));
            color.a *= alpha;
            return color;
        }

        static void Edges(Rect rect, Vector4 radii, float y, out float left, out float right)
        {
            float fromTop = y - rect.yMin, fromBottom = rect.yMax - y;
            left = rect.xMin + Mathf.Max(Inset(radii.x, fromTop), Inset(radii.w, fromBottom));
            right = rect.xMax - Mathf.Max(Inset(radii.y, fromTop), Inset(radii.z, fromBottom));
        }

        static float Inset(float radius, float distance)
        {
            if (radius <= 0f || distance >= radius) return 0f;
            float offset = radius - distance;
            return radius - Mathf.Sqrt(Mathf.Max(0f, radius * radius - offset * offset));
        }

        static void Perimeter(Rect rect, Vector4 radii, Vector2[] points)
        {
            for (int corner = 0; corner < 4; corner++)
            {
                float radius = radii[corner];
                Vector2 center = corner switch
                {
                    0 => new Vector2(rect.xMin + radius, rect.yMin + radius),
                    1 => new Vector2(rect.xMax - radius, rect.yMin + radius),
                    2 => new Vector2(rect.xMax - radius, rect.yMax - radius),
                    _ => new Vector2(rect.xMin + radius, rect.yMax - radius)
                };
                for (int step = 0; step < 8; step++)
                {
                    float angle = (-180f + corner * 90f + step * 90f / 7f) * Mathf.Deg2Rad;
                    points[corner * 8 + step] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                }
            }
        }

        static void Quad(MeshWriteData mesh, int start, Vector2 a, Vector2 b, Vector2 c, Vector2 d,
            Color first, Color second, bool horizontal = false)
        {
            mesh.SetNextVertex(new Vertex { position = new Vector3(a.x, a.y, Vertex.nearZ), tint = first });
            mesh.SetNextVertex(new Vertex { position = new Vector3(b.x, b.y, Vertex.nearZ), tint = horizontal ? first : second });
            mesh.SetNextVertex(new Vertex { position = new Vector3(c.x, c.y, Vertex.nearZ), tint = second });
            mesh.SetNextVertex(new Vertex { position = new Vector3(d.x, d.y, Vertex.nearZ), tint = horizontal ? second : first });
            mesh.SetNextIndex((ushort)start); mesh.SetNextIndex((ushort)(start + 1)); mesh.SetNextIndex((ushort)(start + 2));
            mesh.SetNextIndex((ushort)(start + 2)); mesh.SetNextIndex((ushort)(start + 3)); mesh.SetNextIndex((ushort)start);
        }
    }
}
