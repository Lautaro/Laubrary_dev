using System.Collections.Generic;
using UnityEngine;
using Laubrary.Combat2D;
using Laubrary.GoreLab;

namespace Laubrary.GoreLabDemo
{
    /// <summary>
    /// The demo's shotgun. A blast is a fan of pellets that each fly on their own as ordinary Combat2D projectiles: every pellet damages the first
    /// damageable it touches (any imp, including several in one blast) and the touched imp is wounded along that pellet's own line.
    /// The fan is drawn before the shot from the same seed that fires it, so what the preview shows is what flies.
    /// </summary>
    public sealed class GoreLabShotgun : MonoBehaviour
    {
        [Header("Blast")]
        [Range(1, 60)] public int pellets = 30;
        [Tooltip("Half-angle of the pellet fan in degrees. Most pellets stay near the middle.")]
        [Range(0f, 45f)] public float coneDeg = 12f;

        [Header("Pellet")]
        [Tooltip("World units per second.")]
        [Range(5f, 100f)] public float speed = 45f;
        [Tooltip("How far a pellet flies before it disappears, in world units.")]
        public float range = 40f;
        [Tooltip("Energy of a pellet: more digs deeper and wider.")]
        [Range(0.3f, 30f)] public float energy = 7f;
        [Tooltip("How much of its energy a pellet loses per world unit flown (down to a third).")]
        public float fadePerUnit = 0.03f;
        [Tooltip("Combat damage each pellet deals on top of the wound.")]
        public float damage = 1f;

        /// <summary>One line, kept current while pellets land: how the last blast went.</summary>
        public string Summary { get; private set; } = "";

        sealed class Volley
        {
            public int fired, ended, hits, wounds;
            public readonly HashSet<GoreBody> imps = new HashSet<GoreBody>();
        }

        // One in-flight pellet: remembers where it started and which way it goes, because the wound must follow that line.
        sealed class Flight
        {
            public GoreLabShotgun owner;
            public Projectile projectile;
            public Vector2 origin, dir;
            public int seed;
            public Volley volley;
            public IWoundRecipe custom;      // a single-shot weapon wounds with its own recipe along the contact instead of the pellet recipe

            public void OnHit(Hurtbox box, DamageInfo info)
            {
                volley.hits++;
                var body = box != null ? box.GetComponentInParent<GoreBody>() : null;
                if (body == null) return;
                // The wound starts a hair before the contact point, so the recipe's own distance fade does not count the whole flight;
                // the pellet's real flight distance fades its energy here instead.
                float flown = Vector2.Distance(origin, info.point);
                owner._recipe.energy = owner.energy * Mathf.Max(0.35f, 1f - owner.fadePerUnit * flown);
                if (custom != null)
                {
                    if (body.ApplyWound(custom, info.point - dir * 0.7f, info.point + dir * 0.7f, seed)) { volley.wounds++; volley.imps.Add(body); }
                    return;
                }
                Vector2 from = info.point - dir * 0.1f;
                // A pellet that tunnels through leaves a hole that can be hidden behind the body; then it still scars the surface it struck.
                owner._surface.energy = owner._recipe.energy;
                if (body.ApplyWound(owner._recipe, from, info.point + dir, seed)
                    || body.ApplyWound(owner._surface, from, info.point + dir, seed))
                {
                    volley.wounds++;
                    volley.imps.Add(body);
                }
            }

            public void OnExpired()
            {
                projectile.Hit -= OnHit;
                projectile.Expired -= OnExpired;
                volley.ended++;
                owner.Refresh(volley);
            }
        }

        ShotgunRecipe _recipe, _surface;
        Projectile _template;
        readonly List<LineRenderer> _rays = new List<LineRenderer>();
        Volley _last;

