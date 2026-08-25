// SpriteFxRelight.cs
// Fake 2D illumination: a sprite is shaded as if a real light were near it, without any authored normal map and
// without any lighting in the renderer. The "surface" is INVENTED from the sprite's own pixels (SpriteFxAuxMap
// builds the height field), differentiated into a normal, and lit with Lambert + Blinn specular. Pure float
// math over the frame buffer — no UnityEngine.Random, no Mathf.PerlinNoise — so preview, bake and runtime
// playback take the SAME code path with the same inputs and produce the same result on one machine, which is
// what Laubrary's determinism rule is actually protecting (a bake must match the preview it was judged from).
// It is not a claim of bit-identical output ACROSS platforms: Mathf.Pow/Sin/Cos/Sqrt are not guaranteed to
// round identically on a different CPU architecture or between Mono and IL2CPP.
//
// The shading half mirrors PyreField.ReliefLight (Runtime/Pyre) deliberately: same clamp-to-edge central
// difference, same normal construction, and the consumer's tuned 0.35 + light * 1.15 brightness spread. It is
// re-implemented rather than referenced because SpriteFx must not depend on Pyre.
using System;
using UnityEngine;

namespace Laubrary.SpriteFx
{
    /// Whether the fake light is infinitely far away (one direction for the whole frame) or sits at a point
    /// somewhere near the sprite plane, so its reach and angle vary across the picture.
    public enum SfxLightMode
    {
        Directional,
        Point
    }

    /// Which signal from the sprite's own pixels becomes the invented surface.
    ///
    /// Deliberately its own small enum rather than reusing SpriteFxAuxMap's AuxMapGenerator: that one is
    /// APPEND-ONLY (it is serialized on every authored SpriteFxAuxMapGate, so its indices can never move) and
    /// holds ten values, most of which are meaningless or actively wrong as a relief source — Dark and Hue
    /// proximity read the fully transparent surround as the HIGHEST part of the surface, which tilts every
    /// silhouette normal inward and inverts the rim. Three named options that each do something distinct is
    /// also one clean row of radios instead of ten that wrap off the card.
    public enum SfxReliefSource
    {
        Silhouette,   // AuxMapGenerator.EdgeDistanceIn — inflates the silhouette into a rounded volume
        Slab,         // AuxMapGenerator.Alpha          — flat, so only the outline turns: a rim light
        Brightness    // AuxMapGenerator.Luma           — the sprite's own painted values become relief
    }

    /// Everything about a light that does NOT vary per pixel, resolved once: the clamps, the trig, the light
    /// vector and its half-vector for a directional light, and the sprite frame a point light is placed in.
    /// Passed by `in` to the per-pixel shade so the hot loop carries no setup.
    public readonly struct SfxLight
    {
        public readonly bool isPoint;
        public readonly float dirX, dirY, dirZ;         // directional: the (unit) light vector
        public readonly float halfX, halfY, halfZ;      // directional: its Blinn half-vector
        public readonly float posX, posY, posZ;         // point: position in half-frame units, above the plane
        public readonly float radius, falloff;
        public readonly float centreX, centreY, half;   // point: the SPRITE rect its position is measured in
        public readonly float relief, ambient, diffuse, specular, shine;

        internal SfxLight(bool isPoint,
            float dirX, float dirY, float dirZ, float halfX, float halfY, float halfZ,
            float posX, float posY, float posZ, float radius, float falloff,
            float centreX, float centreY, float half,
            float relief, float ambient, float diffuse, float specular, float shine)
        {
            this.isPoint = isPoint;
            this.dirX = dirX; this.dirY = dirY; this.dirZ = dirZ;
            this.halfX = halfX; this.halfY = halfY; this.halfZ = halfZ;
            this.posX = posX; this.posY = posY; this.posZ = posZ;
            this.radius = radius; this.falloff = falloff;
            this.centreX = centreX; this.centreY = centreY; this.half = half;
            this.relief = relief; this.ambient = ambient;
            this.diffuse = diffuse; this.specular = specular; this.shine = shine;
        }
    }

