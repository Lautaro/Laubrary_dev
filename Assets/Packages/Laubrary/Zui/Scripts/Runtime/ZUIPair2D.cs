using System;

/// <summary>
/// Marks two animatable fields as ONE spatial value, so a reflected editor draws them as a single 2D
/// control instead of two unrelated sliders.
///
/// The layout rules are blunt about this: an X/Y pair that means a position, an offset or a direction is a
/// 2D-control case, never two 1D fields side by side. Packing them into a row fixes the width and not the
/// problem — you are still aiming one spatial value by dragging two numbers and watching a preview to find
/// out where it went.
///
/// A hand-written window just calls <c>Z.Value2D</c>. A REFLECTED one cannot: the drawer sees a flat list
/// of fields and has no way to know that <c>offsetX</c> and <c>offsetY</c> are two halves of one thing —
/// which is why every reflected effect in the package draws its offsets as two sliders today. This is the
/// missing information, declared where the data is, so it travels to every window that draws the type.
///
/// Put it on the X field and name the Y field:
/// <code>
/// [ZUIPair2D("offsetY", "Offset")]
/// [Range(-1.5f, 1.5f)] public ZUIValue offsetX = new ZUIValue(0f);
/// [Range(-1.5f, 1.5f)] public ZUIValue offsetY = new ZUIValue(0f);
/// </code>
/// The Y field keeps its own <c>[Range]</c> (it supplies the vertical bounds) and is not drawn again on its
/// own. An unresolvable partner name is ignored and both fields draw normally, so a rename degrades to the
/// old layout rather than to an empty card.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class ZUIPair2DAttribute : Attribute
{
    /// Name of the field holding the Y half. Must be a sibling field of the same type.
    public string YField { get; }

    /// Label for the combined control. Null uses the X field's own nicified name, which is usually wrong
    /// ("Offset X" naming a control that edits both axes) — so pass the shared noun.
    public string Label { get; }

    public ZUIPair2DAttribute(string yField, string label = null)
    {
        YField = yField;
        Label = label;
    }
}
