using UnityEngine;
using UnityEngine.InputSystem;
using Laubrary.Chunks;
using Laubrary.Zoetrope;
using Laubrary.ZoetropePyre;

namespace Laubrary.Demos.ProtoGuyDemo
{
    /// <summary>
    /// Demo-only: lets this scene be used to judge a Chunks recipe against a REAL kill — ProtoGuy's own weapon,
    /// a real projectile, a real hurtbox, the disc's own Health — by choosing, with a number key, which recipe
    /// the disc's Death reaction throws.
    ///
    /// The reason it exists at all: a recipe that has only ever been fired from a probe has never been judged
    /// where a game actually uses it. A real hit carries a contact point and an attacker-to-target direction
    /// that a code-driven kill cannot, and any card that inherits the burst direction reads exactly those. The
    /// only way to see that is to shoot the thing.
    ///
    /// **It never writes to the character asset.** The character the spawner hands out is a runtime COPY, made
    /// once per recipe and thrown away with Play mode; the copy's Death row is what gets re-pointed. The asset
    /// on disk is only ever read, so the scene's real wiring is exactly what it was the moment Play mode ends,
    /// and a session that forgets to press the restore key still leaves nothing behind. That is the whole
    /// reason for the copy: editing a ScriptableObject at play time edits the one every other scene loads, and
    /// this character is the owner's, shared, and wired for what the game does — not for a test.
    ///
    /// The split is the same one the sibling ChunksUseCaseDemoTrigger keeps: this decides only WHICH recipe is
    /// armed, never what it looks like or when it goes off. WHEN is the player's trigger finger, and WHAT the
    /// burst does stays entirely in the recipe, authored in the Chunks window.
    ///
    /// Entries are a list rather than three named fields so a fourth use case costs a row in the Inspector
    /// instead of an edit here.
    /// </summary>
    public class ChunksUCDeathSwitcher : MonoBehaviour
    {
        [System.Serializable]
        public class Entry
        {
            [Tooltip("Recipe the disc's Death reaction throws while this entry is armed. Its own cards decide " +
                     "what appears; nothing here configures the burst.")]
            public ChunkSpec recipe;

            [Tooltip("Number key along the top of the keyboard that arms this recipe.")]
            [Range(1, 9)] public int key = 4;

            [Tooltip("Name shown in the on-screen key list. Empty falls back to the recipe's asset name.")]
            public string label;
        }

        [Tooltip("Spawner whose discs get the switched Death recipe. Empty finds the one on this object.")]
        public FlyingDiscSpawner spawner;

        [Tooltip("Which key arms which recipe. One row per use case.")]
        public Entry[] entries = new Entry[0];

        [Tooltip("Key that puts the disc back on the character's own authored Death recipe. 0 = the zero key.")]
        [Range(0, 9)] public int restoreKey = 0;

        [Tooltip("Show the key list and what is currently armed in the corner of the game view, so the demo can " +
                 "be driven and read without the Inspector.")]
        public bool showKeyLegend = true;

        Zoe _authored;          // the character asset as the scene wired it — only ever read
        Zoe _armedCopy;         // the runtime copy currently handed to the spawner, or null when authored is live
        string _armedLabel = "";

        void Awake()
        {
            if (spawner == null) spawner = GetComponent<FlyingDiscSpawner>();
            // Captured before the spawner's own Start runs, so what gets restored is what the SCENE says, never
            // a copy left armed from an earlier switch.
            if (spawner != null) _authored = spawner.discDef;
        }

        void Update()
        {
            // Null when no keyboard is present (a headless run, a player on a pad). Not an error — there is
            // simply nothing to read this frame.
            var keyboard = Keyboard.current;
            if (keyboard == null || spawner == null) return;

            Key restore = DigitKey(restoreKey);
            if (restore != Key.None && keyboard[restore].wasPressedThisFrame) { Arm(null, null); return; }

            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.recipe == null) continue;

                Key code = DigitKey(entry.key);
                if (code == Key.None) continue;