    /// Relief shading from a height field: builds a per-pixel normal by differentiating the field, then dots it
    /// against a light. Pure math over plain arrays — no Unity object state — so it is directly testable and is
    /// shared by preview, bake and runtime alike.
    ///
    /// The height field MUST already be smoothed before it gets here (RelightModifier hands BuildMap its blur
    /// passes). Differentiating a hard pixel-art edge produces a normal that snaps by 90 degrees in one pixel,
    /// which reads as a black crease along every silhouette rather than a rounded volume.
    public static class SfxRelief
    {
        /// A light infinitely far away: one vector for the whole frame. `angleDeg` is its bearing in the screen
        /// plane (0 = from the right, 90 = from above), `elevationDeg` how far it is tipped up out of that plane
        /// toward the viewer.
        public static SfxLight Directional(float angleDeg, float elevationDeg, float relief,
            float ambient, float diffuse, float specular, float shine)
        {
            float a = angleDeg * Mathf.Deg2Rad, e = elevationDeg * Mathf.Deg2Rad;
            float ce = Mathf.Cos(e);
            float dx = Mathf.Cos(a) * ce, dy = Mathf.Sin(a) * ce, dz = Mathf.Sin(e);
            float dl = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
            if (dl < 1e-6f) { dx = 0f; dy = 0f; dz = 1f; dl = 1f; }
            dx /= dl; dy /= dl; dz /= dl;
            HalfVector(dx, dy, dz, out float hx, out float hy, out float hz);
            return new SfxLight(false, dx, dy, dz, hx, hy, hz,
                                0f, 0f, 1f, 1f, 1f, 0f, 0f, 1f,
                                Mathf.Max(0.01f, relief), ambient, diffuse, specular, Mathf.Max(1f, shine));
        }

        /// A light floating above the sprite plane at a position measured in HALF-FRAME UNITS: (0,0) is
        /// `centreX,centreY`, and one unit is `half` pixels in BOTH axes. Passing the SPRITE's centre and half
        /// its shorter side keeps the space isotropic — a radius covers the same distance horizontally as
        /// vertically — at the cost that the LONGER axis of a non-square frame runs past ±1 (a 128x32 frame
        /// reaches its left and right edges at about ±4). The caller supplies the frame explicitly rather than
        /// this deriving it from W,H, because the buffer being shaded is not always the picture: an overflow
        /// margin around it must not move the light.
        ///
        /// `posZ` is the light's height above the plane in those same units — raise it and the light spreads
        /// flatter and wider, drop it and it rakes across the surface picking out every slope. Attenuation is
        /// measured on the sprite PLANE (the height does not eat into the reach), so `radius` is exactly how far
        /// across the picture the light carries, and `falloff` shapes the curve from full brightness at the
        /// light to nothing at the rim.
        public static SfxLight Point(float posX, float posY, float posZ, float radius, float falloff,
            float centreX, float centreY, float half, float relief,
            float ambient, float diffuse, float specular, float shine)
        {
            return new SfxLight(true, 0f, 0f, 1f, 0f, 0f, 1f,
                                posX, posY, Mathf.Max(0.001f, posZ),
                                Mathf.Max(1e-4f, radius), Mathf.Max(0.01f, falloff),
                                centreX, centreY, Mathf.Max(1f, half),
                                Mathf.Max(0.01f, relief), ambient, diffuse, specular, Mathf.Max(1f, shine));
        }

