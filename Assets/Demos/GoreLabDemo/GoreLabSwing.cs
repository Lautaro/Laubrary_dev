using System.Collections.Generic;
using UnityEngine;
using Laubrary.Combat2D;
using Laubrary.GoreLab;

namespace Laubrary.GoreLabDemo
{
    /// <summary>
    /// A melee swing as a real Combat2D attack: a round hitbox travels along the swipe line, and every hurtbox it touches takes the swing's damage
    /// exactly once through the same funnel a game's weapons use (faction check, hit filters, health). The wound is the whole swipe line, so a
    /// slice cuts where the line crosses the body, whichever limb that is.
    /// </summary>
    public sealed class GoreLabSwing : MonoBehaviour
    {
        Vector2 _a, _b, _pos;
        float _speed, _radius, _damage, _travelled, _length;
        IWoundRecipe _recipe;
        int _seed;
        readonly HashSet<Hurtbox> _seen = new HashSet<Hurtbox>();
        LineRenderer _trail;
        float _fadeFor = 0.35f, _endedAt = -1f;
        public int imps, wounds;

        /// <summary>Starts a swing from a to b. The recipe wounds each imp it reaches along that same line.</summary>
        public static GoreLabSwing Begin(Vector2 a, Vector2 b, IWoundRecipe recipe, int seed, float speed = 40f, float radius = 0.3f, float damage = 1f)
        {
            var go = new GameObject("Swing (" + recipe.DisplayName + ")");
            var sw = go.AddComponent<GoreLabSwing>();
            sw._a = a; sw._b = b; sw._pos = a; sw._recipe = recipe; sw._seed = seed;
            sw._speed = speed; sw._radius = radius; sw._damage = damage;
            sw._length = Vector2.Distance(a, b);
            sw._trail = go.AddComponent<LineRenderer>();
            sw._trail.material = new Material(Shader.Find("Sprites/Default"));
            sw._trail.startWidth = sw._trail.endWidth = 0.09f;
            sw._trail.positionCount = 2;
            sw._trail.sortingOrder = 1002;
            sw._trail.useWorldSpace = true;
            sw._trail.SetPosition(0, a);
            sw._trail.SetPosition(1, a);
            return sw;
        }

        void Update()
        {
            if (_endedAt >= 0f)
            {
                float k = 1f - (Time.time - _endedAt) / _fadeFor;
                if (k <= 0f) { Destroy(gameObject); return; }
                _trail.startColor = _trail.endColor = new Color(1f, 1f, 1f, k);
                return;
            }
            float step = _speed * Time.deltaTime;
            Vector2 dir = _length > 1e-5f ? (_b - _a) / _length : Vector2.right;
            // sweep in small steps so a slow frame cannot jump over an imp
            float remaining = step;
            while (remaining > 0f && _travelled < _length)
            {
                float d = Mathf.Min(remaining, _radius);
                _travelled = Mathf.Min(_length, _travelled + d);
                remaining -= d;
                _pos = _a + dir * _travelled;
                Probe();
            }
            _trail.SetPosition(1, _pos);
            _trail.startColor = _trail.endColor = new Color(1f, 1f, 1f, 0.9f);
            if (_travelled >= _length) _endedAt = Time.time;
        }

        void Probe()
        {
            Physics2D.SyncTransforms();
            foreach (var col in Physics2D.OverlapCircleAll(_pos, _radius))
            {
                var hb = Combat.FindHurtbox(col);
                if (hb == null || _seen.Contains(hb)) continue;
                Vector2 point = col.ClosestPoint(_pos);
                if (!Combat.TryDamage(hb, null, gameObject, _damage, point, out var info, gameObject)) continue;
                _seen.Add(hb);
                imps++;
                var body = hb.GetComponentInParent<GoreBody>();
                if (body != null && body.ApplyWound(_recipe, _a, _b, _seed)) wounds++;
            }
        }
    }
}
