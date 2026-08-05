using System;

/// <summary>
/// Marks an animatable value whose payload is really a WHOLE NUMBER, so its control snaps to integers
/// instead of pretending to be continuous.
///
/// The failure it fixes is quiet and infuriating. An outline's thickness is a count of pixels — the code
/// rounds it and searches that many neighbours — but the slider offered every fraction in between. So the
/// first half-unit of travel did nothing at all, the value then crossed a rounding boundary and a whole
/// pixel of outline appeared at once, and on a zoomed pixel-art preview that one pixel arrives several
/// screen pixels wide. It reads as a broken, wildly over-sensitive dial; it is actually a continuous
/// control wired to a discrete quantity.
///
/// With this, every position on the track is a value the effect can actually take, and each step is
/// exactly one visible unit. Animation is unaffected — an envelope still produces fractions and the
/// effect still rounds them; this governs how the value is AUTHORED, not how it is evaluated.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class ZUIWholeNumberAttribute : Attribute
{
}