        /// The 0..1 shading term for one pixel: differentiate the height field into a normal, dot it against the
        /// light, add the Blinn highlight. This is the whole per-pixel path — the buffer-filling helpers below
        /// and RelightModifier's own loop both call exactly this, so what the tests measure is what ships.
        public static float ShadeAt(float[] height, int W, int H, int x, int y, in SfxLight L)
        {
            // Central difference, CLAMPED to the raster edge — never wrapped or mirrored, which would invent a
            // slope out of the opposite side of the picture.
            //
            // On the outermost row/column the clamp makes the span 1 pixel instead of the interior's 2, and the
            // difference is never divided by its span, so those pixels read a slope of roughly double strength —
            // a one-pixel hairline on art drawn flush to the frame edge. Kept deliberately: this is inherited
            // verbatim from PyreField.ReliefLight, and an effect authored against one must look the same on the
            // other. Dividing by the span here would silently change every existing Pyre-tuned asset.
            float hl = height[y * W + Mathf.Max(0, x - 1)], hr = height[y * W + Mathf.Min(W - 1, x + 1)];
            float hd = height[Mathf.Max(0, y - 1) * W + x], hu = height[Mathf.Min(H - 1, y + 1) * W + x];
            float nx = -(hr - hl) * L.relief, ny = -(hu - hd) * L.relief, nz = 1f;
            float nl = Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
            nx /= nl; ny /= nl; nz /= nl;

            float lx, ly, lz, hx, hy, hz, att;
            if (L.isPoint)
            {
                float ux = (x - L.centreX) / L.half, uy = (y - L.centreY) / L.half;
                float dx = L.posX - ux, dy = L.posY - uy;
                // No zero-length guard: posZ is floored at 0.001 by SfxRelief.Point, so this can never be 0.
                float dl = Mathf.Sqrt(dx * dx + dy * dy + L.posZ * L.posZ);
                lx = dx / dl; ly = dy / dl; lz = L.posZ / dl;
                HalfVector(lx, ly, lz, out hx, out hy, out hz);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                att = Mathf.Pow(Mathf.Clamp01(1f - d / L.radius), L.falloff);
            }
            else
            {
                lx = L.dirX; ly = L.dirY; lz = L.dirZ;
                hx = L.halfX; hy = L.halfY; hz = L.halfZ;
                att = 1f;
            }

            float ndl = Mathf.Max(0f, nx * lx + ny * ly + nz * lz);
            float spec = Mathf.Pow(Mathf.Max(0f, nx * hx + ny * hy + nz * hz), L.shine);
            // `att` is fixed at 1 for a Directional light (no radius, the whole frame is lit), so folding it
            // into the ambient term here changes nothing there. For a Point light `att` runs to exactly 0 at
            // `radius`, and multiplying ambient by it too is what makes THAT the edge of the effect: without
            // it, ambient stayed a flat floor added everywhere regardless of distance, so a pixel a mile
            // outside the light's circle still came back with a non-zero `lit` — and Tint/Shading downstream
            // read that as "this pixel is (dimly) lit", darkening and tinting the whole sprite instead of only
            // the part inside the radius.
            return Mathf.Clamp01((L.ambient + L.diffuse * ndl + L.specular * spec) * att);
        }

        /// Fills `light` (length W*H) with the shading term for every pixel. RelightModifier does NOT use this —
        /// it folds ShadeAt into its own pixel loop rather than allocating a second whole-frame buffer — but it
        /// is how the shading is measured in isolation, and it is the obvious thing for another caller to want.
        public static void ShadeAll(float[] light, float[] height, int W, int H, in SfxLight L)
        {
            if (light == null || height == null || W <= 0 || H <= 0) return;
            int n = W * H;
            if (light.Length < n || height.Length < n) return;

            for (int y = 0, i = 0; y < H; y++)
                for (int x = 0; x < W; x++, i++)
                    light[i] = ShadeAt(height, W, H, x, y, L);
        }

        /// <inheritdoc cref="ShadeAll"/> Convenience for a DIRECTIONAL light — see <see cref="Directional"/>.
        public static void ShadeDirectional(float[] light, float[] height, int W, int H,
            float angleDeg, float elevationDeg, float relief,
            float ambient, float diffuse, float specular, float shine)
            => ShadeAll(light, height, W, H,
                        Directional(angleDeg, elevationDeg, relief, ambient, diffuse, specular, shine));

