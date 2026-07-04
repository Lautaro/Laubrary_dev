using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Larder
{
    /// A dependency-free procedural pixel burst — the "explosion built from the sprite's own colours". Call the static
    /// Burst(pos, colours, scale) with colours sampled off the Ware's texture and it spawns a short-lived object that
    /// flings a dozen-odd tiny tinted quads outward under gravity, fading as they go, then deletes itself. The 1px
    /// white sprite every chunk shares is built once and cached, so a burst allocates nothing but the GameObjects.
    public class WareDebris : MonoBehaviour
    {
        static Sprite pixel;

        struct Chunk
        {
            public Transform t;
            public SpriteRenderer sr;
            public Vector3 vel;
            public Color color;
        }

        readonly List<Chunk> chunks = new();
        float life;
        float maxLife = 0.8f;
        float gravity = 9f;

        /// Spawn a burst at a world point. colours are the palette; scale sizes both the chunks and their speed.
        public static void Burst(Vector3 pos, IList<Color32> colors, float scale)
        {
            if (colors == null || colors.Count == 0) return;
            var go = new GameObject("WareDebris");
            go.transform.position = pos;
            go.AddComponent<WareDebris>().Spawn(colors, Mathf.Max(0.05f, scale));
        }

        void Spawn(IList<Color32> colors, float scale)
        {
            EnsurePixel();
            int n = colors.Count;
            for (int i = 0; i < n; i++)
            {
                var child = new GameObject("chunk");
                child.transform.SetParent(transform, false);
                float px = Random.Range(0.03f, 0.09f) * scale * 8f;
                child.transform.localScale = Vector3.one * px;

                var sr = child.AddComponent<SpriteRenderer>();
                sr.sprite = pixel;
                Color col = colors[i];
                sr.color = col;
                sr.sortingOrder = 500;

                Vector2 dir = Random.insideUnitCircle.normalized;
                if (dir == Vector2.zero) dir = Vector2.up;
                float speed = Random.Range(1.2f, 4.5f) * scale;
                chunks.Add(new Chunk
                {
                    t = child.transform,
                    sr = sr,
                    vel = new Vector3(dir.x * speed, dir.y * speed + Random.Range(0.5f, 2f) * scale, 0f),
                    color = col
                });
            }
        }

        void Update()
        {
            life += Time.deltaTime;
            float k = Mathf.Clamp01(1f - life / maxLife);
            for (int i = 0; i < chunks.Count; i++)
            {
                var c = chunks[i];
                c.vel.y -= gravity * Time.deltaTime;
                c.t.position += c.vel * Time.deltaTime;
                var col = c.color;
                col.a = k;
                c.sr.color = col;
                chunks[i] = c;
            }
            if (life >= maxLife) Destroy(gameObject);
        }

        static void EnsurePixel()
        {
            if (pixel != null) return;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "WareDebrisPixel"
            };
            tex.SetPixels32(new[] { new Color32(255, 255, 255, 255) });
            tex.Apply();
            pixel = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            pixel.name = "WareDebrisPixel";
        }
    }
}
