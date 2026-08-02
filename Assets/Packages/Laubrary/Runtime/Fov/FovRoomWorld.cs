using System;
using FOW;
using UnityEngine;

namespace Laubrary.Fov
{
    /// The room recipe for the Pixel-Perfect Fog Of War asset: configure-or-create ONE FogOfWarWorld
    /// bounded to a rect room, tuned for a 2D top-down room whose unexplored space is near-black and
    /// whose explored ground stays visible but dimmed. Serializable, so a game embeds it as Inspector
    /// knobs; call ConfigureOrCreate(roomBounds) once the room's world rect is known.
    ///
    /// Deliberately FIXED (not knobs) because changing any of them is a different recipe, not a tuning:
    /// - is2D + XY plane: this is the flat-room recipe.
    /// - FogSampleMode.Texture: the screen samples the fog RT — the live cone rendered into it each
    ///   frame PLUS the regrow memory and FovReveal's SnapFull stamps. NOT "Both": with Both the
    ///   fullscreen shader compiles FOW_SAMPLE_REALTIME, which compiles TextureSample_float out
    ///   entirely, so "Both" shows the live cone ONLY and every memory behaviour (floor, stamps,
    ///   reveal modes) silently never reaches the screen — verified empirically against the shader
    ///   2026-08-02. Texture is the only mode where explored ground actually stays dimly visible.
    /// - UseRegrow on: explored stays revealed (the memory floor knob dims it).
    /// - AsyncReadbackFogDataToCpu on: FovReveal's threshold polling needs it — without it every
    ///   SampleFogTextureColorAtPoint is a blocking ReadPixels.
    /// - HidersUseFogTexture OFF — a SAFETY INVARIANT, never a knob: hiders (FovActorHide) must gate on
    ///   the revealer's LIVE line-of-sight geometry, so no memory floor, regrow value or SnapFull stamp
    ///   in the fog texture can ever reveal a hidden actor in an explored-but-unlit area. Turning it on
    ///   would leak every enemy standing on remembered ground.
    [Serializable]
    public class FovRoomWorld
    {
        [Tooltip("Colour of unlit fog. The ALPHA picks the asset's Solid-appearance blend: 0 = solid " +
                 "fill (unseen is a uniform veil, nothing reads through), 1 = multiplicative tint " +
                 "(scene x colour — bright sprites stay readable through the 'black', which is exactly " +
                 "how blocker walls were visible from spawn). Keep 0 for unseen-is-UNSEEN. The value " +
                 "reaches the shader unconverted, so author it in LINEAR terms; the default displays " +
                 "as roughly sRGB (0.02, 0.02, 0.03).")]
        public Color fogColor = new Color(0.0015f, 0.0015f, 0.0026f, 0f);

        [Tooltip("Fog texture resolution, in texels per world unit. The texture maps the room rect, so " +
                 "resolution scales with room size; 16 = one texel per art pixel on a 16-ppu art grid.")]
        [Min(1f)] public float texelsPerUnit = 16f;

        [Tooltip("Floor of the fog texture's resolution per axis, so a tiny room still gets a usable texture.")]
        [Min(16)] public int minResolution = 64;

        [Tooltip("Ceiling of the fog texture's resolution per axis, so a huge room can't allocate an " +
                 "absurd render texture.")]
        [Min(64)] public int maxResolution = 2048;

        [Tooltip("How much visibility explored ground keeps once out of sight (MaxFogRegrowAmount). A " +
                 "FovReveal bound to this world re-owns the value live, so its reveal mode can flip mid-play.")]
        [Range(0f, 1f)] public float memoryFloor = 0.45f;

        [Tooltip("Pixelate the fog on a world-space grid so fog pixels sit on the art's own grid rather " +
                 "than the screen. Off = smooth analog fog.")]
        public bool pixelate = true;

        [Tooltip("Fog pixels per world unit when pixelation is on. Match the art's pixels-per-unit.")]
        [Min(1f)] public float pixelDensity = 16f;

        [Tooltip("Snap the revealer's origin to the pixelation grid too, so the cone's edge never " +
                 "shimmers by half a fog pixel as the revealer moves.")]
        public bool roundRevealerPosition = true;

        [Tooltip("Let revealed light leak in an arc past obstacle corners (the shader's FOW_BLEED_ON / " +
                 "CalculateFogBleed path: blocked sight segments are extended by an arc swung around " +
                 "the corner, growing with angular distance from it). That arc IS the see-around-the-" +
                 "edge leak — floor behind a wall lights up near every corner — so the room recipe " +
                 "defaults it OFF. Lighting the wall itself is Sight Extra Amount's job, not this.")]
        public bool allowBleeding = false;

