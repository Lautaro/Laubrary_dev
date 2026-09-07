using System;
using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Chunks
{
    /// One flying debris bit. It carries its own velocity, spin and lifetime and self-manages every frame:
    /// integrate gravity + air drag, move, spin (or face its travel direction), bounce/settle on an optional floor,
    /// evaluate size / alpha / tint over its normalised life, and destroy itself when its life ends (or shortly
    /// after it settles). A <see cref="DebrisScatter"/> capability creates it from the pool and calls
    /// <see cref="Init"/>, handing over the capability itself plus whichever modifiers were aimed at it.
    /// If handed an <see cref="IChunkAnimation"/> it also cycles that animation's frames instead of showing a
    /// static sprite — the size/alpha/tint-over-life curves still apply on top, so a Pyre fireball instance can
    /// shrink/fade via the spec exactly like a plain chunk does.
    /// Everything is null-guarded so a half-wired chunk goes inert rather than throwing. Uses scaled Time.deltaTime.
    [DisallowMultipleComponent]
    public class Chunk : MonoBehaviour
    {
        DebrisScatter debris;
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

        bool tumbling;          // pseudo-3D squash+shade mode instead of a flat 2D spin (sampled debris only)

        IChunkTrailSource trail; // the puff a Trail modifier leaves behind, or null
        float trailInterval;
        float trailClock;       // seconds since the last puff
        CircleCollider2D hitCollider;
        Hitbox hitbox;

        [Tooltip("Set by a pool owner. When true, reaching maxLife fires Finished instead of destroying the " +
                 "GameObject — the pool decides its fate, not this component.")]
        public bool pooled;

        /// Fired once when a pooled chunk reaches maxLife — the pool's own signal to reclaim this instance.
        /// Never fires when a non-pooled chunk just destroyed itself instead.
        public event Action Finished;

        const float SettleSpeed = 0.4f;   // below this on the floor, a chunk rests/despawns
        const float FloorEps = 0.0001f;

        /// Configure a freshly created chunk. worldSize is the desired on-screen size in world units.
        /// animation, if supplied, is cycled instead of the sprite the scatter assigned. tumble requests the
        /// pseudo-3D squash+shade mode (see ChunkTumble) instead of a flat 2D spin/faceVelocity — the scatter
        /// only ever passes true for a chunk it actually sourced via SampledChunkSprites. trailModifier and
        /// hitsModifier are whichever of those the recipe aimed at this scatter, or null.
        public void Init(DebrisScatter debris, Vector2 velocity, float angularVel, float life, float worldSize,
                         Color baseColor, IChunkAnimation animation = null, bool tumble = false,
                         Combatant owner = null, Trail trailModifier = null, Hits hitsModifier = null)
        {
            this.debris = debris;
            this.velocity = velocity;
            this.angularVel = angularVel;
            this.maxLife = Mathf.Max(0.01f, life);
            this.baseColor = baseColor;
            this.life = 0f;
            this.settled = false;
            this.tumbling = tumble;
            this.spinAngle = 0f;   // a reused (pooled) chunk must not inherit its previous life's rotation
            this.trail = trailModifier != null ? trailModifier.Source : null;
            this.trailInterval = trailModifier != null ? Mathf.Max(0.01f, trailModifier.interval) : 0f;
            this.trailClock = 0f;

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

            if (hitsModifier != null)
            {
                hitsModifier.Attach(gameObject, spriteUnit * 0.5f, owner);
                hitCollider = GetComponent<CircleCollider2D>();
                hitbox = GetComponent<Hitbox>();
            }
            else
            {
                if (hitCollider == null) hitCollider = GetComponent<CircleCollider2D>();
                if (hitCollider != null) hitCollider.enabled = false;
                if (hitbox == null) hitbox = GetComponent<Hitbox>();
                if (hitbox != null) hitbox.enabled = false;
            }
        }

        void Update()
        {
            if (debris == null) { Destroy(gameObject); return; }

            float dt = Time.deltaTime;
            life += dt;

            if (!settled)
            {
                // integrate: gravity, then exponential air drag, then move
                velocity.y -= debris.gravity * dt;
                if (debris.drag > 0f) velocity *= Mathf.Exp(-debris.drag * dt);

                Vector3 pos = transform.position;
                pos.x += velocity.x * dt;
                pos.y += velocity.y * dt;

                if (debris.useFloor && pos.y <= debris.floorY + FloorEps && velocity.y <= 0f)
                {
                    pos.y = debris.floorY;
                    velocity.y = -velocity.y * debris.bounciness;
                    velocity.x *= 1f - debris.floorFriction;

                    if (velocity.magnitude < SettleSpeed)
                    {
                        if (debris.restOnFloor)
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
                if (tumbling)
                {
                    // Pseudo-3D squash+shade (applied in ApplyLook) replaces real 2D rotation entirely —
                    // spinAngle here is reused purely as the tumble phase accumulator, in degrees.
                    spinAngle += angularVel * dt;
                }
                else if (debris.faceVelocity)
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

            if (trail != null && !settled)
            {
                trailClock += dt;
                if (trailClock >= trailInterval)
                {
                    trailClock -= trailInterval;
                    trail.SpawnPuff(transform.position, sr != null ? sr.sortingOrder : 0);
                }
            }

            ApplyLook(Mathf.Clamp01(life / maxLife));

            if (life >= maxLife)
            {
                if (pooled) Finished?.Invoke();
                else Destroy(gameObject);
            }
        }

        // Size / colour / alpha at normalised life t (0→1). Tumbling chunks additionally squash their
        // width and shade their colour per ChunkTumble, faking a lit 3D fragment turning in place.
        void ApplyLook(float t)
        {
            float sizeMul = ZUIEnvelopeEvaluator.Evaluate(debris.sizeEnvelope, t, 1f);
            float scaleY = baseScale * Mathf.Max(0f, sizeMul);
            float scaleX = scaleY;
            float shade = 1f;
            if (tumbling)
            {
                (float squashX, float tumbleShade) = ChunkTumble.Evaluate(spinAngle, debris.tumbleShadeStrength);
                scaleX = scaleY * squashX;
                shade = tumbleShade;
            }
            transform.localScale = new Vector3(scaleX, scaleY, 1f);

            if (sr == null) return;
            Color tint = debris.colorOverLife != null ? debris.colorOverLife.Evaluate(t) : Color.white;
            float alpha = ZUIEnvelopeEvaluator.Evaluate(debris.alphaEnvelope, t, 1f);
            Color c = baseColor * tint * shade;
            c.a = baseColor.a * tint.a * Mathf.Clamp01(alpha);
            sr.color = c;
        }
    }
}
