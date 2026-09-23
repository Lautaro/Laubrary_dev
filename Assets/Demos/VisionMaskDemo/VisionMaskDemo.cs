using System.Collections.Generic;
using Laubrary.VisionMask;
using UnityEngine;

namespace Laubrary.Demos
{
    /// VisionMask demo: a dark room, a flashlight sweeping back and forth, three walls that cast shadows, and
    /// monsters wandering through the light. Watch a monster cross the cone's edge or a wall's shadow: only
    /// the part of it under the light is drawn, cut along the light's exact edge — never the whole monster,
    /// never a faded monster. The floor is drawn with the same mask at a low hidden alpha, so the lit area
    /// you see IS the area the mask uses.
    ///
    /// Everything is built from code-generated textures at Play, so the demo ships no assets.
    public class VisionMaskDemo : MonoBehaviour
    {
        [Tooltip("Full angle of the flashlight cone, degrees.")]
        [Range(5f, 360f)] public float coneAngle = 55f;
        [Tooltip("How far the flashlight reaches, world units.")]
        public float coneRange = 9f;
        [Tooltip("Glow around the player seen in every direction and through walls.")]
        public float bodyGlow = 1.2f;
        [Tooltip("Degrees per second the flashlight sweeps.")]
        public float sweepSpeed = 25f;
        [Tooltip("How many monsters wander the room.")]
        [Range(1, 20)] public int monsters = 6;
        [Tooltip("Monster speed, world units per second.")]
        public float monsterSpeed = 1.2f;
        [Tooltip("Visibility of the floor outside the light: shows where the mask draws.")]
        [Range(0f, 1f)] public float floorGhost = 0.12f;
        [Tooltip("Seed for monster placement and heading.")]
        public int seed = 7;

        VisionCone cone;
        readonly List<Transform> walkers = new();
        readonly List<Vector2> headings = new();
        readonly Rect room = new Rect(-11f, -6f, 22f, 12f);   // local to this object: the demo can sit anywhere
        float t;

        void Start()
        {
            VisionMask.VisionMask.Plane = VisionPlane.XY;
            var rng = new System.Random(seed);
            var px = MakeTex(4, 4, (x, y) => Color.white);

            // Floor: masked with a faint ghost, so the lit shape is visible on screen.
            var floor = SpriteObj("Floor", px, 4f, new Color(0.55f, 0.6f, 0.7f), 0);
            floor.transform.localPosition = Vector3.zero;
            floor.transform.localScale = new Vector3(room.width, room.height, 1f);
            VisionMask.VisionMask.Mask(floor.gameObject, floorGhost);

            // Walls: always drawn, and they block the light.
            foreach (var w in new[] { new Rect(3f, 0.5f, 0.6f, 4f), new Rect(-4f, -4.5f, 5f, 0.6f), new Rect(6f, -3.5f, 0.6f, 2.5f) })
            {
                var wall = SpriteObj("Wall", px, 4f, new Color(0.25f, 0.25f, 0.3f), 5);
                wall.transform.localPosition = new Vector3(w.center.x, w.center.y, 0f);
                wall.transform.localScale = new Vector3(w.width, w.height, 1f);
                wall.gameObject.AddComponent<BoxCollider2D>().size = Vector2.one;   // the sprite is 1x1 local; the scale makes it the wall
            }

            // Player + flashlight.
            var player = SpriteObj("Player", Blob(16, new Color(0.3f, 0.9f, 1f)), 16f, Color.white, 20);
            player.transform.localPosition = new Vector3(-7f, 0f, 0f);
            var eye = new GameObject("Flashlight");
            eye.transform.SetParent(player.transform, false);
            cone = eye.AddComponent<VisionCone>();
            cone.angle = coneAngle; cone.range = coneRange; cone.omniRadius = bodyGlow;
            cone.occluders = 1 << 0;   // walls sit on Default; monsters have no colliders

            // Monsters: masked, fully invisible outside the light.
            for (int i = 0; i < monsters; i++)
            {
                var col = Color.HSVToRGB((float)rng.NextDouble(), 0.7f, 1f);
                var m = SpriteObj("Monster " + (i + 1), Blob(24, col), 16f, Color.white, 10);
                m.transform.localPosition = new Vector3(Mathf.Lerp(-3f, 10f, (float)rng.NextDouble()), Mathf.Lerp(-5f, 5f, (float)rng.NextDouble()), 0f);
                float a = (float)(rng.NextDouble() * Mathf.PI * 2);
                headings.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
                walkers.Add(m.transform);
                VisionMask.VisionMask.Mask(m.gameObject);
            }
        }

        void Update()
        {
            if (cone == null) return;
            cone.angle = coneAngle; cone.range = coneRange; cone.omniRadius = bodyGlow;
            t += Time.deltaTime;
            float aim = Mathf.Sin(t * sweepSpeed * Mathf.Deg2Rad * 2f) * 35f;
            cone.SetAim(new Vector2(Mathf.Cos(aim * Mathf.Deg2Rad), Mathf.Sin(aim * Mathf.Deg2Rad)));

            for (int i = 0; i < walkers.Count; i++)
            {
                var p = (Vector2)walkers[i].localPosition + headings[i] * monsterSpeed * Time.deltaTime;
                var h = headings[i];
                if (p.x < room.xMin + 1f || p.x > room.xMax - 0.5f) h.x = -h.x;
                if (p.y < room.yMin + 0.5f || p.y > room.yMax - 0.5f) h.y = -h.y;
                headings[i] = h;
                walkers[i].localPosition = new Vector3(p.x, p.y, 0f);
                var sr = walkers[i].GetComponent<SpriteRenderer>();
                if (sr != null) sr.flipX = h.x < 0f;   // faces its walk: the mask keeps flips intact
            }
        }

        SpriteRenderer SpriteObj(string name, Texture2D tex, float ppu, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu);
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        /// A round critter with two eyes looking to the right — asymmetric so a flip is visible.
        static Texture2D Blob(int size, Color body)
        {
            float r = size * 0.5f - 0.5f;
            return MakeTex(size, size, (x, y) =>
            {
                float dx = x - r, dy = y - r;
                if (dx * dx + dy * dy > r * r) return Color.clear;
                float ex = size * 0.68f, ey = size * 0.62f;
                if ((x - ex) * (x - ex) + (y - ey) * (y - ey) < size * size * 0.012f) return Color.black;
                if ((x - ex + size * 0.22f) * (x - ex + size * 0.22f) + (y - ey) * (y - ey) < size * size * 0.012f) return Color.black;
                return body * (0.75f + 0.25f * (y / (float)size));
            });
        }

        static Texture2D MakeTex(int w, int h, System.Func<int, int, Color> f)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) t.SetPixel(x, y, f(x, y));
            t.Apply();
            return t;
        }
    }
}
