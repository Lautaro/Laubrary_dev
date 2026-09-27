using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds.EditorTools {

    /// <summary>
    /// A silent, display-only play of a chain's modifiers, run by the real engine in real time, so the analyser can show
    /// what the chain would be doing right now even while nothing is playing.
    ///
    /// **Why the engine itself and not a re-computation.** The values that matter — where every modulated parameter is at
    /// this instant — come out of the engine's own modifier code: oscillator shapes, step lists, glides, strength curves,
    /// and the "keeps running between plays" clocks (an oscillator set to Always, a timed step list with Retrigger off).
    /// Re-deriving them here would be a second implementation free to drift from the one being heard. So this starts an
    /// ordinary voice exactly as a play would — which joins every running clock where it has got to — and steps it forward
    /// as real time passes. The effects are switched off in its copy of the chain, because only the parameter values are
    /// read and nothing listens to the audio; switching an effect off does not switch off its modulation.
    ///
    /// Owns native memory, so it is released on every restart, on <see cref="Dispose"/>, and before scripts reload.
    /// </summary>
    public sealed class ChainDisplayVoice : System.IDisposable {

        const int SR = 48000;
        const int STEP = ZoundDspConstants.CONTROL_BLOCK;

        static readonly HashSet<ChainDisplayVoice> live = new HashSet<ChainDisplayVoice>();
        static bool hooked;

        SapRealtimeVoice voice;
        bool created;
        ChainLayout layout;
        PcmClip silence;
        int renderedSteps;

        public float playSeconds { get; private set; }
        public bool isRunning => created;

        /// <summary>Starts a fresh display play of <paramref name="chain"/>, lasting <paramref name="seconds"/>.</summary>
        public void Start(ZoundEffectChain chain, float seconds, Zound zound) {
            ReleaseVoice();
            if (chain == null || chain.IsEmpty || chain.modifiers == null || chain.modifiers.Count == 0) return;
            HookReload();

            var copy = chain.DeepCopy();
            foreach (var n in copy.nodes) n.enabled = false;
            layout = ChainLayout.Build(copy, SR);
            playSeconds = System.Math.Max(0.05f, seconds);
            int frames = (int)(playSeconds * SR) + STEP * 2;
            if (silence == null || silence.frames != frames)
                silence = new PcmClip { channels = 1, frequency = SR, frames = frames, samples = new float[frames], valid = true, peak = 0f };

            voice = SapRealtimeVoice.Create(silence, layout, SR, 0d, frames, 1f, 1f, playSeconds, false, 7, false,
                                            Allocator.Persistent, zound);
            created = true;
            renderedSteps = 0;
            live.Add(this);
        }

        /// <summary>Moves the display play forward to <paramref name="seconds"/> after its start, one engine step at a time.</summary>
        public void AdvanceTo(float seconds) {
            if (!created) return;
            int target = (int)(System.Math.Min(seconds, playSeconds) * SR / STEP);
            // Bounded, so a long stall (a breakpoint, a slow frame) cannot turn into one enormous catch-up.
            int budget = 4096;
            while (renderedSteps < target && !voice.finished && budget-- > 0) {
                voice.RenderBlock(STEP);
                renderedSteps++;
            }
        }

        /// <summary>The value the display play is applying to one effect parameter right now.</summary>
        public bool TryGetParam(int nodeIndex, int paramIndex, out float value) {
            value = 0f;
            if (!created) return false;
            int flat = SapVoiceRegistry.FlatIndexOf(layout, nodeIndex, paramIndex);
            var p = voice.sap.pLive;
            if (flat < 0 || !p.IsCreated || flat >= p.Length) return false;
            value = p[flat];
            return true;
        }

        void ReleaseVoice() {
            if (created) { voice.Dispose(); created = false; }
            live.Remove(this);
        }

        public void Dispose() => ReleaseVoice();

        static void HookReload() {
            if (hooked) return;
            hooked = true;
            AssemblyReloadEvents.beforeAssemblyReload += () => {
                foreach (var v in new List<ChainDisplayVoice>(live)) v.ReleaseVoice();
            };
            EditorApplication.quitting += () => {
                foreach (var v in new List<ChainDisplayVoice>(live)) v.ReleaseVoice();
            };
        }
    }
}
