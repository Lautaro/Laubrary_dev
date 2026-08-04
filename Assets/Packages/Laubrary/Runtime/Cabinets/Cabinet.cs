using UnityEngine;

namespace Laubrary.Cabinets
{
    /// THE presentation contract for one gameplay style — the arcade cabinet you walk up to. A pseudo-racer
    /// has one, a world map has another, a top-down scavenge level has a third; each decides how much world
    /// fits on screen and how chunky a pixel looks, and nothing else in the game has an opinion about it.
    ///
    /// The one idea worth understanding before touching any field here: THREE things decide how pixel art
    /// looks on screen, and only ONE of them lives in this asset.
    ///
    ///   1. Art PPU        — texture pixels per world unit. A property of the ART, fixed at import time and
    ///                       the same everywhere in a project. NOT a per-style choice.
    ///   2. Virtual res    — the logical pixel canvas a scene renders into. THIS asset. THIS is the knob.
    ///   3. Window fit     — how that canvas scales to the physical display. Also this asset.
    ///
    /// So "make this scene look like a C64" does not mean re-importing art at a different PPU — it means
    /// rendering into a SMALLER virtual canvas, so each art pixel covers more of the display. The same
    /// character sprite works in a 320x200 cabinet and a 640x400 one with no reimport, which is exactly what
    /// makes "the same character shows up in the wrong cabinet" cheap to do as a joke instead of a rebuild.
    ///
    /// Deliberately NOT a LauAsset. A Cabinet is pure semantics — there is no picture of it, so it earns no
    /// thumbnail, no browser chrome and no Recall/New/Edit gutter (see the UI guide's LauAsset rule). It is
    /// picked as a plain object reference, or resolved automatically via <see cref="Current"/>.
    [CreateAssetMenu(menuName = "Laubrary/Cabinet", fileName = "Cabinet")]
    public class Cabinet : ScriptableObject
    {
        public enum Projection
        {
            /// Flat 2D — top-down, side-on, world maps. Framing is driven entirely by virtualResolution.
            Orthographic,
            /// Fake-3D / real-3D — the pseudo-racer. virtualResolution still sets the render canvas, but
            /// how much world is visible is a function of FOV and distance, so it cannot be derived here.
            Perspective,
        }

        public enum WindowFit
        {
            /// Upscale by the largest WHOLE number that fits, letterbox the remainder. The only mode where
            /// every art pixel is exactly the same size as every other one. Costs black bars.
            IntegerOnly,
            /// Scale to fit the window preserving aspect, fractional allowed. No bars, but pixels land on
            /// fractional device pixels and shimmer when things move.
            Letterbox,
            /// Keep the pixel scale and reveal MORE world to fill the window instead of scaling up. Crisp
            /// like IntegerOnly with no bars — but a widescreen player genuinely sees more than a 4:3 one.
            ExpandView,
            /// Fill the window, breaking aspect ratio. Almost never what you want; here because sometimes
            /// the joke IS the squashed picture.
            Stretch,
        }

        [Header("Screen")]
        [Tooltip("The logical pixel canvas this style renders into. FEWER pixels means each one covers more " +
                 "of the display — that is what reads as a C64. More pixels reads as a later arcade board. " +
                 "This is the knob that decides the look; the art's import settings are not.")]
        public Vector2Int virtualResolution = new Vector2Int(320, 200);

        [Tooltip("Art pixels per world unit. This must MATCH how the sprites were actually imported — " +
                 "setting it here reimports nothing, it only tells the camera how much world one screenful " +
                 "covers. Change it and everything silently reframes.")]
        [Min(1)] public int referencePpu = 16;

        [Header("Camera")]
        [Tooltip("Flat 2D, or a real 3D frustum for the fake-3D racer. Orthographic scenes get their framing " +
                 "computed for them; perspective scenes cannot, so they keep their own field of view.")]
        public Projection projection = Projection.Orthographic;

        [Tooltip("Vertical field of view, used only by a perspective cabinet. Ignored entirely when the " +
                 "projection is orthographic.")]
        [Range(1f, 179f)] public float fieldOfView = 60f;

        [Tooltip("How the virtual canvas is mapped onto the real window when the two do not divide evenly.")]
        public WindowFit windowFit = WindowFit.IntegerOnly;

        [Tooltip("Round rendered positions onto the pixel grid, so sprites never straddle half a pixel. " +
                 "Turn it off for smooth sub-pixel motion at the cost of shimmering edges.")]
        public bool pixelSnap = true;

        [Header("Look")]
        [Tooltip("Optional palette / post-processing pass applied on top of the geometry. Empty means the " +
                 "scene renders in its native colours — the cabinet still controls resolution and framing.")]
        public CabinetLook look;

        // ---- Derived geometry. All of it is a pure function of the fields above, so the editor preview,
        // the runtime camera and any tool overlay all read the same numbers from the same place. ----

        /// How much WORLD one screenful covers, in Unity units. The number every tool actually wants.
        /// Meaningless for a perspective cabinet, where distance decides it — check <see cref="IsFlat"/>.
        public Vector2 WorldUnitsVisible =>
            new Vector2((float)virtualResolution.x / Mathf.Max(1, referencePpu),
                        (float)virtualResolution.y / Mathf.Max(1, referencePpu));

