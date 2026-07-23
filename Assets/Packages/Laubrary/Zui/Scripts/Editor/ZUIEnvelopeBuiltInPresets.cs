// ZUIEnvelopeBuiltInPresets.cs
// ZUI's own shipped envelope-shape presets — deliberately hard-coded here, not .asset files, so ZUI keeps
// its "tools ship zero assets" rule (authoring.md #2) even while shipping a built-in preset set. This is
// the ZUI-wide tier; a project's own saved shapes live separately in ZUIEnvelopePresetLibrary (an
// auto-created host-project asset, same pattern as PyreLayerLibrary). See ZUIEnvelopePreset.cs for why
// values are normalized to [0,1] rather than stored in some concrete field's range.
//
// `exponent` shapes the interpolation BETWEEN two points (ZUIEnvelopeEvaluator: Lerp(a, b, t^exponent)) —
// >1 holds near the start longer before rising (an ease-IN feel), <1 rises quickly then settles (ease-OUT).

using System.Collections.Generic;

public static class ZUIEnvelopeBuiltInPresets
{
    static ZUIEnvelopePoint P(float time, float value, float exponent = 1f) => new ZUIEnvelopePoint(time, value, exponent);

    static ZUIEnvelopePreset Preset(string name, params ZUIEnvelopePoint[] pts)
    {
        var preset = new ZUIEnvelopePreset { name = name };
        preset.points.AddRange(pts);
        return preset;
    }

    public static readonly ZUIEnvelopePreset[] All =
    {
        Preset("Linear",         P(0f, 0f), P(1f, 1f)),
        Preset("Ease In",        P(0f, 0f), P(1f, 1f, 2.5f)),
        Preset("Ease Out",       P(0f, 0f, 0.4f), P(1f, 1f)),
        Preset("Ease In-Out",    P(0f, 0f), P(0.5f, 0.5f), P(1f, 1f)),
        Preset("Hold Then Rise", P(0f, 0f), P(0.7f, 0f), P(1f, 1f)),
        Preset("Spike",          P(0f, 0f), P(0.5f, 1f), P(1f, 0f)),
        Preset("Bell",           P(0f, 0f), P(0.3f, 0.9f), P(0.7f, 0.9f), P(1f, 0f)),
        Preset("Full Then Fall", P(0f, 1f), P(0.6f, 1f), P(1f, 0f)),
    };
}
