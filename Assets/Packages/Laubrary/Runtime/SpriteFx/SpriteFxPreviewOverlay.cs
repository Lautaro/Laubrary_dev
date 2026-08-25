// SpriteFxPreviewOverlay.cs
// The registration seam that lets ONE SpriteFx effect draw its OWN diagnostic overlay into a preview — a light's
// radius circle, a mask's boundary, a warp's pivot — without any window knowing that effect exists.
//
// Why the contract lives in the RUNTIME assembly, unguarded by #if UNITY_EDITOR: the modifiers themselves live
// here (SpriteFxModifiers.cs, SpriteFxRelight.cs, …) and a runtime assembly cannot reference editor code, so an
// editor-side interface would be unimplementable by the only types that could ever implement it. RelightModifier's
// TryGetPointGizmo already sat here unguarded for exactly this reason; this file generalises that precedent rather
// than inventing a new one. Nothing in here touches UnityEditor, and drawing a ring into a Color32[] costs a
// player build nothing but a few unreferenced methods.
//
// The editor half — collecting the effects that want an overlay, giving each a toggle, and remembering which are
// on — is SpriteFxPreviewOverlays (Editor/SpriteFx/SpriteFxPreviewOverlays.cs).
using UnityEngine;

namespace Laubrary.SpriteFx
{
    /// <summary>
    /// The preview's own pixel buffer, handed to an effect so it can mark itself on the picture that was just
    /// rendered — in the SAME buffer-pixel space the stack was shaded in, so what is drawn cannot drift from what
    /// was shaded.
    /// </summary>
    ///
    /// It carries the drawing primitives rather than leaving each effect to re-derive them, because every one of
    /// them is a trap someone has already paid for once: blending that loses the mark over a bright sprite,
    /// blending that loses it over a TRANSPARENT one, and a circle stepped in fixed increments that reads as a
    /// dashed polygon the moment the radius grows. An effect that needs a ring should get a correct ring for free.
    public readonly struct SpriteFxOverlayCanvas
    {
        /// The live buffer — writes land straight in the picture about to be shown. Row-major, y * Width + x.
        public readonly Color32[] Pixels;
        public readonly int Width;
        public readonly int Height;

        public SpriteFxOverlayCanvas(Color32[] pixels, int width, int height)
        {
            Pixels = pixels;
            Width = width;
            Height = height;
        }

        /// One pixel, clipped to the buffer and alpha-blended over whatever is already there.
        ///
        /// The alpha rule is the load-bearing half: the blended colour is a normal over-composite, but the
        /// resulting ALPHA is max(dst, src) rather than the composite's. A marker is a diagnostic, and the most
        /// interesting place to put one is often OUTSIDE the silhouette — a light sitting off the sprite, a reach
        /// that overshoots the frame. Composited honestly it would inherit the surround's zero alpha and be
        /// invisible in precisely the case it was drawn for.
        public void Plot(int x, int y, Color32 col)
        {
            if (Pixels == null) return;
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;   // off the stage — a marker may sit outside the frame
            int i = y * Width + x;
            float a = col.a / 255f;
            Color32 d = Pixels[i];
            Pixels[i] = new Color32(
                (byte)(d.r * (1f - a) + col.r * a),
                (byte)(d.g * (1f - a) + col.g * a),
                (byte)(d.b * (1f - a) + col.b * a),
                (byte)Mathf.Max(d.a, col.a));   // stay visible even where the sprite itself is transparent
        }

        /// A one-pixel ring at `radius` around (cx, cy).
        ///
        /// The step count scales with the radius on purpose. Stepped a fixed number of times, consecutive plotted
        /// points on a large circle land more than a pixel apart and the ring reads as a dashed polygon rather
        /// than a circle — which is worse than useless when the whole reason it is on screen is to judge a reach.
        /// The 24 floor keeps a tiny radius from collapsing into a few stray dots; the 720 ceiling stops a huge
        /// one from plotting the same pixel hundreds of times over.
        public void Circle(float cx, float cy, float radius, Color32 col)
        {
            int steps = Mathf.Clamp(Mathf.CeilToInt(radius * 6f), 24, 720);
            for (int i = 0; i < steps; i++)
            {
                float t = i / (float)steps * Mathf.PI * 2f;
                Plot(Mathf.RoundToInt(cx + Mathf.Cos(t) * radius),
                     Mathf.RoundToInt(cy + Mathf.Sin(t) * radius), col);
            }
        }

        /// A small solid square centred on (cx, cy) — `halfSize` 1 is the 3x3 block a position marker wants. A
        /// single pixel is not findable on a busy sprite; a block is, and still points at one exact coordinate.
        public void Dot(float cx, float cy, int halfSize, Color32 col)
        {
            int dx = Mathf.RoundToInt(cx), dy = Mathf.RoundToInt(cy);
            for (int oy = -halfSize; oy <= halfSize; oy++)
                for (int ox = -halfSize; ox <= halfSize; ox++)
                    Plot(dx + ox, dy + oy, col);
        }

        /// A straight line between two points (a plain DDA — one step per pixel along the longer axis, so it is
        /// gap-free at any angle). Not used by any effect yet; it is here because a direction, an axis or a
        /// bounding edge is the obvious next thing an overlay needs, and the alternative is the next effect
        /// hand-rolling its own inside a modifier.
        public void Line(float x0, float y0, float x1, float y1, Color32 col)
        {
            float dx = x1 - x0, dy = y1 - y0;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy))));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Plot(Mathf.RoundToInt(x0 + dx * t), Mathf.RoundToInt(y0 + dy * t), col);
            }
        }
    }

    /// <summary>
    /// Opt-in capability on a <see cref="PyreModifier"/>: "I have something to show on the preview picture."
    /// </summary>
    ///
    /// <remarks>
    /// <para><b>The recipe.</b> To give a SpriteFx effect its own preview visual, implement this interface ON THE
    /// MODIFIER — and do nothing else. The host window collects every enabled instance that currently wants an
    /// overlay, gives each one its own toggle, and draws the ones that are switched on. The toggle appears the
    /// moment an instance wants it and disappears with it. <b>Never add a toggle to the window for a specific
    /// effect</b>: that is the hardcoding this interface exists to delete, it puts a permanently-visible control
    /// in a transport row for a stack that may contain no such effect at all, and it grows the row by one for
    /// every effect that ever wants to draw something.</para>
    ///
    /// <para><b>Instances, not types — but the TYPE decides.</b> By default several instances of the same effect
    /// in one stack each get their OWN toggle, and each draws only itself: two Fake Lights means two "Light
    /// radius" toggles, disambiguated by the host, either of which can be on alone. That is the right answer
    /// almost always, because the whole reason to mark a light is to tell it apart from the other one. When it is
    /// NOT the right answer — a dozen instances that only make sense read together, or a marker that is identical
    /// for every instance and would just overdraw itself — the type says so by returning a shared
    /// <see cref="OverlayGroupKey"/>, and the host collapses all of them behind a single toggle instead. Either
    /// way <see cref="DrawPreviewOverlay"/> draws only THIS instance's marks and never scans the stack for its
    /// siblings; grouping changes how many toggles there are, never who draws what.</para>
    ///
    /// <para><b>Who checks <c>enabled</c>.</b> Not you. The collector already requires
    /// <see cref="PyreModifier.enabled"/> before it will even look at an instance, so
    /// <see cref="WantsPreviewOverlay"/> must answer only the narrower question this effect alone can answer —
    /// "given how I am currently configured, is there anything to draw?" Re-checking <c>enabled</c> here is
    /// harmless but redundant; OMITTING the configuration check is not, because then the toggle shows for a Fake
    /// Light in Directional mode, which has neither a position nor a radius to mark.</para>
    ///
    /// <para>Drawing happens straight after the stack has run, into the buffer that is about to be shown, and
    /// never into a bake or a real render — an overlay is a diagnostic for the person authoring the effect, not
    /// part of the effect.</para>
    /// </remarks>
    public interface ISpriteFxPreviewOverlay
    {
        /// Short label for this overlay's toggle — a noun for the thing being marked ("Light radius"), not a verb
        /// and not a sentence. The host may append an ordinal when a stack holds several of the same effect.
        string OverlayLabel { get; }

        /// The toggle's hover tooltip: what the overlay marks and what it is for. The host may append a sentence
        /// identifying WHICH instance this toggle belongs to, so do not try to say that here.
        string OverlayTooltip { get; }

        /// Whether THIS instance has anything to show right now, given its current configuration. The instance
        /// decides; the host never guesses from the type. (A Fake Light answers true only in Point mode.)
        bool WantsPreviewOverlay { get; }

        /// <summary>
        /// What several of me in one stack should mean. Return <c>null</c> — the answer for almost every effect,
        /// and what a Fake Light returns — for "each instance is its own thing": one toggle each, separately
        /// switchable, so the author can light up exactly the one they are tuning. Return a non-null constant
        /// (any stable string; the type name is the obvious choice) for "all of my instances are one visual":
        /// every instance sharing that key collapses behind a SINGLE toggle that draws all of them at once.
        /// </summary>
        ///
        /// The choice belongs to the type because only the type knows whether its marks are distinguishing or
        /// identical. Two lights at different positions are worth telling apart; a dozen markers that all draw the
        /// same frame outline are not, and a dozen toggles for them would be the same clutter this whole pattern
        /// exists to prevent. Grouping is across instances of one key, so two DIFFERENT effects never merge.
        string OverlayGroupKey { get; }

        /// Mark this instance on the picture that was just rendered, using the canvas primitives.
        void DrawPreviewOverlay(SpriteFxOverlayCanvas canvas);
    }
}
