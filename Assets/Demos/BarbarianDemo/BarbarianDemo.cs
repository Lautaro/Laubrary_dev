using UnityEngine;
using UnityEngine.InputSystem;
using Laubrary.Zoetrope;
using Laubrary.ZoeCharacter;
using ZoeStates;

namespace Laubrary.Demos.BarbarianDemo
{
    /// <summary>
    /// Spawns the Barbarian Zoe at Play-time (same reason as ProtoGuySpawner: the spawn wires runtime-only state
    /// that a pre-baked scene object would lose) and turns button + stick-direction inputs into the actions the
    /// character declares.
    ///
    /// Moving and facing are the character's own data: its side-view player controller walks it left/right
    /// along one line with a facing of its own (walking against it is a backpedal), and its motion pose picks
    /// the walk, backpedal or stand look. This script only decides WHICH declared action plays WHEN — the look
    /// of each action, and any travel it carries, lives on the Barbarian asset's event list. The one mechanic
    /// it adds: a completed turnaround flips the facing, because the turnaround art ends facing the other way.
    ///
    /// Directions are read relative to the facing: "forward" is the way he faces, so the same input does the
    /// same move whichever way he is turned. The move table is <see cref="AttackMove"/> and <see cref="Update"/>.
    /// </summary>
    public class BarbarianDemo : MonoBehaviour
    {
        [Tooltip("The character to spawn.")]
        public Zoe zoeDef;

        [Header("Buttons")]
        [Tooltip("Attack. Which attack depends on the stick direction (see the move table in this script).")]
        public InputAction attack = Button("<Gamepad>/buttonSouth", "<Keyboard>/j");
        [Tooltip("Evade: a dodge, or a jump backwards with the stick held back.")]
        public InputAction evade = Button("<Gamepad>/buttonEast", "<Keyboard>/k");
        [Tooltip("Turn around: plays the turnaround, then faces the other way.")]
        public InputAction turnaround = Button("<Gamepad>/rightShoulder", "<Keyboard>/u");

        [Header("Stick")]
        [Tooltip("How far the stick must lean before it counts as a direction for a move.")]
        [Range(0.2f, 0.9f)] public float directionThreshold = 0.5f;
        [Tooltip("Seconds the stick must stay forward-down before it rolls. Pressing attack within this time " +
                 "does the long gut stab instead, which shares that direction.")]
        [Range(0f, 0.4f)] public float rollDelay = 0.12f;

        enum Dir { Neutral, Forward, ForwardUp, Up, BackUp, Back, BackDown, Down, ForwardDown }

        ReactionFxPlayer _reactions;
        SideViewMotionDriver _mover;
        IZoeInputSource _input;
        Dir _lastDir;
        float _rollArmedAt = -1f;

        static InputAction Button(params string[] paths)
        {
            var a = new InputAction(type: InputActionType.Button);
            foreach (var p in paths) a.AddBinding(p);
            return a;
        }

        void OnEnable()  { attack.Enable(); evade.Enable(); turnaround.Enable(); }
        void OnDisable() { attack.Disable(); evade.Disable(); turnaround.Disable(); }

        void Start()
        {
            if (zoeDef == null) { Debug.LogError("BarbarianDemo: zoeDef not assigned."); return; }
            var go = ZoeSpawner.SpawnCharacter(zoeDef, transform.position);
            if (go == null) return;
            _reactions = go.GetComponent<ReactionFxPlayer>();
            _mover = go.GetComponent<SideViewMotionDriver>();
            _input = go.GetComponent<IZoeInputSource>();
            if (_reactions != null) _reactions.EventFinished += OnActionFinished;
        }

        void OnDestroy()
        {
            if (_reactions != null) _reactions.EventFinished -= OnActionFinished;
        }

        // Flip only when the turn played to its end: an interrupted turn leaves him facing where he started.
        void OnActionFinished(string id, bool interrupted)
        {
            if (!interrupted && _mover != null && id == BarbarianStates.Turnaround) _mover.Turn();
        }

        static string AttackMove(Dir d) => d switch
        {
            Dir.Neutral     => BarbarianStates.GutStab,
            Dir.Forward     => BarbarianStates.Headbutt,
            Dir.Back        => BarbarianStates.HeadChop,
            Dir.Up          => BarbarianStates.OverCleave,
            Dir.Down        => BarbarianStates.PushKick,
            Dir.ForwardUp   => BarbarianStates.NeckSlice,
            Dir.ForwardDown => BarbarianStates.GutStabLong,
            Dir.BackUp      => BarbarianStates.SwordCyclone,
            Dir.BackDown    => BarbarianStates.HeavyChop,
            _ => null,
        };

        void Update()
        {
            if (_reactions == null || _mover == null) return;
            Dir dir = ReadDir();

            if (attack.WasPressedThisFrame())
            {
                _rollArmedAt = -1f;   // forward-down + attack is the gut stab, not a roll
                _reactions.Raise(AttackMove(dir));
            }
            else if (evade.WasPressedThisFrame())
                _reactions.Raise(dir == Dir.Back ? BarbarianStates.JumpBack : BarbarianStates.Dodge);
            else if (turnaround.WasPressedThisFrame())
                _reactions.Raise(BarbarianStates.Turnaround);

            // Roll: the stick moved INTO forward-down and stayed there for rollDelay without an attack press.
            if (dir == Dir.ForwardDown && _lastDir != Dir.ForwardDown) _rollArmedAt = Time.time;
            if (dir != Dir.ForwardDown) _rollArmedAt = -1f;
            if (_rollArmedAt >= 0f && Time.time - _rollArmedAt >= rollDelay)
            {
                _rollArmedAt = -1f;
                _reactions.Raise(BarbarianStates.Roll);
            }
            _lastDir = dir;
        }

        // The stick as one of eight directions relative to the facing, or Neutral inside the threshold.
        Dir ReadDir()
        {
            Vector2 s = _input != null ? _input.ReadMove() : Vector2.zero;
            if (s.magnitude < directionThreshold) return Dir.Neutral;
            float fwd = _mover.FacingRight ? s.x : -s.x;
            float angle = Mathf.Atan2(s.y, fwd) * Mathf.Rad2Deg;   // 0 = forward, 90 = up
            int sector = Mathf.RoundToInt(angle / 45f);
            switch (sector)
            {
                case 0: return Dir.Forward;
                case 1: return Dir.ForwardUp;
                case 2: return Dir.Up;
                case 3: return Dir.BackUp;
                case 4: case -4: return Dir.Back;
                case -3: return Dir.BackDown;
                case -2: return Dir.Down;
                default: return Dir.ForwardDown;   // -1
            }
        }
    }
}
