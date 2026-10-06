using UnityEngine;
using UnityEngine.InputSystem;
using Laubrary.Zoetrope;
using ZoeStates;

namespace Laubrary.Demos.BarbarianDemo
{
    /// <summary>
    /// Spawns the Barbarian Zoe at Play-time (same reason as ProtoGuySpawner: the spawn wires runtime-only state
    /// that a pre-baked scene object would lose) and maps buttons to the actions the character declares.
    ///
    /// Moving and facing are the character's own data: its player controller gives it the left stick / WASD,
    /// and its motion pose mirrors it to face where it last walked. This script only decides WHICH declared
    /// action plays WHEN — the look of each action lives on the Barbarian asset's event list.
    ///
    /// To add an action: add a row to the Barbarian's event list in the Zoe window, then add a button field
    /// here and one line in <see cref="Bindings"/> using the generated name from <c>BarbarianStates</c>.
    /// </summary>
    public class BarbarianDemo : MonoBehaviour
    {
        [Tooltip("The character to spawn.")]
        public Zoe zoeDef;

        [Header("Action buttons")]
        [Tooltip("Gut stab — a quick forward thrust.")]
        public InputAction gutStab = Button("<Gamepad>/buttonWest", "<Keyboard>/j");
        [Tooltip("Heavy over-head chop.")]
        public InputAction heavyChop = Button("<Gamepad>/buttonNorth", "<Keyboard>/i");
        [Tooltip("Evasive dodge.")]
        public InputAction dodge = Button("<Gamepad>/buttonEast", "<Keyboard>/l");
        [Tooltip("Evasive jump backwards.")]
        public InputAction jumpBack = Button("<Gamepad>/buttonSouth", "<Keyboard>/k");
        [Tooltip("Turnaround animation.")]
        public InputAction turnaround = Button("<Gamepad>/rightShoulder", "<Keyboard>/u");
        [Tooltip("Pushed backwards (a knock-back reaction).")]
        public InputAction pushedBackwards = Button("<Gamepad>/leftShoulder", "<Keyboard>/1");
        [Tooltip("Pushed forwards (a shove-from-behind reaction).")]
        public InputAction pushedForwards = Button("<Gamepad>/leftTrigger", "<Keyboard>/2");
        [Tooltip("Long gut stab (8 frames, with sword trail).")]
        public InputAction gutStabLong = Button("<Gamepad>/start", "<Keyboard>/0");
        [Tooltip("Head chop.")]
        public InputAction headChop = Button("<Gamepad>/dpad/up", "<Keyboard>/3");
        [Tooltip("Neck slice.")]
        public InputAction neckSlice = Button("<Gamepad>/dpad/right", "<Keyboard>/4");
        [Tooltip("Over cleave.")]
        public InputAction overCleave = Button("<Gamepad>/dpad/down", "<Keyboard>/5");
        [Tooltip("Push kick.")]
        public InputAction pushKick = Button("<Gamepad>/dpad/left", "<Keyboard>/6");
        [Tooltip("Sword cyclone.")]
        public InputAction swordCyclone = Button("<Gamepad>/rightTrigger", "<Keyboard>/7");
        [Tooltip("Roll.")]
        public InputAction roll = Button("<Gamepad>/rightStickPress", "<Keyboard>/8");
        [Tooltip("Hit — the hurt reaction.")]
        public InputAction hit = Button("<Gamepad>/leftStickPress", "<Keyboard>/9");

        [Header("Same moves without sword trails (keyboard only)")]
        [Tooltip("Long gut stab without trail (older 6-frame version).")]
        public InputAction gutStabLongNoTrail = Button("<Keyboard>/z");
        [Tooltip("Head chop without trail.")]
        public InputAction headChopNoTrail = Button("<Keyboard>/x");
        [Tooltip("Neck slice without trail.")]
        public InputAction neckSliceNoTrail = Button("<Keyboard>/c");
        [Tooltip("Over cleave without trail.")]
        public InputAction overCleaveNoTrail = Button("<Keyboard>/v");
        [Tooltip("Push kick without trail.")]
        public InputAction pushKickNoTrail = Button("<Keyboard>/b");
        [Tooltip("Sword cyclone without trail.")]
        public InputAction swordCycloneNoTrail = Button("<Keyboard>/n");

        ReactionFxPlayer _reactions;

        (InputAction button, string state)[] Bindings => new[]
        {
            (gutStab,         BarbarianStates.GutStab),
            (heavyChop,       BarbarianStates.HeavyChop),
            (dodge,           BarbarianStates.Dodge),
            (jumpBack,        BarbarianStates.JumpBack),
            (turnaround,      BarbarianStates.Turnaround),
            (pushedBackwards, BarbarianStates.PushedBackwards),
            (pushedForwards,  BarbarianStates.PushedForwards),
            (gutStabLong,     BarbarianStates.GutStabLong),
            (headChop,        BarbarianStates.HeadChop),
            (neckSlice,       BarbarianStates.NeckSlice),
            (overCleave,      BarbarianStates.OverCleave),
            (pushKick,        BarbarianStates.PushKick),
            (swordCyclone,    BarbarianStates.SwordCyclone),
            (roll,            BarbarianStates.Roll),
            (hit,             BarbarianStates.Hit),
            (gutStabLongNoTrail,  BarbarianStates.GutStabLongNoTrail),
            (headChopNoTrail,     BarbarianStates.HeadChopNoTrail),
            (neckSliceNoTrail,    BarbarianStates.NeckSliceNoTrail),
            (overCleaveNoTrail,   BarbarianStates.OverCleaveNoTrail),
            (pushKickNoTrail,     BarbarianStates.PushKickNoTrail),
            (swordCycloneNoTrail, BarbarianStates.SwordCycloneNoTrail),
        };

        static InputAction Button(params string[] paths)
        {
            var a = new InputAction(type: InputActionType.Button);
            foreach (var p in paths) a.AddBinding(p);
            return a;
        }

        void OnEnable()  { foreach (var b in Bindings) b.button.Enable(); }
        void OnDisable() { foreach (var b in Bindings) b.button.Disable(); }

        void Start()
        {
            if (zoeDef == null) { Debug.LogError("BarbarianDemo: zoeDef not assigned."); return; }
            var go = ZoeSpawner.SpawnCharacter(zoeDef, transform.position);
            _reactions = go != null ? go.GetComponent<ReactionFxPlayer>() : null;
        }

        void Update()
        {
            if (_reactions == null) return;
            foreach (var b in Bindings)
                if (b.button.WasPressedThisFrame()) _reactions.Raise(b.state);
        }
    }
}
