using System.Collections.Generic;
using UnityEngine;

/// Demo-only: a selectable full-screen background behind the shmup. Ships a few procedural backgrounds and also
/// loads any Sprites you drop in a `Resources/DemoBackgrounds` folder — so you can pick an image at runtime.
/// index -1 = none (the camera's solid colour shows through).
public class DemoBackground : MonoBehaviour
{
    readonly List<Sprite> backgrounds = new();
    SpriteRenderer sr;
    int index = -1;

    void Awake()
    {
        backgrounds.Add(MakeGradient("bg_night", new Color(0.10f, 0.05f, 0.16f), new Color(0.02f, 0.03f, 0.10f)));
        backgrounds.Add(MakeStarfield("bg_stars", new Color(0.02f, 0.02f, 0.06f)));
        backgrounds.Add(MakeGradient("bg_dusk", new Color(0.18f, 0.06f, 0.12f), new Color(0.02f, 0.02f, 0.05f)));
        var loaded = Resources.LoadAll<Sprite>("DemoBackgrounds");   // user-supplied images (optional)
        if (loaded != null) backgrounds.AddRange(loaded);

        var go = new GameObject("Background Sprite");
        go.transform.SetParent(transform, false);
        sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = -1000;
        sr.enabled = false;
    }

    public int Count => backgrounds.Count;
    public int Index => index;

    public void SetIndex(int i)
    {
        index = Mathf.Clamp(i, -1, Count - 1);
        if (sr == null) return;
        if (index < 0 || index >= Count || backgrounds[index] == null) { sr.enabled = false; return; }
        sr.enabled = true;
        sr.sprite = backgrounds[index];
        Fit();
    }

    void LateUpdate() { if (sr != null && sr.enabled) Fit(); }

    void Fit()
    {
        var cam = Camera.main;
        if (cam == null || !cam.orthographic || sr.sprite == null) return;
        sr.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 10f);
        float ch = cam.orthographicSize * 2f, cw = ch * cam.aspect;
        var sz = sr.sprite.bounds.size;
        if (sz.x > 0.0001f && sz.y > 0.0001f)
            sr.transform.localScale = new Vector3(cw / sz.x, ch / sz.y, 1f) * 1.02f;
    }

    static Sprite MakeGradient(string name, Color top, Color bottom)
    {
        int w = 32, h = 64;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            var c = Color.Lerp(bottom, top, y / (float)(h - 1));
            for (int x = 0; x < w; x++) px[y * w + x] = c;
        }
        tex.SetPixels(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16f);
    }

    static Sprite MakeStarfield(string name, Color bg)
    {
        int w = 96, h = 128;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = bg;
        var rng = new System.Random(12345);
        int stars = w * h / 55;
        for (int s = 0; s < stars; s++)
        {
            int x = rng.Next(w), y = rng.Next(h);
            float b = 0.4f + (float)rng.NextDouble() * 0.6f;
            px[y * w + x] = new Color(b, b, b * 0.95f, 1f);
        }
        tex.SetPixels(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16f);
    }
}
