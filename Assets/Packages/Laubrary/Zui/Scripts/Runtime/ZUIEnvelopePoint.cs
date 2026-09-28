// ZUIEnvelopePoint.cs
// Shared envelope point primitive. Runtime-safe, so consumer data types in
// runtime assemblies can hold List<ZUIEnvelopePoint> directly.
// ZUI.Envelope (in ZUI.Editor) renders and mutates these.

using System;

/// <summary>Per-point editing permissions. Drives both input behaviour and visual variant.</summary>
public enum ZUIEnvelopeEditState
{
    Editable    = 0,
    XEditable   = 1,
    YEditable   = 2,
    NotEditable = 3,
}

[Serializable]
public class ZUIEnvelopePoint
{
    public float time;
    public float value;
    public float exponent;
    public ZUIEnvelopeEditState editState;
    /// <summary>
    /// A random point (Zounds, T-0483): each play moves it anywhere inside an ellipse of these radii around where it was
    /// drawn -- X as a fraction of the curve's length, Y in the curve's own values. Both nought: an ordinary point.
    /// </summary>
    public float randomX, randomY;
    /// <summary>Where in the ellipse a random point tends to land: 0.5 evenly over it, towards 0 the middle, towards 1 the edge.</summary>
    public float randomBias = 0.5f;

    public ZUIEnvelopePoint() { exponent = 1f; }
    public ZUIEnvelopePoint(float time, float value, float exponent = 1f,
                            ZUIEnvelopeEditState editState = ZUIEnvelopeEditState.Editable)
    {
        this.time = time;
        this.value = value;
        this.exponent = exponent;
        this.editState = editState;
    }
}