        [Tooltip("Push the reveal past whatever surface a sight ray hits, so the wall itself lights " +
                 "when the cone hits it. The shader doubles this in practice: the blocked segment is " +
                 "extended by (this + 1/pixelDensity), and the edge-soften window only STARTS another " +
                 "full extra beyond that (FogOfWarLogic.hlsl RadialEdgeSoftening) — full brightness " +
                 "reaches 2x(this + 1/pixelDensity) into the surface and dies edgeSoftenDistance later. " +
                 "Budget for wall thickness t: 2x(value + 1/pixelDensity) + edgeSoftenDistance <= t. " +
                 "Default 0.25 with 16 px/unit pixelation: full-bright 0.625 into a 1-unit wall, fully " +
                 "extinguished at 0.975 — the wall face lights, nothing behind it ever does.")]
        [Min(0f)] public float sightExtraAmount = 0.25f;

        [Tooltip("Shade the pushed-past reveal off over this distance rather than hard-stopping it. " +
                 "Part of the wall-thickness budget on sightExtraAmount — see its tooltip.")]
        [Min(0f)] public float edgeSoftenDistance = 0.35f;

        /// One FogOfWarWorld per scene, configured for THIS room. Reuses a scene-authored world if one
        /// exists; builds one otherwise. Values land BEFORE the component initializes (fresh worlds are
        /// built on an inactive GameObject, existing ones are toggled off around the write), because
        /// FogOfWarWorld reads its whole config once, up front.
        public FogOfWarWorld ConfigureOrCreate(Bounds room)
        {
            var world = UnityEngine.Object.FindAnyObjectByType<FogOfWarWorld>(FindObjectsInactive.Include);
            bool fresh = world == null;
            if (fresh)
            {
                var go = new GameObject("FogOfWarWorld");
                go.SetActive(false);    // configure first — Awake/Initialize is the one-shot config read
                world = go.AddComponent<FogOfWarWorld>();
            }
            else
                world.enabled = false;  // cleanup + re-Initialize below, the same toggle the asset itself uses

            world.is2D = true;
            world.GamePlaneOrientation = FogOfWarWorld.GamePlane.XY;   // implied by is2D; stated anyway
            // Texture, NOT Both: Both's screen pass compiles the texture sample out (see class doc),
            // so it can never show the memory floor or SnapFull stamps. ConstantBlur off keeps the
            // fog texels crisp instead of gaussian-smearing them.
            world.FOWSamplingMode = FogOfWarWorld.FogSampleMode.Texture;
            world.UseConstantBlur = false;
            world.FowColor = fogColor;
            world.UseWorldBounds = true;
            world.WorldBounds = room;
            world.FowResX = Mathf.Clamp(Mathf.RoundToInt(room.size.x * texelsPerUnit), minResolution, maxResolution);
            world.FowResY = Mathf.Clamp(Mathf.RoundToInt(room.size.y * texelsPerUnit), minResolution, maxResolution);
            world.UseRegrow = true;                  // explored stays revealed...
            world.MaxFogRegrowAmount = memoryFloor;  // ...dimmed to the memory floor — FovReveal re-owns this live
            world.PixelateFog = pixelate;            // WorldSpacePixelate is only honoured with this on
            world.WorldSpacePixelate = pixelate;     // fog pixels sit on the WORLD grid, not the screen
            world.PixelDensity = pixelDensity;
            world.RoundRevealerPosition = roundRevealerPosition;

            // Lit blockers: see the class comment on sightExtraAmount/allowBleeding — under pixelation the
            // asset adds 1/PixelDensity on top of sightExtraAmount.
            world.AllowBleeding = allowBleeding;
            world.SightExtraAmount = sightExtraAmount;
            world.EdgeSoftenDistance = edgeSoftenDistance;

            // The safety invariant + the readback FovReveal's polling depends on — see the class comment.
            world.HidersUseFogTexture = false;
            world.AsyncReadbackFogDataToCpu = true;

            if (fresh) world.gameObject.SetActive(true);
            else world.enabled = true;

            // The asset hardcodes the fog RT to Bilinear. When the fog is pixelated on the world grid
            // the RT's texels ARE that grid (texelsPerUnit == pixelDensity by default), so Point keeps
            // the fog pixels crisp; smooth analog fog keeps Bilinear. Play mode only — the RT does not
            // exist in edit mode.
            var rt = world.GetFOWRT();
            if (rt != null) rt.filterMode = pixelate ? FilterMode.Point : FilterMode.Bilinear;

            // (Re)initializing reset the asset's global effect-strength to 1 — re-assert the debug
            // reveal-all flag so the toggle survives room rebuilds.
            FovDebug.Apply();
            return world;
        }
    }
}
