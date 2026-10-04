using System;
using UnityEngine;
using Laubrary.Chunks;

namespace Laubrary.GoreLab
{
    // Flying pieces, crumbs and blood. Everything flying is a pooled Chunk; the sprites made for pieces are destroyed when their chunk finishes.
    public sealed partial class GoreBody
    {
        DebrisScatter _debris;
        Sprite _dot;
        Func<double> _rng;
        bool _bleeding;
        float _bleedTime;
        float _bleedClock;

        const float BleedInterval = 0.07f;
        const float BleedCone = 0.21f;          // half angle in radians (about 12 degrees)
        const float BleedStopBelow = 0.04f;

        void EnsureDebris(float ppu)
        {
            if (_debris == null) _debris = new DebrisScatter { drag = 0.3f, useFloor = true, floorFriction = 0.6f, restOnFloor = true };
            _debris.gravity = gravity;
            _debris.bounciness = bounciness;
            _debris.floorY = transform.position.y;     // the character's feet: its pivot is at the bottom
            if (_rng == null) _rng = GoreRng.Rng(rig.cut.seed * 7 + 11);
            if (_dot == null)
            {
                var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "GoreDot" };
                tex.SetPixel(0, 0, Color.white);
                tex.Apply(false);
                _dot = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect);
                _dot.name = "GoreDot";
            }
        }

        void DestroyDebrisAssets()
        {
            if (_dot != null) { if (_dot.texture != null) Destroy(_dot.texture); Destroy(_dot); _dot = null; }
        }

        static Color32 Unpack(uint c) { return new Color32((byte)(c & 255), (byte)((c >> 8) & 255), (byte)((c >> 16) & 255), 255); }

        Chunk Launch(Sprite sprite, Vector3 pos, Vector2 velocity, float spin, float worldSize, Color tint, Action onDone)
        {
            var chunk = ChunkPool.Get();
            var sr = chunk.GetComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerID = _sr.sortingLayerID;
            sr.sortingOrder = _sr.sortingOrder + 1;
            chunk.transform.SetParent(null);
            chunk.transform.position = pos;
            chunk.Init(_debris, velocity, spin, pieceLife, worldSize, tint, null, false, null, null, null, true);
            Action done = null;
            done = () => { chunk.Finished -= done; ChunkPool.Release(chunk); if (onDone != null) onDone(); };
            chunk.Finished += done;
            return chunk;
        }

        Sprite MakePieceSprite(GorePiece p, float ppu)
        {
            int gw = p.grid.w, gh = p.grid.h;
            var tex = new Texture2D(gw, gh, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "GorePiece" };
            var up = new uint[gw * gh];
            for (int y = 0; y < gh; y++) Array.Copy(p.grid.px, y * gw, up, (gh - 1 - y) * gw, gw);   // texture rows run bottom-up
            tex.SetPixelData(up, 0);
            tex.Apply(false);
            var s = Sprite.Create(tex, new Rect(0, 0, gw, gh), new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect);
            s.name = "GorePiece";
            return s;
        }

        // What the newest wound tore off: pieces of 3+ pixels become bodies of their own, crumbs become single flying pixels.
        void SpawnDebris(GoreViewSlot slot, GoreFrameResult r, bool flip)
        {
            float ppu = slot.source.pixelsPerUnit <= 0f ? 16f : slot.source.pixelsPerUnit;
            EnsureDebris(ppu);
            float scale = Mathf.Abs(_sr.transform.lossyScale.x);
            Vector2 lift = Vector2.up * launchLift;

            for (int i = 0; i < r.pieces.Count; i++)
            {
                GorePiece p = r.pieces[i];
                Sprite sprite = MakePieceSprite(p, ppu);
                Vector3 pos = ViewToWorld(slot.source, flip, p.x + p.grid.w * 0.5, p.y + p.grid.h * 0.5);
                Vector2 dir = new Vector2((float)p.outX, -(float)p.outY);
                dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.up;
                Vector2 v = dir * launchSpeed * (0.8f + 0.4f * (float)_rng()) + lift;
                float spin = ((float)_rng() - 0.5f) * 720f;
                float size = Mathf.Max(p.grid.w, p.grid.h) / ppu * scale;
                Launch(sprite, pos, v, spin, size, Color.white, () => { if (sprite != null) { if (sprite.texture != null) Destroy(sprite.texture); Destroy(sprite); } });
            }

            float chance = Mathf.Clamp01(gore * 1.2f);
            Color blood = Unpack(rig.style.blood.Length > 1 ? rig.style.blood[1] : 0xFF0606C0u);
            Vector2 baseDir = r.pieces.Count > 0 ? new Vector2((float)r.pieces[0].outX, -(float)r.pieces[0].outY) : new Vector2((float)r.outX, -(float)r.outY);
            baseDir = baseDir.sqrMagnitude > 1e-6f ? baseDir.normalized : Vector2.up;
            for (int i = 0; i < r.gibs.Count; i++)
            {
                if ((float)_rng() > chance) continue;
                GoreGib g = r.gibs[i];
                Vector3 pos = ViewToWorld(slot.source, flip, g.x, g.y);
                float ang = ((float)_rng() - 0.5f) * 0.9f, ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                Vector2 d = new Vector2(baseDir.x * ca - baseDir.y * sa, baseDir.x * sa + baseDir.y * ca);
                Vector2 v = d * launchSpeed * (0.3f + 0.7f * (float)_rng()) + lift * (float)_rng();
                Color c = Color.Lerp(Unpack(g.colour), blood, 0.22f);
                Launch(_dot, pos, v, 0f, scale / ppu, c, null);
            }
        }

        // ------------------------------------------------------------------ bleeding: drops from the wound of the frame on screen, fading by itself

        void StartBleeding() { _bleeding = true; _bleedTime = 0f; _bleedClock = 0f; }
        void StopBleeding() { _bleeding = false; }

        void TickBleed()
        {
            if (!_bleeding || !Ready) return;
            _bleedTime += Time.deltaTime;
            float pressure = bleedPressure * Mathf.Exp(-_bleedTime / Mathf.Max(0.05f, bleedFade));
            if (pressure < BleedStopBelow || gore <= 0f) { if (pressure < BleedStopBelow) _bleeding = false; return; }
            Sprite drawn = _player.CurrentSprite;
            if (drawn == null || !_sr.enabled) return;
            bool flip = _player.flipX;
            if (!_slots.TryGetValue((drawn, flip), out var slot) || slot == null || slot.bleed.Length == 0) return;

            _bleedClock += Time.deltaTime;
            float ppu = drawn.pixelsPerUnit <= 0f ? 16f : drawn.pixelsPerUnit;
            EnsureDebris(ppu);
            float scale = Mathf.Abs(_sr.transform.lossyScale.x);
            int guard = 0;
            while (_bleedClock >= BleedInterval && guard++ < 4)
            {
                _bleedClock -= BleedInterval;
                int n = Mathf.CeilToInt(pressure * 3f * gore);
                for (int k = 0; k < n; k++)
                {
                    GoreBleedPoint bp = slot.bleed[(int)(_rng() * slot.bleed.Length) % slot.bleed.Length];
                    Vector3 pos = ViewToWorld(slot.source, flip, bp.x + 0.5, bp.y + 0.5);
                    float ang = ((float)_rng() - 0.5f) * 2f * BleedCone, ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                    Vector2 d = new Vector2(bp.nx, -bp.ny);
                    d = d.sqrMagnitude > 1e-6f ? d.normalized : Vector2.up;
                    d = new Vector2(d.x * ca - d.y * sa, d.x * sa + d.y * ca);
                    Vector2 v = d * bleedSpeed * pressure * (0.4f + 0.6f * (float)_rng());
                    uint[] palette = rig.style.blood;
                    Color c = Unpack(palette[(int)(_rng() * palette.Length) % palette.Length]);
                    Launch(_dot, pos, v, 0f, scale / ppu, c, null);
                }
            }
        }
    }
}
