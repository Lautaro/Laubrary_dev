using System;

namespace Laubrary.Rulesets
{
    // Authoring metadata for the rules editor window: a category path (with optional sub-categories via
    // '/') and free-form tags. Purely descriptive — no runtime behaviour. The window builds its left-hand
    // tree from RuleCategory and its filter chips from RuleTags. A rule with neither lands under "Misc".
    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public sealed class RuleCategoryAttribute : Attribute
    {
        public string Path;   // e.g. "Pieces/Timing", "Hazard/Water"
        public RuleCategoryAttribute(string path) { Path = path; }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public sealed class RuleTagsAttribute : Attribute
    {
        public string[] Tags;
        public RuleTagsAttribute(params string[] tags) { Tags = tags; }
    }
}
