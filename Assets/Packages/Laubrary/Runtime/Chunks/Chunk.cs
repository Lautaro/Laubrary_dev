using UnityEngine;

namespace Laubrary.Chunks
{
    /// One flying debris bit. It carries its own velocity, spin and lifetime and self-manages every frame:
    /// integrate gravity + air drag, move, spin (or face its travel direction), bounce/settle on an optional floor,
    /// evaluate size / alpha / tint over its normalised life, and destroy itself when its life ends (or shortly
    /// after it settles). The <see cref="ChunkEmitter"/> creates it, adds a SpriteRenderer, and calls <see cref="Init"/>.
    /// If handed an <see cref="IChunkAnimation"/> it also cycles that animation's frames instead of showing a
    /// static sprite — the size/alpha/tint-over-life curves still apply on top, so a Pyre fireball instance can
    /// shrink/fade via the spec exactly like a plain chunk does.
    /// Everything is null-guarded so a half-wired chunk goes inert rather than throwing. Uses scaled Time.deltaTime.
    [DisallowMultipleComponent]
    public class Chunk : MonoBehaviour
    {
        ChunkSpec spec;
        SpriteRenderer sr;

        Vector2 velocity;
        float angularVel;      // deg/s (already signed)
        float spinAngle;       // accumulated rotation for the free-spin case
        float life;            // seconds elapsed
        float maxLife;         // seconds total
        float baseScale;       // local scale that yields the requested world size at sizeMul == 1
        Color baseColor;       // per-chunk tint (palette colour or white), before the gradient/alpha
        bool settled;          // came to rest on the floor

        IChunkAnimation anim;
        Sprite[] animFrames;
        float animClock;

        const float SettleSpeed = 0.4f;   // below this on the floor, a chunk rests/despawns
        const float FloorEps = 0.0001f;

        /// Configure a freshly created chunk. worldSize is the desired on-screen size in world units.
        /// animation, if supplied, is cycled instead of the sprite the emitter assigned.
        public void Init(ChunkSpec spec, Vector2 velocity, float angularVel, float life, float worldSize, Color baseColor,
                         IChunkAnimation animation = null)
        {
            this.spec = spec;
            this.velocity = velocity;
            this.angularVel = angularVel;
            this.maxLife = Mathf.Max(0.01f, life);
            this.baseColor = baseColor;
            this.life = 0f;
            this.settled = false;

            sr = GetComponent<SpriteRenderer>();

            anim = animation;
            animFrames = anim?.GetFrames();
            animClock = 0f;
            if (animFrames != null && animFrames.Length > 0 && sr != null)
                sr.sprite = animFrames[0];

            float spriteUnit = 1f;
            if (sr != null && sr.sprite != null)
                spriteUnit = Mathf.Max(sr.sprite.bounds.size.x, sr.sprite.bounds.size.y);
            baseScale = Mathf.Max(0.0001f, worldSize) / Mathf.Max(0.0001f, spriteUnit);

            transform.localScale = Vector3.one * baseScale;
            ApplyLook(0f);
        }

        void Update()
        {
            if (spec == null) { Destroy(gameObject); return; }

            float dt = Time.deltaTime;
            life += dt;

            if (!settled)
            {
                // integrate: gravity, then exponential air drag, then move
                velocity.y -= spec.gravity * dt;
                if (spec.drag > 0f) velocity *= Mathf.Exp(-spec.drag * dt);

                Vector3 pos = transform.position;
                pos.x += velocity.x * dt;
                pos.y += velocity.y * dt;

                if (spec.useFloor && pos.y <= spec.floorY + FloorEps && velocity.y <= 0f)
                {
                    pos.y = spec.floorY;
                    velocity.y = -velocity.y * spec.bounciness;
                    velocity.x *= 1f - spec.floorFriction;

                    if (velocity.magnitude < SettleSpeed)
                    {
                        if (spec.restOnFloor)
                        {
                            settled = true;
                            velocity = Vector2.zero;
                        }
                        else
                        {
                            // not resting on the floor: settle briefly then despawn (a short linger, not a blink-out)
                            settled = true;
                            velocity = Vector2.zero;
                            maxLife = Mathf.Min(maxLife, life + 0.05f);
                        }
                    }
                }

                transform.position = pos;

                // orientation
                if (spec.faceVelocity)
                {
                    if (velocity.sqrMagnitude > 0.0001f)
                    {
                        float a = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
                        transform.rotation = Quaternion.Euler(0f, 0f, a);
                    }
                }
                else
                {
                    spinAngle += angularVel * dt;
                    transform.rotation = Quaternion.Euler(0f, 0f, spinAngle);
                }
            }

            if (animFrames != null && animFrames.Length > 0 && sr != null)
            {
                animClock += dt * Mathf.Max(0.01f, anim.Fps);
                int frame = Mathf.FloorToInt(animClock);
                if (frame >= animFrames.Length)
                {
                    if (anim.Loop) { animClock %= animFrames.Length; frame = Mathf.FloorToInt(animClock); }
                    else frame = animFrames.Length - 1;
                }
                sr.sprite = animFrames[frame];
            }

            ApplyLook(Mathf.Clamp01(life / maxLife));

            if (life >= maxLife) Destroy(gameObject);
        }

        // Size / colour / alpha at normalised life t (0→1).
        void ApplyLook(float t)
        {
            float sizeMul = spec.sizeOverLife != null ? spec.sizeOverLife.Evaluate(t) : 1f;
            transform.localScale = Vector3.one * (baseScale * Mathf.Max(0f, sizeMul));

            if (sr == null) return;
            Color tint = spec.colorOverLife != null ? spec.colorOverLife.Evaluate(t) : Color.white;
            float alpha = spec.alphaOverLife != null ? spec.alphaOverLife.Evaluate(t) : 1f;
            Color c = baseColor * tint;
            c.a = baseColor.a * tint.a * Mathf.Clamp01(alpha);
            sr.color = c;
        }
    }
}
