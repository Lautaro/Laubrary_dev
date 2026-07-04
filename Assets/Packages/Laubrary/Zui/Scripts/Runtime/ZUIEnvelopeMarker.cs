// ZUIEnvelopeMarker.cs
// Vertical-line marker for ZUI.Envelope. Used for loop points, trim points,
// play heads, etc. Runtime-safe so consumer serialized data can hold the list.

using System;

[Serializable]
public class ZUIEnvelopeMarker
{
    /// <summary>X-axis position of the marker, in the envelope's time domain (xMin..xMax).</summary>
    public float time;

    /// <summary>If false, the marker cannot be dragged (still visible, no hover ring).</summary>
    public bool  editable = true;

    /// <summary>When true, this marker cannot cross the marker before it in the list.
    /// Used to form ordered pairs (start, end) like loop or trim points.</summary>
    public bool  clampToPrev = false;

    /// <summary>When true, the band between the previous marker and this one is tinted
    /// (loop region / trim region visual). Only meaningful with clampToPrev=true.</summary>
    public bool  fillBandFromPrev = false;

    public ZUIEnvelopeMarker() { }
    public ZUIEnvelopeMarker(float time, bool editable = true,
                             bool clampToPrev = false, bool fillBandFromPrev = false)
    {
        this.time = time;
        this.editable = editable;
        this.clampToPrev = clampToPrev;
        this.fillBandFromPrev = fillBandFromPrev;
    }
}
