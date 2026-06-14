using System;
using System.Collections.Generic;

namespace Laubrary.Rulesets
{
    // Category + tag taxonomy for the rules editor window. A rule's category/tags are resolved from its
    // [RuleCategory]/[RuleTags] attributes. If a rule carries no attribute, an optional central registration
    // (RuleTaxonomy.Register) is consulted as a fallback; failing that the rule lands under "Misc" with no
    // tags. Purely descriptive — no runtime behaviour.
    //
    // Registration is optional: games that prefer a single central table over per-class attributes can call
    //   RuleTaxonomy.Register("MyRule", "Pieces/Timing", "piece", "speed");
    // typically from a [RuntimeInitializeOnLoadMethod] or [InitializeOnLoadMethod] at startup. Attributes
    // always win over registrations.
    public static class RuleTaxonomy
    {
        public struct Entry { public string Category; public string[] Tags; }

        static readonly Dictionary<string, Entry> Registered = new Dictionary<string, Entry>();

        // Optionally register a category + tags for a rule type by its simple type name (t.Name). Consulted
        // only when the type itself carries no [RuleCategory]/[RuleTags] attribute. Safe to call repeatedly;
        // the last call for a given typeName wins.
        public static void Register(string typeName, string category, params string[] tags)
        {
            if (string.IsNullOrEmpty(typeName)) return;
            Registered[typeName] = new Entry { Category = category, Tags = tags ?? Array.Empty<string>() };
        }

        public static void ClearRegistrations() => Registered.Clear();

        public static string CategoryOf(Type t)
        {
            var attr = (RuleCategoryAttribute)Attribute.GetCustomAttribute(t, typeof(RuleCategoryAttribute));
            if (attr != null && !string.IsNullOrEmpty(attr.Path)) return attr.Path;
            if (Registered.TryGetValue(t.Name, out var e) && !string.IsNullOrEmpty(e.Category)) return e.Category;
            return "Misc";
        }

        public static string[] TagsOf(Type t)
        {
            var attr = (RuleTagsAttribute)Attribute.GetCustomAttribute(t, typeof(RuleTagsAttribute));
            if (attr != null && attr.Tags != null && attr.Tags.Length > 0) return attr.Tags;
            if (Registered.TryGetValue(t.Name, out var e) && e.Tags != null && e.Tags.Length > 0) return e.Tags;
            return Array.Empty<string>();
        }
    }
}
