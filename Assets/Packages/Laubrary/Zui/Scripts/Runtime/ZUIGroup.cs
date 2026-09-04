using System;

/// <summary>
/// The BOX a reflected field lands in, so a long card reads as a handful of named parts instead of one
/// undifferentiated wall of sliders.
///
/// A reflected dump is the only maintainable way to draw a generator that declares a hundred-odd dials —
/// hand-listing them goes stale the moment the generator gains one. But a flat dump loses the structure the
/// generator actually has: the dials that shape the body, the dials that shape the trail, and the dials that
/// tune the noise underneath are three different jobs, and only the author's memory was keeping them apart.
///
/// Grouping restores that structure without a per-tool drawer. A group's box appears where its FIRST member
/// is declared, so declaration order still decides layout; fields carrying no group flow inline exactly as
/// before, so a card that adopts this for some of its fields keeps the rest unchanged, and every card that
/// adopts none of it is untouched.
///
/// <see cref="Advanced"/> is for the dials that are real but rarely the answer — noise octaves, seed offsets,
/// anisotropy. Such a box is moved to the END of the card and starts folded, so the dials stay reachable
/// without being the first thing a reader has to wade through.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class ZUIGroupAttribute : Attribute
{
    /// The box's title. Fields sharing a title share a box.
    public readonly string Group;

    /// The box's tooltip — what this part of the picture IS, not a restatement of the title. Only the first
    /// member of a group needs to carry it; the drawer takes the first non-empty one it finds.
    public string Tooltip;

    /// Send this box to the end of the card and fold it on first sight.
    public bool Advanced;

    public ZUIGroupAttribute(string group) { Group = group; }
}
