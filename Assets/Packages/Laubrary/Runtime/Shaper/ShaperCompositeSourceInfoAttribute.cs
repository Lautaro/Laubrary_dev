using System;

namespace Laubrary.Shaper
{
    /// <summary>
    /// T-0173 — how an <see cref="IShaperCompositeSource"/> that is NOT a hosted <c>PyreForm</c> announces itself
    /// to the window's generator picker.
    ///
    /// <b>Why an opt-in attribute rather than a blanket type scan.</b> The picker used to enumerate exactly one
    /// family — every concrete <c>PyreForm</c> in the domain, read off that form's own
    /// <c>PyreFormInfoAttribute</c> so a newly written form appears automatically rather than silently missing
    /// (<c>ShaperWindow.Sections.cs</c>, <c>ShowGeneratorMenu</c>). A composite source is a broader thing than a
    /// form: <see cref="IShaperCompositeSource"/> is also implemented by internal plumbing types that are
    /// assembled by other code paths and must never be offered as a thing an author picks. Enumerating the
    /// interface directly would surface those the moment anyone adds one, which is the failure mode
    /// <c>PyreFormInfoAttribute</c> already avoids by making membership a declaration. So a source joins the
    /// picker by SAYING it is author-pickable, exactly as a form does, and nothing joins it by accident.
    ///
    /// The type must additionally be concrete, <c>[Serializable]</c> and have a public parameterless constructor
    /// — the picker instantiates it and assigns it to a <c>[SerializeReference]</c> field.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ShaperCompositeSourceInfoAttribute : Attribute
    {
        /// <summary>The name the picker shows. Should match the source's own <c>SourceLabel</c> so the picked
        /// entry and the card header afterwards read as the same thing.</summary>
        public string DisplayName { get; }

        /// <summary>The picker section this source sits under, so related sources group together rather than
        /// scattering through one flat list.</summary>
        public string Group { get; }

        public ShaperCompositeSourceInfoAttribute(string displayName, string group = "Sources")
        {
            DisplayName = displayName;
            Group = group;
        }
    }
}
