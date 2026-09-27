// A kept check, runnable from the Laubrary menu — answers the one question that stands between the engine and sharing a
// single copy of a sound's audio between all the voices playing it: when the engine tears a playing sound down, has the
// audio side ACTUALLY stopped rendering it by the time the call returns, or has it only been told to?
//
// Why this cannot be settled by reading the documentation or by reasoning about it. The engine's teardown call is the
// strongest thing on offer and it would be a strange design if it did not wait — but this branch has three separate
// occasions where exactly that kind of confident reasoning was wrong, and each was caught only by measuring. The whole
// point of a memory-safety barrier is that it does not rest on an assumption.
//
// It is three steps with the editor left alone in between, deliberately. An earlier check on this engine held the main
// thread at full tilt for four hundred milliseconds, concluded nothing was being rendered, and was measuring its own
// interference: a busy main thread starves the very thing being watched. So step 2 exists purely to establish that the
// audio side keeps going while the main thread is asleep — because if it did not, then every "it has gone quiet" reading
// a waiting barrier ever takes would be the wait's own doing rather than the engine's, and the barrier would be a
// comforting illusion.
//
// A note on running step 3: if the bad case is real, the audio side is rendering a voice whose memory has just been
// released. That is not a hazard this check introduces — it is what the engine already does at every library
// invalidation — but it is why the answer is worth having.
using System.Text;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;

public static class ZoundsStopCompletionCheck {

    const float TONE_HZ = 440f;
    const int SLEEP_MS = 60;

    static GameObject host;
    static ZoundSapVoiceGenerator generator;

    [MenuItem("Laubrary/Zounds/Checks/14 - Step 1, start a sound playing")]
    public static void StartFromMenu() { Debug.Log("[ZoundsStopCompletionCheck]\n" + Start()); }

    [MenuItem("Laubrary/Zounds/Checks/14 - Step 2, does it render while the main thread sleeps")]
    public static void IndependenceFromMenu() { Debug.Log("[ZoundsStopCompletionCheck]\n" + MeasureIndependence()); }

    [MenuItem("Laubrary/Zounds/Checks/14 - Step 3, does tearing down actually stop it")]
    public static void BarrierFromMenu() { Debug.Log("[ZoundsStopCompletionCheck]\n" + MeasureBarrier()); }

    [MenuItem("Laubrary/Zounds/Checks/14 - Step 4, clean up")]
    public static void CleanUpFromMenu() { CleanUp(); Debug.Log("[ZoundsStopCompletionCheck] cleaned up."); }

    /// <summary>Starts a looping tone through the real audio graph and returns immediately, leaving it playing.</summary>
    public static string Start() {
        CleanUp();

        host = new GameObject("ZoundsStopCompletionCheck") { hideFlags = HideFlags.DontSave };
        var carrier = host.AddComponent<AudioSource>();
        carrier.playOnAwake = false;
        carrier.volume = 0.25f;

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

        // Built in memory and never added to the library: the owner authors in this project live, so a check that left
        // a sound behind would be editing his work.
        var sound = new Klip(-781) { name = "(stop-completion check)" };
        sound.effectChain = new ZoundEffectChain();
        sound.effectChain.nodes.Add(new ZoundEffectNode(ZoundEffectType.Gain));

        generator = ZoundSapPlayback.EnsureGenerator(carrier);
        var layout = ChainLayout.Build(sound.effectChain, rate);
        generator.SetPlay(pcm, layout, 0d, frames, 1f, 1f, 1f, true, 781, true, sound);
        carrier.clip = null;
        carrier.generator = generator;
        carrier.loop = true;
        carrier.Play();

        return "A quiet 440 Hz tone is now looping. Give the editor a second, then run step 2.";
    }

