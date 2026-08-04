using System;

/// <summary>
/// Marks a float field as a HUE in degrees (0–360), so a reflected editor draws a colour swatch beside its
/// slider — pick the colour, and the field takes that colour's hue.
///
/// Authoring a recolour by typing degrees means holding a conversion in your head that nothing on screen
/// helps with: "the armour is orange, so… 25?" You cannot see 25. The swatch makes the value legible at a
/// glance and, more importantly, lets the answer come from a colour you actually picked rather than one you
/// guessed and corrected.
///
/// Only the HUE travels in or out. Saturation and value are shown at full so the swatch reads as the pure
/// hue rather than as some particular shade of it — this field is a position on the wheel, not a colour, and
/// the control should not imply it stores more than it does.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class ZUIHueAttribute : Attribute
{
}
