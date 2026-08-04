using System;

/// <summary>
/// Hides a field in a reflected editor unless a sibling field currently holds one of the listed values —
/// so a card shows the dials that apply to what it is set to, and only those.
///
/// The problem it solves is one every mode-bearing effect has: a Wipe set to Square still showed Crescent
/// Bite and Crescent Thickness, and a Soft edge still showed Fade Angle, because a reflected drawer renders
/// whatever fields exist. Those dials do nothing in that state, and their own tooltips said so — which the
/// layout rules call out directly: a conditional tooltip must read for the CURRENT state, never leave
/// "X shape only" wording showing while X is off. Two dials that do nothing are worse than a longer card,
/// because the reader has to try them to find out.
///
/// <code>
/// [ZUIShowIf("shape", "Crescent")]
/// [Range(0f, 2f)] public float crescentBite = 0.9f;
/// </code>
///
/// Values are matched against the sibling's <c>ToString()</c>, case-insensitively — enums by member name,
/// bools as "True"/"False". A field naming a sibling that does not exist is always SHOWN: failing open
/// means a rename costs a stale dial, while failing closed would silently delete a control from the UI
/// with nothing to indicate it ever existed.
///
/// A field named by any <c>[ZUIShowIf]</c> automatically redraws its card when edited, so the dials it
/// reveals appear on the same click that reveals them — the host only has to supply
/// <c>Options.OnStructureChanged</c>, which every stack-style host already does.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class ZUIShowIfAttribute : Attribute
{
    /// Name of the sibling field whose value decides whether this one is relevant.
    public string Field { get; }

    /// The values of that sibling for which this field applies. Compared to its ToString(), ignoring case.
    public string[] Values { get; }

    public ZUIShowIfAttribute(string field, params string[] values)
    {
        Field = field;
        Values = values ?? Array.Empty<string>();
    }
}
