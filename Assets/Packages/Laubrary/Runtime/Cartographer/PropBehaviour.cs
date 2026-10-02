using UnityEngine;

namespace Laubrary.Cartographer
{
    /// A script that acts on a PART of the level: put one on a prop's prefab, stamp the prop, and when
    /// the level builds in Play mode the spawned component is handed its context — which instance built it
    /// and which placement it came from. "Spawn the player here", "emit enemies from here", "open when the
    /// room clears" are all this shape.
    ///
    /// Cartographer owns only the hand-off; the behaviours themselves are game code, and the tool never
    /// learns what they do — the same line the tag system draws. Unlike a named spot, a behaviour scales to
    /// many copies of the same prop: each instance knows its own placement, so nothing is name-keyed.
    ///
    /// Settings live on the behaviour's prefab — different flavours of one behaviour are prefab variants.
    /// (Per-placement overrides would need a serialized payload per placement; that machinery waits for a
    /// second real use case.)
    public abstract class PropBehaviour : MonoBehaviour
    {
        /// The instance that built the level this behaviour is part of.
        public LevelInstance Level { get; private set; }

        /// The placement that put this behaviour here — its prop, cell, rotation and mirror.
        public PropPlacement Placement { get; private set; }

        public Prop Prop => Placement?.prop;
        public Vector2Int Cell => Placement != null ? Placement.cell : default;

        /// Called by LevelInstance right after the behaviour's prefab is spawned at its placement.
        public void Attach(LevelInstance level, PropPlacement placement)
        {
            Level = level;
            Placement = placement;
            OnPlaced();
        }

        /// The behaviour's entry point: the level is built, the context is set, act. Runtime only —
        /// an editing scene shows the level, not the gameplay.
        protected virtual void OnPlaced() { }

        /// What this behaviour does, in one line, addressed to whoever is placing the prop in the level
        /// editor — NOT to a programmer reading the class. Code attached to a prop is invisible work: the
        /// author sees a stamp go down and has no way to know it will seal a corridor or roll an exit when
        /// the level generates. Overriding this is how a behaviour tells them.
        ///
        /// Called on the PREFAB's component, so the answer may quote the prefab's configured values ("Seals
        /// aisles up to 6 cells long") — that is the point, a flavour is a prefab variant. Cartographer only
        /// relays the string; it still never learns what a behaviour means, the same line the tag system
        /// draws. The default names the type, which is honest but rarely enough — override it.
        public virtual string Describe() => Spaced(GetType().Name);

        /// "AisleGateBehaviour" → "Aisle Gate". Runtime-safe: ObjectNames.NicifyVariableName is editor-only
        /// and this type lives in the runtime assembly.
        static string Spaced(string typeName)
        {
            if (typeName.EndsWith("Behaviour")) typeName = typeName.Substring(0, typeName.Length - 9);
            var sb = new System.Text.StringBuilder(typeName.Length + 8);
            for (int i = 0; i < typeName.Length; i++)
            {
                if (i > 0 && char.IsUpper(typeName[i]) && !char.IsUpper(typeName[i - 1])) sb.Append(' ');
                sb.Append(typeName[i]);
            }
            return sb.ToString();
        }

        // ☠️ There is deliberately NO virtual OnProcgen here. A behaviour that rewrites the level at procgen
        // time declares itself by implementing ILevelMutator — an interface the compiler checks — because
        // the virtual-with-an-empty-body version could be left unoverridden, and then a prop marked procgen
        // carrying that behaviour ran the pass and changed nothing, with nothing anywhere saying so.
    }
}
