// ShaperMockGenerators — T-0137. The composite-generator catalog was wrong: it shipped nine INVENTED names
// (Blob, Sparkle, Mesh, Wisp, Halo, Bolt, Cloud, Ripple, Shard) instead of the REAL nine T-0112 already
// catalogued in the Shaper engine worktree (`PyreCompositeCatalog`, `D:\UNITY\Laubrary Dev - Shaper\Assets\
// Packages\Laubrary\Runtime\PyreShaper\PyreCompositeCatalog.cs`) — caught live by the project owner asking
// directly whether per-generator UI had been considered at all. It had not: the Composite card showed a
// name picker and nothing else, no matter which of the nine was selected, the same gap the effect catalog
// had before T-0135 grew it to real scale. This file fixes both: the real nine names + their REAL
// palette-indifferent/veil-multiplied classification (a sourced fact, not invented), and a small
// representative parameter set per generator (or per real Pyre-form FAMILY — Jet/Radial Jet/Explosive Jet
// share JetFormBase in the real engine, so they share one mock param class here too) so picking a generator
// visibly changes what you can author, mirroring how the effect catalog proves the same "picking X shows
// X's own dials" pattern via ZuiReflect.
//
// These are NOT the real Orb/Torch/ArcBurst/... field lists (that would mean reading five separate real
// Pyre form source files field-by-field, out of scope for a UI mock) — each class below is a plausible,
// clearly-labelled STAND-IN, the same posture ShaperMockEffect's generic entries already take for the
// effect catalog's own top-up entries.
using UnityEngine;

namespace ShaperMock.Editor
{
    public readonly struct ShaperMockGeneratorEntry
    {
        public readonly string DisplayName;
        public readonly bool PaletteIndifferent;
        public readonly string ReasonNote;
        public readonly System.Func<ShaperMockGeneratorParams> NewParams;

        public ShaperMockGeneratorEntry(string displayName, bool paletteIndifferent, string reasonNote,
            System.Func<ShaperMockGeneratorParams> newParams)
        {
            DisplayName = displayName;
            PaletteIndifferent = paletteIndifferent;
            ReasonNote = reasonNote;
            NewParams = newParams;
        }
    }

    [System.Serializable]
    public abstract class ShaperMockGeneratorParams { }

    // Orb / Torch / Arc Burst / Plasma Bloom — the four REAL palette-indifferent generators (their edge
    // rule has zero dependency on the palette, per PyreCompositeCatalog's own doc comment).
    [System.Serializable]
    public sealed class MockOrbParams : ShaperMockGeneratorParams
    {
        [Range(0f, 1f)] public float energy = 0.6f;
        [Range(0, 255)] public int lutIndex = 128;
        [Range(0f, 1f)] public float glowSharpness = 0.4f;
    }

    [System.Serializable]
    public sealed class MockTorchParams : ShaperMockGeneratorParams
    {
        [Range(0f, 1f)] public float cooling = 0.5f;
        [Range(0f, 2f)] public float flameHeight = 1f;
        [Range(0f, 1f)] public float flicker = 0.3f;
    }

    [System.Serializable]
    public sealed class MockArcBurstParams : ShaperMockGeneratorParams
    {
        [Range(1, 12)] public int arcCount = 5;
        [Range(0f, 360f)] public float burstSpread = 120f;
        [Range(0f, 1f)] public float jitter = 0.25f;
    }

    [System.Serializable]
    public sealed class MockPlasmaBloomParams : ShaperMockGeneratorParams
    {
        [Range(0f, 1f)] public float bloomRadius = 0.5f;
        [Range(0f, 2f)] public float turbulence = 0.8f;
        [Range(-1f, 1f)] public float swirl = 0f;
    }

    // Inferno / Fork Blast / Jet / Radial Jet / Explosive Jet — the five REAL veil-multiplied generators
    // (the palette's transparency multiplies into an edge rule the generator computed on its own).
    [System.Serializable]
    public sealed class MockInfernoParams : ShaperMockGeneratorParams
    {
        [Range(0f, 1f)] public float veilStrength = 0.7f;
        [Range(0f, 2f)] public float turbulence = 1f;
        [Range(0f, 4f)] public float heatFalloff = 1.5f;
    }

    [System.Serializable]
    public sealed class MockForkBlastParams : ShaperMockGeneratorParams
    {
        [Range(1, 8)] public int forkCount = 3;
        [Range(0f, 2f)] public float blastRadius = 0.8f;
        [Range(0f, 180f)] public float spreadAngle = 45f;
    }

    /// Jet, Radial Jet and Explosive Jet share this one param class — the real engine's own JetFormBase
    /// family (PyreCompositeCatalog: "same JetFormBase family as Jet").
    [System.Serializable]
    public sealed class MockJetParams : ShaperMockGeneratorParams
    {
        [Range(0f, 4f)] public float jetLength = 2f;
        [Range(0f, 1f)] public float rampHeat = 0.6f;
        [Range(0f, 180f)] public float spread = 20f;
    }

    public static class ShaperMockCompositeCatalog
    {
        const string ProceduralNote = "Procedural generator — an energy field / turbulence sum / LUT read, "
            + "never a baked sprite — so it's still NotYetSplit (reason 2), not authored data (reason 1).";

        /// The REAL nine (T-0112's PyreCompositeCatalog.All), in the same order, with the REAL
        /// palette-indifferent/veil-multiplied split. Representative (not real) per-generator dials.
        public static readonly ShaperMockGeneratorEntry[] All =
        {
            new ShaperMockGeneratorEntry("Inferno", false, ProceduralNote + " Palette-dependent (veil-multiplied).",
                () => new MockInfernoParams()),
            new ShaperMockGeneratorEntry("Fork Blast", false, ProceduralNote + " Palette-dependent (veil-multiplied).",
                () => new MockForkBlastParams()),
            new ShaperMockGeneratorEntry("Orb", true, ProceduralNote + " Palette-indifferent.",
                () => new MockOrbParams()),
            new ShaperMockGeneratorEntry("Torch", true, ProceduralNote + " Palette-indifferent.",
                () => new MockTorchParams()),
            new ShaperMockGeneratorEntry("Arc Burst", true, ProceduralNote + " Palette-indifferent.",
                () => new MockArcBurstParams()),
            new ShaperMockGeneratorEntry("Plasma Bloom", true, ProceduralNote + " Palette-indifferent.",
                () => new MockPlasmaBloomParams()),
            new ShaperMockGeneratorEntry("Jet", false, ProceduralNote + " Palette-dependent (veil-multiplied; JetFormBase family).",
                () => new MockJetParams()),
            new ShaperMockGeneratorEntry("Radial Jet", false, ProceduralNote + " Palette-dependent (veil-multiplied; JetFormBase family).",
                () => new MockJetParams()),
            new ShaperMockGeneratorEntry("Explosive Jet", false, ProceduralNote + " Palette-dependent (veil-multiplied; JetFormBase family).",
                () => new MockJetParams()),
        };

        /// A generator "publishes sheets" for the effect catalog's NeedsSheets gate when it is
        /// palette-indifferent — a real, sourced stand-in relationship (an edge rule computed independently
        /// of the palette is the kind of generator likely to publish its own edge-distance/heat data),
        /// replacing the earlier `index % 2 == 0` placeholder that had no basis at all.
        public static bool PublishesSheets(int generatorIndex)
            => generatorIndex >= 0 && generatorIndex < All.Length && All[generatorIndex].PaletteIndifferent;
    }
}
