// A kept check, runnable from the Laubrary menu — read-only. For every sound in the library, reports whether the real-time path can find its ORIGINAL audio and
// whether that audio is readable. Changes nothing.
using System.Text;
using UnityEditor;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;

public static class ZoundsSourceAudioCheck {

    [MenuItem("Laubrary/Zounds/Checks/8 - Can each sound reach the chain")]
    public static void RunFromMenu() { UnityEngine.Debug.Log("[ZoundsSourceAudioCheck]\n" + Execute()); }

    public static string Execute() {
        var sb = new StringBuilder();
        sb.Append("=== CAN THE REAL-TIME PATH FIND EACH SOUND'S ORIGINAL AUDIO? ===\n");
        var project = ZoundsProject.Instance;
        if (project == null || project.zoundLibrary == null) return sb.Append("no Zounds project loaded\n").ToString();

        var klips = project.zoundLibrary.klips;
        int found = 0, readable = 0;
        for (int i = 0; i < klips.Count; i++) {
            var k = klips[i];
            if (k == null) continue;
            sb.Append("  '").Append(k.name).Append("'\n");
            sb.Append("      source path: ").Append(string.IsNullOrEmpty(k.audioClipPath) ? "(none)" : k.audioClipPath).Append('\n');
            sb.Append("      output path: ").Append(string.IsNullOrEmpty(k.outputClipPath) ? "(none)" : k.outputClipPath).Append('\n');

            var clip = ZoundSapPlayback.LoadSourceClip(k);
            if (clip == null) {
                sb.Append("      real-time path: CANNOT find the source audio -> would fall back to the rendered file\n");
                continue;
            }
            found++;
            string why = ZoundPcmCache.Validate(clip);
            if (!string.IsNullOrEmpty(why)) {
                sb.Append("      real-time path: found '").Append(clip.name).Append("' but ").Append(why).Append('\n');
                continue;
            }
            readable++;
            sb.Append("      real-time path: OK -> plays through the chain from '").Append(clip.name).Append("'\n");
        }

        sb.Append("\nklips: ").Append(klips.Count).Append("   source found: ").Append(found)
          .Append("   will use the chain: ").Append(readable).Append('\n');
        if (klips.Count == 0) sb.Append("(no klips authored yet - add one and run this again)\n");
        return sb.ToString();
    }
}
