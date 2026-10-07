using UnityEngine;

namespace Laubrary.ZoetropeZoeCharacter
{
    /// Where the stick points, relative to the way the character FACES: Forward is toward its facing, Back is
    /// away from it, so the same command works facing left or right. Any matches every direction; Neutral is
    /// the stick at rest.
    public enum StickDirection
    {
        Any, Neutral, Forward, ForwardUp, Up, BackUp, Back, BackDown, Down, ForwardDown,
    }

    /// <summary>
    /// One row of a side-view character's command list: "this button (or the stick alone), with the stick
    /// this way, plays this declared action". The action is picked from the character's own event list and
    /// the button from its controls asset — never typed — in the Zoe window.
    /// </summary>
    [System.Serializable]
    public class SideViewCommand
    {
        [Tooltip("The button action (from the controls asset's Player map) that triggers this command. Empty = " +
                 "the stick alone: moving the stick into the direction triggers it.")]
        public string button = "";

        [Tooltip("Which way the stick must point, relative to the character's facing.")]
        public StickDirection direction = StickDirection.Any;

        [Tooltip("The declared action this command plays.")]
        public string eventId = "";

        [Tooltip("Stick-only commands: seconds the stick must stay in the direction before it fires. A button " +
                 "press in that time cancels it, so a button command sharing the direction can win.")]
        [Min(0f)] public float holdSeconds = 0.12f;

        [Tooltip("Face the other way when the action finishes (not when something interrupts it) — for a " +
                 "turnaround whose art ends facing the other way.")]
        public bool turnWhenDone = false;
    }
}
