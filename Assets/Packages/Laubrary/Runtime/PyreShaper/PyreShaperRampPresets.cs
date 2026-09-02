// PyreShaperRampPresets.cs — T-0172, the ramp-preset picker for ShaperFillKind.RampByQuantity.
//
// Runtime/Shaper cannot reference Pyre (the same rule PyreShaperEval already lives under — see its own class
// doc). Pyre's ~35 shipped ramps live in PyreRampPresets (Runtime/Pyre/PyreShade.cs:339-644) as PyreRamp
// factories. This bridge lives in Runtime/PyreShaper, which CAN reference Pyre, and converts each preset's
// stops into a plain ZuiGradient — a REFERENCE for the picker, never a copy of the data into Shaper's own
// contract: applying a preset writes the converted ZuiGradient onto ShaperFillDef.rampGradient by VALUE, so
// once picked it is fully authored, editable and detached from Pyre. No reflection: PyreRampPresets' public
// surface mixes parameterless PyreRamp factories with helper overloads taking a string, so a signature-based
// reflection scan would still need a name allow-list to exclude the latter — this table IS that allow-list,
// written directly rather than filtered out of GetMethods().
//
// The conversion is a DELIBERATE APPROXIMATION, stated rather than hidden: PyreShade evaluates a ramp's stops
// through PyreRampSpace (LinearLight or Srgb blending, PyreShade.cs:138-164), while a plain UnityEngine.Gradient
// interpolates in whatever space Unity's own Gradient uses. So a converted preset is a faithful STARTING POINT
// — same stops, same positions, same colours — not a byte-identical re-render of the Pyre ramp. That is
// adequate for a picker whose whole job is "start here and keep tuning", and exact colorimetric parity is not
// what T-0172 asked for.

using System.Collections.Generic;
using Laubrary.Pyre;
using UnityEngine;

namespace Laubrary.PyreShaper
{
    /// <summary>See file header. Discovered once as a fixed table, not per-frame and not by reflection.</summary>
    public static class PyreShaperRampPresets
    {
        /// <summary>One named preset: a human label and the Pyre factory that builds its <see cref="PyreRamp"/>.</summary>
        public readonly struct Preset
        {
            public readonly string name;
            public readonly System.Func<PyreRamp> factory;
            public Preset(string name, System.Func<PyreRamp> factory) { this.name = name; this.factory = factory; }
        }

        /// <summary>
        /// Every preset this bridge exposes, grouped as Pyre itself groups them (Ember/soot, Jet, Plasma, Orb,
        /// Torch). 31 entries — PyreShade's own ~35 count includes a few name aliases (e.g. Jet's VIOLET ==
        /// WYRM) this table does not duplicate.
        /// </summary>
        public static readonly Preset[] All =
        {
            new Preset("Ember", PyreRampPresets.Ember),
            new Preset("Ember Soot", PyreRampPresets.EmberSoot),

            new Preset("Jet — Torch", PyreRampPresets.JetTorch),
            new Preset("Jet — Dirty", PyreRampPresets.JetDirty),
            new Preset("Jet — Dirty Soot", PyreRampPresets.JetDirtySoot),
            new Preset("Jet — Gold", PyreRampPresets.JetGold),
            new Preset("Jet — Gold Heat", PyreRampPresets.JetGoldHeat),
            new Preset("Jet — Wyrm", PyreRampPresets.JetWyrm),
            new Preset("Jet — Burner", PyreRampPresets.JetBurner),
            new Preset("Jet — Burner Tips", PyreRampPresets.JetBurnerTips),
            new Preset("Jet — Whirl", PyreRampPresets.JetWhirl),
            new Preset("Jet — Solar", PyreRampPresets.JetSolar),
            new Preset("Jet — Ghost", PyreRampPresets.JetGhost),
            new Preset("Jet — Cordite", PyreRampPresets.JetCordite),
            new Preset("Jet — Cordite Soot", PyreRampPresets.JetCorditeSoot),
            new Preset("Jet — Toxic", PyreRampPresets.JetToxic),

            new Preset("Plasma — Ion", PyreRampPresets.PlasmaIon),
            new Preset("Plasma — Cryo", PyreRampPresets.PlasmaCryo),
            new Preset("Plasma — Volt", PyreRampPresets.PlasmaVolt),
            new Preset("Plasma — Toxin", PyreRampPresets.PlasmaToxin),
            new Preset("Plasma — Flare", PyreRampPresets.PlasmaFlare),

            new Preset("Orb — Ember", PyreRampPresets.OrbEmber),
            new Preset("Orb — Frost", PyreRampPresets.OrbFrost),
            new Preset("Orb — Gold", PyreRampPresets.OrbGold),
            new Preset("Orb — Toxin", PyreRampPresets.OrbToxin),
            new Preset("Orb — Volt", PyreRampPresets.OrbVolt),

            new Preset("Torch — Hot", PyreRampPresets.TorchHot),
            new Preset("Torch — Ember", PyreRampPresets.TorchEmber),
            new Preset("Torch — White", PyreRampPresets.TorchWhite),
            new Preset("Torch — Rim", PyreRampPresets.TorchRim),
            new Preset("Torch — Gold", PyreRampPresets.TorchGold),
        };

        /// <summary>UnityEngine.Gradient's own key cap — a converted preset beyond this many stops is subsampled, never silently truncated.</summary>
        const int MaxGradientKeys = 8;

        /// <summary>
        /// Converts a Pyre ramp into a fresh <see cref="ZuiGradient"/> the caller owns outright — no shared
        /// reference back to anything Pyre-side. Null/empty in, null out (FC-6.5's "half-configured never
        /// renders empty" is the CALLER's job when applying this; a picker itself may legitimately have
        /// nothing to offer for a broken preset).
        /// </summary>
        public static ZuiGradient ToZuiGradient(PyreRamp ramp)
        {
            if (ramp?.stops == null || ramp.stops.Count == 0) return null;

            var stops = new List<PyreRampStop>(ramp.stops);
            stops.Sort((a, b) => a.pos.CompareTo(b.pos));
            if (stops.Count > MaxGradientKeys) stops = Subsample(stops, MaxGradientKeys);

            var ck = new GradientColorKey[stops.Count];
            var ak = new GradientAlphaKey[stops.Count];
            for (int i = 0; i < stops.Count; i++)
            {
                ck[i] = new GradientColorKey(stops[i].color, stops[i].pos);
                ak[i] = new GradientAlphaKey(stops[i].color.a, stops[i].pos);
            }
            var g = new Gradient();
            g.SetKeys(ck, ak);
            return new ZuiGradient { gradient = g };
        }

        /// <summary>Evenly-spaced stop selection, always keeping the first and last (the ramp's true endpoints).</summary>
        static List<PyreRampStop> Subsample(List<PyreRampStop> stops, int count)
        {
            var outList = new List<PyreRampStop>(count);
            for (int i = 0; i < count; i++)
            {
                int idx = Mathf.RoundToInt(i * (stops.Count - 1) / (float)(count - 1));
                outList.Add(stops[idx]);
            }
            return outList;
        }
    }
}