    /// <summary>
    /// Establishes whether the audio side keeps rendering while the main thread sleeps — the prerequisite for any
    /// waiting barrier meaning anything at all.
    /// </summary>
    public static string MeasureIndependence() {
        var sb = new StringBuilder();
        if (generator == null) return "Nothing was started — run step 1 first.\n";

        long before = generator.RenderTicket;
        if (before < 0L) return "This voice has no block counter, which should be impossible — it is allocated with the voice.\n";

        System.Threading.Thread.Sleep(SLEEP_MS);
        long after = generator.RenderTicket;

        // Two bumps per block, so the number of blocks is half the movement.
        long blocks = (after - before) / 2L;
        AudioSettings.GetDSPBufferSize(out int bufferLength, out int _);
        int rate = AudioSettings.outputSampleRate;
        double expected = bufferLength > 0 && rate > 0 ? SLEEP_MS / 1000d * rate / bufferLength : 0d;

        sb.Append("graph reports it playing:  ").Append(generator.IsPlaying).Append('\n');
        sb.Append("counter before the sleep:  ").Append(before).Append('\n');
        sb.Append("counter after the sleep:   ").Append(after).Append('\n');
        sb.Append("blocks during ").Append(SLEEP_MS).Append(" ms:     ").Append(blocks)
          .Append(" (expected about ").Append(expected.ToString("F0")).Append(" at ")
          .Append(bufferLength).Append(" frames per block)\n");
        sb.Append("caught mid-block:          ").Append(generator.RenderInProgress).Append("\n\n");

        if (blocks <= 0L) {
            sb.Append("THE AUDIO SIDE DID NOT ADVANCE while the main thread slept. If that is genuinely how this graph\n")
              .Append("behaves, then a barrier that WAITS for quiet is worthless — the waiting would be what produced the\n")
              .Append("quiet, and it would report success in exactly the situation it is meant to catch. Do not rely on the\n")
              .Append("barrier until this is understood; check first that the sound is actually playing, because a stopped\n")
              .Append("sound gives the same reading for an entirely innocent reason.\n");
        }
        else if (blocks < expected * 0.5d) {
            sb.Append("It advanced, but well short of the expected rate. Worth understanding before trusting a settle\n")
              .Append("window measured in blocks, since the window assumes blocks arrive one buffer apart.\n");
        }
        else {
            sb.Append("THE AUDIO SIDE IS INDEPENDENT of the main thread: it rendered at roughly the expected rate while\n")
              .Append("the main thread was asleep. So a barrier that sleeps and watches the counter is observing the\n")
              .Append("engine rather than its own interference, and a counter that has stopped moving means the engine\n")
              .Append("has stopped rendering.\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// The question itself: does the graph's teardown return before or after its audio side has let go — and does the
    /// confirming barrier agree?
    /// </summary>
    public static string MeasureBarrier() {
        var sb = new StringBuilder();
        if (generator == null) return "Nothing was started — run step 1 first.\n";
        if (!generator.IsPlaying) return "The sound is no longer playing, so there is nothing to tear down. Run step 1 again.\n";

        long beforeTeardown = generator.RenderTicket;

        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool torn = generator.DestroyAndConfirm(out bool confirmed);
        double teardownMs = clock.Elapsed.TotalMilliseconds;
        long atReturn = generator.RenderTicket;

        // Read before the sleep, because these are what distinguish the two explanations for a counter that carries on
        // moving: the torn-down sound still being rendered, or the audio source having simply asked for a NEW one.
        bool graphHasInstanceAgain = generator.IsPlaying;
        int registeredAfter = SapVoiceRegistry.Count;
        var carrier = host != null ? host.GetComponent<AudioSource>() : null;
        bool carrierStillPlaying = carrier != null && carrier.isPlaying;

        System.Threading.Thread.Sleep(SLEEP_MS);
        long afterWait = generator.RenderTicket;

        long duringTeardown = (atReturn - beforeTeardown) / 2L;
        long afterReturn = (afterWait - atReturn) / 2L;

        sb.Append("there was a sound to tear down:  ").Append(torn).Append('\n');
        sb.Append("seen to stop before destroying:  ").Append(confirmed).Append('\n');
        sb.Append("whole teardown took:             ").Append(teardownMs.ToString("F1"))
          .Append(" ms (settle window ").Append((SapVoiceRegistry.DefaultSettleSeconds * 1000d).ToString("F0"))
          .Append(" ms)\n");
        sb.Append("audio source still playing:      ").Append(carrierStillPlaying).Append('\n');
        sb.Append("graph still has an instance:     ").Append(graphHasInstanceAgain).Append('\n');
        sb.Append("voices registered afterwards:    ").Append(registeredAfter).Append('\n');
        sb.Append("blocks during the teardown:      ").Append(duringTeardown).Append('\n');
        sb.Append("blocks AFTER it returned:        ").Append(afterReturn).Append('\n');
        sb.Append("counter left mid-block:          ").Append((afterWait & 1L) != 0L).Append("\n\n");

        if (!confirmed) {
            sb.Append("THE SOUND COULD NOT BE SEEN TO STOP, so it was deliberately left playing rather than destroyed.\n")
              .Append("That is the refusal working as designed, but it should not happen to an ordinary sound on an idle\n")
              .Append("machine — if it happens here, silencing the audio source is not stopping the mixer pulling on it,\n")
              .Append("and nothing may be freed until that is understood.\n");
        }
        else if (afterReturn > 0L || carrierStillPlaying || graphHasInstanceAgain) {
            sb.Append("SOMETHING IS STILL ALIVE AFTER A CONFIRMED TEARDOWN, which should be impossible: either a block\n")
              .Append("was rendered after the sound was destroyed, or the audio source is still playing and will ask for a\n")
              .Append("fresh sound behind us. Investigate before freeing anything.\n");
        }
        else {
            sb.Append("THE TEARDOWN IS SAFE AND COMPLETE. The audio source was silenced, the sound was observed to stop\n")
              .Append("being rendered, and only then was it destroyed — nothing rendered afterwards and nothing is left\n")
              .Append("holding it. This is the barrier sharing one copy of a sound's audio between voices depends on.\n\n")
              .Append("Worth knowing what this replaced: destroying the sound WITHOUT silencing its audio source first\n")
              .Append("left the mixer pulling on something that no longer existed, once per audio block, for twenty-seven\n")
              .Append("seconds, and then the engine's own thread-safety validation faulted and closed the editor. If the\n")
              .Append("console fills with complaints about an invalid processor handle after a teardown, that is back.\n");
        }
        sb.Append("\nRun step 4 to clean up.\n");
        return sb.ToString();
    }

    public static void CleanUp() {
        if (host != null) Object.DestroyImmediate(host);
        host = null;
        generator = null;
    }
}
