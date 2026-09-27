// A kept check, runnable from the Laubrary menu — confirms that a sound set up to play through the effect chain
// really is wired to it: no rendered file, a chain voice attached, and the voice reachable for live edits.
//
// Deliberately non-destructive. It builds the sound it tests entirely in memory and never adds it to the project's
// library, so it cannot disturb whatever someone is authoring at the time. It also does not write, delete or import
// any asset; it only reads one existing source file.
//
// What it can and cannot establish: whether audio is actually AUDIBLE needs a person and a running game, because
// edit mode does not necessarily drive the audio graph. What it does establish is everything up to that point, which
// is where a wiring mistake would actually be — the source audio being readable, the chain being laid out, the
// rendered file being bypassed, and the voice being addressable by the editor for a live change.
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;

public static class ZoundsRealtimePathCheck {

    [MenuItem("Laubrary/Zounds/Checks/7 - Is playback wired to the effect chain")]
    public static void RunFromMenu() { Execute(); }

    public static string Execute() {
        var sb = new StringBuilder();
        sb.Append("=== IS PLAYBACK WIRED TO THE EFFECT CHAIN? ===\n");
        int failures = 0;

        // One real source file, whichever is to hand. Read only.
        var clip = FindAnySourceClip(out string clipPath);
        if (clip == null) {
            sb.Append("  no source audio found to test with — put a .wav under Assets/ and run again\n");
            Debug.Log("[ZoundsRealtimePathCheck]\n" + sb);
            return sb.ToString();
        }
        sb.Append("  source audio: ").Append(clipPath).Append("  (").Append(clip.channels).Append("ch, ")
          .Append(clip.frequency).Append(" Hz, load type ").Append(clip.loadType).Append(")\n");

        string why = ZoundPcmCache.Validate(clip);
        if (!string.IsNullOrEmpty(why)) {
            sb.Append("  NOTE: this clip cannot feed the chain (").Append(why)
              .Append("). That is a per-clip import setting, not a wiring fault.\n");
        }

        // An in-memory sound, never added to the library.
        var klip = new Klip(-999) { name = "(realtime path check)" };
        klip.audioClipPath = clipPath;
        klip.effectChain = new ZoundEffectChain();
        var gain = new ZoundEffectNode(ZoundEffectType.Gain);
        gain.p[0] = 0.4f;
        klip.effectChain.nodes.Add(gain);

        var host = new GameObject("ZoundsRealtimePathCheck") { hideFlags = HideFlags.HideAndDontSave };
        try {
            var carrier = host.AddComponent<AudioSource>();
            carrier.playOnAwake = false;
            // Stand in for the rendered file, so "the rendered file was bypassed" is a real observation rather
            // than something that was never set in the first place.
            carrier.clip = clip;

            var voice = ZoundSapPlayback.StartVoice(klip, carrier, clip, 1f, 1f, 12345,
                                                    out string reason, out float duration);

            if (voice == null) {
                sb.Append("  StartVoice declined: ").Append(reason).Append('\n');
                sb.Append(string.IsNullOrEmpty(why)
                    ? "  <<< FAILED: it should have accepted this clip\n"
                    : "  (expected, given the note above — not a wiring fault)\n");
                if (string.IsNullOrEmpty(why)) failures++;
            }
            else {
                failures += Check(sb, "the rendered file is bypassed (no clip on the audio source)", carrier.clip == null);
                failures += Check(sb, "a chain voice is attached to the audio source",
                                  carrier.GetComponent<ZoundSapVoiceGenerator>() == voice);
                failures += Check(sb, "the voice knows which sound it plays, so an edit can find it",
                                  ReferenceEquals(voice.playingZound, klip));
                failures += Check(sb, "a play length was worked out without a file to measure", duration > 0f);
                failures += Check(sb, "the chain was laid out with our effect in it",
                                  voice.playingLayout != null && voice.playingLayout.nodeCount == 1);

                // The editor addresses an edit as "effect 0, parameter 0". Confirm that resolves to a real slot.
                int flat = SapVoiceRegistry.FlatIndexOf(voice.playingLayout, 0, 0);
                failures += Check(sb, "an editor's (effect, parameter) pair resolves to a real engine slot", flat >= 0);
                sb.Append("     effect 0 parameter 0 resolves to flat slot ").Append(flat).Append('\n');

                sb.Append("  voices the registry is tracking: ").Append(SapVoiceRegistry.Count).Append('\n');
                sb.Append("  NOT checked here, needs a running game and a person: whether it is audible, and\n")
                  .Append("  whether a live change is delivered — delivery needs the audio graph to have really\n")
                  .Append("  taken the voice, which edit mode does not guarantee.\n");
            }
        }
        finally {
            Object.DestroyImmediate(host);
        }

        sb.Append(failures == 0 ? "\nVERDICT: playback is wired to the chain.\n"
                                : "\nVERDICT: " + failures + " wiring check(s) FAILED.\n");
        var result = sb.ToString();
        Debug.Log("[ZoundsRealtimePathCheck]\n" + result);
        return result;
    }

    static int Check(StringBuilder sb, string what, bool ok) {
        sb.Append(ok ? "  ok   " : "  FAIL ").Append(what).Append('\n');
        return ok ? 0 : 1;
    }

    /// Any audio file in the project, preferring one already imported so it can be read.
    static AudioClip FindAnySourceClip(out string path) {
        path = null;
        var guids = AssetDatabase.FindAssets("t:AudioClip");
        string firstAny = null;
        foreach (var g in guids) {
            string p = AssetDatabase.GUIDToAssetPath(g);
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>(p);
            if (c == null) continue;
            if (firstAny == null) firstAny = p;
            if (string.IsNullOrEmpty(ZoundPcmCache.Validate(c))) { path = p; return c; }
        }
        if (firstAny == null) return null;
        path = firstAny;
        return AssetDatabase.LoadAssetAtPath<AudioClip>(firstAny);
    }
}
