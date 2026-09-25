using System.Text;
using UnityEngine;
using UnityEngine.Audio;

namespace Laubrary.Zounds.Dsp.Native {

    /// <summary>
    /// Plays a tone through the real realtime path — a bus mixer, the native effect on
    /// Unity's own mixer thread, a voice with a chain — and reports what the audio thread
    /// actually did, read straight out of the shared control block.
    ///
    /// This is the measurement half of "it works": the offline equivalence probe proves
    /// the DSP is right, and this proves it is really running on the mixer thread and
    /// reaching the output. The numbers to look at are the bus callback count (the mixer
    /// thread is ticking the plugin at all), the bus peak (something was written to the
    /// output), and the node's own peak and state (the voice rendered and completed).
    ///
    /// <see cref="Run"/> is a coroutine-free two-part call because a render takes real
    /// time: <see cref="Begin"/> starts it, then <see cref="Report"/> a second or so
    /// later says what happened.
    /// </summary>
    public static class ZoundNativeSmokeTest {

        private static ZoundNativeBuses buses;
        private static GameObject host;
        private static int nodeId = -1;
        private static long startCallbacks;
        private static float startedAt;

        /// <summary>
        /// Starts a one-second tone through bus 0 with a Gain chain on it. With
        /// <paramref name="loop"/> the source never runs out, which is what the GC
        /// measurement needs: something must actually be rendering while the collections
        /// are forced, or a clean result would only mean nothing was playing.
        /// </summary>
        public static string Begin(AudioMixerGroup target = null, float gain = 0.5f, bool loop = false) {
            var sb = new StringBuilder("[Zounds] native realtime smoke test\n");
            int sampleRate = AudioSettings.outputSampleRate;
            AudioSettings.GetDSPBufferSize(out int bufferSize, out _);

            if (!ZoundsNative.Available && !ZoundsNative.Initialise(sampleRate, bufferSize)) {
                sb.Append("  the native engine is not available: ").Append(ZoundsNative.LoadError).Append('\n');
                return sb.ToString();
            }

            End();
            host = new GameObject("ZoundsNativeSmokeTest") { hideFlags = HideFlags.HideAndDontSave };
            buses = new ZoundNativeBuses(host.transform, HideFlags.HideAndDontSave, sampleRate);
            int bus = buses.GetOrCreate(target);
            if (bus < 0) { sb.Append("  no bus: ").Append(buses.SetupError).Append('\n'); return sb.ToString(); }
            sb.Append("  bus ").Append(bus).Append(" created, routed to ")
              .Append(target != null ? target.name : "(listener)").Append('\n');

            // A second of a 440 Hz tone, stereo.
            int frames = sampleRate;
            var pcm = new float[frames * 2];
            for (int i = 0; i < frames; i++) {
                float v = 0.6f * Mathf.Sin(2f * Mathf.PI * 440f * i / sampleRate);
                pcm[i * 2] = v; pcm[i * 2 + 1] = v;
            }
            const int PCM_ID = 2;
            if (ZoundsNative.Zounds_UploadPcm(PCM_ID, pcm, frames, 2, sampleRate, 0.6f) == 0) {
                sb.Append("  the PCM upload was refused\n"); return sb.ToString();
            }

            var chain = new ZoundEffectChain();
            chain.nodes.Add(new ZoundEffectNode(ZoundEffectType.Gain) { p = new[] { gain } });
            var layout = ChainLayout.Build(chain, sampleRate);
            int layoutId = NativeChainBlob.IdFor(layout);
            if (layoutId == 0) { sb.Append("  the chain layout was refused\n"); return sb.ToString(); }

            nodeId = 0;
            ZoundsNative.Zounds_ForceFree(nodeId);
            var args = new ZoundsNative.PrepareArgs {
                tokenId = 9001, busIndex = bus, groupIndex = -1, depth = 0,
                pcmId = PCM_ID, layoutId = layoutId, isGroup = 0,
                startFrame = 0, endFrame = frames,
                basePitch = 1f, outGain = 1f, sourceDuration = 1f, loop = loop ? 1 : 0,
            };
            if (ZoundsNative.Zounds_PrepareNode(nodeId, ref args) == 0) {
                sb.Append("  the node refused to prepare\n"); return sb.ToString();
            }
            unsafe { startCallbacks = ZoundsNative.Bus(bus)->callbackCount; }
            startedAt = Time.realtimeSinceStartup;
            ZoundsNative.Zounds_PublishNode(nodeId);
            sb.Append("  a 440 Hz tone is playing through a Gain of ").Append(gain)
              .Append("; call Report in about a second\n");
            return sb.ToString();
        }

