using UnityEngine;

namespace Laubrary.Combat2D
{
    /// What KIND of damage this was — fire, ice, crushing, a plain bullet. Purely descriptive: it changes no
    /// numbers on its own (<see cref="Health"/> never reads it), it exists so a target can REACT differently to
    /// being burned than to being shot.
    ///
    /// An asset rather than a string or an enum, for the same three reasons <see cref="Faction"/> is one, which
    /// this deliberately mirrors: a project adds its own kinds without touching the package, a reference can be
    /// PICKED from a browser instead of typed (see the project's standing "never type a reference string" rule),
    /// and a renamed asset keeps every reference to it because references are by GUID.
    ///
    /// A null damage type is "unspecified", and an event filtering on a specific type simply does not match it —
    /// so plain untyped damage keeps flowing to plain untyped reactions, and adding types to a project is purely
    /// additive.
    [CreateAssetMenu(menuName = "Laubrary/Colosseum/Damage Type", fileName = "DamageType")]
    public class DamageType : ScriptableObject
    {
        [Tooltip("Shown in logs / debug UI and as this type's label in pickers.")]
        public string displayName;

        [Tooltip("A colour for debug gizmos / editor tinting — no gameplay meaning.")]
        public Color color = Color.white;
    }
}
