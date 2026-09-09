using System.Collections.Generic;
using System.Linq;
using Laubrary.Combat2D;
using Laubrary.Launimator;
using Laubrary.PreviewKit.Editor;
using Laubrary.Zoetrope;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZoetropeLaunimator.Editor
{
    /// <summary>
    /// "Spawn a Zoe into a sandbox and look at it" — the composition-level preview. Owns a
    /// <see cref="LiveScenePreview"/> and spawns through the REAL <see cref="ZoeSpawner.SpawnCharacter"/>/
    /// <see cref="ZoeSpawner.EquipWeapon"/> path — not a reimplementation — so this window can never visually
    /// drift from what a Zoe actually looks like in gameplay (the "one process" rule,
    /// ZOE_ARCHITECTURE_DESIGN.md §3). Shows the full composed character: composite body (if any), equipped
    /// weapon, a muzzle position marker, and the hurtbox outline.
    ///
    /// The character it spawns is the authority for which clip can be played here: every part's player
    /// carries the version that declares them, so the clip control is a picker over what this character
    /// actually has, never a typed name.
    /// </summary>
    public class ZoePreviewWindow : ZuiWindow
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

        IMGUIContainer _stage;
        Label _partsLine;

        [MenuItem("Laubrary/Zoetrope/Zoe Preview")]
        public static void Open() => GetWindow<ZoePreviewWindow>("Zoe Preview");

        void OnEnable()
        {
            _lastTick = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
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
            if (_partsLine != null) _partsLine.text = PartsText();
            _stage?.MarkDirtyRepaint();
        }

        protected override void BuildUI(VisualElement root)
        {
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;

            root.Add(Z.HGroup(
                Z.Field("Zoe", "The character to spawn — it is built through the same spawner gameplay uses, "
                    + "so what you see here is what the game shows.",
                    Z.Object<Zoe>(_zoe, "The character to spawn.", v => { _zoe = v; Rebuild(); }, 220f)),
                Z.Field("Weapon", "Equips this weapon on the spawned character and marks its muzzle in red. "
                    + "Leave empty to see the character unarmed.",
                    Z.Object<WeaponDef>(_weapon, "The weapon to equip, if any.", v => { _weapon = v; Rebuild(); }, 220f))));

            if (_zoe == null)
            {
                TearDown();
                root.Add(Z.Help("Pick a Zoe above to see it spawned exactly as it would be in gameplay."));
                return;
            }

            EnsureSpawned();

            _stage = new IMGUIContainer(DrawStage);
            _stage.style.flexGrow = 1f;
            _stage.style.minHeight = 280f;
            _stage.AddToClassList("zui-stage");
            _stage.tooltip = "The character spawned in a sandbox scene. The red cross marks the equipped "
                + "weapon's muzzle; the green outline is the hurtbox gameplay hits.";
            root.Add(_stage);

            var clips = ClipOptions();
            root.Add(Z.HGroup(
                ClipPicker(clips),
                Z.Button("Restart", clips.Count > 0
                    ? "Replays the picked clip on every part from its first frame."
                    : "Nothing to replay: pick a clip first.", RestartClips),
                Z.MicroSlider("View height", _worldHeight, 0.5f, 20f,
                    "How many world units of height the sandbox camera frames — lower zooms in on the character.",
                    v => { _worldHeight = v; _stage?.MarkDirtyRepaint(); }, 170f, showValue: true, defaultValue: 4f)));

            _partsLine = new Label(PartsText()) { tooltip = "Each part animates on its own clock, so the parts "
                + "can be showing different clips at once." };
            _partsLine.style.whiteSpace = WhiteSpace.NoWrap;
            _partsLine.style.overflow = Overflow.Hidden;
            _partsLine.AddToClassList("zui-text--subtle");
            root.Add(_partsLine);
        }

        /// Every clip name this spawned character can play, unioned over its parts in first-seen order.
        /// The spawned players ARE the owner of that list — a composite keeps its animations on the parts,
        /// so asking the character rather than the Zoe asset is what makes this work on ProtoGuy at all.
        List<string> ClipOptions()
        {
            var names = new List<string>();
            if (_go == null) return names;
            foreach (var p in _go.GetComponentsInChildren<ZonedAnimationPlayer>())
            {
                if (p.version == null || p.version.animations == null) continue;
                foreach (var a in p.version.animations)
                    if (a != null && !string.IsNullOrEmpty(a.name) && !names.Contains(a.name)) names.Add(a.name);
            }
            return names;
        }

        /// The picker, with the two states the reference rule requires: a real list when the character
        /// declares clips, and a GREYED picker saying why when it declares none — never a text field, which
        /// would let a name be typed that nothing can play.
        VisualElement ClipPicker(List<string> clips)
        {
            const string tip = "Which declared animation every part plays. The list is this character's own — "
                + "a composite unions what its parts declare.";
            if (clips.Count == 0)
            {
                var empty = Z.Dropdown(0, new List<string> { "None declared" },
                    "This character declares no animations, so there is nothing to play. Give its View a "
                    + "Lauminary version with at least one animation.", _ => { }, 150f);
                empty.SetEnabled(false);
                var emptyField = Z.Field("Clip", "This character declares no animations, so there is nothing "
                    + "to play. Give its View a Lauminary version with at least one animation.", empty);
                emptyField.tooltip = "This character declares no animations, so there is nothing to play.";
                return emptyField;
            }

            var choices = new List<string> { "(none)" };
            choices.AddRange(clips);
            int idx = string.IsNullOrEmpty(_clip) ? 0 : choices.IndexOf(_clip);
            if (idx < 0) { choices.Insert(1, _clip); idx = 1; }
            var shown = choices.Select(c => clips.Contains(c) || c == "(none)" ? c : c + " (unresolved)").ToList();

            return Z.Field("Clip", tip, Z.Dropdown(idx, shown, tip, i =>
            {
                _clip = i <= 0 ? "" : choices[Mathf.Clamp(i, 0, choices.Count - 1)];
                RestartClips();
            }, FitWidth(shown)));
        }

        /// Size the picker against its OWN longest option so no clip name clips. Measured by character
        /// count on purpose: EditorStyles is null while a retained panel is being built.
        static float FitWidth(IList<string> options, float min = 110f, float max = 260f)
        {
            int longest = 0;
            foreach (var o in options) longest = Mathf.Max(longest, (o ?? "").Length);
            return Mathf.Clamp(longest * 7.5f + 30f, min, max);
        }

        string PartsText()
        {
            if (_go == null) return "";
            var players = _go.GetComponentsInChildren<ZonedAnimationPlayer>();
            if (players.Length == 0) return "No independently-timed parts.";
            return $"{players.Length} independently-timed part(s): "
                 + string.Join(", ", System.Array.ConvertAll(players, p => $"{p.gameObject.name}={p.CurrentClip}"));
        }

        void EnsureSpawned()
        {
            if (_go != null && _spawnedFor == _zoe && _spawnedWeaponFor == _weapon) return;
            TearDown();

            _go = ZoeSpawner.SpawnCharacter(_zoe, Vector3.zero);
            Live.Adopt(_go);
            _combatant = _go.GetComponent<Combatant>();
            _hurtbox = _go.GetComponent<BoxCollider2D>();

            if (_weapon != null)
            {
                var muzzleGo = new GameObject("~Muzzle");
                muzzleGo.transform.SetParent(_go.transform, false);
                muzzleGo.transform.localPosition = _weapon.muzzleOffset;
                _muzzle = muzzleGo.transform;
                ZoeSpawner.EquipWeapon(_go, _weapon, _combatant, _muzzle);
            }

            _spawnedFor = _zoe;
            _spawnedWeaponFor = _weapon;
            RestartClips();
        }

        void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            _go = null; _combatant = null; _muzzle = null; _hurtbox = null;
            _spawnedFor = null; _spawnedWeaponFor = null;
        }

        void RestartClips()
        {
            if (_go == null || string.IsNullOrEmpty(_clip)) return;
            foreach (var p in _go.GetComponentsInChildren<ZonedAnimationPlayer>()) p.Play(_clip, loop: true);
        }

        void DrawStage()
        {
            if (_go == null || _stage == null) return;
            var view = new Rect(0f, 0f, _stage.contentRect.width, _stage.contentRect.height);
            if (view.width < 1f || view.height < 1f) return;

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
