using UnityEngine;
using UnityEngine.InputSystem;
using Laubrary.Chunks;

/// Fires the Chunks use-case recipes from a real keypress, the way a game would fire them: the number key is
/// read through the ordinary input pipeline and the matching emitter's own <c>Burst()</c> is called with no
/// arguments. Nothing here configures the effect — which blasts appear, where, in what order and how they
/// fly is entirely the ChunkSpec's business, which is the point: the recipe is authored in the Chunks
/// window, and the only thing game code decides is WHEN it goes off.
///
/// The entries are a list rather than three named fields so a fourth use case costs a row in the Inspector
/// instead of an edit here.
public class ChunksUseCaseDemoTrigger : MonoBehaviour
{
    [System.Serializable]
    public class Entry
    {
        [Tooltip("Emitter fired when this entry's key is pressed. Its own recipe decides what appears.")]
        public ChunkEmitter emitter;

        [Tooltip("Number key along the top of the keyboard that fires this emitter.")]
        [Range(1, 9)] public int key = 1;

        [Tooltip("Name shown in the on-screen key list. Empty falls back to the emitter's object name.")]
        public string label;
    }

    [Tooltip("Which key fires which emitter. One row per use case.")]
    public Entry[] entries = new Entry[0];

    [Tooltip("Show the key list in the corner of the game view, so the demo can be driven without the Inspector.")]
    public bool showKeyLegend = true;

    void Update()
    {
        // Keyboard.current is null when no keyboard is present (a headless run, a player on a pad), which is
        // not an error — there is simply nothing to read this frame.
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry == null || entry.emitter == null) continue;

            Key code = DigitKey(entry.key);
            if (code == Key.None) continue;

            if (keyboard[code].wasPressedThisFrame)
                entry.emitter.Burst();
        }
    }

    static Key DigitKey(int number)
    {
        switch (number)
        {
            case 1: return Key.Digit1;
            case 2: return Key.Digit2;
            case 3: return Key.Digit3;
            case 4: return Key.Digit4;
            case 5: return Key.Digit5;
            case 6: return Key.Digit6;
            case 7: return Key.Digit7;
            case 8: return Key.Digit8;
            case 9: return Key.Digit9;
            default: return Key.None;
        }
    }

    // Drawn the same way the sibling ChunksDemoSpawner draws its own key list, and anchored to the bottom so
    // the two do not sit on top of each other. Bottom-left, not centre, because the middle of the view is
    // where the bursts are.
    void OnGUI()
    {
        if (!showKeyLegend || entries == null || entries.Length == 0) return;

        var style = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        style.normal.textColor = Color.white;

        var text = new System.Text.StringBuilder();
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry == null || entry.emitter == null) continue;
            string label = string.IsNullOrEmpty(entry.label) ? entry.emitter.name : entry.label;
            text.Append(entry.key).Append(": ").Append(label).Append('\n');
        }

        float height = 20f * entries.Length + 10f;
        GUI.Label(new Rect(10f, Screen.height - height - 10f, 620f, height), text.ToString(), style);
    }
}
