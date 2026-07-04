using UnityEngine;

namespace Laubrary.Larder
{
    /// A Ware's six working colours. The generator only ever reads these, so a Ware's whole look can be re-skinned by
    /// swapping the palette — the silhouette/fill/label logic never hard-codes a colour. body is the base; bodyDark /
    /// bodyLight are its shading poles (gradients, glow, shadow); accent paints bands/corners/spots; label is the
    /// paper of the fake label patch; ink is the "printed" scribble on it.
    [System.Serializable]
    public struct WarePalette
    {
        public Color body;
        public Color bodyDark;
        public Color bodyLight;
        public Color accent;
        public Color label;
        public Color ink;

        public WarePalette(Color body, Color bodyDark, Color bodyLight, Color accent, Color label, Color ink)
        {
            this.body = body;
            this.bodyDark = bodyDark;
            this.bodyLight = bodyLight;
            this.accent = accent;
            this.label = label;
            this.ink = ink;
        }
    }

    /// A tiny fixed library of named colour schemes. Kept deliberately small and hand-picked so every random roll
    /// still looks like it belongs in the same grimy corner store: rusty tins, kraft cardboard, faded cola reds.
    public static class WarePalettes
    {
        static Color C(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);

        public static readonly WarePalette[] All =
        {
            // Rust — oxidised metal tin
            new WarePalette(C(140, 84, 54), C(84, 46, 30), C(196, 132, 92), C(70, 92, 70), C(214, 206, 182), C(48, 34, 26)),
            // Cannery — pale green food can
            new WarePalette(C(120, 158, 96), C(66, 96, 54), C(178, 206, 150), C(196, 76, 58), C(232, 228, 210), C(40, 54, 34)),
            // Kraft — brown cardboard / crate
            new WarePalette(C(158, 118, 74), C(104, 74, 42), C(202, 166, 116), C(120, 84, 48), C(226, 210, 176), C(60, 42, 26)),
            // Cola — faded red package
            new WarePalette(C(168, 52, 46), C(104, 28, 26), C(214, 96, 82), C(228, 200, 92), C(232, 226, 214), C(48, 22, 20)),
            // Mint — cold blue-green box
            new WarePalette(C(96, 164, 158), C(48, 100, 98), C(160, 214, 206), C(232, 132, 96), C(234, 236, 230), C(30, 58, 56)),
            // Mono — grey generic carton
            new WarePalette(C(150, 148, 146), C(92, 90, 90), C(202, 200, 196), C(96, 112, 140), C(228, 226, 222), C(44, 44, 46)),
        };

        public static WarePalette Get(int index) => All[((index % All.Length) + All.Length) % All.Length];

        public static int Count => All.Length;

        /// Pick a scheme with a deterministic rng (so preview == bake == runtime for the same seed).
        public static WarePalette Pick(System.Random rng) => All[rng.Next(All.Length)];
        public static int PickIndex(System.Random rng) => rng.Next(All.Length);
    }
}
