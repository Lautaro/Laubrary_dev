// PyrePlusSpec — the asset behind the PyrePlus prototype (see PYREPLUS_DESIGN.md).
//
// PyrePlus is a PARALLEL rework of Pyre's shape system, kept entirely separate from the shipping
// Pyre/BlastSpec/Layer/BlastRenderer so the real tool is never at risk. One implicit layer organized into
// three conceptual sections — Shape, Swarm, Modifiers — instead of Pyre's one flat ~40-field Layer. This is
// SLICE 1: the Shape section only (one particle). Swarm and Modifiers land in later slices; their fields are
// stubbed here so the data model and asmdef wiring are in place.
using System.Collections.Generic;
using Laubrary.Pyre;
using UnityEngine;

namespace Laubrary.PyrePlus
{
    [CreateAssetMenu(menuName = "Laubrary/Pyre Plus", fileName = "PyrePlus")]
    public class PyrePlusSpec : ScriptableObject
    {
        // ── canvas / timing (mirrors Pyre's own top-level fields) ─────────────────
        [Min(1)] public int canvasSize = 64;
        [Min(1)] public int frameCount = 16;
        public int seed = 1234;
        public Color background = new Color(0f, 0f, 0f, 0f);
        public float pixelsPerUnit = 16f;

        // ── Shape — the particle's own look (mandatory section) ────────────────────
        public Gradient colorOverLife = DefaultColor();
        public ZUIValue alpha = DefaultAlpha();     // over the particle's OWN life
        public ZUIValue size = DefaultSize();       // radius in pixels, over the particle's own life
        [Range(0f, 1f)] public float edgeSoftness = 0.4f;   // soft rim vs hard pixel edge

        // opt-in Shape fields (later slices): a per-particle position path, a 2D spin.

        // ── Swarm (later slice) ────────────────────────────────────────────────────
        public bool swarmEnabled = false;
        // count / spawn mode / shape kind / shape transform land here.

        // ── Modifiers — reuses Pyre's own PyreModifier directly, zero reimplementation ──
        [SerializeReference] public List<PyreModifier> modifiers = new List<PyreModifier>();

        // ── editor preview state (cosmetic; never affects the render) ──────────────
        [HideInInspector] public float previewZoom = 4f;
        [HideInInspector] public float previewFps = 12f;
        [HideInInspector] public int previewFrame = 0;

        public int Width => Mathf.Max(1, canvasSize);
        public int Height => Mathf.Max(1, canvasSize);

        static Gradient DefaultColor()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f),
                    new GradientColorKey(new Color(1f, 0.5f, 0.1f), 0.5f),
                    new GradientColorKey(new Color(0.5f, 0.1f, 0.05f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        static ZUIValue DefaultAlpha()
        {
            // fade in fast, hold, fade out — a particle's own life envelope.
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = 1f };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, 0f));
            v.points.Add(new ZUIEnvelopePoint(0.15f, 1f));
            v.points.Add(new ZUIEnvelopePoint(0.7f, 1f));
            v.points.Add(new ZUIEnvelopePoint(1f, 0f));
            return v;
        }

        static ZUIValue DefaultSize()
        {
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = 24f };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, 4f));
            v.points.Add(new ZUIEnvelopePoint(0.4f, 22f));
            v.points.Add(new ZUIEnvelopePoint(1f, 16f));
            return v;
        }
    }
}
