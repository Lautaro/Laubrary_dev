using UnityEngine;

namespace Laubrary.SpriteFx
{
    /// Project-wide dispatch config for the runtime SpriteFx filter (slice #47). A single bool — should the
    /// stateless colour/mask stack run as an inline managed loop or as the Burst <see cref="SfxStackJob"/> — plus
    /// room to grow (an auto-threshold later). <see cref="SpriteFxFilter"/> reads <see cref="UseBurstJobs"/> to pick
    /// its path (a per-component override can force either way).
    ///
    /// Ships ZERO assets (the Laubrary rule): the package never contains a SpriteFxSettings.asset. The static
    /// <see cref="Instance"/> loads an OPTIONAL asset named "SpriteFxSettings" from any Resources/ folder in the
    /// HOST project; if none exists it falls back to a hidden in-memory default. So a project that does nothing gets
    /// the safe default (inline); a project that wants Burst just creates one asset (Assets ▸ Create ▸ Laubrary ▸
    /// SpriteFx ▸ Settings) and ticks the box.
    ///
    /// DEFAULT = INLINE (useBurstJobs = false), deliberately. Pixel-art sprites are small (a 64×64 frame is 4096
    /// pixels); at that size the fixed cost of allocating a NativeArray, scheduling a job and blocking on Complete()
    /// typically exceeds the whole inline loop, and inline needs no Burst warm-up/compile. Burst wins when the
    /// buffers get large or many filters run at once — so it is an explicit opt-in, not the thing you get by
    /// default. (A future auto-threshold could switch to Burst above N pixels; the field is reserved for that.)
    [CreateAssetMenu(fileName = "SpriteFxSettings", menuName = "Laubrary/SpriteFx/Settings", order = 0)]
    public class SpriteFxSettings : ScriptableObject
    {
        [Tooltip("When ON, SpriteFxFilter dispatches its per-pixel stack to the Burst SfxStackJob; when OFF (the " +
                 "default) it runs the identical math inline on the main thread. Inline is the safe default for " +
                 "small pixel-art sprites (job scheduling + Burst warm-up cost more than the loop itself there); " +
                 "turn Burst on for large textures or many simultaneous filters. Both paths are byte-identical to " +
                 "within the documented 1/255 Burst-vs-Mono codegen limit.")]
        public bool useBurstJobs = false;

        // ── static resolution ────────────────────────────────────────────────────────────────────────────────
        const string ResourceName = "SpriteFxSettings";
        static SpriteFxSettings _instance;

        /// The resolved settings: a host-project Resources/SpriteFxSettings asset if one exists, else a hidden
        /// in-memory default (inline). Cached after the first lookup.
        public static SpriteFxSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<SpriteFxSettings>(ResourceName);
                    if (_instance == null)
                    {
                        _instance = CreateInstance<SpriteFxSettings>();
                        _instance.hideFlags = HideFlags.HideAndDontSave;
                    }
                }
                return _instance;
            }
        }

        /// Convenience: the resolved inline-vs-Burst toggle. SpriteFxFilter reads this when its own dispatch mode
        /// is "Use project setting".
        public static bool UseBurstJobs => Instance.useBurstJobs;

        /// Drop the cached instance so the next access re-resolves (e.g. after a settings asset is created/edited).
        public static void ClearCache() => _instance = null;
    }
}
