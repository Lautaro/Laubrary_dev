using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Laubrary.PreviewKit;
using Laubrary.SpriteFx;

namespace Laubrary.TextSplash
{
    /// Direction a splash slides in FROM / out TO. None = no slide (fade/scale only).
    public enum SplashDir { None, Left, Right, Top, Bottom }

    /// Easing applied to a transition's 0..1 progress. `Custom` reads the transition's own envelope instead.
    public enum SplashEase { Linear, SmoothStep, EaseIn, EaseOut, EaseInOut, Back, Elastic, Bounce, Custom }

    /// Axis a per-letter 3D spin rotates about.
    public enum SplashAxis { X, Y, Z }

    /// The order letters take their turn in when a spawn stagger cascades across the line.
    public enum SplashOrder { LeftToRight, RightToLeft, CentreOut, EdgesIn, Random }

    // ──────────────────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>An animatable scalar (a ZUI "MultCont" — Static / Min-Max / Curve, with the ⋯ multiplier menu) plus
    /// the ONE thing a splash needs on top: whether it reads the WHOLE line's life or each LETTER's own life.
    ///
    /// Curve mode is authored against a normalized 0..1 life, so <see cref="value"/> is created with duration 1.
    /// Min-Max mode rolls ONCE — per play, or per LETTER when <see cref="perLetter"/> is on — never per frame:
    /// `ZUIValue.Evaluate` calls `Random.Range` on every call, which would turn a random size into a strobe. The roll
    /// is a deterministic hash of the play's seed and the letter index, so the same play always looks the same and
    /// the editor preview matches the runtime.</summary>
    [Serializable]
    public class SplashScalar
    {
        [Tooltip("The value — static, a random min/max rolled once, or a curve over life.")]
        public ZUIValue value = new ZUIValue(1f);

        [Tooltip("Scope. ON = each letter reads this against its OWN lifetime, so a staggered line ripples — a curve " +
                 "plays out per letter, and a Min-Max rolls a fresh value for every letter. OFF = the whole line " +
                 "shares one lifetime and every letter gets the same value at the same moment. Turning this on is " +
                 "enough to make the line animate per letter; it needs no other switch.")]
        public bool perLetter = false;

        public SplashScalar() { }
        public SplashScalar(float v, float yMin, float yMax)
        {
            value = new ZUIValue(v) { yMin = yMin, yMax = yMax, duration = 1f, cooldown = -1f };
        }

        /// <summary>Value at a normalized 0..1 life. <paramref name="rollKey"/> seeds Min-Max so it rolls once and
        /// stays put: pass the play's seed for a line-wide value, or the seed combined with the letter index for a
        /// per-letter one. Static and Curve modes ignore it.</summary>
        public float Evaluate(float life, int rollKey)
        {
            if (value == null) return 0f;
            if (value.mode != ZUIValue.Mode.MinMax) return value.Evaluate(Mathf.Clamp01(life));
            // Sfx.Hash01 is the package's frozen deterministic hash — reused so a roll is stable across the
            // preview, the runtime and a reload, which UnityEngine.Random could never be.
            float t = Sfx.Hash01(rollKey, 0, 0);
            return Mathf.Lerp(value.min, value.max, t) * value.Multiplier();
        }

        /// The life this scalar should be read at, given the line life and a letter's own life.
        public float LifeFor(float lineLife, float letterLife) => perLetter ? letterLife : lineLife;

        /// The Min-Max roll key for this scalar: per-letter scalars roll per letter, line-wide ones roll once.
        public int RollKey(int playSeed, int letterIndex, int salt)
            => perLetter ? unchecked(playSeed * 397 ^ (letterIndex + 1) * 92821 ^ salt * 6151)
                         : unchecked(playSeed * 397 ^ salt * 6151);
    }

    // ──────────────────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>One END of a splash — how it comes IN, or how it goes OUT. Both ends carry the same knobs so an
    /// entrance and an exit can differ completely (fly in from the left with a spin, fade straight out, …).
    /// Progress always runs 0 → 1 in the direction of PLAY: the in-transition eases from "off-stage" (0) to "at rest"
    /// (1); the out-transition eases from "at rest" (0) to "off-stage" (1).</summary>
    [Serializable]
    public class SplashTransition
    {
        [Min(0f)] [Tooltip("Seconds this end takes.")]
        public float duration = 0.35f;

        [Tooltip("Edge the text flies from (in) / to (out). None = no slide — it fades and/or scales in place.")]
        public SplashDir direction = SplashDir.Bottom;

        [Range(0f, 1f)] [Tooltip("Where the slide starts (in) / ends (out), between just-off-screen and the resting " +
                 "place. 0 = fully outside the viewport edge (the text is completely hidden); 1 = already at its " +
                 "destination, so there is no slide at all. 0.5 starts it half way in.")]
        public float slideDistance = 0f;

        [Tooltip("Easing curve for this end. Custom reads the envelope below.")]
        public SplashEase ease = SplashEase.SmoothStep;

        [Tooltip("Custom easing envelope (Ease = Custom): X = raw progress 0..1, Y = eased progress.")]
        public ZUIValue easeCurve = MakeEaseCurve();

        [Range(-1440f, 1440f)] [Tooltip("Degrees spun THROUGH over this end (0 = none). Per-letter mode spins each " +
                 "letter about its own centre; whole-line mode spins the line about its centre.")]
        public float spinDegrees = 0f;

        [Tooltip("Axis the spin rotates about. X/Y foreshorten to a flip under the flat overlay camera; Z = in-plane.")]
        public SplashAxis spinAxis = SplashAxis.Y;

        [Range(0f, 4f)] [Tooltip("Scale at the off-stage extreme (1 = no scale punch; 0 = grows from nothing; " +
                 "2 = shrinks in from double size).")]
        public float scale = 1f;

        // NOTE: this end carries no `fade` and no `stagger` of its own, on purpose.
        //   fade    — removed: the `alpha` SplashScalar already owns opacity, and two independent ways to fade the
        //             same text just fight each other. For a fade-in, curve `alpha` (per-letter if you want a ripple).
        //   stagger — removed: it now lives ONCE on the asset as `spawnStagger` + `order`, because a letter needs a
        //             single well-defined LIFETIME for the per-letter scalars to run over; two different staggers
        //             for the two ends would leave "this letter's life" ambiguous.

        /// Eased 0..1 progress for a raw 0..1 progress.
        public float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            if (ease == SplashEase.Custom) return easeCurve != null ? easeCurve.Evaluate(t) : t;
            return SplashEasing.Apply(ease, t);
        }