        /// What Camera.orthographicSize must be for this cabinet. Unity measures it as HALF the height.
        public float OrthographicSize => virtualResolution.y * 0.5f / Mathf.Max(1, referencePpu);

        public bool IsFlat => projection == Projection.Orthographic;

        /// The virtual canvas's aspect. 320x200 is 1.6 — which is 16:10, the Steam Deck's native shape.
        public float Aspect => virtualResolution.y == 0 ? 1f : (float)virtualResolution.x / virtualResolution.y;

        /// The largest whole-number upscale that still fits inside a display of this size. Always at least 1,
        /// because rendering nothing is never the more useful answer.
        public int MaxIntegerScale(Vector2Int display)
        {
            if (virtualResolution.x <= 0 || virtualResolution.y <= 0) return 1;
            return Mathf.Max(1, Mathf.Min(display.x / virtualResolution.x, display.y / virtualResolution.y));
        }

        /// True when the canvas tiles the display exactly — no letterbox, no fractional pixels, nothing
        /// wasted. This is the condition worth designing a virtual resolution AROUND for a fixed-screen
        /// target like a handheld.
        public bool FitsExactly(Vector2Int display) =>
            virtualResolution.x > 0 && virtualResolution.y > 0 &&
            display.x % virtualResolution.x == 0 && display.y % virtualResolution.y == 0 &&
            display.x / virtualResolution.x == display.y / virtualResolution.y;

        /// How many GRID CELLS of a given size are visible. The form a level editor cares about: "will my
        /// room fit on one screen?" is this number, not a pixel count.
        public Vector2 CellsVisible(int cellPixels) =>
            cellPixels <= 0 ? Vector2.zero
                            : new Vector2((float)virtualResolution.x / cellPixels,
                                          (float)virtualResolution.y / cellPixels);

        // ---- Resolution. How a tool gets a Cabinet without being handed one. ----

        const string DefaultResourcePath = "Cabinet Default";
        static Cabinet cached;
        static Cabinet fallback;
        // The NEGATIVE answer has to be cached too, not just the positive one. A project that has opted out
        // entirely is the common case during a retrofit, and without this flag every Current/Active call
        // would run a scene-wide FindFirstObjectByType plus a Resources.Load — per caller, per frame, for
        // the entire lifetime of a project that is not even using the feature.
        static bool resolved;

        /// The Cabinet actually CONFIGURED for the current scene, or null when nobody has chosen one.
        ///
        /// Order: the active <see cref="CabinetStage"/> in the loaded scene, then a project default at
        /// Resources/Cabinet Default. No fallback — null here means "this project has not opted in", which
        /// is exactly what an existing scene needs to hear so it can keep its own camera behaviour instead
        /// of being silently reframed by a default nobody asked for.
        public static Cabinet Active
        {
            get
            {
                if (resolved) return cached;
                resolved = true;

                var stage =
#if UNITY_2023_1_OR_NEWER
                    Object.FindFirstObjectByType<CabinetStage>(FindObjectsInactive.Exclude);
#else
                    Object.FindObjectOfType<CabinetStage>();
#endif
                if (stage != null && stage.cabinet != null) return cached = stage.cabinet;

                return cached = Resources.Load<Cabinet>(DefaultResourcePath);
            }
        }

        /// The Cabinet in force right now, for code that just needs NUMBERS — a tool drawing a screen-bounds
        /// overlay, a preview sizing itself. NEVER null, because a tool forced to null-check its own
        /// presentation contract will simply not bother and will hardcode something instead.
        ///
        /// Two properties rather than one because the two callers want opposite things from "unconfigured":
        /// a TOOL wants sane numbers regardless, while GAME CODE being retrofitted must be able to tell that
        /// nothing was chosen and leave its existing behaviour alone. Collapsing them into one property
        /// means one of those two callers silently gets the wrong answer.
        public static Cabinet Current => Active != null ? Active : Fallback;

        /// The cabinet you get when a project has defined none. Deliberately a real, sane 16:10 pixel-art
        /// setup rather than zeroes, so a fresh project renders something reasonable instead of a black
        /// screen that looks like a bug in this system.
        public static Cabinet Fallback
        {
            get
            {
                if (fallback != null) return fallback;
                fallback = CreateInstance<Cabinet>();
                fallback.name = "Cabinet (built-in fallback)";
                fallback.hideFlags = HideFlags.HideAndDontSave;
                return fallback;
            }
        }

        /// Drop the cached lookup. Called when a stage is enabled/disabled or a scene changes — anything that
        /// could make Current answer differently than it did a moment ago.
        public static void Invalidate() { cached = null; resolved = false; }

        /// A scene load can swap which stage exists without any stage's OnEnable running in an order we can
        /// rely on, so the cache is dropped on every load as well.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad() => Invalidate();

        void OnValidate()
        {
            // A zero or negative canvas divides by nothing and silently produces infinities downstream.
            virtualResolution.x = Mathf.Max(1, virtualResolution.x);
            virtualResolution.y = Mathf.Max(1, virtualResolution.y);
            Invalidate();
        }
    }
}
