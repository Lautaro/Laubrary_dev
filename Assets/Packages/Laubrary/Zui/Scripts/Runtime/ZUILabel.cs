using System;

/// <summary>
/// The DISPLAY NAME a reflected field shows, when its serialized name cannot be changed.
///
/// A reflected card labels its dials by nicifying the C# field name, which is right almost everywhere and
/// wrong exactly where the field name is an algorithm's shorthand. A generator ported from a research
/// program keeps that program's parameter keys as its serialized names — they are what its contract files,
/// its parity dumps and its [MovedFrom] history are all written in — so renaming the field to read better
/// would break authored assets to fix a caption. The result on screen is a card of "A 0", "Turb Oct Shell",
/// "Rip R", "Core P": each one a real dial with a real visible effect, none of them readable.
///
/// This separates the two. The field keeps the name the data is stored under; the card shows the name the
/// author thinks in. Nothing about serialization, contract loading or reflection-by-name is affected —
/// only what the label says.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class ZUILabelAttribute : Attribute
{
    /// The caption to draw instead of the nicified field name.
    public readonly string Label;

    public ZUILabelAttribute(string label) { Label = label; }
}
