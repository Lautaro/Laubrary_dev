using System;
using UnityEngine;
using Laubrary.Combat2D;

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

        bool tumbling;          // pseudo-3D squash+shade mode instead of a flat 2D spin (sampled debris only)

        float trailClock;       // seconds since the last trail puff (see spec.trailSource)
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
        /// animation, if supplied, is cycled instead of the sprite the emitter assigned. tumble requests
        /// the pseudo-3D squash+shade mode (see ChunkTumble) instead of a flat 2D spin/faceVelocity — the
        /// emitter only ever passes true for a chunk it actually sourced via SampledChunkSprites.
        public void Init(ChunkSpec spec, Vector2 velocity, float angularVel, float life, float worldSize, Color baseColor,
                         IChunkAnimation animation = null, bool tumble = false, Combatant owner = null)
        {
            this.spec = spec;
            this.velocity = velocity;
            this.angularVel = angularVel;
            this.maxLife = Mathf.Max(0.01f, life);
            this.baseColor = baseColor;
            this.life = 0f;
            this.settled = false;
            this.tumbling = tumble;
            this.spinAngle = 0f;   // a reused (pooled) chunk must not inherit its previous life's rotation
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

            if (spec.useHitDetection)
            {
                // Trigger callbacks need a Rigidbody2D on at least one side of the pair — Chunk moves itself via
                // plain transform math (not physics), so this is Kinematic purely to make OnTriggerEnter2D fire
                // against a static target's own collider; it never gets pushed by real Physics2D forces.
                var rb = GetComponent<Rigidbody2D>();
                if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.simulated = true;

                hitCollider = GetComponent<CircleCollider2D>();
                if (hitCollider == null) hitCollider = gameObject.AddComponent<CircleCollider2D>();
                hitCollider.isTrigger = true;
                hitCollider.radius = spriteUnit * 0.5f * spec.hitRadiusScale;
                hitCollider.enabled = true;

                hitbox = GetComponent<Hitbox>();
                if (hitbox == null) hitbox = gameObject.AddComponent<Hitbox>();
                hitbox.owner = owner;
                hitbox.damage = spec.hitDamage;
                hitbox.oncePerTarget = true;
                hitbox.enabled = true;
                hitbox.Arm();
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
                if (tumbling)
                {
                    // Pseudo-3D squash+shade (applied in ApplyLook) replaces real 2D rotation entirely —
                    // spinAngle here is reused purely as the tumble phase accumulator, in degrees.
                    spinAngle += angularVel * dt;
                }
                else if (spec.faceVelocity)
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

            var trailSource = spec.TrailSource;
            if (trailSource != null && !settled)
            {
                trailClock += dt;
                if (trailClock >= spec.trailInterval)
                {
                    trailClock -= spec.trailInterval;
                    trailSource.SpawnPuff(transform.position, sr != null ? sr.sortingOrder : 0);
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
            float sizeMul = spec.sizeOverLife != null ? spec.sizeOverLife.Evaluate(t) : 1f;
            float scaleY = baseScale * Mathf.Max(0f, sizeMul);
            float scaleX = scaleY;
            float shade = 1f;
            if (tumbling)
            {
                (float squashX, float tumbleShade) = ChunkTumble.Evaluate(spinAngle, spec.tumbleShadeStrength);
                scaleX = scaleY * squashX;
                shade = tumbleShade;
            }
            transform.localScale = new Vector3(scaleX, scaleY, 1f);

            if (sr == null) return;
            Color tint = spec.colorOverLife != null ? spec.colorOverLife.Evaluate(t) : Color.white;
            float alpha = spec.alphaOverLife != null ? spec.alphaOverLife.Evaluate(t) : 1f;
            Color c = baseColor * tint * shade;
            c.a = baseColor.a * tint.a * Mathf.Clamp01(alpha);
            sr.color = c;
        }
    }
}
