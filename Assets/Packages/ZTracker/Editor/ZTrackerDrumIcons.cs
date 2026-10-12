using Laubrary.ZTracker.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    /// <summary>Small vector percussion symbols, independent of installed font glyphs.</summary>
    internal static class ZTrackerDrumIcons
    {
        internal static VisualElement Create(string tag, float size = 18)
        {
            var icon = new VisualElement { name = "drum-icon", pickingMode = PickingMode.Ignore, tooltip = DrumTags.Canonical(tag) };
            icon.style.width = size; icon.style.height = size; icon.style.flexShrink = 0;
            icon.generateVisualContent += context => Draw(context.painter2D, DrumTags.Category(tag), icon.contentRect);
            return icon;
        }

        static void Draw(Painter2D p, DrumCategory category, Rect rect)
        {
            float scale = Mathf.Min(rect.width, rect.height) / 20;
            Vector2 Point(float x, float y) => new Vector2(rect.x + x * scale, rect.y + y * scale);
            void Line(params float[] coordinates)
            {
                p.BeginPath(); p.MoveTo(Point(coordinates[0], coordinates[1]));
                for (int i = 2; i < coordinates.Length; i += 2) p.LineTo(Point(coordinates[i], coordinates[i + 1]));
                p.Stroke();
            }
            void Ellipse(float x, float y, float rx, float ry)
            {
                p.BeginPath();
                for (int i = 0; i <= 24; i++)
                {
                    float angle = i * Mathf.PI / 12;
                    var point = Point(x + Mathf.Cos(angle) * rx, y + Mathf.Sin(angle) * ry);
                    if (i == 0) p.MoveTo(point); else p.LineTo(point);
                }
                p.ClosePath(); p.Stroke();
            }
            p.strokeColor = new Color(.87f, .94f, 1); p.lineWidth = Mathf.Max(1, 1.25f * scale);
            switch (category)
            {
                case DrumCategory.Kick:
                    Ellipse(10, 9, 6.5f, 6.5f); Ellipse(12, 11, 1.5f, 1.5f);
                    Line(5, 14, 3, 18); Line(15, 14, 17, 18); break;
                case DrumCategory.Snare:
                    Ellipse(10, 6, 7, 2.5f); Line(3, 6, 3, 14, 17, 14, 17, 6);
                    Line(4, 8, 7, 13, 10, 8, 13, 13, 16, 8); break;
                case DrumCategory.Hats:
                    Ellipse(10, 7, 7.5f, 2); Line(3, 11, 17, 11); Line(10, 3, 10, 17); Line(6, 18, 10, 16, 14, 18); break;
                case DrumCategory.Percussion:
                    Ellipse(10, 4, 5, 2); Line(5, 4, 7, 17, 13, 17, 15, 4); Line(6, 8, 14, 8); break;
                case DrumCategory.Shaker:
                    Line(4, 7, 12, 3, 16, 13, 8, 17, 4, 7); Line(5, 10, 13, 6); Line(7, 14, 15, 10); break;
                case DrumCategory.FX:
                    Line(11, 2, 4, 11, 10, 11, 8, 18, 16, 8, 10, 8, 11, 2); break;
                default:
                    Line(10, 2, 18, 10, 10, 18, 2, 10, 10, 2); Ellipse(10, 10, 2, 2); break;
            }
        }
    }
}
