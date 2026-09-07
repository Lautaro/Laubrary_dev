using System.Collections.Generic;
using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Demos.ProtoGuyDemo
{
    /// <summary>
    /// DEBUG-ONLY (T-0247), plain native game code -- NOT a Laubrary tool. Draws a persistent line from the
    /// muzzle out along the fire direction on every shot, so you can eyeball how well distributed ProtoGuy's
    /// 16 aim directions are over several shots. Lines accumulate; a small on-screen button clears them.
    ///
    /// ProtoGuy's character (and its equipped <see cref="ProjectileWeapon"/>) is spawned live at Play time
    /// (see <c>ProtoGuySpawner</c>), so this rescans the scene on an interval rather than wiring a reference
    /// in the saved scene -- same reasoning <c>DemoWeaponSwitch</c> already uses for finding the WeaponSwitcher.
    ///
    /// Set <see cref="enableVisualizer"/> to false (or disable the GameObject) to turn this off entirely.
    /// </summary>
    public class FireDirectionVisualizer : MonoBehaviour
    {
        [Tooltip("Turn the whole visualiser off without removing the component.")]
        public bool enableVisualizer = true;
        [Tooltip("How long each drawn line is, in world units.")]
        public float lineLength = 6f;
        [Tooltip("Line colour.")]
        public Color lineColor = Color.red;
        [Tooltip("Line width, in world units.")]
        public float lineWidth = 0.03f;
        [Tooltip("Seconds between rescanning the scene for newly-spawned weapons (ProtoGuy is spawned at Play time, not present in the saved scene).")]
        public float rescanInterval = 0.5f;

        readonly HashSet<ProjectileWeapon> _hooked = new HashSet<ProjectileWeapon>();
        readonly List<GameObject> _lines = new List<GameObject>();
        Transform _container;
        Material _lineMaterial;
        float _rescanAt;

        // Projectile's real travel direction is private -- read it by reflection rather than re-deriving via
        // ProjectileWeapon.ResolvedAimDirection(), which RE-RESOLVES live (it prefers a MuzzleVectorTracker's
        // animation-drawn aim when one is present) and so can disagree with the direction this specific shot
        // actually launched with by the time the Fired event handler asks again.
        static readonly System.Reflection.FieldInfo s_ProjectileDirField =
            typeof(Projectile).GetField("dir", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        void OnEnable() => _rescanAt = 0f;

        void Update()
        {
            if (!enableVisualizer) return;
            if (Time.time < _rescanAt) return;
            _rescanAt = Time.time + rescanInterval;
            Rescan();
        }

        // Hooks every ProjectileWeapon found so far, including ones on weapon slots that are currently
        // inactive (WeaponSwitcher disables the non-active slot rather than destroying it) -- subscribing
        // once here is enough even before that slot is ever switched to.
        void Rescan()
        {
            var weapons = FindObjectsByType<ProjectileWeapon>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var w in weapons)
            {
                if (w == null || _hooked.Contains(w)) continue;
                _hooked.Add(w);
                w.Fired += OnFired;
                w.HitscanFired += (target, _) => OnHitscanFired(w, target);
            }
        }

        void OnFired(Projectile p)
        {
            if (!enableVisualizer || p == null) return;
            Vector2 origin = p.transform.position;
            Vector2 dir = s_ProjectileDirField != null ? (Vector2)s_ProjectileDirField.GetValue(p) : Vector2.up;
            DrawLine(origin, dir);
        }

        void OnHitscanFired(ProjectileWeapon w, Vector3 target)
        {
            if (!enableVisualizer || w == null) return;
            Vector2 origin = (w.muzzle != null ? w.muzzle : w.transform).position;
            DrawLine(origin, (Vector2)target - origin);
        }

        void DrawLine(Vector2 origin, Vector2 dir)
        {
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.up;
            dir.Normalize();

            if (_container == null) _container = new GameObject("~FireDirectionLines").transform;

            var go = new GameObject("FireLine");
            go.transform.SetParent(_container, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.SetPosition(0, origin);
            lr.SetPosition(1, origin + dir * lineLength);
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.numCapVertices = 4;
            lr.material = LineMaterial();
            lr.startColor = lineColor;
            lr.endColor = lineColor;
            _lines.Add(go);
        }

        Material LineMaterial()
        {
            if (_lineMaterial == null) _lineMaterial = new Material(Shader.Find("Sprites/Default"));
            return _lineMaterial;
        }

        /// Destroys every accumulated line. Wired to the on-screen debug button below.
        public void ClearLines()
        {
            for (int i = 0; i < _lines.Count; i++)
                if (_lines[i] != null) Destroy(_lines[i]);
            _lines.Clear();
        }

        void OnGUI()
        {
            if (!enableVisualizer) return;
            const float w = 190f, h = 26f, pad = 8f;
            if (GUI.Button(new Rect(pad, pad, w, h), $"Clear Fire Lines ({_lines.Count})"))
                ClearLines();
        }
    }
}