        /// <summary>What the audio thread did since <see cref="Begin"/>.</summary>
        public static unsafe string Report() {
            var sb = new StringBuilder("[Zounds] native realtime smoke test — report\n");
            if (!ZoundsNative.Available) { sb.Append("  the native engine is not available\n"); return sb.ToString(); }
            float elapsed = Time.realtimeSinceStartup - startedAt;

            int busIndex = -1;
            var node = ZoundsNative.Node(nodeId >= 0 ? nodeId : 0);
            busIndex = node->busIndex;
            var bus = ZoundsNative.Bus(busIndex);
            var header = ZoundsNative.Header();

            long callbacks = bus->callbackCount - startCallbacks;
            sb.Append("  after ").Append(elapsed.ToString("F2")).Append(" s:\n");
            sb.Append("  bus ").Append(busIndex).Append(": bound=").Append(bus->bound)
              .Append(" callbacks=").Append(callbacks)
              .Append(" lastPeak=").Append(bus->lastPeak.ToString("F4"))
              .Append(" nodesRendered=").Append(bus->lastNodes)
              .Append(" frames=").Append(bus->lastFrames).Append('x').Append(bus->lastChannels)
              .Append(" stalls=").Append(bus->stallCount)
              .Append(" maxCallbackMs=").Append(ToMs(bus->maxCallbackTicks, header->qpcFrequency).ToString("F3"))
              .Append(" maxGapMs=").Append(ToMs(bus->maxGapTicks, header->qpcFrequency).ToString("F1"))
              .Append('\n');
            sb.Append("  node ").Append(nodeId).Append(": state=").Append((VoiceState)node->state)
              .Append(" peak=").Append(node->lastPeak.ToString("F4"))
              .Append(" elapsed=").Append(node->elapsedSamples)
              .Append(" onsets=").Append(node->onsetTotal)
              .Append('\n');
            sb.Append("  dsp clock=").Append(header->dspSampleClock).Append(" sampleRate=").Append(header->sampleRate).Append('\n');

            // The verdict, spelled out, so a passing run cannot be confused with a run
            // where the mixer never called the plugin at all.
            if (callbacks == 0) sb.Append("  VERDICT: the mixer never called the plugin — the bus mixer is suspended or the effect is not on it.\n");
            else if (bus->lastPeak <= 0.0001f && node->state == (int)VoiceState.Free && node->lastPeak > 0.0001f)
                sb.Append("  VERDICT: the voice rendered and has already finished; the bus is quiet again because the tone is over.\n");
            else if (node->lastPeak <= 0.0001f && node->elapsedSamples == 0)
                sb.Append("  VERDICT: the plugin is ticking but the voice never rendered.\n");
            else sb.Append("  VERDICT: the plugin is ticking on the mixer thread and the voice rendered through it.\n");
            return sb.ToString();
        }

        private static double ToMs(long ticks, long freq) => freq > 0 ? ticks * 1000.0 / freq : 0.0;

        public static void End() {
            if (nodeId >= 0 && ZoundsNative.Available) ZoundsNative.Zounds_ForceFree(nodeId);
            nodeId = -1;
            buses?.Teardown();
            buses = null;
            if (host != null) {
                if (Application.isPlaying) Object.Destroy(host); else Object.DestroyImmediate(host);
                host = null;
            }
        }
    }
}
