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
    }

    [Serializable]
    public sealed class ShaperMockLayer
    {
        public string name = "Layer 1";
        public bool enabled = true;
        public ShaperMockNode root = new ShaperMockNode { name = "Root" };
    }

    public enum ShaperMockNodeKind { Primitive, Bag, Composite }
    public enum ShaperMockShapeKind { Disc, Ngon, Star }
    public enum ShaperMockCombineMode { Add, Subtract, Intersect, Blend }
    public enum ShaperMockCompositeReason { NotYetSplit, AuthoredData }

    [Serializable]
    public sealed class ShaperMockNode
    {
        public ShaperMockNodeKind kind = ShaperMockNodeKind.Primitive;

        // Only meaningful when this node lives inside a Bag's `members` list (§B2). A root layer node
        // never surfaces these through the UI, so their defaults (Add, unnamed) are harmless there.
        public string name = "Node";
        public bool enabled = true;
        public ShaperMockCombineMode combineMode = ShaperMockCombineMode.Add;

        // ── Primitive (§B4) ──────────────────────────────────────────────────────────────────────────
        public ShaperMockShapeKind shapeKind = ShaperMockShapeKind.Disc;
        public float discRadius = 0.6f;
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

        // ── Extrusion / bevel (§D3) — already ZUIValue in the real engine, so the mock uses ZUIValue too.
        // Shown on Primitive and Composite (both are leaf shapes); a Bag has no own geometry to extrude.
        public ZUIValue extrudeDepth = new ZUIValue(0f);
        public ZUIValue extrudeAngle = new ZUIValue(0f);
        public ZUIValue extrudeCurve = new ZUIValue(0f);
        public ZUIValue extrudeTaper = new ZUIValue(0f);
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
    public enum ShaperMockRampQuantity { Heat, Density, Height }
    public enum ShaperMockStripMode { Angular, Projection }

    [Serializable]
    public sealed class ShaperMockFill
    {
        public ShaperMockFillKind kind = ShaperMockFillKind.Solid;

        // Solid
        public Color solidColor = new Color(0.35f, 0.7f, 1f, 1f);

        // Gradient — seeded eagerly, not lazily: Unity's serializer can silently promote a C#-null
        // Gradient to a non-null empty default the moment ANYTHING snapshots this object (confirmed live
        // via Undo.RecordObject), which defeats a lazy `gradient ??= Default()` the instant Undo has ever
        // touched the document. A real default from construction has nothing to race against.
        public Gradient gradient = DefaultGradient();
        public float gradientAngleDegrees;

        // RampByQuantity (§C1) — greyed out with the reason when the shape publishes no such quantity
        // (§B4's own literal-follow-the-rule case). This mock has no real quantity pipeline, so every
        // Primitive/Bag/Composite is treated as NOT publishing one — the greyed state is always shown,
        // demonstrating the rule rather than a real gate.
        public ShaperMockRampQuantity rampQuantity = ShaperMockRampQuantity.Heat;
        public Color rampTint = new Color(1f, 0.4f, 0.15f, 1f);
        public float rampInputLow = 0f;
        public float rampInputHigh = 1f;

        // Texture — the source is a picker over a small mock texture-name library, never a typed path.
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
        public Vector2 positionJitter = new Vector2(0.1f, 0.1f);
        public float rotationJitterDegrees = 15f;
        public float scaleJitter = 0.2f;
        public float lifetimeStagger = 0.3f;
        public bool interact = false;

        public const int HardCap = 64;
    }

    // ── Light rig (§D2) ─────────────────────────────────────────────────────────────────────────────
    [Serializable]
    public sealed class ShaperMockLightRig
    {
        public List<ShaperMockLight> lights = new List<ShaperMockLight>();
        public ZUIValue ambientIntensity = new ZUIValue(0.25f);
        public Color ambientColour = new Color(0.5f, 0.55f, 0.65f);
    }

    [Serializable]
    public sealed class ShaperMockLight
    {
        public string name = "Light";
        public bool enabled = true;
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
    }

    // ── Effects (§F) ────────────────────────────────────────────────────────────────────────────────
    public enum ShaperMockEffectStage { Pre, Post }
    public enum ShaperMockEffectBucket { BufferFree, BufferPadded, NeedsSheets, Stuck }

    // ── Composite catalog (§B5) moved to ShaperMockGenerators.cs (T-0137) — it now carries the REAL nine
    // names + real palette-indifferent classification from T-0112's PyreCompositeCatalog, plus a
    // representative per-generator parameter set, instead of nine invented placeholder names with no
    // per-generator data at all.
}