        /// <inheritdoc cref="ShadeAll"/> Convenience for a POINT light — see <see cref="Point"/> for what the
        /// centre and half-extent mean and why they are given rather than derived from W,H.
        public static void ShadePoint(float[] light, float[] height, int W, int H,
            float lightX, float lightY, float lightZ, float radius, float falloff,
            float centreX, float centreY, float half, float relief,
            float ambient, float diffuse, float specular, float shine)
            => ShadeAll(light, height, W, H,
                        Point(lightX, lightY, lightZ, radius, falloff, centreX, centreY, half,
                              relief, ambient, diffuse, specular, shine));

        // Blinn half-vector: the bisector of the light and the viewer, with the viewer fixed head-on at (0,0,1)
        // because a sprite is always looked at square-on.
        static void HalfVector(float lx, float ly, float lz, out float hx, out float hy, out float hz)
        {
            hx = lx; hy = ly; hz = lz + 1f;
            float l = Mathf.Sqrt(hx * hx + hy * hy + hz * hz);
            if (l < 1e-6f) { hx = 0f; hy = 0f; hz = 1f; return; }
            hx /= l; hy /= l; hz /= l;
        }
    }

    /// Lights a flat sprite as though something bright were near it — a muzzle flash throwing light back onto the
    /// gun and the arm holding it, a torch raking across a wall, a rim of moonlight down one edge of a character.
    ///
    /// There is NO real lighting here and no normal map anywhere: the sprite is a flat picture and stays one. What
    /// this does is INVENT a surface out of the sprite's own content, differentiate it into a per-pixel normal, and
    /// shade that against a light — so the result follows the art's actual shape and animates with it for free,
    /// with nothing to author and nothing to keep in sync.
    ///
    /// Which content signal becomes that surface is the one choice that changes the character of the result.
    /// Silhouette inflates the shape into a rounded volume and is the general "make this read as 3D" answer. Slab
    /// is flat, so only the outline catches the light — that is a rim light. Brightness turns the sprite's own
    /// painted brightness into relief, sculpting the highlights and shading the artist already put there.
    ///
    /// Muzzle-flash recipe: Point mode, put the light on the barrel tip, keep Radius short so it dies before it
    /// reaches the rest of the character, and animate Add from 0 up and straight back down over the shot. Shading
    /// and Tint carry the form and the colour; Add is the flash itself.
    [Serializable]
    public class RelightModifier : PostModifier, ISpriteFxPreviewOverlay
    {
        // `heightSource` was briefly an AuxMapGenerator during this effect's first build and is now its own
        // three-value enum. No [FormerlySerializedAs] and no index-mapping shim: nothing has ever shipped with
        // the old field, so there is no authored asset anywhere holding the old serialized values.
        [Tooltip("Which signal from the sprite's own pixels becomes the fake surface. Silhouette inflates the " +
                 "shape into a rounded volume — the general choice. Slab is a flat card, so only the outline " +
                 "turns away from the light: a rim light. Brightness turns the sprite's own painted brightness " +
                 "into relief, sculpting the highlights and shading already in the art.")]
        public SfxReliefSource heightSource = SfxReliefSource.Silhouette;

        [Range(1, 64)]
        [ZUIShowIf("heightSource", "Silhouette")]
        [Tooltip("How many pixels in from the silhouette the invented volume takes to round off. Small reads as " +
                 "a thin bevel around the edge, large as one fat dome over the whole shape. Fixed for the whole " +
                 "effect — the surface is rebuilt from it, so animating it would pop.")]
        public int depthRadius = 8;

