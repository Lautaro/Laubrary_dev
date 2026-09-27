// A kept check, runnable from the Laubrary menu — answers one question that a great deal depends on: does the audio graph
// actually RENDER a sound while the editor is merely sitting there, or does it only hold onto it?
//
// This matters far more than it sounds. If the graph renders in edit mode, then pressing play on a sound and dragging a
// parameter is a complete edit-and-listen loop with no game running, which is the whole appeal of a real-time chain. If it
// does not, every audible check has to happen in play mode instead, and any claim that something "can be heard in the
// editor" is wrong.
//
// It is deliberately TWO steps with a wait in between, and that is the point. An earlier single-step version blocked the
// main thread for four hundred milliseconds while waiting, concluded that nothing was being rendered, and was measuring
// its own interference — a graph needs the main thread to service it, and a test that holds the main thread hostage
// starves the very thing it is testing. Start it, let the editor breathe, then read.
using System.Text;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;

public static class ZoundsEditModeRenderCheck {

    const float TONE_HZ = 440f;
    static GameObject host;
    static ZoundSapVoiceGenerator generator;

    [MenuItem("Laubrary/Zounds/Checks/10 - Step 1, start a tone playing")]
    public static void StartFromMenu() { Debug.Log("[ZoundsEditModeRenderCheck]\n" + Start()); }

    [MenuItem("Laubrary/Zounds/Checks/10 - Step 2, did it render")]
    public static void ReadFromMenu() { Debug.Log("[ZoundsEditModeRenderCheck]\n" + Read()); }

    /// <summary>Starts a looping tone and returns immediately, leaving it playing.</summary>
    public static string Start() {
        Stop();
        // Requested before the voice exists, because that is when the engine decides whether to keep a copy of its output.
        ZoundSapVoiceGenerator.monitorSamples = 4096;

        host = new GameObject("ZoundsEditModeRenderCheck") { hideFlags = HideFlags.DontSave };
        var carrier = host.AddComponent<AudioSource>();
        carrier.playOnAwake = false;

        int rate = AudioSettings.outputSampleRate;
        int frames = rate;
        var samples = new float[frames * 2];
        for (int i = 0; i < frames; i++) {
            float v = 0.6f * Mathf.Sin(2f * Mathf.PI * TONE_HZ * i / rate);
            samples[i * 2] = v;
            samples[i * 2 + 1] = v;
        }
        var pcm = new PcmClip {
            channels = 2, frequency = rate, frames = frames, samples = samples, valid = true, peak = 0.6f,
        };

        var sound = new Klip(-780) { name = "(edit-mode render check)" };
        sound.effectChain = new ZoundEffectChain();
        sound.effectChain.nodes.Add(new ZoundEffectNode(ZoundEffectType.Gain));

        generator = ZoundSapPlayback.EnsureGenerator(carrier);
        var layout = ChainLayout.Build(sound.effectChain, rate);
        generator.SetPlay(pcm, layout, 0d, frames, 1f, 1f, 1f, true, 780, true, sound);
        carrier.clip = null;
        carrier.generator = generator;
        carrier.loop = true;
        carrier.Play();

        return "A 440 Hz tone is now looping. You should be able to HEAR it. Give it a second, then run step 2.";
    }

    /// <summary>Reads what the running voice has actually produced.</summary>
    public static string Read() {
        var sb = new StringBuilder();
        if (generator == null) return "Nothing was started — run step 1 first.\n";

        sb.Append("graph reports it playing: ").Append(generator.IsPlaying).Append('\n');
        int pos = generator.MonitorWritePosition;
        sb.Append("samples written so far:   ").Append(pos).Append('\n');

        var buf = new float[1024];
        bool got = generator.ReadMonitor(buf);
        float peak = 0f;
        for (int i = 0; i < buf.Length; i++) { float a = Mathf.Abs(buf[i]); if (a > peak) peak = a; }

        // Counting zero crossings identifies a pure tone without needing a transform, and confirms the samples are the
        // sound that was played rather than something that merely looks plausible.
        int crossings = 0;
        for (int i = 1; i < buf.Length; i++) if ((buf[i - 1] < 0f) != (buf[i] < 0f)) crossings++;
        float impliedHz = crossings * 0.5f * AudioSettings.outputSampleRate / buf.Length;

        sb.Append("monitor readable:        ").Append(got).Append('\n');
        sb.Append("peak amplitude:          ").Append(peak.ToString("R")).Append('\n');
        sb.Append("implied frequency:       ").Append(impliedHz.ToString("F0")).Append(" Hz (played ")
          .Append(TONE_HZ.ToString("F0")).Append(")\n\n");

        if (pos <= 0) {
            sb.Append("THE GRAPH IS NOT RENDERING. It is holding the voice — it reports the sound as playing and will\n")
              .Append("accept a live parameter change — but it never asks it for audio. Anything audible therefore has to\n")
              .Append("be checked with the game running, and 'you can hear it in the editor' would be wrong.\n");
        }
        else if (peak > 0.05f) {
            sb.Append("THE GRAPH IS RENDERING, in edit mode, and the samples are the tone that was played. So pressing\n")
              .Append("play on a sound and dragging a parameter is a complete edit-and-listen loop with no game running.\n");
        }
        else {
            sb.Append("It is rendering but producing silence, which is a different and more interesting problem than not\n")
              .Append("rendering at all — the voice is being asked for audio and returning none.\n");
        }
        return sb.ToString();
    }

    [MenuItem("Laubrary/Zounds/Checks/10 - Step 3, stop and clean up")]
    public static void StopFromMenu() { Stop(); Debug.Log("[ZoundsEditModeRenderCheck] stopped and cleaned up."); }

    public static void Stop() {
        if (host != null) Object.DestroyImmediate(host);
        host = null;
        generator = null;
        // Left off, which is how it is in normal use: keeping a copy of the output costs the audio path a little, and
        // nothing should pay for that unless somebody is looking at it.
        ZoundSapVoiceGenerator.monitorSamples = 0;
    }
}
