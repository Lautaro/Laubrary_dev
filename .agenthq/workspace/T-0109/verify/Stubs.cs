using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class SerializeField : Attribute { }
    public class SerializeReference : Attribute { }
    public class TooltipAttribute : Attribute { public TooltipAttribute(string s) { } }
    public class RangeAttribute : Attribute { public RangeAttribute(float a, float b) { } }
    public class HeaderAttribute : Attribute { public HeaderAttribute(string s) { } }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero { get { return new Vector2(0, 0); } }
        public static Vector2 one { get { return new Vector2(1, 1); } }
        public static Vector2 operator +(Vector2 a, Vector2 b) { return new Vector2(a.x + b.x, a.y + b.y); }
        public static Vector2 operator -(Vector2 a, Vector2 b) { return new Vector2(a.x - b.x, a.y - b.y); }
        public static Vector2 operator *(Vector2 a, float b) { return new Vector2(a.x * b, a.y * b); }
        public float magnitude { get { return (float)Math.Sqrt(x * x + y * y); } }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public Vector3(float x, float y) { this.x = x; this.y = y; this.z = 0; }
        public static Vector3 zero { get { return new Vector3(0, 0, 0); } }
        public static Vector3 one { get { return new Vector3(1, 1, 1); } }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 right { get { return new Vector3(1, 0, 0); } }
        public static Vector3 forward { get { return new Vector3(0, 0, 1); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator -(Vector3 a) { return new Vector3(-a.x, -a.y, -a.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
        public static Vector3 operator *(float b, Vector3 a) { return new Vector3(a.x * b, a.y * b, a.z * b); }
        public static Vector3 operator /(Vector3 a, float b) { return new Vector3(a.x / b, a.y / b, a.z / b); }
        public float magnitude { get { return (float)Math.Sqrt(x * x + y * y + z * z); } }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public Vector3 normalized { get { float m = magnitude; return m > 0 ? new Vector3(x / m, y / m, z / m) : zero; } }
        public static float Dot(Vector3 a, Vector3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
        public static Vector3 Normalize(Vector3 a) { return a.normalized; }
        public static Vector3 Cross(Vector3 a, Vector3 b) { return new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x); }
        public override string ToString() { return "(" + x + ", " + y + ", " + z + ")"; }
    }

    public struct Vector4 { public float x, y, z, w; public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; } }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; this.a = 1f; }
        public static Color white { get { return new Color(1, 1, 1, 1); } }
        public static Color black { get { return new Color(0, 0, 0, 1); } }
        public static Color red { get { return new Color(1, 0, 0, 1); } }
        public static Color green { get { return new Color(0, 1, 0, 1); } }
        public static Color blue { get { return new Color(0, 0, 1, 1); } }
        public static Color gray { get { return new Color(.5f, .5f, .5f, 1); } }
        public static Color clear { get { return new Color(0, 0, 0, 0); } }
        public static Color operator *(Color c, float f) { return new Color(c.r * f, c.g * f, c.b * f, c.a * f); }
        public static Color operator *(Color c, Color d) { return new Color(c.r * d.r, c.g * d.g, c.b * d.b, c.a * d.a); }
        public static Color operator +(Color a, Color b) { return new Color(a.r + b.r, a.g + b.g, a.b + b.b, a.a + b.a); }
        public static Color operator -(Color a, Color b) { return new Color(a.r - b.r, a.g - b.g, a.b - b.b, a.a - b.a); }
        public static Color Lerp(Color a, Color b, float t) { return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t); }
        public float this[int i]
        {
            get { return i == 0 ? r : i == 1 ? g : i == 2 ? b : a; }
            set { if (i == 0) r = value; else if (i == 1) g = value; else if (i == 2) b = value; else a = value; }
        }
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
        public float xMin { get { return x; } }
        public float yMin { get { return y; } }
        public float xMax { get { return x + width; } }
        public float yMax { get { return y + height; } }
    }

    public struct GradientColorKey { public Color color; public float time; public GradientColorKey(Color c, float t) { color = c; time = t; } }
    public struct GradientAlphaKey { public float alpha; public float time; public GradientAlphaKey(float a, float t) { alpha = a; time = t; } }

    public class Gradient
    {
        public GradientColorKey[] colorKeys = new GradientColorKey[0];
        public GradientAlphaKey[] alphaKeys = new GradientAlphaKey[0];
        public Color Evaluate(float t) { return Color.white; }
        public void SetKeys(GradientColorKey[] c, GradientAlphaKey[] a) { colorKeys = c; alphaKeys = a; }
    }

    public class AnimationCurve
    {
        public float Evaluate(float t) { return t; }
        public static AnimationCurve Linear(float a, float b, float c, float d) { return new AnimationCurve(); }
        public static AnimationCurve EaseInOut(float a, float b, float c, float d) { return new AnimationCurve(); }
    }

    public class Texture2D { public int width, height; public string name = ""; public bool isReadable = true; public Texture2D(int w, int h) { width = w; height = h; } public Color32[] GetPixels32() { return new Color32[width*height]; } }

    public static class Mathf
    {
        public const float PI = 3.14159265358979f;
        public const float Infinity = float.PositiveInfinity;
        public const float NegativeInfinity = float.NegativeInfinity;
        public const float Epsilon = 1.401298E-45f;
        public const float Deg2Rad = 0.0174532924f;
        public const float Rad2Deg = 57.29578f;
        public static float Sqrt(float f) { return (float)Math.Sqrt(f); }
        public static float Abs(float f) { return Math.Abs(f); }
        public static int Abs(int f) { return Math.Abs(f); }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static float Max(float a, float b, float c) { return Max(Max(a, b), c); }
        public static int Max(int a, int b) { return a > b ? a : b; }
        public static float Min(float a, float b) { return a < b ? a : b; }
        public static float Min(float a, float b, float c) { return Min(Min(a, b), c); }
        public static int Min(int a, int b) { return a < b ? a : b; }
        public static float Clamp(float v, float a, float b) { return v < a ? a : (v > b ? b : v); }
        public static int Clamp(int v, int a, int b) { return v < a ? a : (v > b ? b : v); }
        public static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
        public static float Floor(float f) { return (float)Math.Floor(f); }
        public static float Ceil(float f) { return (float)Math.Ceiling(f); }
        public static int FloorToInt(float f) { return (int)Math.Floor(f); }
        public static int CeilToInt(float f) { return (int)Math.Ceiling(f); }
        public static int RoundToInt(float f) { return (int)Math.Round((double)f, MidpointRounding.ToEven); }
        public static float Round(float f) { return (float)Math.Round((double)f, MidpointRounding.ToEven); }
        public static float Pow(float a, float b) { return (float)Math.Pow(a, b); }
        public static float Exp(float a) { return (float)Math.Exp(a); }
        public static float Log(float a) { return (float)Math.Log(a); }
        public static float Log10(float a) { return (float)Math.Log10(a); }
        public static float Sin(float a) { return (float)Math.Sin(a); }
        public static float Cos(float a) { return (float)Math.Cos(a); }
        public static float Tan(float a) { return (float)Math.Tan(a); }
        public static float Atan(float a) { return (float)Math.Atan(a); }
        public static float Atan2(float a, float b) { return (float)Math.Atan2(a, b); }
        public static float Acos(float a) { return (float)Math.Acos(a); }
        public static float Asin(float a) { return (float)Math.Asin(a); }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); }
        public static float LerpUnclamped(float a, float b, float t) { return a + (b - a) * t; }
        public static float InverseLerp(float a, float b, float v) { return a == b ? 0f : Clamp01((v - a) / (b - a)); }
        public static float Sign(float f) { return f >= 0f ? 1f : -1f; }
        public static float Repeat(float t, float l) { return Clamp(t - (float)Math.Floor(t / l) * l, 0f, l); }
        public static float MoveTowards(float a, float b, float d) { return Math.Abs(b - a) <= d ? b : a + Sign(b - a) * d; }
        public static bool Approximately(float a, float b) { return Math.Abs(b - a) < 1e-6f; }
        public static float SmoothStep(float a, float b, float t) { t = Clamp01(t); t = t * t * (3f - 2f * t); return a + (b - a) * t; }
        public static float PerlinNoise(float a, float b) { return 0.5f; }
    }

    public static class Debug
    {
        public static void Log(object o) { }
        public static void LogWarning(object o) { }
        public static void LogError(object o) { }
    }

    public static class Random
    {
        static System.Random r = new System.Random(12345);
        public static float value { get { return (float)r.NextDouble(); } }
        public static float Range(float a, float b) { return a + (float)r.NextDouble() * (b - a); }
        public static int Range(int a, int b) { return r.Next(a, b); }
        public static void InitState(int s) { r = new System.Random(s); }
    }

    public class Object { public string name; }
}

namespace UnityEngine
{
    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color32(Color c)
        {
            return new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f), (byte)(Mathf.Clamp01(c.b) * 255f), (byte)(Mathf.Clamp01(c.a) * 255f));
        }
        public static implicit operator Color(Color32 c) { return new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f); }
    }
}

[System.Serializable]
public class ZuiGradient
{
    public UnityEngine.Color Evaluate(float t) { return UnityEngine.Color.white; }
    public UnityEngine.Color Evaluate(float t, float phase, float life) { return UnityEngine.Color.white; }
    public UnityEngine.Gradient ToGradient() { return new UnityEngine.Gradient(); }
}