        [Range(0f, 1f)]
        [ZUIShowIf("heightSource", "Silhouette")]
        [Tooltip("How much of an alpha or colour step counts as a silhouette edge to measure distance from. " +
                 "Raise it to ignore soft antialiased fringes and seed only from hard boundaries. Fixed for the " +
                 "whole effect — the surface is rebuilt from it, so animating it would pop.")]
        public float edgeThreshold = 0.25f;

        [Range(0, 6)]
        [Tooltip("Blur passes over the invented surface BEFORE its slope is measured. Pixel art has one-pixel " +
                 "cliffs everywhere, and a slope taken straight off one is a black crease rather than a curve — " +
                 "this is what turns it into a shape. Fixed for the whole effect — the surface is rebuilt from " +
                 "it, so animating it would pop.")]
        public int soften = 2;

        [Range(0f, 16f)]
        [Tooltip("How steeply the invented surface turns away from the viewer. Low is a nearly flat card that " +
                 "barely shades; high exaggerates every bump into a hard-edged relief. Animatable.")]
        public ZUIValue relief = new ZUIValue(4f);

        [Tooltip("Directional is a light infinitely far away — one angle over the whole frame, like sunlight. " +
                 "Point is a light sitting somewhere near the sprite, with a position and a reach.")]
        public SfxLightMode mode = SfxLightMode.Point;

        [Range(-180f, 180f)]
        [ZUIShowIf("mode", "Directional")]
        [Tooltip("Bearing the light comes from, in the screen plane — 0 is from the right, 90 from above. " +
                 "Animatable.")]
        public ZUIValue angle = new ZUIValue(45f);

        [Range(0f, 90f)]
        [ZUIShowIf("mode", "Directional")]
        [Tooltip("How far the light is tipped up out of the screen plane toward the viewer. 0 rakes across the " +
                 "surface and maximises contrast; 90 is head-on and nearly flattens it. Animatable.")]
        public ZUIValue elevation = new ZUIValue(35f);

        [ZUIPair2D("lightY", "Light position")]
        [Range(-4f, 4f)]
        [ZUIShowIf("mode", "Point")]
        [Tooltip("Where the light sits over the picture. (0,0) is the centre of the frame and 1 unit is HALF the " +
                 "frame's shorter side, in both axes — so ±1 reaches the edge on the shorter axis, and the " +
                 "longer axis of a wide or tall frame runs further out than that. It may sit outside the frame " +
                 "entirely. Animatable — drag it along a barrel or a swing.")]
        public ZUIValue lightX = new ZUIValue(0f);

        [Range(-4f, 4f)]
        [ZUIShowIf("mode", "Point")]
        [Tooltip("Where the light sits over the picture, vertically, in the same half-frame units as its " +
                 "horizontal position. (0,0) is the centre of the frame. Animatable.")]
        public ZUIValue lightY = new ZUIValue(0f);

        [Range(0.05f, 3f)]
        [ZUIShowIf("mode", "Point")]
        [Tooltip("How far off the sprite plane the light floats, in the same units as its position. Low rakes " +
                 "across the surface and picks out every slope; high spreads flatter and softer. Animatable.")]
        public ZUIValue lightHeight = new ZUIValue(0.5f);

        [Range(0.05f, 4f)]
        [ZUIShowIf("mode", "Point")]
        [Tooltip("How far across the picture the light carries before it dies, measured on the sprite plane " +
                 "(1 = half the frame's shorter side). Keep it short for a muzzle flash so the light reaches the " +
                 "gun and the hand but not the whole character. Animatable.")]
        public ZUIValue radius = new ZUIValue(1.2f);

        [Range(0.25f, 4f)]
        [ZUIShowIf("mode", "Point")]
        [Tooltip("Shape of the fade between full brightness at the light and nothing at its rim. 1 is a straight " +
                 "ramp; higher concentrates the light into a tight hotspot with a long dim tail. Animatable.")]
        public ZUIValue falloff = new ZUIValue(2f);

