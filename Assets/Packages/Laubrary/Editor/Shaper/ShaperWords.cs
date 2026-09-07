// ShaperWords — the ONE place an engine identifier becomes a word on screen (T-0257).
//
// Every choice control in this window used to print `Enum.GetNames(typeof(X))` verbatim, so the screen said
// `RampByQuantity`, `TapestrySteel`, `PathTangent` and `FrameStep` — twenty-five controls printing the
// programmer's identifiers at a pixel artist. Translating them one call site at a time would have produced
// twenty-five private opinions about what `Subtract` means, which is exactly how two cards end up disagreeing
// about the same word (`Subtract` genuinely means two different things here — see the Combine and Mask
// entries below, which is why the table is keyed by TYPE and member, never by member alone).
//
// THE DATA IS UNTOUCHED. This maps a member NAME to a display string at draw time; nothing is renamed, no
// serialized value moves, and a member with no entry falls through to its own identifier. Adding an enum
// member without adding a row here is therefore safe — it shows as the identifier, the same as before — so
// this file can never be the reason a new option fails to appear.
//
// Wording source: the analysis appendix "UI inventory, native-control audit, naming" §4.2 and the main
// document's §6 table, used verbatim except where a shorter word was needed to fit a control (each of those
// is called out in the comment beside it).
using System;
using System.Collections.Generic;

namespace Laubrary.Shaper.Editor
{
    /// <summary>
    /// Enum member → the word the window shows for it. Keyed <c>"TypeName.MemberName"</c>, because the same
    /// identifier means different things on different enums.
    /// </summary>
    internal static class ShaperWords
    {
        static readonly Dictionary<string, string> Table =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ── Fill kind ────────────────────────────────────────────────────────────────────────────────
            { "ShaperFillKind.RampByQuantity", "Ramp by value" },
            { "ShaperFillKind.IndexedStrip",   "Colour bands" },
            { "ShaperFillKind.HeightField",    "Height field" },
            { "ShaperFillKind.TapestrySteel",  "Brushed metal" },
            { "ShaperFillKind.OverPhase",      "One colour over time" },
            // "Procedural" is the implementation's word for it; what the author picks is a pattern.
            { "ShaperFillKind.Procedural",     "Pattern" },

            // ── Fill space / fit / mapping ───────────────────────────────────────────────────────────────
            { "ShaperFillSpace.Stamped",       "Moves with the shape" },
            { "ShaperFillSpace.Fixed",         "Stays put" },
            { "ShaperFillFit.Uniform",         "Keep proportions" },
            { "ShaperFillFit.Stretch",         "Stretch to fit" },
            { "ShaperTextureMapping.Fitted",   "Fit once" },
            { "ShaperTextureMapping.Tiled",    "Repeat" },
            { "ShaperSpriteFitMode.Uniform",   "Keep proportions" },
            { "ShaperSpriteFitMode.Stretch",   "Stretch to fit" },

            // ── Gradient ─────────────────────────────────────────────────────────────────────────────────
            { "ShaperGradientMode.ByEdgeDistance", "From the edge inwards" },

            // ── Quantities. Coverage is the engine's word for alpha; the rest are the plain nouns already.
            { "ShaperQuantity.Coverage",         "Opacity" },
            { "ShaperQuantity.EdgeDistance",     "Distance from edge" },
            { "ShaperQuantity.SurfaceDirection", "Surface direction" },
            { "ShaperMaskQuantity.Coverage",     "Opacity" },
            { "ShaperMaskQuantity.EdgeDistance", "Distance from edge" },
            { "ShaperMaskQuantity.Luma",         "Brightness" },

            // ── Strip parameterisation ───────────────────────────────────────────────────────────────────
            { "ShaperStripParameterisation.Angular",    "Around the shape" },
            { "ShaperStripParameterisation.Projection", "Across the shape" },

            // ── Noise. "Value noise" is an algorithm name, not a description of what it looks like.
            { "ShaperNoiseKind.Value", "Smooth" },
            { "ShaperNoiseKind.Steps", "Banded" },

            // ── Combine vs Mask: the same three identifiers, two different meanings, and this is the whole
            //    reason the table is keyed by type. Combine folds one member into a bag; Mask cuts a finished
            //    layer with another layer.
            { "ShaperCombineMode.Subtract",  "Cut out" },
            { "ShaperCombineMode.Intersect", "Keep overlap" },
            { "ShaperMaskMode.Clip",         "Keep inside" },
            { "ShaperMaskMode.Subtract",     "Cut away" },
            { "ShaperMaskMode.Intersect",    "Keep overlap" },

            // ── Swarm ────────────────────────────────────────────────────────────────────────────────────
            { "ShaperSwarmOrient.Outward",     "Away from centre" },
            { "ShaperSwarmOrient.PathTangent", "Along the path" },
            { "ShaperSwarmTiming.Stagger",     "Spread out" },
            { "ShaperSwarmTiming.Window",      "All at once, for a while" },
            { "ShaperSwarmTiming.FrameStep",   "One per frame" },

            // ── Surface direction provider ───────────────────────────────────────────────────────────────
            { "ShaperNormalKind.Constant", "Flat" },
            { "ShaperNormalKind.Profile",  "Follow the surface" },

            // ── Primitives, for the shape picker. It deliberately did NOT nicify these (that turns NGon into
            //    "N Gon"), so it printed the identifiers instead; these are the drawn shapes' own names.
            { "ShaperPrimitiveKind.Rect",    "Rectangle" },
            { "ShaperPrimitiveKind.Capsule", "Pill" },
            { "ShaperPrimitiveKind.NGon",    "Polygon" },
            { "ShaperPrimitiveKind.Sprite",  "Image" },
        };

        static readonly Dictionary<Type, string[]> Cache = new Dictionary<Type, string[]>();

        /// <summary>The word for one enum value — its own identifier when the table has no entry.</summary>
        internal static string Of(Enum value)
            => value == null ? "" : Of(value.GetType(), value.ToString());

        internal static string Of(Type enumType, string member)
            => enumType != null && Table.TryGetValue(enumType.Name + "." + member, out string word)
                 ? word : member;

        /// <summary>
        /// The words for a whole enum, in <see cref="Enum.GetNames"/> order — i.e. index-compatible with the
        /// <c>(int)value</c> every <c>Z.MiniRadio</c>/<c>Z.Segmented</c> call site already passes as its
        /// selected index. Drop-in for <c>Enum.GetNames(typeof(X))</c>.
        /// </summary>
        internal static string[] Names(Type enumType)
        {
            if (Cache.TryGetValue(enumType, out string[] cached)) return cached;
            string[] raw = Enum.GetNames(enumType);
            var words = new string[raw.Length];
            for (int i = 0; i < raw.Length; i++) words[i] = Of(enumType, raw[i]);
            Cache[enumType] = words;
            return words;
        }
    }
}
