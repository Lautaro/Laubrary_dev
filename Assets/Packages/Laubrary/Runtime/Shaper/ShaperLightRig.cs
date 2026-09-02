using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>APPEND-ONLY: serialized as an int. LR-1.2.</summary>
    public enum ShaperLightKind { Directional = 0, Point = 1 }

    /// <summary>
    /// One authored light. LR-1.2.
    ///
    /// Every scalar is a <see cref="ZUIValue"/> sampled ONCE at compile through
    /// <see cref="ShaperValue.Sample"/> (LR-1.8), exactly like <c>ShaperBorderDef.width</c>. The colour is a
    /// plain <see cref="Color"/> and deliberately does NOT animate: an animated colour wants a gradient, a
    /// gradient wants a <c>t</c>, and a light has no spatial parameterisation to give it one (LR-1.8).
    /// </summary>
    [Serializable]
    public class ShaperLight
    {
        public string name = "Light";
        public bool enabled = true;
        public ShaperLightKind kind = ShaperLightKind.Directional;

        /// <summary>
        /// Authored in sRGB (that is what Unity's picker gives). Decoded ONCE at compile through
        /// <c>ShaperSrgb.Decode</c> (<c>ShaperFillContract.cs:436</c>). Alpha is NOT read — FC-2.2's rule,
        /// applied to a light.
        ///
        /// <b>There is no second, specular colour on a light (LR-1.2).</b> Pyre carries one
        /// (<c>gemSpecularFill</c>, <c>Pyre.cs:368</c>) and it is the same double-meaning defect
        /// <c>ShaperBlend</c> and <c>ShaperSweep</c> were split to remove: one lamp showing two colours with
        /// no rule saying which is the lamp. The surface half of a highlight's colour lives on the receiver,
        /// at <see cref="ShaperLightResponse.specularTint"/> (LR-4.4).
        /// </summary>
        public Color colour = Color.white;

        /// <summary>
        /// Multiplies the light's contribution. Animatable.
        /// Defaults to <c>0.943 = 0.82 * 1.15</c> — the exact value that reproduces Pyre's relief-light
        /// contrast remap when paired with <see cref="ShaperLightRig.ambientIntensity"/> (LR-1.7).
        /// </summary>
        public ZUIValue intensity = new ZUIValue(0.943f);

        /// <summary>Directional: where the light comes FROM, in the canvas frame (LR-1.5). Degrees.</summary>
        public ZUIValue yaw = new ZUIValue(-55f);

        /// <summary>
        /// Directional pitch, degrees. Default 36 sits between the two lineages this reconciles: Pyre's solids
        /// default their key pitch to 38 degrees (<c>Pyre.cs:352</c>) and <c>PyreField.ReliefLight</c>'s
        /// hard-wired elevation <c>lz = 0.72</c> is <c>atan(0.72) = 35.75</c> degrees
        /// (<c>PyreField.cs:192</c>) — 2.3 degrees apart, which is LR-0.4's evidence that one rig is
        /// achievable rather than aspirational.
        /// </summary>
        public ZUIValue pitch = new ZUIValue(36f);

        /// <summary>Point: absolute canvas position, in canvas pixels, +Z toward the viewer (LR-1.5).</summary>
        public ZUIValue posX = new ZUIValue(0f);
        /// <summary>Point: absolute canvas position, in canvas pixels (LR-1.5).</summary>
        public ZUIValue posY = new ZUIValue(0f);

        /// <summary>
        /// 40 px out of the screen. With <see cref="range"/> also 40 the attenuation at the canvas centre is
        /// exactly <c>1/(1+1) = 0.5</c> — the number LT-6 hand-checks (LR-1.5).
        /// </summary>
        public ZUIValue posZ = new ZUIValue(40f);

        /// <summary>
        /// Point only. Canvas pixels. The distance at which attenuation reaches 1/2 (LR-2.4).
        /// <b>There is deliberately no falloff-exponent dial</b> — a range and an exponent both control "how
        /// fast it dies", and two dials for one quantity is the defect LR-1.2 refuses. A harder falloff is a
        /// second light at a shorter range, which is one authored object rather than one authored ambiguity.
        /// </summary>
        public ZUIValue range = new ZUIValue(40f);

        /// <summary>
        /// Blinn-Phong highlight strength for THIS light. The highlight's COLOUR is the light's colour times
        /// the receiving layer's <see cref="ShaperLightResponse.specularTint"/> (LR-4.4) — a light does not
        /// carry a second colour.
        /// </summary>
        public ZUIValue specular = new ZUIValue(0.9f);
    }

    /// <summary>
    /// The document's one light rig: a document ambient plus an ordered list of at most
    /// <see cref="MaxLights"/> lights (LR-1.1, LR-1.4).
    ///
    /// <b>No light is owned by a node, a layer, a fill, a border or a generator, and no stage may add one at
    /// render time.</b> The failure that rule prevents is the one Pyre already has: <c>gemAmbient</c> is a
    /// per-layer float (<c>Pyre.cs:358</c>) and <c>ReliefLight</c>'s ambient is a hard-wired default argument
    /// (<c>PyreField.cs:192</c>), so two layers in one picture are lit by two ambients that no author chose
    /// and no control shows. That is the "not one scene" defect at its smallest, and one rig is the whole fix.
    ///
    /// Flat and <c>[Serializable]</c>, with no <c>SerializeReference</c> hierarchy — the same shape
    /// <c>ShaperFillDef</c> and <c>ShaperBorderDef</c> take, for the reason <c>ShaperBorderDef.cs:6-11</c>
    /// gives: it does not nest, and a managed-reference hierarchy imports the nulls-against-a-broken-assembly
    /// hazard this project has already been bitten by.
    /// </summary>
    [Serializable]
    public class ShaperLightRig
    {
        /// <summary>
        /// LR-1.4. The cap is what lets the compiled rig be a fixed-size blittable struct taken by <c>in</c>
        /// with no managed array inside the parameter block (BC-1.2's fourth bullet). Eight rather than four
        /// (which would refuse a key + fill + two accents, an ordinary setup) or sixteen (which doubles every
        /// document's floor cost for a case nobody has asked for). Changing it touches this constant, the
        /// compiled struct and one test.
        /// </summary>
        public const int MaxLights = ShaperLightRigCompiled.MaxLights;

        /// <summary>
        /// LR-1.3 — exactly ONE ambient term exists, here, on the rig. A light carries no ambient.
        ///
        /// Per-light ambient means N lights sum to N ambients, so adding a second lamp brightens the shadows
        /// for no authored reason and switching a lamp off darkens shadows that lamp was never pointed at.
        /// Both read as bugs and neither is undoable without touching a dial on a different object.
        /// </summary>
        public Color ambientColour = Color.white;

        /// <summary>
        /// LR-1.3 / LR-1.7. Default <c>0.18</c> is <c>PyreField.ReliefLight</c>'s own ambient default
        /// (<c>PyreField.cs:192</c>), carried with its provenance per BC-3.6.
        ///
        /// <b>Reproducing Pyre's relief contrast remap (LR-1.7).</b> Pyre applies a second gain downstream of
        /// <c>ReliefLight</c> at two sites — <c>Clamp01(value * (0.35f + light[i] * 1.15f))</c>
        /// (<c>PyreRenderer.cs:1821</c>) and <c>float shade = 0.35f + light[i] * 1.15f</c> (<c>:1874</c>) —
        /// on top of <c>ReliefLight</c>'s own <c>ambient + ndl*gain</c> (<c>PyreField.cs:208</c>). That is two
        /// dials for one quantity and is NOT carried across. It is exactly reproducible, which is why
        /// dropping it costs nothing: <c>0.35 + (0.18 + ndl*0.82)*1.15 = 0.557 + ndl*0.943</c>, so a ported
        /// Ramp or heightmap look is recovered by setting <c>ambientIntensity = 0.557</c> and
        /// <c>light.intensity = 0.943</c> on a directional light with <c>atten == 1</c>.
        /// </summary>
        public ZUIValue ambientIntensity = new ZUIValue(0.18f);

        /// <summary>Authored order, and no stage may reorder it (LR-2.3). At most <see cref="MaxLights"/>.</summary>
        public List<ShaperLight> lights = new List<ShaperLight>();

        // ── LR-7.2: the limitation sentences ──────────────────────────────────────────────────────────────
        //
        // LR-7.1: `public const string` fields, not XML doc comments, not a README, not a tooltip. Wave 2
        // ships no window (F8.6), so the sentence has to be FETCHABLE VERBATIM AT RUNTIME by whichever task
        // builds one. The shipped code already establishes exactly this pattern:
        // ShaperQuantities.SurfaceDirectionRefusal (ShaperFillContract.cs:140) and SubtractRefusal (:152) are
        // both const-string refusal sentences authored for a UI that does not exist yet. Each is also the
        // string a diagnostic carries, so there is one sentence per fact and not two that can drift.
        //
        // LR-7.3: a future window MUST show ConsistentNotIdentical where both families are present, and MUST
        // attach each of the other three to the SPECIFIC CONTROL it qualifies rather than pooling them into a
        // help panel. That is the difference between a stated limitation and a buried one, and it is the same
        // standard FC-4.4 sets for the availability gate.

        /// <summary>
        /// B8's honest limitation, stated up front "because it will otherwise be reported as a bug"
        /// (<c>SHAPER_THE_DESIGN.md:194</c>). The UI shows this wherever a document contains both families.
        /// </summary>
        public const string ConsistentNotIdentical =
            "Silhouette and Solids are lit by the same law, the same lights and the same ambient, so they read " +
            "as being in one scene. They will not match pixel for pixel. Silhouette shades a height field and " +
            "Solids shades real geometry, and a sphere and an extruded disc genuinely differ under the same " +
            "light. Consistent, not identical, is the promise.";

        /// <summary>
        /// LR-2.5 / LR-4.6. Shown on the Rim Strength control whenever the layer's surface direction is flat.
        ///
        /// <b>EXTENDED beyond LR-7.2's authored wording, and the reason.</b> LR-7.2 mandated only the
        /// flat-normal half. Rim has a SECOND state in which it is wholly inert and the contract did not name
        /// it: rim is ambient-tinted by LR-2.3's own arithmetic, so on a BLACK ambient the term is exactly
        /// zero at every rim strength (measured <c>S = 0</c> at <c>rimStrength = 5</c>, against
        /// <c>S = 0.06361176</c> at ambient 0.2). "Only my lamps, no ambient" is an ordinary authoring choice
        /// and LR-7.3's standard — "a control that silently does nothing is the failure B4 says must not
        /// survive the rebuild" — applies to it just as much. The second sentence discharges that; the
        /// <see cref="RimNeedsBlackAmbientRelief"/> sibling is the version a UI attaches when it can tell
        /// WHICH of the two states the layer is in, and is the string
        /// <c>ShaperLightProgram.inertRimReason</c> carries verbatim.
        /// </summary>
        public const string RimNeedsRelief =
            "Rim strength has no effect on a layer whose surface direction is flat, because a flat surface " +
            "never faces the viewer edge-on. It appears on Solids immediately, and on Silhouette once a layer " +
            "has relief. It also has no effect while the document ambient is black, because rim is tinted by " +
            "the ambient: it stands for grazing light from everywhere, and a document with no ambient has " +
            "none to give it.";

        /// <summary>
        /// LR-2.5 / LR-7.3. The black-ambient half of <see cref="RimNeedsRelief"/>, on its own, for a UI that
        /// knows the document's ambient is black and can therefore name the actual cause rather than both.
        /// Carried verbatim by <c>ShaperLightProgram.inertRimReason</c> (LR-7.1: one sentence per fact).
        /// </summary>
        public const string RimNeedsBlackAmbientRelief =
            "Rim strength has no effect while the document ambient is black, because rim is tinted by the " +
            "ambient: it stands for grazing light from everywhere, and a document with no ambient has none " +
            "to give it. Raise the document ambient, or leave rim at zero.";

        /// <summary>LR-4.5. Shown on the Cast Shadows and Receive Shadows controls whenever either is ticked.</summary>
        public const string ShadowsNotComputed =
            "Cast and receive shadows are saved with the document, but no shadow is computed yet. The picture " +
            "is resolved by looking straight down, and a shadow needs a ray pointed at the light.";

        /// <summary>LR-3.2. Shown on the Surface Direction control while the only provider is Constant.</summary>
        public const string SilhouetteIsFlatUntilExtrusion =
            "This layer's surface faces one fixed direction. Lighting, falloff and colour are real and match " +
            "the rest of the document, but there is no relief to catch a highlight until the layer is extruded.";
    }

    /// <summary>
    /// The per-layer response block (LR-4.1). Every LAYER carries exactly one; a node does not carry one, a
    /// fill does not carry one, and a border does not carry one (LR-5.4 — a border is lit by its HOST's).
    /// </summary>
    [Serializable]
    public class ShaperLightResponse
    {
        // ── the four the design names (SHAPER_THE_DESIGN.md:192) ──

        /// <summary>
        /// LR-4.3. <c>false</c> produces <c>L = (1,1,1)</c> and <c>S = (0,0,0)</c>: the albedo is written
        /// through unchanged, BIT FOR BIT, which is testable (LT-8) and is why it is not a silent no-op.
        /// </summary>
        public bool receiveLighting = true;

        /// <summary>
        /// LR-4.2. Multiplies each light's diffuse and specular contribution. Does NOT scale ambient (LR-1.3)
        /// or rim (LR-2.3). <c>0</c> means the layer is lit by ambient and rim only — <i>in shadow</i> — which
        /// is a different and reachable state from <see cref="receiveLighting"/> off, and the difference is
        /// the point of having both.
        /// </summary>
        public ZUIValue intensityScale = new ZUIValue(1f);

        /// <summary>
        /// LR-4.5 — authored, serialized and persisted; changes no pixel in Wave 2; absent from
        /// <see cref="ShaperResponseCompiled"/> so the law cannot see it. Setting it raises
        /// <c>hasUnimplementedShadow</c> carrying <see cref="ShaperLightRig.ShadowsNotComputed"/> verbatim.
        ///
        /// <b>Why not implement them.</b> Not merely unscheduled — unimplementable on v1's resolve. BC-2.3:
        /// "v1 implements exactly one ray direction: straight down, orthographic... There is no march, no
        /// acceleration structure and no camera in v1". A shadow test is by definition a ray from a surface
        /// point TOWARD a light, which is straight down only for the one light configuration that casts no
        /// interesting shadow.
        ///
        /// <b>Why carry the flags at all.</b> Omitting them means the document format changes when shadows
        /// land, so every document authored between now and then silently loses the author's intent, and the
        /// eventual task has a migration instead of an addition.
        /// </summary>
        public bool castShadows = false;

        /// <summary>LR-4.5 — see <see cref="castShadows"/>.</summary>
        public bool receiveShadows = false;

        /// <summary>
        /// LR-4.6. Defaults to 0 — OFF — because a rim is an authored look rather than a physical default, and
        /// a non-zero default would put a halo on every layer of every existing document the moment the rig
        /// lands.
        /// </summary>
        public ZUIValue rimStrength = new ZUIValue(0f);

        // ── three added here beyond the design's four, flagged rather than smuggled (LR-4.4) ──
        //
        // Without them the Solids port is lossy in a visible way: gemSpecular (Pyre.cs:363, default 0.9),
        // gemSpecPower (:367, default 48) and gemSpecularFill (:368, default (0.9,0.95,1)) have no home and
        // the Blinn-Phong hotspot that is the entire visual signature of a Pyre gem disappears.
        //
        // They are on the LAYER and not on the LIGHT because a specular exponent is a statement about how
        // rough the SURFACE is and a highlight tint is a statement about whether the surface is a dielectric
        // (highlight takes the light's colour) or a metal (highlight takes the surface's). On the light, two
        // lights in one document could not disagree about a surface's roughness, which is exactly backwards.
        //
        // This does not violate FC-8.1's refusal of a "reflective or metallic dial on any fill": F8.1 refuses
        // such a dial ON A FILL, quoting B4's "Reflective, shiny, bevelled and lit are not fills. They are
        // what happens AFTER a fill". These are not on a fill. They are on the layer's lighting response —
        // precisely the "after a fill" slot B4 points at, which the design itself calls "a small per-layer
        // response block". Recorded because the boundary is thin and someone will test it.

        /// <summary>LR-4.4. <c>Pyre.cs:363</c>'s default.</summary>
        public ZUIValue specular = new ZUIValue(0.9f);

        /// <summary>LR-4.4. <c>Pyre.cs:367</c>'s default.</summary>
        public ZUIValue specularPower = new ZUIValue(48f);

        /// <summary>
        /// LR-4.4. <c>Pyre.cs:368</c>'s default. A flat <see cref="Color"/>, so a SPATIALLY VARYING specular
        /// tint does not survive the port (<c>gemSpecularFill</c> is a full <c>ZuiFill</c> and can be Linear,
        /// Radial or Noise, evaluated per pixel at <c>PyreRenderer.cs:4325</c>). The recovery is an ordinary
        /// Shaper fill on a second node, not a re-added <c>ZuiFill</c>.
        /// </summary>
        public Color specularTint = new Color(0.9f, 0.95f, 1f);

        // ── the surface-direction provider for this layer (LR-3.1) ──

        /// <summary>LR-3.1 / LR-3.2. Wave 2's only Silhouette provider is <c>Constant</c>.</summary>
        public ShaperNormalKind normalKind = ShaperNormalKind.Constant;

        /// <summary>
        /// The authored constant direction, normalised at compile (LR-3.5). <c>(0,0,1)</c> — facing the
        /// viewer — is the default and is also the degenerate answer a provider writes when it cannot produce
        /// a direction.
        /// </summary>
        public Vector3 normalConstant = new Vector3(0f, 0f, 1f);

        /// <summary>
        /// LR-2.5. Default <c>2.2</c> is not a taste: the reference app's Fresnel term is
        /// <c>pow(1 - clamp01(normalZ), 2.2)</c> (<c>index.html:1510</c>, quoted at
        /// <c>BUFFER_CONTRACT.md:246</c>), and with <c>V = (0,0,1)</c> — v1's only view direction —
        /// <c>N.V</c> IS <c>nz</c>, so the two expressions are identical and adopting 2.2 is a reconciliation
        /// rather than a guess.
        /// </summary>
        public ZUIValue rimPower = new ZUIValue(2.2f);
    }

    /// <summary>
    /// One layer: a shape-tree root plus the one lighting response block LR-4.1 gives it.
    ///
    /// LR-0.1 records that no <c>ShaperLayer</c> type existed before T-0108 — "a layer" was one
    /// <see cref="ShaperNode"/> root handed to <c>ShaperFillResolver.Resolve</c>, and FC-3.2's "layer root"
    /// means exactly that. This type adds the response block to that root and nothing else.
    /// </summary>
    [Serializable]
    public class ShaperLayer
    {
        public string name = "Layer";
        public bool enabled = true;
        [SerializeReference] public ShaperNode root;
        public ShaperLightResponse response = new ShaperLightResponse();

        /// <summary>
        /// T-0170 — this layer's STABLE identity within its document, and the only thing another layer's
        /// <see cref="mask"/> may point at. <b>0 means "not yet allocated"</b>; ids are handed out from 1 by
        /// <see cref="ShaperDocument.IdOf"/> at the moment a reference to the layer is first made, so a
        /// document nobody has masked serialises exactly as it did before this field existed.
        ///
        /// It exists because the two obvious alternatives are both broken: a NAME is a typed reference (the
        /// project's standing rule against those) that breaks on rename and matches the wrong layer when two
        /// share a name, and a list INDEX re-points itself every time the author drags the list, which is
        /// authored data specifically meant to be dragged.
        /// </summary>
        public int id = 0;

        /// <summary>
        /// T-0170 — which OTHER layer of this document cuts this one, and how. Default is an unset mask
        /// (<c>sourceLayerId == 0</c>), which the renderer skips entirely, so every existing document renders
        /// bit-identically.
        /// </summary>
        public ShaperLayerMask mask = new ShaperLayerMask();

        /// <summary>
        /// T-0170 — false makes this layer a PURE MASK: it still resolves, and other layers may still read it
        /// as a mask source, but it never composites into the picture.
        ///
        /// Without it a masking shape has to be drawn to be usable, so every mask arrives with its own stencil
        /// visibly painted over the picture. Hiding it with <see cref="enabled"/> instead would be wrong and
        /// is deliberately NOT what that flag does: a disabled layer is OFF, including as a mask source, which
        /// is the answer an author expects from an eye/enable toggle.
        /// </summary>
        public bool contributesToPicture = true;

        /// <summary>
        /// T-0109, HS-7.1 — this layer's Z POSITION offset, in canvas pixels, <b>SIGNED</b>, added to the
        /// ordering base <c>layerIndex × <see cref="ShaperDocument.layerSpacing"/></c> to give the layer's
        /// base plane (HS-7.2, computed by <see cref="ShaperHeightCompiler.LayerBase"/>).
        ///
        /// Sampled once per compile through <see cref="ShaperValue.Sample"/> on the DOCUMENT's phase, like
        /// every other layer-level dial (LR-1.8).
        ///
        /// <b>Not to be confused with <see cref="ShaperHeightDef.depth"/>, which is THICKNESS</b> — HS-7.4
        /// records the naming trap in full at that field. Thickness is never negative; this is, and must be:
        /// an offset that cannot go negative can only push layers apart in one direction.
        ///
        /// <b>No range restriction, and that is a decision rather than an omission.</b> The reference app
        /// would need one, because its height buffer's empty-cell sentinel (<c>−9999</c>, tested against
        /// <c>−900</c>) becomes REACHABLE the moment a signed offset exists, and a layer pushed far enough
        /// back would vanish rather than go behind. Shaper carries occupancy in <c>coverage</c>, so there is
        /// no sentinel to collide with — HS-7.3.
        /// </summary>
        public ZUIValue zOffset = new ZUIValue(0f);

        /// <summary>
        /// T-0109 — this layer's HEIGHT STAGE (extrusion profile + bevel + thickness), or <c>null</c> when the
        /// layer is a flat silhouette. HS-1.4's "and NOT when it is absent" is the null case, not a
        /// zero-<c>depth</c> stage: a null here compiles to <see cref="ShaperHeightOp.present"/> false and the
        /// layer publishes exactly <c>ShaperQuantitySet.ShippedShapeEngine</c>.
        ///
        /// <b>T-0109 FIX F4a added this field, and its absence was the wiring gap.</b> The height stage was
        /// built, audited and rendered by the audit's own fixtures, but no authored object referred to it, so
        /// <see cref="ShaperHeight.FillTile"/> had no caller in the shipping path and FC-2.5's
        /// <c>height_final = height_shape + heightDelta·coverageEff</c> was still <c>0 + …</c>. Compiled by
        /// <see cref="ShaperLightCompiler.BindLayer"/>, which is where the layer's <c>base</c> (HS-7.2) is
        /// already known.
        ///
        /// <c>null</c> by default, so every existing document renders bit-identically to before.
        /// </summary>
        [SerializeReference] public ShaperHeightDef height;

        /// <summary>
        /// T-0166 — this layer's lifetime window, first frame INCLUSIVE. Honoured by
        /// <see cref="ShaperDocumentRenderer"/>: a frame outside <see cref="startFrame"/>..<see cref="endFrame"/>
        /// contributes nothing from this layer, exactly as a disabled layer does. Default 0 so an existing
        /// document's window is the whole animation and nothing changes until authored.
        /// </summary>
        public int startFrame = 0;

        /// <summary>Last frame INCLUSIVE, or <b>-1</b> for "the document's last frame" — matching Pyre's own
        /// convention (<c>Pyre.cs:197-199</c>) so a window never needs updating when <c>frameCount</c> changes.</summary>
        public int endFrame = -1;

        /// <summary>
        /// T-0163 — this layer's PRE-COMPOSITE effects, applied to the layer's own resolved picture before it
        /// composites into the document. The document's own list
        /// (<see cref="ShaperDocument.effects"/>) is the post-composite half; an entry's stage is which of the
        /// two lists holds it and nothing else (<c>ShaperEffects.cs</c> header).
        ///
        /// This is what Pyre's per-layer modifier stack is, and what design C2 promised. It is not free: a
        /// layer carrying any enabled effect leaves the premultiplied-float composite for one 8-bit round trip
        /// (<see cref="ShaperDocumentRenderer.RenderPhaseInto"/> documents the cost). Empty by default, and an
        /// empty list takes the old float path exactly, so every existing document is bit-identical.
        /// </summary>
        public List<ShaperEffectRef> effects = new List<ShaperEffectRef>();

        /// <summary>
        /// Deep copy for the layer list's Duplicate action (T-0166), so the copy shares no mutable reference
        /// with its source. <b>Deliberately NOT a JsonUtility round-trip</b>, even though that is the shape
        /// Pyre's own layer-duplicate task description suggests: JsonUtility does not serialize
        /// <c>[SerializeReference]</c> fields at all — <see cref="ShaperNode.children"/>,
        /// <see cref="ShaperCompositeDef.source"/>, this layer's own <see cref="root"/> and <see cref="height"/>
        /// are every one of them <c>[SerializeReference]</c>, and ZUIValue's own clipboard code already
        /// documents the failure mode a JsonUtility round-trip hits here: it "does not serialize managed
        /// references at all" (<c>ZUIValue.cs:63-64</c>) — a JsonUtility clone of this layer would silently
        /// come back with an EMPTY shape tree. <see cref="ShaperDeepClone"/> walks the real object graph
        /// instead, so it clones a <c>[SerializeReference]</c> polymorphic field by its actual runtime type
        /// with no special-casing needed, and shares (never clones) any <c>UnityEngine.Object</c> asset
        /// reference it meets along the way, matching every other "Duplicate" in the codebase.
        /// </summary>
        /// <remarks>
        /// T-0170 — the copy's own <see cref="id"/> is CLEARED, so the duplicate is a new layer rather than a
        /// second layer claiming the original's identity (which would make every mask naming the original
        /// resolve to whichever of the two the lookup reached first). Its <see cref="mask"/> reference is
        /// deliberately kept: a duplicate should be masked by the same source its original was.
        /// </remarks>
        public ShaperLayer Clone()
        {
            var copy = ShaperDeepClone.Clone(this);
            copy.id = 0;
            return copy;
        }
    }

    /// <summary>
    /// A small generic deep-clone over a plain C# object graph, built for <see cref="ShaperLayer.Clone"/> —
    /// see that method's doc for why a JsonUtility round-trip cannot do this job. Value types (structs) and
    /// strings are shared/copied by value (this engine's authored structs — Vector2, Color, ZUIEnvelopePoint —
    /// carry no reference fields of their own); Lists and arrays are rebuilt element-by-element; every other
    /// reference field is recursively cloned via its OWN <c>MemberwiseClone</c> plus field reflection, keyed
    /// by the object's ACTUAL runtime type so a <c>[SerializeReference]</c> polymorphic field clones correctly
    /// with no per-type special case. A <c>UnityEngine.Object</c> (an asset reference) is shared, never cloned.
    /// </summary>
    static class ShaperDeepClone
    {
        public static T Clone<T>(T obj) where T : class
            => (T)CloneValue(obj, new Dictionary<object, object>());

        static object CloneValue(object obj, Dictionary<object, object> seen)
        {
            if (obj == null) return null;
            if (obj is UnityEngine.Object || obj is string) return obj;   // shared, never cloned

            var type = obj.GetType();
            if (type.IsValueType) return obj;   // struct: no reference fields in this engine's authored data

            if (seen.TryGetValue(obj, out var already)) return already;

            if (type.IsArray)
            {
                var srcArr = (Array)obj;
                var elemType = type.GetElementType();
                var dstArr = Array.CreateInstance(elemType, srcArr.Length);
                seen[obj] = dstArr;
                for (int i = 0; i < srcArr.Length; i++)
                    dstArr.SetValue(elemType.IsValueType ? srcArr.GetValue(i) : CloneValue(srcArr.GetValue(i), seen), i);
                return dstArr;
            }

            if (obj is System.Collections.IList list)
            {
                var dstList = (System.Collections.IList)Activator.CreateInstance(type);
                seen[obj] = dstList;
                var elemType = type.IsGenericType ? type.GetGenericArguments()[0] : typeof(object);
                foreach (var item in list)
                    dstList.Add(elemType.IsValueType ? item : CloneValue(item, seen));
                return dstList;
            }

            var clone = MemberwiseCloneMethod.Invoke(obj, null);
            seen[obj] = clone;
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                                              | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (f.FieldType.IsValueType || f.FieldType == typeof(string)) continue;
                    var val = f.GetValue(obj);
                    if (val != null) f.SetValue(clone, CloneValue(val, seen));
                }
            }
            return clone;
        }

        static readonly System.Reflection.MethodInfo MemberwiseCloneMethod =
            typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    }
}