        [Range(0f, 1f)]
        [Tooltip("The unlit floor — how much light a pixel gets where nothing reaches it. At the default the " +
                 "sprite keeps roughly its original brightness in the dark; lower it to let the light carve real " +
                 "darkness out of the parts it misses. Animatable.")]
        public ZUIValue ambient = new ZUIValue(0.5f);

        [Range(0f, 2f)]
        [Tooltip("Strength of the plain surface shading — how much brighter a face turned toward the light gets " +
                 "than one turned away. This is what makes the shape read. Animatable.")]
        public ZUIValue diffuse = new ZUIValue(0.85f);

        [Range(0f, 2f)]
        [Tooltip("Strength of the glossy highlight riding on top of the shading. Adds a wet or metallic sheen on " +
                 "the slopes angled halfway between the light and the viewer. Animatable.")]
        public ZUIValue specular = new ZUIValue(0.35f);

        [Range(1f, 64f)]
        [Tooltip("How tight that glossy highlight is. Low smears it over a broad area as a soft satin; high " +
                 "shrinks it to a small hard glint. Animatable.")]
        public ZUIValue shine = new ZUIValue(16f);

        [Tooltip("Colour of the light. Tint and the additive flash both carry it; the plain shading does not, so " +
                 "the sprite can be lit in shape without being coloured.")]
        public Color lightColor = Color.white;

        [Range(0f, 1f)]
        [Tooltip("How much the light modulates the sprite's existing colour — the form and volume. 0 leaves " +
                 "brightness exactly as painted, 1 fully darkens the parts facing away and brightens the parts " +
                 "facing the light. Animatable.")]
        public ZUIValue shading = new ZUIValue(1f);

        [Range(0f, 1f)]
        [Tooltip("How far the light's colour pulls the sprite's own colour toward it, in proportion to how lit " +
                 "each pixel is. This is what makes an orange flash read as orange on grey metal. Animatable.")]
        public ZUIValue tint = new ZUIValue(0.35f);

        [Range(0f, 2f)]
        [Tooltip("Extra light added on top, only where the light actually reaches. This is the flash dial — " +
                 "animate it up and straight back down over a shot and the light blows out for a frame or two. " +
                 "Animatable.")]
        public ZUIValue add = new ZUIValue(0f);

        float reliefV, angleV, elevationV, lightXV, lightYV, lightHeightV, radiusV, falloffV;
        float ambientV, diffuseV, specularV, shineV, shadingV, tintV, addV;

        public override string DisplayName => "Fake light";

        // Only ever recolours where pixels already are — the light never paints into empty space.
        public override int OutwardReachPx() => 0;

        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            reliefV = Mathf.Clamp(e(relief, 0), 0f, 16f);
            angleV = Mathf.Clamp(e(angle, 1), -180f, 180f);
            elevationV = Mathf.Clamp(e(elevation, 2), 0f, 90f);
            lightXV = Mathf.Clamp(e(lightX, 3), -4f, 4f);
            lightYV = Mathf.Clamp(e(lightY, 4), -4f, 4f);
            lightHeightV = Mathf.Clamp(e(lightHeight, 5), 0.05f, 3f);
            radiusV = Mathf.Clamp(e(radius, 6), 0.05f, 4f);
            falloffV = Mathf.Clamp(e(falloff, 7), 0.25f, 4f);
            ambientV = Mathf.Clamp01(e(ambient, 8));
            diffuseV = Mathf.Clamp(e(diffuse, 9), 0f, 2f);
            specularV = Mathf.Clamp(e(specular, 10), 0f, 2f);
            shineV = Mathf.Clamp(e(shine, 11), 1f, 64f);
            shadingV = Mathf.Clamp01(e(shading, 12));
            tintV = Mathf.Clamp01(e(tint, 13));
            addV = Mathf.Clamp(e(add, 14), 0f, 2f);
        }

