using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The amber lightning bolt that marks "game code can move this" (T-0492), painted rather than typed: the editor font
    /// has no bolt of its own, and the fallback one it borrows sits too low to survive a narrow slot, so in the 14 px
    /// mark after a slider it came out as an underscore. Painted, it is the same crisp shape at any size.
    /// </summary>
    public class ZpocBolt : VisualElement {

        public static readonly Color Amber = new Color(1f, 150f / 255f, 40f / 255f);
        /// <summary>The bolt of something game code could reach but does not yet (no id): cold and dull, so only a set id is lit.</summary>
        public static readonly Color Unlit = new Color(0.43f, 0.48f, 0.54f);

        Color color = Amber;
        /// <summary>The bolt's colour: <see cref="Amber"/> (lit) or <see cref="Unlit"/>.</summary>
        public Color Color { get => color; set { if (color == value) return; color = value; MarkDirtyRepaint(); } }

        public ZpocBolt() {
            generateVisualContent += Paint;
        }

        void Paint(MeshGenerationContext ctx) {
            var r = contentRect;
            float s = Mathf.Min(r.width, r.height) * 0.72f;
            if (s <= 1f) return;
            float cx = r.x + r.width * 0.5f, cy = r.y + r.height * 0.5f;
            // A bolt in a unit box, centred: down-left stroke, a notch, then down-left again.
            Vector2 P(float x, float y) => new Vector2(cx + (x - 0.5f) * s * 0.62f, cy + (y - 0.5f) * s);
            var p = ctx.painter2D;
            p.fillColor = color;
            p.BeginPath();
            p.MoveTo(P(0.72f, 0f));
            p.LineTo(P(0.18f, 0.56f));
            p.LineTo(P(0.50f, 0.56f));
            p.LineTo(P(0.30f, 1f));
            p.LineTo(P(0.84f, 0.42f));
            p.LineTo(P(0.52f, 0.42f));
            p.ClosePath();
            p.Fill();
        }
    }
}
