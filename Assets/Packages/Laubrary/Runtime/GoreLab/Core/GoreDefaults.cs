// Default values of the prototype, in one place. The engine owner may refine this file; the Fleshy palette is the prototype's.
namespace Laubrary.GoreLab
{
    public static class GoreDefaults
    {
        public static uint Rgb(int r, int g, int b) { return 0xFF000000u | ((uint)b << 16) | ((uint)g << 8) | (uint)r; }

        public static GoreStyle FleshyStyle()
        {
            return new GoreStyle
            {
                flesh = new[] { Rgb(110, 12, 14), Rgb(150, 22, 22), Rgb(190, 40, 34), Rgb(224, 94, 72) },
                bone = new[] { Rgb(232, 222, 196), Rgb(196, 184, 152) },
                blood = new[] { Rgb(70, 0, 0), Rgb(120, 6, 6), Rgb(170, 14, 14), Rgb(214, 34, 28), Rgb(244, 78, 56) },
                crater = Rgb(92, 6, 10),
                goreDark = Rgb(78, 4, 8),
            };
        }
    }
}