        public override void Apply(Color32[] buf, int W, int H)
        {
            if (W <= 0 || H <= 0 || buf == null || buf.Length < W * H) return;
            if (shadingV <= 0.001f && tintV <= 0.001f && addV <= 0.001f) return;

            Color32[] src = buf;
            AuxMapGenerator gen;
            switch (heightSource)
            {
                case SfxReliefSource.Slab: gen = AuxMapGenerator.Alpha; break;
                case SfxReliefSource.Brightness:
                    gen = AuxMapGenerator.Luma;
                    // Luma reads the RGB of EVERY pixel, including the ones behind zero alpha, and the soften
                    // blur then spreads that into the visible pixels beside them. Atlas bleed and lossy
                    // compression leave real colour back there, so without this the invented surface would
                    // depend on pixels nobody can see. Zeroing the hidden colour first makes the surround the
                    // lowest part of the surface — which is what the other two sources already do, both being
                    // forced to 0 wherever alpha is. (respectAlpha would not do this job: BuildMap applies it
                    // AFTER the blur, by which point the hidden colour has already spread inward.)
                    src = (Color32[])buf.Clone();
                    for (int i = 0; i < src.Length; i++) if (src[i].a == 0) src[i] = default;
                    break;
                default: gen = AuxMapGenerator.EdgeDistanceIn; break;
            }

            // The height field IS the fake surface. BuildMap already handles the blur (soften), which has to
            // happen before the slope is measured, not after.
            float[] height = SpriteFxAuxMap.BuildMap(src, W, H, gen,
                edgeThreshold, depthRadius, 0f, 60f,
                1f, 0f, 1f, 0f, Mathf.Clamp(soften, 0, 6), false, false);

            // The picture may be sitting inside a PADDED buffer, because something else in the stack reaches
            // outward. The light is placed against the SPRITE rect, never against the whole canvas — otherwise
            // adding an unrelated glow to the stack (the margin is the sum of every effect's reach) would drag a
            // tuned muzzle flash off the barrel it was placed on. 0 means the host never told us, in which case
            // the buffer IS the picture and this is exactly what it always did.
            bool known = pictureW > 0 && pictureH > 0;
            int sw = known ? pictureW : W, sh = known ? pictureH : H;
            int ox = known ? padX : 0, oy = known ? padY : 0;

            SfxLight L = mode == SfxLightMode.Point
                ? SfxRelief.Point(lightXV, lightYV, lightHeightV, radiusV, falloffV,
                                  ox + (sw - 1) * 0.5f, oy + (sh - 1) * 0.5f,
                                  Mathf.Max(1f, Mathf.Min(sw, sh) * 0.5f),
                                  reliefV, ambientV, diffuseV, specularV, shineV)
                : SfxRelief.Directional(angleV, elevationV, reliefV, ambientV, diffuseV, specularV, shineV);

            // One pass, no second whole-frame buffer: the shading term is computed where it is consumed. At
            // 256x256 a float[] per frame is a quarter-megabyte on the Large Object Heap, every Update.
            const float inv255 = 1f / 255f;
            for (int y = 0, i = 0; y < H; y++)
                for (int x = 0; x < W; x++, i++)
                {
                    Color32 c = buf[i];
                    if (c.a == 0) continue;   // never paint light into empty space — that is what keeps reach at 0

                    float r = c.r * inv255, g = c.g * inv255, b = c.b * inv255;
                    float lit = SfxRelief.ShadeAt(height, W, H, x, y, L);

                    // Form: modulate the colour the sprite already has. 0.35 + L * 1.15 is Pyre's tuned
                    // shadow-to-highlight spread, so an unlit face darkens without going black.
                    float mul = Mathf.Lerp(1f, 0.35f + lit * 1.15f, shadingV);
                    r *= mul; g *= mul; b *= mul;

                    // Colour: pull the lit parts toward the light's own colour, in proportion to how lit they are.
                    float t = tintV * lit;
                    r = Mathf.Lerp(r, r * lightColor.r, t);
                    g = Mathf.Lerp(g, g * lightColor.g, t);
                    b = Mathf.Lerp(b, b * lightColor.b, t);

                    // Flash: additive, gated on how far above the ambient floor this pixel is, so raising it blows
                    // out the lit side rather than washing the whole sprite evenly.
                    float over = Mathf.Max(0f, lit - ambientV);
                    r += lightColor.r * over * addV;
                    g += lightColor.g * over * addV;
                    b += lightColor.b * over * addV;

                    buf[i] = new Color32(ToByte(r), ToByte(g), ToByte(b), c.a);
                }
        }

