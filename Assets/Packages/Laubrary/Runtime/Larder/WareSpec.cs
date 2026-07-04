using UnityEngine;

namespace Laubrary.Larder
{
    /// Every dial that defines ONE Ware. This is the single source of truth shared by the editor preview, the PNG
    /// baker, and the runtime demo — hand the same WareSpec (+ a damage stage) to WareGenerator and you get identical
    /// pixels everywhere. Nothing here references UnityEditor, so it lives happily in the runtime assembly and can be
    /// created at play time (the demo shelf spins up dozens of throwaway specs, never touching disk).
    [CreateAssetMenu(menuName = "Laubrary/Larder/Ware Spec", fileName = "Ware")]
    public class WareSpec : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("The master seed. Same seed + same dials + same damage stage → identical pixels, forever.")]
        public int seed = 12345;
        public WareKind kind = WareKind.Box;
        public WareShape shape = WareShape.RoundedRect;

        [Header("Silhouette")]
        [Range(0.2f, 1f)] public float widthRatio = 0.72f;
        [Range(0.2f, 1f)] public float heightRatio = 0.95f;

        [Header("Body")]
        public FillMode fill = FillMode.Gradient;
        [Tooltip("Index into WarePalettes.All when useCustomColors is off.")]
        public int paletteIndex = 2;
        public bool useCustomColors = false;
        public Color customBody = new Color(0.62f, 0.46f, 0.29f);
        public Color customBodyDark = new Color(0.41f, 0.29f, 0.16f);
        public Color customBodyLight = new Color(0.79f, 0.65f, 0.45f);
        public Color customAccent = new Color(0.47f, 0.33f, 0.19f);
        public Color customLabel = new Color(0.89f, 0.82f, 0.69f);
        public Color customInk = new Color(0.24f, 0.16f, 0.10f);

        [Header("Decoration")]
        public LabelStyle label = LabelStyle.Horizontal;
        [Range(0.2f, 1f)]
        [Tooltip("Label patch width as a fraction of the body (used most by CenterPatch).")]
        public float labelWidth = 0.6f;
        public CornerStyle corner = CornerStyle.None;
        public BandMode bands = BandMode.None;
        [Range(1, 6)] public int bandCount = 2;
        public SpotMode spots = SpotMode.None;
        [Tooltip("Draw a lid strip across the top (jars/tins/crates).")]
        public bool hasLid = false;

        [Header("Output")]
        [Range(24, 64)] public int resolution = 48;
        [Range(2, 4)] public int damageStages = 3;
        [Tooltip("Sprite pixels-per-unit. Default == resolution so an intact Ware is roughly one world unit tall.")]
        public float pixelsPerUnit = 48f;

        /// The palette this Ware actually paints with — either a library scheme or the custom colours.
        public WarePalette Palette =>
            useCustomColors
                ? new WarePalette(customBody, customBodyDark, customBodyLight, customAccent, customLabel, customInk)
                : WarePalettes.Get(paletteIndex);

        /// A detached copy (not an asset) — used by the editor's variation grid so rolling variations never mutates
        /// the asset the user is tuning.
        public WareSpec Clone() => Instantiate(this);

        /// Roll a fresh random look. Randomises the VISUAL dials (and the seed, so the jagged damage differs too) but
        /// leaves output settings — resolution / damageStages / pixelsPerUnit — alone so a shelf stays uniform in size.
        public void Randomize(System.Random rng)
        {
            seed = rng.Next(int.MinValue, int.MaxValue);
            kind = (WareKind)rng.Next(5);

            // Bias the silhouette toward what the kind usually looks like, then let the rest roam free.
            switch (kind)
            {
                case WareKind.Book: shape = WareShape.Rectangular; widthRatio = Rand(rng, 0.28f, 0.5f); heightRatio = Rand(rng, 0.85f, 1f); break;
                case WareKind.Can: shape = WareShape.Round; widthRatio = Rand(rng, 0.45f, 0.68f); heightRatio = Rand(rng, 0.7f, 0.95f); break;
                case WareKind.Crate: shape = WareShape.Rectangular; widthRatio = Rand(rng, 0.8f, 1f); heightRatio = Rand(rng, 0.62f, 0.9f); break;
                case WareKind.Carton: shape = WareShape.Rectangular; widthRatio = Rand(rng, 0.6f, 0.9f); heightRatio = Rand(rng, 0.7f, 0.95f); break;
                default: // Box
                    shape = (WareShape)rng.Next(3); // Rectangular / RoundedRect / Round
                    widthRatio = Rand(rng, 0.5f, 0.95f); heightRatio = Rand(rng, 0.6f, 0.98f); break;
            }

            fill = (FillMode)rng.Next(4);
            useCustomColors = false;
            paletteIndex = WarePalettes.PickIndex(rng);

            label = (LabelStyle)rng.Next(4);
            labelWidth = Rand(rng, 0.4f, 0.9f);
            corner = (CornerStyle)rng.Next(4);
            bands = (BandMode)rng.Next(4);
            bandCount = rng.Next(1, 5);
            spots = (SpotMode)rng.Next(3);
            hasLid = kind == WareKind.Can || kind == WareKind.Crate ? rng.Next(2) == 0 : rng.Next(4) == 0;
        }

        static float Rand(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        void OnValidate()
        {
            damageStages = Mathf.Clamp(damageStages, 1, 4);
            resolution = Mathf.Clamp(resolution, 8, 128);
            if (pixelsPerUnit < 1f) pixelsPerUnit = 1f;
        }
    }
}
