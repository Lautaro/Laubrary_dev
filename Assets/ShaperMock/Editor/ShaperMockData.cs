// ShaperMockData — the mock's own in-memory data model. Deliberately NOT ShaperNode / ShaperFillDef / any
// other committed Shaper production runtime type (T-0129's ground rule): this file lives outside
// Laubrary.Shaper entirely, in its own ShaperMock.Editor namespace, so a production Shaper type cannot
// even be named here by accident.
//
// ShaperMockDocument is a ScriptableObject purely so Undo.RecordObject has a real UnityEngine.Object to
// record against — the same reason Pyre's own `spec` is one. It is created fresh in memory per window
// (ScriptableObject.CreateInstance), never written to disk, and discarded on domain reload or window
// close: the mock stays disposable, Undo still works exactly like it will on the real Shaper document.
//
// T-0131 extends this with the coordinator scope: Bag/Composite node kinds (a node tree, not just one
// Primitive per layer), the remaining five fill kinds, a Border stage, a document-level Light rig +
// per-node lighting response, extrusion/bevel, a Swarm card and an Effects list. Fields called out by the
// design doc as already-`ZUIValue` in the real engine (border width, light-rig dials, extrusion/bevel) use
// a real `ZUIValue` here too, so the mock actually demonstrates the envelope-ready control, not a stand-in.
// Fill's own per-kind dials stay plain floats, matching the vertical slice's own precedent (gradient angle
// is a plain MicroSlider, not a Z.Value) — §H2 of the design doc lists fill dials as an existing-engine
// fact, not something this UI-only mock needs to re-litigate.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShaperMock.Editor
{
    public sealed class ShaperMockDocument : ScriptableObject
    {
        public ShaperMockCanvas canvas = new ShaperMockCanvas();
        public List<ShaperMockLayer> layers = new List<ShaperMockLayer>();
        public ShaperMockLightRig lightRig = new ShaperMockLightRig();

        // Cache-state (§G) — entirely simulated view state, not a real bake. Cleared on any structural
        // edit (mirrors real per-node dirty propagation invalidating the cache) and refilled progressively
        // by a non-blocking EditorApplication.update tick, so the strip visibly "bakes" over real time
        // instead of jumping to fully-cached — the same non-blocking-preview posture T-0115's real
        // pre-baker has. Not [Serializable] on purpose: it is presentation state, never authored data.
        [NonSerialized] public HashSet<int> cachedFrames = new HashSet<int>();

        /// A fresh document seeded with one Primitive layer — the "single Primitive layer root" the first
        /// slice demonstrated, still the default so the absence rule (§B4) has a plain baseline to compare
        /// against a Bag/Composite layer.
        public static ShaperMockDocument CreateSeeded()
        {
            var doc = CreateInstance<ShaperMockDocument>();
            doc.hideFlags = HideFlags.DontSave;
            doc.layers.Add(new ShaperMockLayer { name = "Layer 1" });
            doc.lightRig.lights.Add(ShaperMockLight.CreateDefault("Key"));
            return doc;
        }
    }

    [Serializable]
    public sealed class ShaperMockCanvas
    {
        public int width = 64;
        public int height = 64;
        public int frameCount = 1;
        public int seed = 0;
        // Real ShaperDocument.layerSpacing/pixelSize (ShaperLightRig.cs) — document-level fields the mock's
        // Canvas section didn't show at all (T-0138 #14).
        public float layerSpacing = 0.75f;
        public float pixelSize = 1f;
    }

    [Serializable]
    public sealed class ShaperMockLayer
    {
        public string name = "Layer 1";
        public bool enabled = true;
        public ShaperMockNode root = new ShaperMockNode { name = "Root" };
        // Real ShaperLayer.zOffset (ShaperLightRig.cs) — this layer's own Z-position offset, canvas pixels,
        // SIGNED, already ZUIValue in the real engine. Was simply missing from the mock entirely (T-0140).
        public ZUIValue zOffset = new ZUIValue(0f);
    }

    // "Solid" (T-0138 #1) — a UI-MOCK PLACEMENT CHOICE, not a confirmed real integration point. The real
    // ShaperNodeKind (ShaperNode.cs) has exactly 3 values today; the real engine has not yet decided whether
    // Solids becomes a 4th node kind, a Composite-style alternate source, or a separate document/layer-level
    // slot. See ShaperMockSolids.cs's header comment for the full open question, and the Z.Help box
    // ShaperMockWindow.BuildSolidBody adds when this kind is selected.
    public enum ShaperMockNodeKind { Primitive, Bag, Composite, Solid }
    // Real ShaperPrimitiveKind order (T-0138 #8) — was {Disc, Ngon, Star}, a 3-of-7 stand-in. "Disc" had no
    // real analog; the real equal-radius case is Ellipse (a circle IS an equal-radius ellipse), so this
    // renames rather than adds a redundant 8th entry.
    public enum ShaperMockShapeKind { Rect, Ellipse, Diamond, Triangle, Capsule, Ngon, Star }
    // Real ShaperCombineMode (ShaperOps.cs) is exactly 3 values — "Blend" was invented (T-0138 #2). Softness
    // is a property every mode has (ShaperMockNode.blend below), not a 4th mode.
    public enum ShaperMockCombineMode { Add, Subtract, Intersect }
    public enum ShaperMockCompositeReason { NotYetSplit, AuthoredData }

    /// Mirrors the real ShaperBlend (ShaperNode.cs) — the softness dials a node carries for folding into its
    /// parent (Add/Intersect blend width+sharpness, Subtract carve strength), reused unchanged for a swarm's
    /// own instance-merge dial (T-0138 #11, real ShaperSwarmDef.merge is the same type).
    [Serializable]
    public sealed class ShaperMockBlend
    {
        public float width = 0f;
        [Range(0f, 1f)] public float sharpness = 0.5f;
        [Range(0f, 1f)] public float carveStrength = 1f;
    }

    // Real ShaperShellAlignment (ShaperOps.cs) — shared by Shell and Border, same as the real engine.
    public enum ShaperMockShellAlignment { Centred, Inward, Outward }

    /// Mirrors the real ShaperSweep (ShaperNode.cs) — an operator on the finished shape, present on every
    /// node kind. Radial fields (start/extent degrees) and longitudinal fields (start/extent fraction) are
    /// four SEPARATE authored fields, not one pair reinterpreted per axis — the same "one field showing two
    /// quantities" fault ShaperBlend was split to avoid.
    [Serializable]
    public sealed class ShaperMockSweep
    {
        public bool enabled = false;
        public float startDegrees = 0f;
        public float extentDegrees = 360f;
        [Range(0f, 1f)] public float startFraction = 0f;
        [Range(0f, 1f)] public float extentFraction = 1f;
    }

    /// Mirrors the real ShaperShell (ShaperNode.cs) — keep only a band at a constant distance from a node's
    /// own edge. Present on every node kind, identity is enabled == false.
    [Serializable]
    public sealed class ShaperMockShell
    {
        public bool enabled = false;
        public float thickness = 4f;
        public ShaperMockShellAlignment alignment = ShaperMockShellAlignment.Centred;
    }

    // Real ShaperExtrusionTechnique / ShaperBevelTechnique (ShaperHeight.cs) — APPEND-ONLY order in the real
    // engine; mirrored here for the same reason (T-0138 #4).
    public enum ShaperMockExtrusionTechnique { Flat, Linear, Stepped, Dome, Round, Taper, Pyramid }
    public enum ShaperMockBevelTechnique { None, Linear, Rounded, Cove, Ogee, Stepped }

    [Serializable]
    public sealed class ShaperMockNode
    {
        public ShaperMockNodeKind kind = ShaperMockNodeKind.Primitive;

        // Only meaningful when this node lives inside a Bag's `members` list (§B2). A root layer node
        // never surfaces these through the UI, so their defaults (Add, unnamed) are harmless there.
        public string name = "Node";
        public bool enabled = true;
        public ShaperMockCombineMode combineMode = ShaperMockCombineMode.Add;
        // The softness dials this node carries for folding into its parent (T-0138 #3) — real ShaperNode.blend.
        public ShaperMockBlend blend = new ShaperMockBlend();

        // ── Sweep / Shell (T-0138 #12/#13) — real ShaperNode.sweep/shell, present on every node kind. ──────
        public ShaperMockSweep sweep = new ShaperMockSweep();
        public ShaperMockShell shell = new ShaperMockShell();

        // ── Primitive (§B4) ──────────────────────────────────────────────────────────────────────────
        public ShaperMockShapeKind shapeKind = ShaperMockShapeKind.Rect;
        public float rectHalfW = 0.5f;
        public float rectHalfH = 0.5f;
        public float rectCornerRadius = 0f;
        public float ellipseRx = 0.6f;
        public float ellipseRy = 0.6f;
        public float diamondRx = 0.5f;
        public float diamondRy = 0.5f;
        public float triangleBase = 0.8f;
        public float triangleHeight = 0.8f;
        public float capsuleHalfLength = 0.35f;
        public float capsuleRadius = 0.2f;
        public int ngonSides = 6;
        public float ngonRadius = 0.6f;
        public float ngonRotation = 0f;
        public float ngonCornerRadius = 0f;
        public int starArms = 5;
        public float starRadius = 0.65f;
        public float starLength = 0.55f;
        public float starBaseWidth = 0.35f;
        public float starSkew = 0f;

        // ── Bag (§B2, §B4) ───────────────────────────────────────────────────────────────────────────
        public List<ShaperMockNode> bagMembers = new List<ShaperMockNode>();

        // ── Composite (§B5) ──────────────────────────────────────────────────────────────────────────
        public int compositeGeneratorIndex = 0;
        public ShaperMockCompositeReason compositeReason = ShaperMockCompositeReason.NotYetSplit;
        public string compositeReasonNote = "";
        // The SELECTED generator's own authored dials (T-0137) — proves "picking a generator shows that
        // generator's own fields", the same pattern the effect catalog already demonstrates via ZuiReflect.
        // Re-created (never left stale) whenever compositeGeneratorIndex changes.
        [SerializeReference] public ShaperMockGeneratorParams generatorParams = ShaperMockCompositeCatalog.All[0].NewParams();
        // Real ShaperCompositeDef's buffer-sizing fields (T-0138 #15) — plain fields, not ZUIValue, matching
        // the design doc's own precedent for buffer-sizing dials (Canvas width/height).
        public float compositeHalfExtentX = 64f;
        public float compositeHalfExtentY = 64f;
        public int compositeBakeWidth = 128;
        public int compositeBakeHeight = 128;

        // ── Solid (T-0138 #1) ────────────────────────────────────────────────────────────────────────
        // Always-present, only READ when kind == Solid — same pattern as the Primitive shapeKind fields
        // above and the Composite fields below, not the null-gated absence pattern Fill/Border use (a Solid
        // has no "absent" state to represent; it's simply not the current kind). See ShaperMockSolids.cs.
        public ShaperMockSolidDef solid = new ShaperMockSolidDef();

        // ── Extrusion / bevel (§D3) — already ZUIValue in the real engine, so the mock uses ZUIValue too.
        // Shown on Primitive and Composite (both are leaf shapes); a Bag has no own geometry to extrude.
        public ShaperMockExtrusionTechnique extrudeTechnique = ShaperMockExtrusionTechnique.Flat;
        public ZUIValue extrudeDepth = new ZUIValue(0f);
        public ZUIValue extrudeAngle = new ZUIValue(0f);
        public ZUIValue extrudeSteps = new ZUIValue(4f);
        public ZUIValue extrudeCurve = new ZUIValue(0f);
        public ZUIValue extrudeTaper = new ZUIValue(0f);
        public ShaperMockBevelTechnique bevelTechnique = ShaperMockBevelTechnique.None;
        public ZUIValue bevelAmount = new ZUIValue(0f);
        public ZUIValue bevelSteps = new ZUIValue(0f);

        // ── Fill (§C) — null = "no fill of its own", painted by the nearest ancestor's fill (§C3).
        // [SerializeReference] so Undo can actually represent the null state (a plain [Serializable] class
        // field can't serialize null — Unity silently substitutes a default instance on snapshot).
        [SerializeReference] public ShaperMockFill fill = new ShaperMockFill();

        // ── Border (§D1) — absent by default at every level (BD-1.1).
        [SerializeReference] public ShaperMockBorder border = null;

        // ── Swarm (§E) — present on every node kind, but starts disabled ("+ Enable swarm").
        public ShaperMockSwarm swarm = new ShaperMockSwarm();

        // ── Effects (§F) — only rendered in the UI for a Composite-kind node (§B4); harmless elsewhere.
        [SerializeReference] public List<ShaperMockEffect> effects = new List<ShaperMockEffect>();

        // ── Lighting response (§D2) — per-node, packed into a small BoxKeyed on every node's own card.
        public ShaperMockLightResponse lightResponse = new ShaperMockLightResponse();

        /// A fresh Bag member — no own fill by default (bags own their fill; members don't, §C3).
        public static ShaperMockNode NewBagMember(string name, ShaperMockCombineMode mode)
            => new ShaperMockNode { kind = ShaperMockNodeKind.Primitive, name = name, combineMode = mode, fill = null };
    }

    // ── Fill (§C1–§C3) ──────────────────────────────────────────────────────────────────────────────
    public enum ShaperMockFillKind { Solid, Gradient, RampByQuantity, Texture, IndexedStrip, HeightField, TapestrySteel }
    // Real ShaperQuantity (ShaperFillContract.cs) is the closed 9-value vocabulary — was a 3-value stand-in
    // {Heat, Density, Height} (T-0138 #17). Order/values match the real enum.
    public enum ShaperMockRampQuantity { Coverage, Height, EdgeDistance, Heat, Density, Soot, Depth, Age, SurfaceDirection }
    public enum ShaperMockStripMode { Angular, Projection }
    // Real ShaperFillComposite/ShaperFillSpace/ShaperFillFit (ShaperFillContract.cs) — shared by EVERY fill
    // kind, not authored on any one of them (T-0138 #5).
    public enum ShaperMockFillComposite { Over, Add }
    public enum ShaperMockFillSpace { Stamped, Fixed }
    public enum ShaperMockFillFit { Uniform, Stretch }
    // Real ShaperGradientMode (ShaperFillContract.cs) — Gradient's own mode, missing entirely from the mock
    // before this task (T-0138 #6).
    public enum ShaperMockGradientMode { Linear, Radial, Angular, ByEdgeDistance }
    // Real ShaperTextureMapping (ShaperFillContract.cs) — Texture's own mapping, missing entirely before
    // this task (T-0138 #7).
    public enum ShaperMockTextureMapping { Fitted, Tiled }

    [Serializable]
    public sealed class ShaperMockFill
    {
        public ShaperMockFillKind kind = ShaperMockFillKind.Solid;

        // ── shared across every fill kind (T-0138 #5) — real ShaperFillDef.composite/space/fit. ───────────
        public ShaperMockFillComposite composite = ShaperMockFillComposite.Over;
        public ShaperMockFillSpace space = ShaperMockFillSpace.Stamped;
        public ShaperMockFillFit fit = ShaperMockFillFit.Uniform;

        // Solid
        public Color solidColor = new Color(0.35f, 0.7f, 1f, 1f);

        // Gradient — seeded eagerly, not lazily: Unity's serializer can silently promote a C#-null
        // Gradient to a non-null empty default the moment ANYTHING snapshots this object (confirmed live
        // via Undo.RecordObject), which defeats a lazy `gradient ??= Default()` the instant Undo has ever
        // touched the document. A real default from construction has nothing to race against.
        public Gradient gradient = DefaultGradient();
        public float gradientAngleDegrees;
        public ShaperMockGradientMode gradientMode = ShaperMockGradientMode.Linear;

        // RampByQuantity (§C1) — greyed out with the reason when the shape publishes no such quantity
        // (§B4's own literal-follow-the-rule case). This mock has no real quantity pipeline, so every
        // Primitive/Bag/Composite is treated as NOT publishing one — the greyed state is always shown,
        // demonstrating the rule rather than a real gate.
        public ShaperMockRampQuantity rampQuantity = ShaperMockRampQuantity.Coverage;
        public Color rampTint = new Color(1f, 0.4f, 0.15f, 1f);
        public float rampInputLow = 0f;
        public float rampInputHigh = 1f;

        // Texture — the source is a picker over a small mock texture-name library, never a typed path.
        public ShaperMockTextureMapping textureMapping = ShaperMockTextureMapping.Fitted;
        public int textureSourceIndex = 0;
        public float textureTilesX = 1f;
        public float textureTilesY = 1f;
        public float textureOffsetU = 0f;
        public float textureOffsetV = 0f;
        public float textureAngleDegrees = 0f;
        public Color textureTint = Color.white;

        // IndexedStrip (§C1, §C2) — a hand-authored list of colour+height slots.
        public List<ShaperMockStripSlot> stripSlots = new List<ShaperMockStripSlot>
        {
            new ShaperMockStripSlot { color = new Color(0.9f, 0.2f, 0.2f), height = 0.3f },
            new ShaperMockStripSlot { color = new Color(0.95f, 0.8f, 0.1f), height = 0.7f },
        };
        public float stripRepeats = 1f;
        public float stripOrientationDegrees = 0f;
        public float stripOffset = 0f;
        public float stripReach = 1f;
        public Color stripPlainColor = Color.grey;
        public ShaperMockStripMode stripMode = ShaperMockStripMode.Angular;

        // HeightField (§C1, §C2) — a thumbnail-grid picker over a small mock preset set (245 in the real
        // library; this mock ships a handful, enough to prove the "click a thumbnail to apply it" shape).
        public int heightFieldPresetIndex = 0;
        public float heightFieldScale = 1f;
        public Color heightFieldTint = Color.white;

        // TapestrySteel (§C1)
        public float steelCells = 8f;
        public float steelOctaves = 3f;
        public float steelSeed = 0f;
        public Color steelBaseLow = new Color(0.25f, 0.27f, 0.3f);
        public Color steelBaseHigh = new Color(0.7f, 0.72f, 0.75f);
        public Color steelRustColor = new Color(0.55f, 0.28f, 0.1f);
        public float steelRustAmount = 0.2f;
        public float steelRustReachPixels = 4f;
        public float steelGrain = 0.15f;

        // Palette-quantise (§C1) — the shared post-stage every fill kind carries. 0 = off (full colour).
        public int quantiseLevels = 0;

        public static bool IsUnseeded(Gradient g)
            => g == null || g.colorKeys == null || g.colorKeys.Length == 0
               || (g.colorKeys.Length == 2 && g.colorKeys[0].color == Color.white && g.colorKeys[1].color == Color.white);

        public static Gradient DefaultGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.85f, 0.3f), 0f), new GradientColorKey(new Color(0.6f, 0.1f, 0.8f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        /// Author-facing names for the 9-value ramp quantity (T-0138 #17) — mirrors the real
        /// ShaperQuantities.Name (ShaperFillContract.cs), used in place of a raw ToString() so a CamelCase
        /// enum member (EdgeDistance) still reads as a phrase in a greyed-reason sentence.
        public static string RampQuantityName(ShaperMockRampQuantity q)
        {
            switch (q)
            {
                case ShaperMockRampQuantity.Coverage: return "coverage";
                case ShaperMockRampQuantity.Height: return "height";
                case ShaperMockRampQuantity.EdgeDistance: return "edge distance";
                case ShaperMockRampQuantity.Heat: return "heat";
                case ShaperMockRampQuantity.Density: return "density";
                case ShaperMockRampQuantity.Soot: return "soot";
                case ShaperMockRampQuantity.Depth: return "depth";
                case ShaperMockRampQuantity.Age: return "age";
                case ShaperMockRampQuantity.SurfaceDirection: return "surface direction";
                default: return "unknown";
            }
        }

        // A stand-in for the real 245-entry preset library (T-0111) — T-0135 grew this from 8 to 64 because
        // 8 always fit on screen unscrolled, which never actually tested whether the picker holds up at
        // anything resembling real scale (the same reason the effect catalog grew to its own real count).
        public static readonly string[] HeightFieldPresetNames = BuildHeightFieldPresetNames();
        public static readonly Color[] HeightFieldPresetTints = BuildHeightFieldPresetTints();

        static string[] BuildHeightFieldPresetNames()
        {
            var families = new[]
            {
                "Cobble", "Planks", "Brick", "Scale Mail", "Rivets", "Cracked Mud", "Woven Cane", "Honeycomb",
                "Slate", "Thatch", "Chainmail", "Tile", "Bark", "Sand Ripple", "Corrugated", "Basalt",
            };
            var variants = new[] { "", " Fine", " Coarse", " Worn" };
            var names = new List<string>();
            foreach (var v in variants)
                foreach (var f in families)
                    names.Add(f + v);
            return names.ToArray();
        }

        static Color[] BuildHeightFieldPresetTints()
        {
            var baseTints = new[]
            {
                new Color(0.55f,0.52f,0.5f), new Color(0.5f,0.36f,0.22f), new Color(0.6f,0.28f,0.22f),
                new Color(0.65f,0.66f,0.7f), new Color(0.45f,0.4f,0.32f), new Color(0.42f,0.32f,0.22f),
                new Color(0.6f,0.5f,0.3f), new Color(0.7f,0.55f,0.2f), new Color(0.35f,0.37f,0.4f),
                new Color(0.75f,0.65f,0.35f), new Color(0.5f,0.5f,0.53f), new Color(0.6f,0.58f,0.5f),
                new Color(0.45f,0.32f,0.2f), new Color(0.8f,0.72f,0.5f), new Color(0.55f,0.45f,0.3f),
                new Color(0.3f,0.3f,0.33f),
            };
            var shades = new[] { 1f, 0.85f, 1.15f, 0.7f };   // matches the four name variants above
            var tints = new List<Color>();
            foreach (var s in shades)
                foreach (var c in baseTints)
                    tints.Add(new Color(Mathf.Clamp01(c.r * s), Mathf.Clamp01(c.g * s), Mathf.Clamp01(c.b * s)));
            return tints.ToArray();
        }

        /// The names a Texture source picker shows — a stand-in, same reasoning as the height-field set.
        public static readonly string[] TextureSourceNames = { "Grunge A", "Grunge B", "Noise", "Hatch" };
    }

    [Serializable]
    public sealed class ShaperMockStripSlot
    {
        public Color color = Color.white;
        public float height = 0.5f;
    }

    // ── Border (§D1) ────────────────────────────────────────────────────────────────────────────────
    [Serializable]
    public sealed class ShaperMockBorder
    {
        public ZUIValue width = new ZUIValue(0.05f);
        public bool joinsCoverage = true;
        // Real ShaperBorderDef.alignment (ShaperBorderDef.cs) — default Inward, NOT Shell's Centred default:
        // "Inward is the default because it is the only one today's Pyre can do" (T-0138 #12).
        public ShaperMockShellAlignment alignment = ShaperMockShellAlignment.Inward;
        [SerializeReference] public ShaperMockFill stripFill = new ShaperMockFill
        {
            kind = ShaperMockFillKind.Solid,
            solidColor = new Color(0.05f, 0.05f, 0.08f, 1f),
        };
    }

    // ── Swarm (§E) ──────────────────────────────────────────────────────────────────────────────────
    [Serializable]
    public sealed class ShaperMockSwarm
    {
        public bool enabled = false;
        public int count = 8;
        // Real ShaperSwarmDef.seed (T-0138 #11) — base seed each instance's own jitter hashes from.
        public int seed = 0;
        public Vector2 positionJitter = new Vector2(0.1f, 0.1f);
        public float rotationJitterDegrees = 15f;
        public float scaleJitter = 0.2f;
        public float lifetimeStagger = 0.3f;
        // Real ShaperSwarmDef.merge (T-0138 #11) — the same ShaperBlend knobs a Bag member's combine mode
        // uses, reused here for how swarm instances fold into one field.
        public ShaperMockBlend merge = new ShaperMockBlend();
        public bool interact = false;

        // The real engine's authored count RANGE is [1,64] ([Range(1,64)] on ShaperSwarmDef.count) — this
        // is that range, not a runtime clamp.
        public const int HardCap = 64;
        // Real ShaperSwarmDef.SimulationHardCap — the SEPARATE clamp applied when a Composite generator is a
        // stateful simulation (IShaperSimulationSource) with no native swarm path. T-0138 #11: the mock
        // previously conflated this with "native available" and invented 24 instead of the real 6.
        public const int SimulationHardCap = 6;
    }

    // ── Light rig (§D2) ─────────────────────────────────────────────────────────────────────────────
    [Serializable]
    public sealed class ShaperMockLightRig
    {
        public List<ShaperMockLight> lights = new List<ShaperMockLight>();
        public ZUIValue ambientIntensity = new ZUIValue(0.25f);
        public Color ambientColour = new Color(0.5f, 0.55f, 0.65f);
    }

    // Real ShaperLightKind (ShaperLightRig.cs) — was missing from the mock's lights entirely (T-0138 #9).
    public enum ShaperMockLightKind { Directional, Point }

    [Serializable]
    public sealed class ShaperMockLight
    {
        public string name = "Light";
        public bool enabled = true;
        public ShaperMockLightKind kind = ShaperMockLightKind.Directional;
        public Color colour = Color.white;
        public ZUIValue intensity = new ZUIValue(1f);
        public ZUIValue yaw = new ZUIValue(45f);
        public ZUIValue pitch = new ZUIValue(35f);
        public ZUIValue posX = new ZUIValue(0f);
        public ZUIValue posY = new ZUIValue(0f);
        public ZUIValue posZ = new ZUIValue(1f);
        public ZUIValue range = new ZUIValue(4f);
        public ZUIValue specular = new ZUIValue(0.5f);

        public static ShaperMockLight CreateDefault(string name) => new ShaperMockLight { name = name };
    }

    // Real ShaperNormalKind (ShaperNormals.cs) — was missing from the mock's lighting response entirely
    // (T-0138 #10).
    public enum ShaperMockNormalKind { Constant, Profile }

    [Serializable]
    public sealed class ShaperMockLightResponse
    {
        public bool receiveLighting = true;
        public ZUIValue intensityScale = new ZUIValue(1f);
        public bool castShadows = true;
        public bool receiveShadows = true;
        public ZUIValue rimStrength = new ZUIValue(0f);
        public ZUIValue specular = new ZUIValue(0.5f);
        public ZUIValue specularPower = new ZUIValue(32f);
        public Color specularTint = Color.white;
        public ZUIValue rimPower = new ZUIValue(2f);
        public ShaperMockNormalKind normalKind = ShaperMockNormalKind.Constant;
    }

    // ── Effects (§F) ────────────────────────────────────────────────────────────────────────────────
    public enum ShaperMockEffectStage { Pre, Post }
    public enum ShaperMockEffectBucket { BufferFree, BufferPadded, NeedsSheets, Stuck }

    // ── Composite catalog (§B5) moved to ShaperMockGenerators.cs (T-0137) — it now carries the REAL nine
    // names + real palette-indifferent classification from T-0112's PyreCompositeCatalog, plus a
    // representative per-generator parameter set, instead of nine invented placeholder names with no
    // per-generator data at all.
}
