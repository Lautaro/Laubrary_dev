using System;

namespace Laubrary.Audio {
    // Match Mathf's scalar float arithmetic without taking an engine dependency. Burst supports these Math intrinsics.
    internal static class AudioMath {
        public const float PI = (float)Math.PI;
        public static float Pow(float a, float b) => (float)Math.Pow(a, b);
        public static float Sin(float x) => (float)Math.Sin(x);
        public static float Cos(float x) => (float)Math.Cos(x);
        public static float Tan(float x) => (float)Math.Tan(x);
        public static float Exp(float x) => (float)Math.Exp(x);
        public static float Log(float x) => (float)Math.Log(x);
        public static float Log(float x, float b) => (float)Math.Log(x, b);
        public static float Abs(float x) => Math.Abs(x);
        public static float Round(float x) => (float)Math.Round(x);
        public static int CeilToInt(float x) => (int)Math.Ceiling(x);
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Clamp(float x, float lo, float hi) { if (x < lo) x = lo; else if (x > hi) x = hi; return x; }
        public static float Clamp01(float x) { if (x < 0f) return 0f; if (x > 1f) return 1f; return x; }
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
    }

    public static class AudioControl {
        public const int BlockFrames = 64;
        public const float SilenceLinear = 0.001f;
        public const float MaxTailSeconds = 10f;
    }
}
