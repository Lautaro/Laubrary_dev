// ShaperMockData — the mock's own in-memory data model. Deliberately NOT ShaperNode / ShaperFillDef / any
// other committed Shaper production runtime type (T-0129's ground rule): this file lives outside
// Laubrary.Shaper entirely, in its own ShaperMock.Editor namespace, so a production Shaper type cannot
// even be named here by accident.
//
// ShaperMockDocument is a ScriptableObject purely so Undo.RecordObject has a real UnityEngine.Object to
// record against — the same reason Pyre's own `spec` is one. It is created fresh in memory per window
// (ScriptableObject.CreateInstance), never written to disk, and discarded on domain reload or window
// close: the mock stays disposable, Undo still works exactly like it will on the real Shaper document.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShaperMock.Editor
{
    public sealed class ShaperMockDocument : ScriptableObject
    {
        public ShaperMockCanvas canvas = new ShaperMockCanvas();
        public List<ShaperMockLayer> layers = new List<ShaperMockLayer>();

        /// A fresh document seeded with one Primitive layer — the "single Primitive layer root" this
        /// slice demonstrates (T-0130 scope).
        public static ShaperMockDocument CreateSeeded()
        {
            var doc = CreateInstance<ShaperMockDocument>();
            doc.hideFlags = HideFlags.DontSave;
            doc.layers.Add(new ShaperMockLayer { name = "Layer 1" });
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
        // This slice hosts exactly one node kind (Primitive) per layer — Bag/Composite and the breadcrumb
        // actually drilling somewhere are out of this slice's scope (design doc §B2/§B3's fuller model).
        public ShaperMockNode root = new ShaperMockNode();
    }

    public enum ShaperMockShapeKind { Disc, Ngon, Star }

    [Serializable]
    public sealed class ShaperMockNode
    {
        public ShaperMockShapeKind shapeKind = ShaperMockShapeKind.Disc;

        // Disc
        public float discRadius = 0.6f;

        // N-gon
        public int ngonSides = 6;
        public float ngonRadius = 0.6f;
        public float ngonRotation = 0f;
        public float ngonCornerRadius = 0f;

        // Star
        public int starArms = 5;
        public float starRadius = 0.65f;
        public float starLength = 0.55f;
        public float starBaseWidth = 0.35f;
        public float starSkew = 0f;

        public ShaperMockFill fill = new ShaperMockFill();
    }

    public enum ShaperMockFillKind { Solid, Gradient }

    [Serializable]
    public sealed class ShaperMockFill
    {
        public ShaperMockFillKind kind = ShaperMockFillKind.Solid;
        public Color solidColor = new Color(0.35f, 0.7f, 1f, 1f);
        // Seeded eagerly, not lazily on first Gradient-mode use: Unity's serializer can silently promote a
        // C#-null Gradient field to a non-null, empty (white→white) default Gradient the moment ANYTHING
        // snapshots this object (confirmed live — Undo.RecordObject alone was enough to trigger it), which
        // defeats a `gradient ??= DefaultGradient()` lazy-seed the instant Undo has ever touched the
        // document. A real default from construction has nothing to race against.
        public Gradient gradient = DefaultGradient();
        public float gradientAngleDegrees;

        /// True when `gradient` is null OR is Unity's own silently-substituted empty default (no authored
        /// keys, or exactly the white→white pair Unity hands back for an empty Gradient) — the check
        /// EnsureGradient uses instead of a plain null-check, for the reason in the field comment above.
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
    }
}
