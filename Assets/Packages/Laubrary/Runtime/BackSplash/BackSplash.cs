using UnityEngine;
using Laubrary.PreviewKit;

namespace Laubrary.BackSplash
{
    /// <summary>
    /// A saved test backdrop: a solid camera background colour plus one zoomable/positionable image.
    /// Recallable from any Laubrary tool with a preview (Pyre, Mirage, ...) — it's a plain asset type, no
    /// bespoke cross-tool picker needed, just an ObjectField. Deliberately minimal: no sprite placement
    /// (Mirage's own previewables cover that) and no skybox/gradient support.
    /// </summary>
    [CreateAssetMenu(menuName = "Laubrary/BackSplash/BackSplash", fileName = "BackSplash")]
    public class BackSplash : ScriptableObject, IVisualPreview
    {
        [Tooltip("Applied to a real Camera as clearFlags=SolidColor + this backgroundColor (Mirage); drawn as " +
                 "a flat fill behind the image by tools with an IMGUI-painted preview instead of a real Camera " +
                 "(Pyre).")]
        public Color cameraColor = new Color(0.08f, 0.08f, 0.10f);

        public Sprite image;
        public Color imageTint = Color.white;
        public float imageZoom = 1f;
        public Vector2 imagePos;

        // A hard bound on imagePos, applied at every edit site (the compact drag-pad, the raw X/Y fields, and
        // the full preview window's own free-drag) — reported: "most values end up showing the image off
        // screen," traced to imagePos being a raw, unbounded pixel offset that every edit path could push
        // arbitrarily far, with no way back except retyping 0. Every preview viewport this backdrop draws
        // into (Pyre, Mirage, this asset's own window) is on the order of a few hundred pixels, so this stays
        // generous enough for real large-offset use (showing just a corner of a big image) while ruling out
        // "dragged it to 40000 and now it's unrecoverable."
        public const float MaxImageOffset = 250f;

        public static Vector2 ClampImagePos(Vector2 pos) => new Vector2(
            Mathf.Clamp(pos.x, -MaxImageOffset, MaxImageOffset),
            Mathf.Clamp(pos.y, -MaxImageOffset, MaxImageOffset));

        // Static-only preview: colour fill + the image roughly centred at its own aspect ratio. Deliberately not
        // pixel-matching the editor's own imagePos/zoom viewport math (that's IMGUI Rect space, this is baked
        // texture space) — a thumbnail only needs to be recognisable, not a precise re-render.
        public Texture2D RenderPreviewTexture()
        {
            const int W = 128, H = 80;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = cameraColor;

            if (image != null && image.texture != null)
            {
                var srcTex = image.texture;
                var r = image.rect;
                float scale = Mathf.Min(W / r.width, H / r.height) * Mathf.Max(0.01f, imageZoom);
                int dw = Mathf.Clamp(Mathf.RoundToInt(r.width * scale), 1, W);
                int dh = Mathf.Clamp(Mathf.RoundToInt(r.height * scale), 1, H);
                int ox = (W - dw) / 2, oy = (H - dh) / 2;

                // Sample the sprite's own sub-rect of its (possibly atlas'd) source texture via a GPU blit
                // instead of Texture2D.GetPixels — GetPixels requires the source texture's "Read/Write Enabled"
                // import flag, which real imported sprites don't have by default (neither of this project's own
                // BackSplash images did), so it silently fell through to a flat colour-only thumbnail with no
                // picture at all. A blit reads GPU pixel DATA directly and works regardless of that flag.
                var rt = RenderTexture.GetTemporary(dw, dh, 0, RenderTextureFormat.ARGB32);
                var prevActive = RenderTexture.active;
                Vector2 uvScale = new Vector2(r.width / srcTex.width, r.height / srcTex.height);
                Vector2 uvOffset = new Vector2(r.x / srcTex.width, r.y / srcTex.height);
                Graphics.Blit(srcTex, rt, uvScale, uvOffset);
                RenderTexture.active = rt;
                var sampled = new Texture2D(dw, dh, TextureFormat.RGBA32, false);
                sampled.ReadPixels(new Rect(0f, 0f, dw, dh), 0, 0);
                sampled.Apply();
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);

                var srcPx = sampled.GetPixels();
                for (int y = 0; y < dh; y++)
                {
                    int py = oy + y;
                    if (py < 0 || py >= H) continue;
                    for (int x = 0; x < dw; x++)
                    {
                        int pxi = ox + x;
                        if (pxi < 0 || pxi >= W) continue;
                        var sc = srcPx[y * dw + x] * imageTint;
                        px[py * W + pxi] = Color.Lerp(px[py * W + pxi], sc, sc.a);
                    }
                }
                DestroyImmediate(sampled);
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        public bool CanAnimatePreview => false;
        public float PreviewFps => 0f;
        public void UpdateAnimatedPreview(Texture2D tex, double time) { }
    }

    /// <summary>
    /// A plain, non-asset copy of a BackSplash's fields — owned inline by a single consumer (MirageView,
    /// BlastSpec) instead of holding a shared reference to a <see cref="BackSplash"/> asset. Editing these
    /// fields (via BackSplashGUI.DrawInline) only ever touches THIS instance, never a shared preset — fixes a
    /// real bug where recalling a shared BackSplash asset into a view and editing it there instantly changed
    /// that same asset for every other view/tool referencing it. Recall copies values IN from a picked preset
    /// (CopyFrom); Save (BackSplashSavePopup) writes values OUT to a preset picked from a thumbnail browser or
    /// a newly named one. Deliberately does NOT remember which preset it came from — no "linked preset" this
    /// copy auto-overwrites, since that's exactly the kind of implicit state that caused the original bug.
    /// </summary>
    [System.Serializable]
    public class BackSplashSettings
    {
        public Color cameraColor = new Color(0.08f, 0.08f, 0.10f);
        public Sprite image;
        public Color imageTint = Color.white;
        public float imageZoom = 1f;
        public Vector2 imagePos;

        public void CopyFrom(BackSplash src)
        {
            if (src == null) return;
            cameraColor = src.cameraColor;
            image = src.image;
            imageTint = src.imageTint;
            imageZoom = src.imageZoom;
            imagePos = src.imagePos;
        }

        public void CopyTo(BackSplash dst)
        {
            if (dst == null) return;
            dst.cameraColor = cameraColor;
            dst.image = image;
            dst.imageTint = imageTint;
            dst.imageZoom = imageZoom;
            dst.imagePos = imagePos;
        }
    }
}
