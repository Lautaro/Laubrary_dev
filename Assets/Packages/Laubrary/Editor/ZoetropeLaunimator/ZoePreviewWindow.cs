using UnityEditor;
using UnityEngine;
using Laubrary.Combat2D;
using Laubrary.Zoetrope;
using Laubrary.Launimator;
using Laubrary.PreviewKit.Editor;

namespace Laubrary.ZoetropeLaunimator.Editor
{
    /// <summary>
    /// "Spawn a Zoe into a sandbox and look at it" — the composition-level preview. Owns a
    /// <see cref="LiveScenePreview"/> and spawns through the REAL <see cref="Bestiary.SpawnCharacter"/>/
    /// <see cref="Bestiary.EquipWeapon"/> path — not a reimplementation — so this window can never visually
    /// drift from what a Zoe actually looks like in gameplay (the "one process" rule,
    /// ZOE_ARCHITECTURE_DESIGN.md §3). Shows the full composed character: composite body (if any), equipped
    /// weapon, a muzzle position marker, and the hurtbox outline.
    /// </summary>
    public class ZoePreviewWindow : ZUIWindow
    {
        Zoe _zoe;
        WeaponDef _weapon;
        Zoe _spawnedFor;
        WeaponDef _spawnedWeaponFor;

        GameObject _go;
        Combatant _combatant;
        Transform _muzzle;
        BoxCollider2D _hurtbox;

        LiveScenePreview _live;
        LiveScenePreview Live => _live ??= new LiveScenePreview();

        float _worldHeight = 4f;
        string _clip = "";
        double _lastTick;

        [MenuItem("Laubrary/Zoetrope/Zoe Preview")]
        public static void Open() => GetWindow<ZoePreviewWindow>("Zoe Preview");

        protected override void OnZUIEnable()
        {
            _lastTick = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            TearDown();
            _live?.Dispose();
            _live = null;
        }

        void Tick()
        {
            if (_go == null) return;
            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - _lastTick);
            _lastTick = now;
            foreach (var p in _go.GetComponentsInChildren<ZonedAnimationPlayer>()) p.Tick(dt);
            Repaint();
        }

        protected override void OnZUI()
        {
            VerticalSpace();
            var newZoe = ObjectField("Zoe", _zoe);
            var newWeapon = ObjectField("Weapon (optional)", _weapon);
            if (newZoe != _zoe || newWeapon != _weapon) { _zoe = newZoe; _weapon = newWeapon; }

            if (_zoe == null)
            {
                Label("Pick a Zoe to preview it spawned exactly as it would be in gameplay.", ZUI.ZTextStyle.Subtle);
                return;
            }

            EnsureSpawned();

            VerticalSpace();
            Rect view = GUILayoutUtility.GetRect(200f, 280f, GUILayout.ExpandWidth(true), GUILayout.Height(280f));
            if (Event.current.type == EventType.Repaint) DrawPreview(view);

            VerticalSpace();
            using (var row = ZUI.HRow())
            {
                if (row.Button("⟲ Restart")) RestartClips();
                if (row.Button("▶ Play")) RestartClips();
            }
            _worldHeight = DelayedFloatField("View height (world units)", _worldHeight);
            _clip = TextField("Clip (all parts)", _clip);

            var players = _go.GetComponentsInChildren<ZonedAnimationPlayer>();
            if (players.Length > 0)
            {
                VerticalSpace();
                Label($"{players.Length} independently-timed part(s): " +
                      string.Join(", ", System.Array.ConvertAll(players, p => $"{p.gameObject.name}={p.CurrentClip}")),
                      ZUI.ZTextStyle.Small);
            }
        }

        void EnsureSpawned()
        {
            if (_go != null && _spawnedFor == _zoe && _spawnedWeaponFor == _weapon) return;
            TearDown();

            _go = Bestiary.SpawnCharacter(_zoe, Vector3.zero);
            Live.Adopt(_go);
            _combatant = _go.GetComponent<Combatant>();
            _hurtbox = _go.GetComponent<BoxCollider2D>();

            if (_weapon != null)
            {
                var muzzleGo = new GameObject("~Muzzle");
                muzzleGo.transform.SetParent(_go.transform, false);
                muzzleGo.transform.localPosition = _weapon.muzzleOffset;
                _muzzle = muzzleGo.transform;
                Bestiary.EquipWeapon(_go, _weapon, _combatant, _muzzle);
            }

            _spawnedFor = _zoe;
            _spawnedWeaponFor = _weapon;
            RestartClips();
        }

        void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            _go = null; _combatant = null; _muzzle = null; _hurtbox = null;
        }

        void RestartClips()
        {
            if (_go == null || string.IsNullOrEmpty(_clip)) return;
            foreach (var p in _go.GetComponentsInChildren<ZonedAnimationPlayer>()) p.Play(_clip, loop: true);
        }

        void DrawPreview(Rect view)
        {
            EditorGUI.DrawRect(view, new Color(0.16f, 0.16f, 0.16f));
            Live.Frame(Vector3.zero, _worldHeight);
            Live.Draw(view);

            // Alignment markers — the same Camera.WorldToScreenPoint technique Pyre's own live preview uses
            // (see PyreWindow.DrawPreview), not a second hand-matched screen-projection formula.
            var cam = Live.Camera;
            float px2pt = view.width / Mathf.Max(1, cam.pixelWidth);
            float py2pt = view.height / Mathf.Max(1, cam.pixelHeight);
            Vector2 ToScreen(Vector3 world)
            {
                Vector3 sp = cam.WorldToScreenPoint(world);
                return new Vector2(view.x + sp.x * px2pt, view.y + (view.height - sp.y * py2pt));
            }

            if (_muzzle != null)
            {
                Vector2 p = ToScreen(_muzzle.position);
                var c = Color.red;
                EditorGUI.DrawRect(new Rect(p.x - 5f, p.y - 1f, 10f, 2f), c);
                EditorGUI.DrawRect(new Rect(p.x - 1f, p.y - 5f, 2f, 10f), c);
            }

            if (_hurtbox != null)
            {
                Vector3 c3 = _hurtbox.transform.TransformPoint(_hurtbox.offset);
                Vector2 half = new Vector2(_hurtbox.size.x * 0.5f, _hurtbox.size.y * 0.5f);
                Vector2 bl = ToScreen(c3 + new Vector3(-half.x, -half.y, 0f));
                Vector2 tr = ToScreen(c3 + new Vector3(half.x, half.y, 0f));
                var rect = Rect.MinMaxRect(Mathf.Min(bl.x, tr.x), Mathf.Min(bl.y, tr.y), Mathf.Max(bl.x, tr.x), Mathf.Max(bl.y, tr.y));
                var col = new Color(0.2f, 1f, 0.4f, 0.6f);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), col);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), col);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), col);
                EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), col);
            }
        }
    }
}
