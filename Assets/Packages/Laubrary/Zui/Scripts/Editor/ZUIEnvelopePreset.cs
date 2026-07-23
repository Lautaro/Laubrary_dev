// ZUIEnvelopePreset.cs
// A reusable envelope SHAPE: point times/values both normalized to [0,1], independent of whatever field's
// actual yMin..yMax range it eventually gets applied to. Reuses ZUIEnvelopePoint unchanged (same convention
// ZUIValue2DControl already follows for its own data) rather than inventing a parallel point type.
//
// Editor-only despite ZUIEnvelopePoint itself being runtime-safe: nothing in gameplay ever evaluates "a
// preset" — only the concrete points a preset gets copied into after being applied, which is the ordinary,
// already-runtime-safe List<ZUIEnvelopePoint> every envelope-holding field already uses.

using System;
using System.Collections.Generic;

[Serializable]
public class ZUIEnvelopePreset
{
    public string name;
    public List<ZUIEnvelopePoint> points = new List<ZUIEnvelopePoint>();
}