        /// <summary>Starts from the rig's own tuned shotgun, so the sliders open on the numbers the author chose.</summary>
        public void Init(GoreRig rig)
        {
            _recipe = new ShotgunRecipe();
            if (rig != null && rig.recipes != null)
                foreach (var r in rig.recipes)
                    if (r is ShotgunRecipe tuned)
                    {
                        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(tuned), _recipe);
                        pellets = Mathf.Clamp(tuned.pellets, 1, 60);
                        coneDeg = Mathf.Clamp((float)tuned.coneDeg, 0f, 45f);
                        energy = Mathf.Clamp((float)tuned.energy, 0.3f, 30f);
                        break;
                    }
            // One pellet per wound, flying straight: the fan is made here, not inside the recipe.
            _recipe.pellets = 1;
            _recipe.coneDeg = 0;
            _recipe.straightOn = false;
            _surface = new ShotgunRecipe();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(_recipe), _surface);
            _surface.straightOn = true;
        }

        /// <summary>The pellet directions of a blast (unit vectors) and each pellet's offset from the aim line, from -1 (edge of the fan) to 1.</summary>
        public void Rays(Vector2 muzzle, Vector2 aim, int seed, List<Vector2> dirs, List<float> offsets)
        {
            dirs.Clear();
            offsets.Clear();
            var rng = new System.Random(seed);
            float baseAngle = Mathf.Atan2(aim.y - muzzle.y, aim.x - muzzle.x), cone = coneDeg * Mathf.Deg2Rad;
            for (int i = 0; i < pellets; i++)
            {
                float gauss = (Next(rng) + Next(rng) + Next(rng) + Next(rng) - 2f) / 0.58f;
                float off = Mathf.Clamp(gauss * 0.55f, -1f, 1f), ang = baseAngle + off * cone;
                dirs.Add(new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)));
                offsets.Add(off);
            }
        }

        static float Next(System.Random rng) { return (float)rng.NextDouble(); }

        readonly List<Vector2> _dirs = new List<Vector2>();
        readonly List<float> _offsets = new List<float>();

        /// <summary>Draws the fan from the muzzle toward the aim point and a little past it; centre pellets are brighter.</summary>
        public void ShowPreview(Vector2 muzzle, Vector2 aim, int seed)
        {
            Rays(muzzle, aim, seed, _dirs, _offsets);
            float length = Mathf.Max(Vector2.Distance(muzzle, aim), 1.5f) + 1f;
            while (_rays.Count < _dirs.Count) _rays.Add(NewRay());
            for (int i = 0; i < _rays.Count; i++)
            {
                var lr = _rays[i];
                bool used = i < _dirs.Count;
                lr.enabled = used;
                if (!used) continue;
                float centre = 1f - Mathf.Abs(_offsets[i]);
                var c = new Color(1f, 0.72f, 0.2f, Mathf.Lerp(0.3f, 0.95f, centre));
                lr.startColor = lr.endColor = c;
                lr.SetPosition(0, muzzle);
                lr.SetPosition(1, muzzle + _dirs[i] * length);
            }
        }

        public void HidePreview()
        {
            foreach (var lr in _rays) if (lr != null) lr.enabled = false;
        }

        LineRenderer NewRay()
        {
            var lr = new GameObject("Pellet ray").AddComponent<LineRenderer>();
            lr.transform.SetParent(transform, false);
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startWidth = lr.endWidth = 0.04f;
            lr.positionCount = 2;
            lr.sortingOrder = 999;
            lr.useWorldSpace = true;
            lr.enabled = false;
            return lr;
        }

        /// <summary>Fires the fan from the muzzle toward the aim point. Returns how many pellets went out.</summary>
        public int Fire(Vector2 muzzle, Vector2 aim, int seed)
        {
            if (_recipe == null) Init(null);
            EnsureTemplate();

            Rays(muzzle, aim, seed, _dirs, _offsets);
            var volley = new Volley { fired = _dirs.Count };
            for (int i = 0; i < _dirs.Count; i++)
            {
                var p = ProjectilePool.Get(_template);
                p.transform.SetPositionAndRotation(muzzle, Quaternion.identity);
                p.transform.SetParent(ProjectileContainer.Root, true);
                if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);
                p.damage = damage;
                p.lifetime = range / Mathf.Max(0.01f, speed);

                var flight = new Flight { owner = this, projectile = p, origin = muzzle, dir = _dirs[i], seed = seed * 1000 + i, volley = volley };
                p.Hit += flight.OnHit;
                p.Expired += flight.OnExpired;
                p.Launch(_dirs[i], null, gameObject, speed);   // no faction: the pellet hurts whatever it touches
            }
            _last = volley;
            Refresh(volley);
            return volley.fired;
        }

        /// <summary>One real projectile from the muzzle toward the aim point; where it lands it wounds with the given recipe (a bullet, for example).</summary>
        public void FireOne(Vector2 muzzle, Vector2 aim, IWoundRecipe recipe, int seed, float shotSpeed)
        {
            EnsureTemplate();
            Vector2 dir = aim - muzzle;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.right;
            dir.Normalize();
            var volley = new Volley { fired = 1 };
            var p = ProjectilePool.Get(_template);
            p.transform.SetPositionAndRotation(muzzle, Quaternion.identity);
            p.transform.SetParent(ProjectileContainer.Root, true);
            if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);
            p.damage = damage;
            p.lifetime = range / Mathf.Max(0.01f, shotSpeed);
            var flight = new Flight { owner = this, projectile = p, origin = muzzle, dir = dir, seed = seed, volley = volley, custom = recipe };
            p.Hit += flight.OnHit;
            p.Expired += flight.OnExpired;
            p.Launch(dir, null, gameObject, shotSpeed);
            _last = volley;
            Refresh(volley);
        }

        void Refresh(Volley v)
        {
            if (v != _last) return;
            int flying = v.fired - v.ended;
            Summary = v.hits + "/" + v.fired + " pellets hit, " + v.wounds + " wounded, " + v.imps.Count + (v.imps.Count == 1 ? " imp" : " imps")
                      + (flying > 0 ? " (" + flying + " flying)" : "");
        }

        // A bare streak pellet, built once and cloned through the shared projectile pool. Kept inactive so it is only a template.
        void EnsureTemplate()
        {
            if (_template != null) return;
            var root = new GameObject("Gore pellet template");
            root.SetActive(false);
            root.transform.SetParent(transform, false);
            root.AddComponent<Rigidbody2D>();
            var col = root.AddComponent<CircleCollider2D>();
            col.radius = 0.1f;
            _template = root.AddComponent<Projectile>();
            _template.pierce = 0;
            _template.faceDirection = true;
            _template.rotationOffsetDeg = 0f;     // the streak is drawn pointing right

            var streak = new GameObject("Streak");
            streak.transform.SetParent(root.transform, false);
            streak.transform.localScale = new Vector3(0.45f, 0.07f, 1f);
            var sr = streak.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            sr.color = new Color(1f, 0.9f, 0.55f, 1f);
            sr.sortingOrder = 900;
        }
    }
}
