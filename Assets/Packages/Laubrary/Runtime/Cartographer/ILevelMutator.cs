namespace Laubrary.Cartographer
{
    /// A PROCGEN MUTATOR: something that rewrites level DATA from a seed, before the level builds.
    ///
    /// This interface IS the declaration. Put it on a component of a prop's prefab, mark the prop
    /// `procgen`, and the pass finds it — there is no flag to also remember and no base-class method to
    /// forget to override. That is the whole point of it being an interface: the compiler will not let a
    /// mutator exist without saying what it does and doing something. The shape it replaces was a virtual
    /// `OnProcgen` on PropBehaviour that did nothing when unoverridden, so a prop could be marked procgen,
    /// carry a behaviour, run the pass and silently change nothing — which is exactly what the first aisle
    /// mutator did for weeks.
    ///
    /// Implement it on a PropBehaviour when the mutator also wants placement context at play time (most do);
    /// any MonoBehaviour on the prefab qualifies, because LevelProcgen looks for the interface, not the base.
    public interface ILevelMutator
    {
        /// What this mutator will DO to the level, in one line, addressed to whoever is placing the prop —
        /// not to a programmer reading the class. Code attached to a prop is invisible work: the author
        /// stamps something and has no way to know it will plug an aisle or roll an exit when the level
        /// generates. This is how a mutator tells them, and Cartographer lists it beside the seed.
        ///
        /// Called on the PREFAB's component, so the answer may quote the prefab's configured values ("Blocks
        /// half the aisles") — a flavour is a prefab variant. Cartographer only relays the string; it never
        /// learns what a mutator means, the same line the tag system draws.
        string DescribeMutation();

        /// Rewrite `level`. Runs on a LEVEL ASSET — usually a LevelProcgen.CloneForRun copy, never a built
        /// instance — and is invoked on the PREFAB's component directly, without a spawned scene object. So
        /// an implementation mutates `level` only (Paint / ErasePaint / Place / PlaceClump, marking what it
        /// adds Origin.Generated) and must never touch its transform or any play-time context: none of it
        /// exists yet.
        ///
        /// `rng` is one stream shared across the whole pass, so its state depends on pass ORDER. A mutator
        /// whose result must be stable against unrelated edits derives its own System.Random from `seed`
        /// (and from something spatial, like the cell it is acting on) instead of drawing from the stream.
        void Mutate(LevelAsset level, PropPlacement placement, System.Random rng, int seed);
    }
}
