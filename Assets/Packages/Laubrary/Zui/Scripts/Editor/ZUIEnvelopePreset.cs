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

    // A STEP-sequence shape stores its normalized [0,1] bar heights here instead of points. A preset is a step
    // preset (not a curve preset) exactly when this list is non-empty — the two shape kinds share one library
    // asset but are told apart by which list carries data, and each Recall popover shows only its own kind.
    public List<float> steps = new List<float>();

    public bool isSteps => steps != null && steps.Count > 0;
}
