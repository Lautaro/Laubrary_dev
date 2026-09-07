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

        /// <summary>
        /// T-0254 — §6.2's classification, moved here off the per-document <c>ShaperCompositeDef.reason</c>
        /// field it used to live on. A source's reason for bypassing the shape/fill split is a fact about the
        /// SOURCE TYPE (every document hosting <c>FireCompositeSource</c> gets the same answer), not a
        /// per-document authoring choice, so it belongs on the declaration that already says the type is
        /// pickable at all. Defaults to <c>NotYetSplit</c> — every hosted <c>PyreForm</c> (the nine generators
        /// <c>PyreCompositeCatalog</c> classifies, reached through <c>PyreFormCompositeSource</c> rather than
        /// this attribute) is exactly that, so only the two stateful simulations need to say otherwise.
        /// </summary>
        public ShaperCompositeReason Reason { get; }

        public ShaperCompositeSourceInfoAttribute(string displayName, string group = "Sources",
                                                  ShaperCompositeReason reason = ShaperCompositeReason.NotYetSplit)
        {
            DisplayName = displayName;
            Group = group;
            Reason = reason;
        }
    }
}