        /// A fresh identity easing envelope (0,0)→(1,1) authored on a 0..1 life.
        public static ZUIValue MakeEaseCurve()
        {
            var v = new ZUIValue(1f)
            {
                mode = ZUIValue.Mode.Curve,
                yMin = 0f, yMax = 1f, duration = 1f, cooldown = -1f
            };
            v.points.Add(new ZUIEnvelopePoint(0f, 0f));
            v.points.Add(new ZUIEnvelopePoint(1f, 1f));
            return v;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────────────────────────────────────
    /// Named easing functions shared by every transition (and by the editor's curve thumbnails).
    public static class SplashEasing
    {
        public static float Apply(SplashEase e, float t)
        {
            t = Mathf.Clamp01(t);
            switch (e)
            {
                case SplashEase.SmoothStep: return t * t * (3f - 2f * t);
                case SplashEase.EaseIn:     return t * t;
                case SplashEase.EaseOut:    return 1f - (1f - t) * (1f - t);
                case SplashEase.EaseInOut:  return t < 0.5f ? 2f * t * t : 1f - 2f * (1f - t) * (1f - t);
                case SplashEase.Back:
                {
                    const float c1 = 1.70158f, c3 = c1 + 1f;
                    float u = t - 1f;
                    return 1f + c3 * u * u * u + c1 * u * u;      // overshoots past 1, settles back
                }
                case SplashEase.Elastic:
                {
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    const float c4 = 2f * Mathf.PI / 3f;
                    return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * c4) + 1f;
                }
                case SplashEase.Bounce:     return Bounce(t);
                default:                    return t;             // Linear (and Custom, handled by the caller)
            }
        }

        static float Bounce(float t)
        {
            const float n1 = 7.5625f, d1 = 2.75f;
            if (t < 1f / d1) return n1 * t * t;
            if (t < 2f / d1) { t -= 1.5f / d1;   return n1 * t * t + 0.75f; }
            if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
            t -= 2.625f / d1; return n1 * t * t + 0.984375f;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>Per-pixel relief lighting off the glyph's own distance field — TMP's built-in bevel, which the
    /// package ships but the DEFAULT material cannot reach: `LiberationSans SDF Material` points at
    /// `TMP_SDF-Mobile`, and every Mobile variant strips shading. Turning this on swaps the twin/face material to
    /// `TextMeshPro/Distance Field` and enables the `BEVEL_ON` keyword.
    ///
    /// It reads as solid, chiselled letters for no extra geometry and no extra draw call, and — unlike anything
    /// built out of offsets — it survives a per-letter spin, because the effect lives inside the SDF rather than in
    /// screen space. Its one honest limit: the light angle is fixed in GLYPH space, so an X/Y spin foreshortens the
    /// letter while its shading stays put.</summary>
    [Serializable]
    public class SplashBevel
    {
        [Tooltip("Light the letters as raised, solid shapes using TMP's SDF bevel. Costs no extra geometry.")]
        public bool enabled = false;

        [Range(0f, 1f)] [Tooltip("How pronounced the relief is.")]
        public float amount = 0.5f;
        [Range(-0.5f, 0.5f)] [Tooltip("Push the bevel in from the edge (negative) or out past it (positive).")]
        public float offset = 0f;
        [Range(-0.5f, 0.5f)] [Tooltip("How far in from the edge the bevel ramp runs. Shares the font atlas's " +
                 "padding budget with the border, so a deep bevel and a fat border compete for the same room.")]
        public float width = 0.25f;
        [Range(0f, 1f)] [Tooltip("Round the bevel's shoulder instead of leaving it a hard chisel.")]
        public float roundness = 0f;
        [Range(0f, 1f)] [Tooltip("Flatten the bevel's peak, for a plateau rather than a ridge.")]
        public float clamp = 0f;

        [Range(0f, 360f)] [Tooltip("Direction the light comes from, in degrees. Fixed in GLYPH space — it does not " +
                 "rotate with a per-letter spin.")]
        public float lightAngle = 180f;
        [Tooltip("Colour of the specular highlight.")]
        public Color specularColor = Color.white;
        [Range(0f, 4f)] [Tooltip("Tightness of the specular highlight — higher is glossier.")]
        public float specularPower = 2f;
        [Range(0f, 1f)] [Tooltip("Strength of the directional shading.")]
        public float diffuse = 0.9f;
        [Range(0f, 1f)] [Tooltip("Light that reaches the faces pointing away — raise it to keep the dark side readable.")]
        public float ambient = 0.5f;
    }

    // ──────────────────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>Real extruded depth, built from N stacked copies of the text stepped away from the viewer — the only
    /// technique here that produces an actual SILHOUETTE, since TMP has no extrusion at all (its glyph quads are
    /// flat, with z hard-coded to 0).
    ///
    /// It requires the splash canvas to be in CAMERA space: on a Screen-Space-Overlay canvas z is ignored outright,
    /// so the layers would collapse into each other and this would look right in the editor preview — which is a
    /// real 3D scene — while being completely invisible in game. Turning this on therefore moves the runtime canvas
    /// to Screen-Space-Camera, the same move <see cref="SplashPixelRig"/> already makes.
    ///
    /// Layers step along each letter's own local −Z, AFTER its spin, so the extrusion turns with the letter instead
    /// of sliding against it. A resting <see cref="tilt"/> is what makes the sides visible while the text is facing
    /// you; with no tilt and no spin an orthographic view sees the layers edge-on and the depth reads as nothing.</summary>
    [Serializable]
    public class SplashDepth
    {
        [Tooltip("Stack copies of the text behind itself to give the letters real extruded depth.")]
        public bool enabled = false;

        [Range(2, 32)] [Tooltip("How many copies. More is smoother but each one is a draw; raise it only until the " +
                 "banding on the sides disappears.")]
        public int layers = 8;

        [Range(0f, 1f)] [Tooltip("How deep the extrusion goes, as a fraction of the font size.")]
        public float distance = 0.15f;

        [Range(0f, 1f)] [Tooltip("How far toward the side colour the BACK of the extrusion goes. This is the depth " +
                 "falloff — 0 makes the sides the same colour all the way back, 1 takes the rearmost layer fully to " +
                 "the side fill. Ignored when the sides are flat.")]
        public float darken = 0.6f;

        [Tooltip("Give every layer the SIDE colour outright, instead of fading into it with depth. This is what a " +
                 "solid extruded block looks like — one flat colour down the side, with the face on top of it — " +
                 "whereas the falloff ramp reads more like a soft shadow behind the letters.")]
        public bool flatSides = false;

        [Tooltip("The SIDES' own fill — solid or a gradient, exactly like the face and the border. The sides are a " +
                 "different surface from the face and almost never want to be a darker copy of it, so they get their " +
                 "own colour rather than only a tint.")]
        public ZuiFill sideFill = new ZuiFill { color = new Color(0.35f, 0.12f, 0.05f, 1f) };

        [Range(0f, 2f)] [Tooltip("Brightness of the sides, applied on top of the side fill. Below 1 reads as the " +
                 "sides falling into shadow; above 1 as light catching them.")]
        public float sideBrightness = 1f;

        [Range(0f, 2f)] [Tooltip("Saturation of the sides, applied on top of the side fill. 0 is grey, 1 leaves the " +
                 "fill alone, above 1 pushes the colour harder — the cheapest way to make an extrusion read as a " +
                 "lit solid rather than a shadow.")]
        public float sideSaturation = 1f;

        [Tooltip("Resting tilt of the line, in degrees. Without some tilt (or a spin) the extrusion is edge-on and " +
                 "invisible — X tilts the top toward you, Y swings the side into view.")]
        public Vector2 tilt = new Vector2(0f, 0f);
    }

    // ──────────────────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>Where a slide starts and ends. Shared by the runtime and the preview so "just off screen" means the
    /// same thing in both.</summary>
    public static class SplashGeometry
    {
        /// <summary>The offset that puts the text FULLY outside the viewport edge <paramref name="dir"/> — its
        /// trailing edge exactly level with the screen edge, so nothing of it shows. All arguments share one unit
        /// space (canvas pixels at runtime, world units in the preview). <paramref name="textHalf"/> is the text's
        /// own half width/height, which is why this needs a laid-out mesh and cannot be a constant.</summary>
        public static Vector2 OffStage(SplashDir dir, float w, float h, Vector2 anchor, Vector2 textHalf)
        {
            switch (dir)
            {
                case SplashDir.Left:   return new Vector2(-(anchor.x * w + textHalf.x), 0f);
                case SplashDir.Right:  return new Vector2((1f - anchor.x) * w + textHalf.x, 0f);
                case SplashDir.Bottom: return new Vector2(0f, -(anchor.y * h + textHalf.y));
                case SplashDir.Top:    return new Vector2(0f, (1f - anchor.y) * h + textHalf.y);
                default:               return Vector2.zero;
            }
        }

        /// <summary>Where a transition's slide begins (entrance) or finishes (exit): `slideDistance` blends from
        /// fully off-stage (0) to the resting place (1), so 1 means "no slide at all".</summary>
        public static Vector2 SlideExtreme(SplashTransition tr, float w, float h, Vector2 anchor, Vector2 textHalf)
        {
            if (tr == null) return Vector2.zero;
            var off = OffStage(tr.direction, w, h, anchor, textHalf);
            return Vector2.Lerp(off, Vector2.zero, Mathf.Clamp01(tr.slideDistance));
        }
    }

    // ──────────────────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>How a glyph becomes CELLS. This is the one choice that decides whether a pixelated splash reads as
    /// pixel art or as a low-resolution photograph of some text.
    ///
    /// MEASURED, over a cap-height sweep of "SPLASH!" (Diagnostics/TextSplash/SplashCoverageProof.cs), counting
    /// 4-connected components (ideal 8, one per letter) and enclosed counters (ideal 2, from the P and the A).
    /// Two findings matter, and the second is not the one this axis was expected to deliver:
    ///
    ///   CONNECTIVITY IS A SIZE PROBLEM, NOT A SAMPLING ONE. Below about 10 cells of cap height every mode that
    ///     draws from an outline font shatters — at 6 cells, SDF gives 23 parts and 0 holes and area-averaging
    ///     gives 20 and 1. Averaging does NOT rescue it, and at 7 cells it measured slightly WORSE (26 vs 23).
    ///     The reason is worth knowing, because it is counter-intuitive: TMP's antialiasing at low resolution
    ///     INFLATES a thin stroke, and that inaccuracy is what was carrying those strokes over the 50% threshold.
    ///     A true coverage measurement is more correct and therefore less generous. The real fix for small text
    ///     is <see cref="PixelFont"/>, whose strokes are a whole cell wide by construction, or a lower cutoff.
    ///
    ///   WHAT AVERAGING ACTUALLY BUYS IS COLOUR. At a 12-cell cap it cut part-covered cells from 485 to 392 and
    ///     distinct colours from 183 to 149; at 16 cells, 622 to 498 and 206 to 178. That is the mush the palette
    ///     lock and the cutoff exist to clean up, reduced at the source instead of repaired afterwards.
    ///
    /// So the three are not better-and-worse versions of one thing — they trade different properties, which is
    /// why all three ship and are switchable per asset.</summary>
    public enum SplashRasterMode
    {
        /// <summary>Rasterize TMP's signed distance field straight into the low-res buffer: one antialiased sample
        /// per cell, then <see cref="SplashPixelation.alphaCutoff"/> decides on or off.
        ///
        /// Its weakness is colour, not structure: essentially every covered cell comes out partly transparent
        /// (98% at a 12-cell cap) and a two-colour splash rasterizes into 183 distinct ones, which is the mush the
        /// cutoff and the palette lock spend their time undoing.
        ///
        /// It remains the right choice for anything that SPINS or SCALES DOWN, and — because its antialiasing
        /// inflates thin strokes — it is measurably no worse than area-averaging at holding small text together.
        /// It is also what every splash authored before this choice existed already renders as.</summary>
        Sdf = 0,

        /// <summary>Render the splash oversampled and box-average each block of samples down to ONE cell, so a
        /// cell's alpha is its TRUE area coverage rather than one point sample of a distance field.
        ///
        /// What that measurably buys is a CLEANER buffer, not intact letterforms: at a 12-cell cap it cut
        /// part-covered cells from 485 to 392 and distinct colours from 183 to 149. Every cell it cleans up is one
        /// the alpha cutoff and the palette lock no longer have to repair. It does NOT fix the shattering of small
        /// text — see the note on <see cref="SplashRasterMode"/>, where measurement contradicted the expectation.
        ///
        /// Averaging is valid on the buffer exactly as it stands because TMP writes PREMULTIPLIED colour — C·a
        /// averages linearly, which a straight colour would not.
        ///
        /// It costs <see cref="SplashPixelation.coverageSamples"/>² times the fragments of <see cref="Sdf"/> and
        /// nothing else: the buffer that is read back, colour-crunched and presented is the same small one.</summary>
        AreaAverage = 1,

        /// <summary>Rasterize the FONT ITSELF at the cell size — a bitmap font asset baked at the sampling size the
        /// splash actually renders at, grid-fitted by the hinter — so a glyph arrives already made of whole cells.
        ///
        /// This is the only one of the three that produces true 1-bit pixel art: zero partial cells and one
        /// distinct colour, in every static condition tested including a half-cell slide. What breaks it is not
        /// sharpness but the LETTERFORMS, and only under transforms the bake cannot anticipate — scaling DOWN
        /// fills the counters in, and rotation shreds the glyphs (26 parts at 30°). Point atlas filtering is
        /// mandatory; bilinear alone took it from 8 parts to 18.
        ///
        /// A hinted bake is per-size by construction, so this mode is honest only for a STATIC size — an animated
        /// size scalar would need a re-bake every frame and is reported rather than silently approximated.</summary>
        PixelFont = 2,
    }

    // ──────────────────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>Render the splash through a low-resolution buffer so it comes out in CHUNKY PIXELS, and (optionally)
    /// run a SpriteFx modifier stack over that buffer. Off by default — it costs a render texture.</summary>
    [Serializable]
    public class SplashPixelation
    {
        [Tooltip("Render the splash into a low-res buffer and blit it back up with point filtering — REAL pixels, " +
                 "not a shader that fakes them. Everything downstream (SpriteFx) then works on those pixels.")]
        public bool enabled = false;

        [Range(1, 32)] [Tooltip("Screen pixels per splash pixel. 1 = native (no pixelation), 8 = very chunky.")]
        public int pixelSize = 4;

        [Tooltip("How a glyph becomes cells — the choice that decides whether this reads as pixel art or as a " +
                 "low-resolution photo of some text. SDF point-samples the distance field once per cell and " +
                 "shatters thin strokes; Coverage box-averages an oversampled render so a cell's alpha is its true " +
                 "area; Pixel font rasterizes the font AT the cell size for true 1-bit output.")]
        public SplashRasterMode rasterMode = SplashRasterMode.Sdf;

        [Tooltip("How many samples across each cell the Coverage mode averages — 4 means 4×4 = 16 samples per " +
                 "cell. It must be a power of two so the reduction is an exact chain of halvings (each bilinear " +
                 "tap then sits precisely between four texels and averages them with no weighting error). More " +
                 "samples measure coverage more finely and cost that many more fragments to render.")]
        [Range(2, 8)] public int coverageSamples = 4;

        [Tooltip("The committed bitmap font the Pixel font mode draws from, baked at the sampling size this splash " +
                 "actually renders at. Maintained AUTOMATICALLY, like the border twin — the editor re-bakes it " +
                 "whenever the font, the size or the pixel size moves it out of date.")]
        public TMP_FontAsset bakedPixelFont;

        // What the committed bitmap font above was baked FROM. A stale bake is worse than a missing one, because it
        // looks like it worked — it would render at the wrong cap height and lose the grid fit that is the whole
        // point of the mode.
        [HideInInspector] public TMP_FontAsset bakedPixelSource;
        [HideInInspector] public int bakedPixelSampling;
        [HideInInspector] public int bakedPixelAtlas;

        [Range(0, 32)] [Tooltip("Quantize each colour channel to this many steps (0 = off). The pixel GRID alone " +
                 "does not make a gradient look like pixel art — the fill is still a smooth ramp, just sampled at " +
                 "low resolution. This is what bands it into flat steps, which is what actually reads as a palette.")]
        public int colorSteps = 6;

        [Tooltip("Snap every pixel to the nearest colour the splash actually USES — its face, border and side fills — " +
                 "instead of letting it be a blend. This is what makes it look like real pixel art. Alpha cutoff " +
                 "fixes the EDGE of the letters; this fixes their INSIDE: a low-res pixel that straddles the white " +
                 "face and the dark border otherwise rasterizes to a grey halfway between them, and a splash whose " +
                 "whole palette is two colours ends up showing dozens. Colour steps cannot do this — it snaps to an " +
                 "even numeric grid that your colours do not sit on.")]
        public bool paletteLock = true;

        [Range(0f, 1f)] [Tooltip("Alpha threshold that makes edges CRISP. TMP's SDF antialiases every edge, and at " +
                 "low resolution those half-transparent pixels are a whole visible pixel of mush — the softness you " +
                 "see around pixelated letters. Any pixel at least this opaque snaps to fully opaque, everything " +
                 "below it disappears, so every pixel is either on or off exactly like real pixel art. 0 = off " +
                 "(keep the antialiasing). It also makes the premultiplied-edge problem moot, since no partly " +
                 "transparent pixel survives.")]
        public float alphaCutoff = 0.5f;

        [Tooltip("Snap the splash's motion to the pixel grid too, so it steps between pixels instead of sliding " +
                 "smoothly through them (true pixel-art movement).")]
        public bool snapMotion = true;

        [Tooltip("Correct the antialiased EDGE of the pixelated splash. TMP writes premultiplied alpha, which the " +
                 "default UI blend multiplies by alpha a second time, so the rim composites darker than it should. " +
                 "On = present through a premultiplied blend that fixes it. Off = the plain UI blend (marginally " +
                 "cheaper, and the safe fallback if the shader is ever stripped from a build).")]
        public bool fixEdgeAlpha = true;

        // Which generation of this block the serialized data came from. 0 means "saved before `alphaCutoff` existed",
        // and that is NOT the same as "the author chose 0" — a C# field default never reaches data already on disk,
        // so an older asset deserializes the new field as 0 and silently renders soft-edged mush no matter what the
        // declared default says. Migrate() uses this to tell the two apart exactly once.
        [HideInInspector] public int version;
        public const int CurrentVersion = 3;

        [Tooltip("Optional SpriteFx stack run over the low-res buffer every frame — the same Brightness / Tint / " +
                 "Contrast / Saturation / Posterize / OrderedDither / LayerDissolve / AlphaMask modifiers a sprite " +
                 "uses. Costs a GPU→CPU read-back of the buffer each frame, which is affordable ONLY because the " +
                 "buffer is low-res; it runs on the Burst path, never the inline one.")]
        public SpriteFxSpec spriteFx;

        /// <summary>Bring data saved by an older version up to date. Only ever runs ONCE per asset (guarded by
        /// <see cref="version"/>), so it can never fight an author who genuinely wants a value this would change.
        ///
        /// v0 → v1: give `alphaCutoff` the 0.5 it was declared with. An asset written before the field existed has no
        /// entry for it and therefore loads 0, which switches the crisp-edge threshold OFF and leaves TMP's
        /// antialiasing as a whole visible pixel of grey mush — the exact symptom this migration exists to stop.
        /// `colorSteps` is deliberately NOT touched: it predates this and a 0 there is real authored data.
        ///
        /// v1 → v2: turn `paletteLock` on. Same trap, one field later — it is declared `true`, but a bool absent from
        /// the serialized data loads `false`, so every existing splash would keep rendering blended greys while the
        /// dial claimed to be on by default. A locked palette is what makes a pixelated splash read as pixel art at
        /// all, so ON is the right state to bring old assets up to; a deliberate OFF survives, because from v2 the
        /// field is in the data and this never runs again.
        ///
        /// v2 → v3: give `coverageSamples` its declared 4. Same trap a third time, and here a 0 would be actively
        /// broken rather than merely off — it is a DIVISOR of the oversampled buffer's size. `rasterMode` needs no
        /// seeding on purpose: its zero IS <see cref="SplashRasterMode.Sdf"/>, which is exactly what every splash
        /// authored before the choice existed already renders as, so silence is the correct migration.</summary>
        public void Migrate()
        {
            if (version >= CurrentVersion) return;
            if (version < 1 && alphaCutoff <= 0f) alphaCutoff = 0.5f;
            if (version < 2) paletteLock = true;
            if (version < 3 && coverageSamples < 2) coverageSamples = 4;
            version = CurrentVersion;
        }

        /// <summary>The oversample factor actually used, forced to a power of two. The reduction to the low buffer is
        /// a chain of exact halvings — each bilinear tap landing precisely between four texels, which averages them
        /// with no weighting error — and a factor of 3 or 5 or 6 would break that into a resample with the very
        /// blur this mode exists to avoid. Rounded DOWN so the dial never costs more than it says.</summary>
        public int CoverageFactor()
        {
            int s = Mathf.Clamp(coverageSamples, 2, 8);
            return s >= 8 ? 8 : s >= 4 ? 4 : 2;
        }

        /// <summary>The sampling size a Pixel-font bake has to use for one font pixel to land on exactly one cell.
        ///
        /// The rig scales the splash canvas by 1/pixelSize precisely so a font size of F points renders F/pixelSize
        /// BUFFER pixels per em. Baking the font at that same number therefore makes the glyph's own raster grid and
        /// the buffer's cell grid the same grid — which is the entire mechanism of the mode. It is rounded, so a
        /// size that is not a whole number of cells shifts very slightly to the nearest one; that is the honest cost
        /// of a grid fit and the window reports it rather than hiding it.</summary>
        public int PixelSampling(float fontSize)
            => Mathf.Clamp(Mathf.RoundToInt(fontSize / Mathf.Max(1, Mathf.Clamp(pixelSize, 1, 32))), 4, 256);

        /// <summary>Whether the Pixel-font mode is both SELECTED and has a bake to draw from — the question every
        /// consumer actually has, asked in one place so the player, the preview and the feature gates cannot answer
        /// it differently. Without a bake the mode has nothing to render and everything falls back to the ordinary
        /// SDF face, which is the right degradation: a splash mid-bake still draws.</summary>
        public bool PixelFontActive()
            => enabled && rasterMode == SplashRasterMode.PixelFont && bakedPixelFont != null;

        /// <summary>Whether the committed bitmap font is missing or no longer matches what the asset now asks for.
        /// Answers false unless the mode is actually selected, so a splash that never uses it is never asked to
        /// carry a bake.</summary>
        public bool NeedsPixelFontBake(TMP_FontAsset source, float fontSize)
        {
            if (!enabled || rasterMode != SplashRasterMode.PixelFont || source == null) return false;
            if (bakedPixelFont == null) return true;
            if (bakedPixelSource != source) return true;
            return bakedPixelSampling != PixelSampling(fontSize);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>The per-letter (or whole-line) time WINDOWS of one play. Pure math, no Unity objects — the runtime
    /// player and the editor preview both drive off this so they can never drift apart.
    ///
    /// Schedule, for `n` letters, where `k` is a letter's TURN in the cascade (its <see cref="Rank"/> under the
    /// chosen <see cref="SplashOrder"/> — left-to-right, centre-out, random, …):
    ///   letter k enters at        k·stagger                and is at rest by  k·stagger + in.duration
    ///   the whole line rests until (n−1)·stagger + in.duration + hold          ( = <see cref="OutBase"/> )
    ///   letter k leaves at        OutBase + k·stagger      and is gone by     that + out.duration
    /// so a letter's LIFETIME is [k·stagger, its exit end] — the window every per-letter scalar curve runs over.
    /// With per-letter OFF the stagger is ignored (n is treated as 1) and the line moves as one block.</summary>
    public readonly struct SplashSchedule
    {
        public readonly float inDuration, outDuration, hold, entryStagger, exitStagger;
        public readonly int letters, seed;
        public readonly SplashOrder entryOrder, exitOrder;

        public SplashSchedule(TextSplash s, int letterCount, float hold, int seed = 0)
        {
            bool pl = s != null && s.PerLetter;
            letters = Mathf.Max(1, pl ? letterCount : 1);
            inDuration = s != null ? Mathf.Max(0f, s.inTransition.duration) : 0f;
            outDuration = s != null ? Mathf.Max(0f, s.outTransition.duration) : 0f;
            this.hold = Mathf.Max(0f, hold);
            entryStagger = pl && s != null ? Mathf.Max(0f, s.spawnStagger) : 0f;
            exitStagger = pl && s != null ? Mathf.Max(0f, s.EffectiveExitStagger) : 0f;
            entryOrder = s != null ? s.order : SplashOrder.LeftToRight;
            exitOrder = s != null ? s.EffectiveExitOrder : SplashOrder.LeftToRight;
            this.seed = seed;
        }

        /// Letter `i`'s turn in the ENTRANCE cascade.
        public int RankIn(int i) => Rank(i, entryOrder);
        /// Letter `i`'s turn in the EXIT cascade — a different order when the exit isn't reusing the entrance's.
        public int RankOut(int i) => Rank(i, exitOrder);

        /// <summary>Letter `i`'s TURN in a cascade, 0..n−1 — which is not its index once the order stops being
        /// left-to-right. Symmetric orders deliberately let a pair share a rank so the two halves move together.</summary>
        public int Rank(int i, SplashOrder order)
        {
            int n = letters;
            if (i < 0 || i >= n) return 0;
            switch (order)
            {
                case SplashOrder.RightToLeft: return n - 1 - i;
                case SplashOrder.CentreOut:   return CentreRank(i, n);
                // CentreRank(0, n) IS the highest rank (the outermost letter), so this mirrors the cascade.
                case SplashOrder.EdgesIn:     return CentreRank(0, n) - CentreRank(i, n);
                case SplashOrder.Random:      return RandomRank(i, n);
                default:                      return i;
            }
        }

        /// <summary>Turns for a centre-out cascade. A symmetric PAIR deliberately shares one turn, so the two halves
        /// bloom together — ranking them one apart would read as a subtly lopsided animation. Odd lines have a lone
        /// centre letter at 0; even lines have a centre pair at 0. Closed-form, so it costs nothing per letter.</summary>
        static int CentreRank(int i, int n)
        {
            float d = Mathf.Abs(i - (n - 1) * 0.5f);          // 0,1,2… for odd n; 0.5,1.5,2.5… for even n
            return Mathf.RoundToInt(n % 2 == 1 ? d : d - 0.5f);
        }

        /// <summary>Turns for a shuffled cascade: how many letters hash lower than this one, ties broken by index so
        /// the order is total and stable. O(n) over a line of text, and allocation-free — note a delegate-based
        /// comparator would BOX this readonly struct on every call, and this runs per letter per frame.</summary>
        int RandomRank(int i, int n)
        {
            float mine = Sfx.Hash01(seed, i, 7919);
            int rank = 0;
            for (int j = 0; j < n; j++)
            {
                float other = Sfx.Hash01(seed, j, 7919);
                if (other < mine || (other == mine && j < i)) rank++;
            }
            return rank;
        }

        /// When the LAST letter has finished entering + the hold has elapsed — the moment the exit starts.
        public float OutBase => (letters - 1) * entryStagger + inDuration + hold;
        /// Total play length: the last letter's exit has finished.
        public float Total => OutBase + (letters - 1) * exitStagger + outDuration;

        /// <summary>Letter `i`'s own window: when it starts entering, and when it has finished leaving. The two ends
        /// can cascade in different orders, so a letter that enters first does not necessarily leave first — but it
        /// still has ONE unambiguous lifetime, which is what every per-letter scalar curve runs over.</summary>
        public float LetterStart(int i) => RankIn(i) * entryStagger;
        public float LetterEnd(int i) => OutBase + RankOut(i) * exitStagger + outDuration;

        /// Letter `i`'s own normalized 0..1 life (its entrance start → its exit end).
        public float LetterLife(int i, float t)
        {
            float a = LetterStart(i), b = LetterEnd(i);
            return b > a ? Mathf.Clamp01((t - a) / (b - a)) : 1f;
        }

        /// The whole line's normalized 0..1 life.
        public float LineLife(float t) { float tot = Total; return tot > 0f ? Mathf.Clamp01(t / tot) : 1f; }

        /// Raw (un-eased) 0..1 progress of letter `i`'s ENTRANCE at time `t`. 1 = fully entered.
        public float InProgress(int i, float t)
        {
            float a = LetterStart(i);
            return inDuration > 0f ? Mathf.Clamp01((t - a) / inDuration) : (t >= a ? 1f : 0f);
        }

        /// Raw (un-eased) 0..1 progress of letter `i`'s EXIT at time `t`. 0 = still at rest, 1 = fully gone.
        public float OutProgress(int i, float t)
        {
            float a = OutBase + RankOut(i) * exitStagger;
            return outDuration > 0f ? Mathf.Clamp01((t - a) / outDuration) : (t >= a ? 1f : 0f);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// A reusable "splash this text on screen" asset — everything needed to fly a line of text in, hold it, and fly
    /// it back out, authored once and played with one call. Carries DEFAULT text + durations, both overridable per
    /// call (<see cref="Show"/>).
    ///
    /// Slice 4 reshapes the authoring model: the entrance and the exit are two independent
    /// <see cref="SplashTransition"/>s (each with its own duration / direction / distance / ease / spin / scale /
    /// fade / stagger), alpha + size + border width are animatable <see cref="SplashScalar"/>s ("MultCont"s) that
    /// can read the line's life or each letter's own, and the border is a full <see cref="ZuiFill"/> that can be
    /// dilated far past a hairline. <see cref="pixelation"/> optionally renders the whole thing through a low-res
    /// buffer for real chunky pixels.
    /// </summary>
    [CreateAssetMenu(menuName = "Laubrary/Text Splash", fileName = "TextSplash")]
    public class TextSplash : ScriptableObject, IVisualPreview
    {
        [Header("Content")]
        [TextArea] public string text = "SPLASH!";
        [Tooltip("Optional TMP font. Null = TMP's default font.")]
        public TMP_FontAsset font;

        [Tooltip("Font size, in points. Animatable — curve it to punch the text bigger as it lands.")]
        public SplashScalar size = new SplashScalar(96f, 8f, 300f);

        [Header("Look")]
        [Tooltip("The text FACE fill — a solid colour or a ZUI gradient (the same fill system Pyre uses).")]
        public ZuiFill fill = new ZuiFill { color = Color.white };

        [Tooltip("Overall opacity. Animatable — curve it for a flicker or a slow bleed-out.")]
        public SplashScalar alpha = new SplashScalar(1f, 0f, 1f);

        [Header("Border")]
        [Tooltip("The BORDER fill — solid or a ZUI gradient, exactly like the face fill.")]
        public ZuiFill borderFill = new ZuiFill { color = Color.black };

        [Tooltip("Border thickness as a FRACTION OF THE FONT SIZE (0.02 = a hairline, 0.25 = a fat cartoon outline). " +
                 "Animatable. Drawn by dilating a duplicate text pass through the glyph's own distance field, which " +
                 "is a TRUE equidistant offset — so counters keep their inner edge and thin strokes fatten evenly.")]
        public SplashScalar borderWidth = new SplashScalar(0.06f, 0f, 0.5f);

        [Tooltip("Padding, in texels, of the font atlas baked for the border pass. THIS is what caps border " +
                 "thickness: TMP's outline can only reach padding ÷ (2 × sampling size) of an em, which on a stock " +
                 "font asset is about 4% — ten times short of this tool's range. 0 = derive it from the widest " +
                 "border the width dial can reach. Bigger padding costs atlas area, so fewer glyphs fit per page.")]
        [Range(0, 128)] public int borderAtlasPadding = 0;

        [Tooltip("Resolution of the baked border atlas. It must hold every glyph the splash uses at the padding " +
                 "above — 1024 fits roughly 45 glyphs at padding 45, 2048 roughly 180.")]
        [Range(256, 4096)] public int borderAtlasSize = 2048;

        [Tooltip("The committed large-padding font asset the border pass draws from. Maintained AUTOMATICALLY — the " +
                 "editor bakes it whenever it is missing or out of date, and a build refuses to ship without it. It " +
                 "has to be committed rather than baked on the fly because TMP nulls the source font on a Static " +
                 "font asset, so a runtime bake can only find the font in the EDITOR and a build would silently fall " +
                 "back to a hairline border.")]
        public TMP_FontAsset bakedBorderFont;

        // What the committed twin above was actually baked FROM. Without this there is no way to tell a valid bake
        // from a stale one after the font, the padding or the atlas size changes — and a stale bake is worse than
        // none, because it looks like it worked.
        [HideInInspector] public TMP_FontAsset bakedBorderSource;
        [HideInInspector] public int bakedBorderPadding;
        [HideInInspector] public int bakedBorderAtlas;

        /// <summary>Whether the committed border twin is missing or no longer matches what the asset now asks for.
        /// The editor watches this and re-bakes; nobody should have to remember to press anything.</summary>
        public bool NeedsBorderBake(TMP_FontAsset source)
        {
            if (source == null) return false;
            if (bakedBorderFont == null) return true;
            if (bakedBorderSource != source) return true;
            if (bakedBorderAtlas != borderAtlasSize) return true;
            return bakedBorderPadding < ResolveBorderPadding(source.faceInfo.pointSize);
        }

        [Header("Layout")]
        [Tooltip("Where the text rests, in viewport space (0.5,0.5 = centre).")]
        public Vector2 anchor = new Vector2(0.5f, 0.5f);

        [Header("Timing")]
        [Min(0f)] [Tooltip("Seconds the text sits at rest between the entrance and the exit (the default; " +
                 "overridable per Show call).")]
        public float holdDuration = 1.5f;

        [Tooltip("How the text ENTERS: duration, direction, distance, easing, spin and scale.")]
        public SplashTransition inTransition = new SplashTransition
        {
            direction = SplashDir.Bottom, ease = SplashEase.EaseOut, duration = 0.35f
        };

        [Tooltip("How the text LEAVES: the same knobs again, independent of the entrance.")]
        public SplashTransition outTransition = new SplashTransition
        {
            direction = SplashDir.Top, ease = SplashEase.EaseIn, duration = 0.35f
        };

        [Header("Per-letter")]
        [Min(0f)] [Tooltip("Seconds between one letter starting and the next — the cascade that gives every letter " +
                 "its OWN lifetime for the per-letter scalars to run over. 0 = the whole line moves as one block.")]
        public float spawnStagger = 0.05f;

        [Tooltip("Which letter takes its turn first as the stagger cascades across the line.")]
        public SplashOrder order = SplashOrder.LeftToRight;

        [Tooltip("Let the EXIT reuse the entrance's stagger and order. Turn it off to give the exit its own — a line " +
                 "that arrives left-to-right and leaves centre-out, say.")]
        public bool exitSameAsEntrance = true;

        [Min(0f)] [Tooltip("Seconds between letters as the line LEAVES (only when the exit isn't reusing the " +
                 "entrance's).")]
        public float exitStagger = 0.05f;

        [Tooltip("Which letter leaves first (only when the exit isn't reusing the entrance's order).")]
        public SplashOrder exitOrder = SplashOrder.LeftToRight;

        /// The stagger/order the EXIT actually runs at, after the reuse-the-entrance switch.
        public float EffectiveExitStagger => exitSameAsEntrance ? spawnStagger : exitStagger;
        public SplashOrder EffectiveExitOrder => exitSameAsEntrance ? order : exitOrder;

        [Tooltip("Force the letters to MOVE (slide / spin / scale) individually even with no spawn stagger. Per-letter " +
                 "animation already switches itself on whenever it is needed — a stagger, or any scalar scoped per " +
                 "letter — so this is only for a per-letter spin with everything landing at once.")]
        public bool perLetterMotion = false;

        /// <summary>Whether letters animate individually. DERIVED, never authored alone: turning on any scalar's
        /// per-letter scope, or giving the line a spawn stagger, is enough. (An earlier build gated all of this
        /// behind one master toggle, so ticking "per letter" on Size by itself did nothing at all.)</summary>
        public bool PerLetter =>
            perLetterMotion || spawnStagger > 0f || EffectiveExitStagger > 0f
            || (alpha != null && alpha.perLetter)
            || (size != null && size.perLetter)
            || (borderWidth != null && borderWidth.perLetter);

        [Header("Colour cycle")]
        [Tooltip("Scroll the FILL colour over time (the face only — the border has its own fill). With per-letter on, " +
                 "the cycle travels across the letters.")]
        public bool cycleFill = false;
        [Range(0f, 4f)] [Tooltip("Fill colour cycles per second.")]
        public float cycleSpeed = 0.5f;
        [Range(0f, 1f)] [Tooltip("Per-letter phase offset — how much the cycle shifts from one letter to the next " +
                 "(a travelling rainbow). Per-letter mode only.")]
        public float cyclePerLetter = 0.1f;

        [Header("Relief")]
        [Tooltip("Per-pixel bevel lighting off the glyph's distance field — solid, chiselled letters for free.")]
        public SplashBevel bevel = new SplashBevel();

        [Tooltip("Real extruded depth from stacked copies. Moves the runtime canvas into camera space, since a " +
                 "screen-space-overlay canvas ignores z entirely.")]
        public SplashDepth depth = new SplashDepth();

        [Header("Pixelation")]
        [Tooltip("Render through a low-res buffer for real chunky pixels.")]
        public SplashPixelation pixelation = new SplashPixelation();

        /// <summary>Whether the splash must live on a CAMERA-space canvas rather than a screen-space-overlay one.
        /// Depth needs it (overlay canvases discard z); the pixel rig needs it for its own reasons and arranges it
        /// itself. One property so the two features cannot each independently decide and fight over the canvas.</summary>
        public bool NeedsCameraSpace => depth != null && depth.enabled;

        /// <summary>Every colour this splash can actually put on screen, for the pixelation palette lock. Gradients
        /// are sampled across their ramp rather than reduced to one colour, because a gradient face legitimately
        /// spans many — the point is only that a pixel must land ON one of these rather than between two of them.
        /// Alpha is ignored: the cutoff already decides whether a pixel exists at all.</summary>
        public void BuildPalette(List<Color32> into, int rampSamples = 12)
        {
            if (into == null) return;
            into.Clear();
            AddFill(into, fill, rampSamples);
            // Only colours actually DRAWN belong in the palette. Listing the border's colour when no border is drawn
            // gives a stray grey somewhere to snap TO — a black pixel in the middle of a borderless white splash,
            // which is worse than the blend it replaced. Same reasoning as gating the side fill on depth.
            if (WidestBorder() > 0f) AddFill(into, borderFill, rampSamples);
            if (depth != null && depth.enabled) AddFill(into, depth.sideFill, rampSamples);
        }

        static void AddFill(List<Color32> into, ZuiFill f, int samples)
        {
            if (f == null) return;
            if (f.mode == ZuiFill.Mode.Solid || f.texture != ZuiFill.TextureKind.None)
            {
                Push(into, f.color);
                return;
            }
            // Sample the ramp end to end. `u` doubles as the life for OverLife and as the axis for Linear/Radial,
            // which is exactly the set of values the renderer can produce from this fill.
            for (int i = 0; i < Mathf.Max(2, samples); i++)
            {
                float u = i / (float)(Mathf.Max(2, samples) - 1);
                Push(into, f.Evaluate(u, u * 2f - 1f, 0f));
            }
        }

        static void Push(List<Color32> into, Color32 c)
        {
            for (int i = 0; i < into.Count; i++)
                if (into[i].r == c.r && into[i].g == c.g && into[i].b == c.b) return;
            into.Add(c);
        }

        /// <summary>Atlas padding the border pass should be baked at: the authored value, or derived from the widest
        /// border the dial can reach. TMP's usable outward dilation is padding ÷ (2 × sampling size) of an em, so
        /// reaching a border of `w` em needs padding ≈ 2·w·samplingSize — plus headroom for a bevel sharing the
        /// budget.</summary>
        public int ResolveBorderPadding(float samplingPointSize)
        {
            if (borderAtlasPadding > 0) return borderAtlasPadding;
            float need = 2f * Mathf.Clamp(WidestBorder(), 0.005f, 1f) * Mathf.Max(1f, samplingPointSize);
            if (bevel != null && bevel.enabled) need *= 1.35f;      // the bevel eats the same padding budget
            return Mathf.Clamp(Mathf.CeilToInt(need * 1.1f), 8, 128);   // 10% headroom for rounding
        }

        /// <summary>The widest border this asset can actually REACH, in em — which is not the same as the widest the
        /// dial could theoretically be dragged to. Sizing the atlas off the dial's range reserved padding for 0.5 em
        /// even when the border was 0.07, and at padding 128 a 512 atlas fits about two glyphs per page: every extra
        /// letter then opens another page. So a STATIC width costs only what it uses; only an animated one has to
        /// reserve for the peak it can hit, because the atlas cannot be re-baked mid-play.</summary>
        public float WidestBorder()
        {
            var v = borderWidth?.value;
            if (v == null) return 0.1f;
            switch (v.mode)
            {
                case ZUIValue.Mode.MinMax: return Mathf.Max(v.min, v.max);
                case ZUIValue.Mode.Curve:
                {
                    // Sample the curve rather than trusting yMax: a curve authored well inside its declared range
                    // would otherwise reserve for a width it never reaches.
                    float peak = 0f;
                    for (int i = 0; i <= 16; i++) peak = Mathf.Max(peak, v.Evaluate(i / 16f));
                    return peak;
                }
                default: return v.staticValue;
            }
        }

        /// The schedule for `letters` characters at this asset's own hold (or an override). `seed` only matters for
        /// the Random letter order and Min-Max rolls; pass the play's seed so the preview matches the runtime.
        public SplashSchedule Schedule(int letters, float? overrideHold = null, int seed = 0)
            => new SplashSchedule(this, letters, overrideHold ?? holdDuration, seed);

        /// Total play length (entrance + hold + exit) for the whole line, ignoring per-letter stagger — one letter
        /// means the cascade contributes nothing.
        public float TotalDuration => Schedule(1).Total;

        /// Play length accounting for per-letter stagger.
        public float SequenceDuration(int letters) => Schedule(letters).Total;

        /// <summary>Splash this text on screen. Null args use the asset's own defaults; pass either to override just
        /// this play. Spawns a self-cleaning overlay, runs the entrance → hold → exit, then despawns.</summary>
        /// <returns>A handle to await completion or cancel early.</returns>
        public SplashHandle Show(string overrideText = null, float? overrideHold = null)
            => SplashPlayer.Play(this, overrideText, overrideHold);

        void OnAwake_MigrateData() { pixelation?.Migrate(); }

        void Awake() => OnAwake_MigrateData();

        void OnValidate()
        {
            // Runs on load in the editor as well as on an inspector edit, so an older asset is brought up to date the
            // first time it is looked at rather than waiting for someone to notice the render is wrong.
            OnAwake_MigrateData();

            // Curve-mode scalars are authored against a normalized 0..1 life — keep their period at 1s so a curve
            // point at x=0.5 means "half way through", whatever the real seconds are.
            NormalizeLife(size); NormalizeLife(alpha); NormalizeLife(borderWidth);
            NormalizeLife(inTransition?.easeCurve); NormalizeLife(outTransition?.easeCurve);
        }

        static void NormalizeLife(SplashScalar s) { if (s != null) NormalizeLife(s.value); }
        static void NormalizeLife(ZUIValue v) { if (v != null) { v.duration = 1f; if (v.cooldown >= 0f) v.cooldown = -1f; } }

        // ── IVisualPreview (browser thumbnail) — not rendered yet; the editor window's live preview is where you
        //    author it. A real held-state thumbnail is a later slice. ──
        public Texture2D RenderPreviewTexture() => null;
        public bool CanAnimatePreview => false;
        public float PreviewFps => 0f;
        public void UpdateAnimatedPreview(Texture2D tex, double time) { }
    }
}
