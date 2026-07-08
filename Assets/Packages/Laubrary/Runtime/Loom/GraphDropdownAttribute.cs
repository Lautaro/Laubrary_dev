using System;

namespace Laubrary.Loom
{
    // Marks a string field on a graph node (or a [Serializable] list-element like an op) as a dropdown whose
    // options come from an instance method on the declaring type — signature: IEnumerable<string> Method().
    // The graph editor renders a dropdown instead of a free-text field, so a visual editor can only pick values
    // that actually exist. Dependent dropdowns (options that depend on a sibling field's current value, e.g. a
    // parameter list that depends on which rule is selected) refresh when any dropdown in the element changes.
    [AttributeUsage(AttributeTargets.Field)]
    public class GraphDropdownAttribute : Attribute
    {
        public readonly string ChoicesMethod;

        public GraphDropdownAttribute(string choicesMethod) { ChoicesMethod = choicesMethod; }
    }
}