                if (keyboard[code].wasPressedThisFrame) { Arm(entry.recipe, LabelOf(entry)); return; }
            }
        }

        /// Point the spawner at a character whose Death reaction throws <paramref name="recipe"/>, or back at
        /// the authored one when it is null.
        void Arm(ChunkSpec recipe, string label)
        {
            if (_authored == null) return;

            var previous = _armedCopy;

            if (recipe == null)
            {
                spawner.discDef = _authored;
                _armedCopy = null;
                _armedLabel = "";
            }
            else
            {
                // A full copy of the character, not an edit of it. Instantiate deep-copies the serialized graph,
                // managed references included, so the copy's Death row is its own object and re-pointing it
                // cannot reach the asset. DontSave keeps it out of the scene and off any save path.
                var copy = Instantiate(_authored);
                copy.name = _authored.name + " (" + label + ")";
                copy.hideFlags = HideFlags.HideAndDontSave;

                int repointed = RepointDeathChunks(copy, recipe);
                if (repointed == 0)
                    Debug.LogWarning("ChunksUCDeathSwitcher: " + _authored.name + "'s Death reaction has no " +
                                     "Spawn Chunks effect to re-point, so " + recipe.name + " will not be thrown.", this);

                spawner.discDef = copy;
                _armedCopy = copy;
                _armedLabel = label;
            }

            // Discs already in the air still carry the character they were spawned from, so they would go on
            // dying the old way. Clearing them hands the spawner's own respawn its usual empty slot, which is
            // the one path that gets every component on the new disc reading the same character.
            ClearLiveDiscs();

            if (previous != null) Destroy(previous);
        }

        /// Set every Spawn Chunks effect on the character's Death reaction to <paramref name="recipe"/>.
        /// Returns how many it found, so a character wired some other way says so instead of silently doing
        /// nothing. Deliberately only the Death reaction: the Hit reaction is what a non-lethal shot shows, and
        /// switching that too would make every shot answer for the recipe under test.
        static int RepointDeathChunks(Zoe character, ChunkSpec recipe)
        {
            var death = character != null ? character.death : null;
            if (death == null || death.fx == null) return 0;

            int count = 0;
            for (int i = 0; i < death.fx.Count; i++)
            {
                var entry = death.fx[i];
                if (entry == null) continue;
                if (entry.fx is SpawnChunkFx spawnChunks) { spawnChunks.chunks = recipe; count++; }
            }
            return count;
        }

        /// Remove the discs currently flying so the spawner refills its slots from the newly armed character.
        void ClearLiveDiscs()
        {
            var live = spawner.GetComponentsInChildren<ReactionFxPlayer>(true);
            for (int i = 0; i < live.Length; i++)
                if (live[i] != null && live[i].gameObject != spawner.gameObject) Destroy(live[i].gameObject);
        }

        void OnDisable()
        {
            // Leaving the spawner pointed at a copy that is about to be destroyed would strand it with nothing
            // to spawn, so the authored character goes back on the way out.
            if (spawner != null && _authored != null) spawner.discDef = _authored;
            if (_armedCopy != null) { Destroy(_armedCopy); _armedCopy = null; }
            _armedLabel = "";
        }

        string LabelOf(Entry entry)
        {
            if (entry == null) return "";
            if (!string.IsNullOrEmpty(entry.label)) return entry.label;
            return entry.recipe != null ? entry.recipe.name : "";
        }

        static Key DigitKey(int number)
        {
            switch (number)
            {
                case 0: return Key.Digit0;
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

        // Top-left: the sibling demo scripts already own the bottom of the view, and what is armed has to be
        // readable in a capture of the burst itself, or a screenshot cannot say which recipe it shows.
        void OnGUI()
        {
            if (!showKeyLegend) return;

            var style = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            style.normal.textColor = Color.white;

            var text = new System.Text.StringBuilder();
            text.Append("Death recipe: ")
                .Append(string.IsNullOrEmpty(_armedLabel) ? "the disc's own" : _armedLabel)
                .Append('\n');
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.recipe == null) continue;
                text.Append(entry.key).Append(": ").Append(LabelOf(entry)).Append('\n');
            }
            text.Append(restoreKey).Append(": the disc's own");

            float height = 20f * (entries.Length + 2) + 10f;
            GUI.Label(new Rect(10f, 10f, 620f, height), text.ToString(), style);
        }
    }
}