        // ── preview overlay (ISpriteFxPreviewOverlay) ───────────────────────────────────────────────────────
        // Where the light sits and how far its radius reaches, drawn straight into the preview's own pixels in
        // the same buffer-pixel space Apply just shaded them in — so tuning the radius can be checked against the
        // sprite instead of guessed from the number alone. It is a diagnostic for the author: never baked, never
        // part of a real render, and off unless someone asks for it.
        //
        // This used to be a TryGetPointGizmo accessor read by a hardcoded toggle in SpriteFxStackWindow's
        // transport row, which meant a "Light radius" control sat there permanently even for stacks holding no
        // light at all. The effect now says what it wants marked and draws it itself; the window only reserves a
        // strip. See ISpriteFxPreviewOverlay for the recipe.

        /// Yellow for the reach, hot orange for the position — two marks that read apart at a glance, and both
        /// chosen to survive over pixel art of any hue. Nearly opaque, because a faint diagnostic over a bright
        /// sprite is one you have to hunt for.
        static readonly Color32 GizmoRingColor = new Color32(255, 240, 80, 235);
        static readonly Color32 GizmoDotColor = new Color32(255, 90, 40, 255);

        public string OverlayLabel => "Light radius";

        public string OverlayTooltip =>
            "Marks this Fake Light's position and its radius circle, so the reach of the light can be checked " +
            "against the sprite directly. The ring is the RADIUS only — light height and falloff also shape how " +
            "far the light visibly carries, so treat it as where the light is aimed, not where it stops. " +
            "Preview-only: never saved into the stack and never baked. Only offered in Point mode, since a " +
            "Directional light has no position or radius to show.";

        /// Null: each Fake Light gets its own toggle. Telling two lights apart is the entire reason to mark one,
        /// so collapsing them behind a shared toggle would delete the information the overlay exists to give.
        public string OverlayGroupKey => null;

        /// Point mode only. A Directional light is infinitely far away — it has neither a position nor a reach,
        /// so there is nothing a marker could honestly point at. (The host owns the `enabled` half of this
        /// question — see ISpriteFxPreviewOverlay.)
        public bool WantsPreviewOverlay => mode == SfxLightMode.Point;

        public void DrawPreviewOverlay(SpriteFxOverlayCanvas canvas)
        {
            if (mode != SfxLightMode.Point) return;

            // The SAME centre/half/position math Apply uses, including its fallback when the host never called
            // SetPicture and the picture rect is therefore unknown — the buffer IS the picture in that case.
            // Re-deriving it any other way is how an indicator drifts from what was actually shaded, which is
            // worse than no indicator: it invites a correction that makes the real thing wrong.
            bool known = pictureW > 0 && pictureH > 0;
            int sw = known ? pictureW : canvas.Width, sh = known ? pictureH : canvas.Height;
            if (sw <= 0 || sh <= 0) return;
            int ox = known ? padX : 0, oy = known ? padY : 0;

            float half = Mathf.Max(1f, Mathf.Min(sw, sh) * 0.5f);
            float centreX = ox + (sw - 1) * 0.5f, centreY = oy + (sh - 1) * 0.5f;
            float px = centreX + lightXV * half;
            float py = centreY + lightYV * half;

            canvas.Circle(px, py, radiusV * half, GizmoRingColor);
            canvas.Dot(px, py, 1, GizmoDotColor);
        }
    }
}
