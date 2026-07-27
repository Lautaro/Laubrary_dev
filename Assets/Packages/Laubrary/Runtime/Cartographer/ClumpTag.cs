using UnityEngine;

namespace Laubrary.Cartographer
{
    /// An open-ended gameplay label on a Clump — "untraversable", "damages", "slows", "starting platform".
    /// Deliberately carries no behaviour of its own: Cartographer owns the label, the consuming project decides
    /// what it does (the same split Combat2D's Faction uses, and the same one Phase 4's tag-to-collision wiring
    /// assumes). An asset rather than a bare string so tags are discoverable in a picker, survive a rename, and
    /// can never silently become a typo.
    [CreateAssetMenu(menuName = "Laubrary/Cartographer/Clump Tag", fileName = "ClumpTag")]
    public class ClumpTag : ScriptableObject
    {
        [Tooltip("What this tag means to gameplay, for whoever wires it up. Cartographer never reads it.")]
        [TextArea(2, 4)] public string description = "";

        [Tooltip("Colour used to mark cells carrying this tag in Cartographer's editor overlays.")]
        public Color editorColor = new Color(1f, 0.8f, 0.2f, 1f);
    }
}
