using System.Diagnostics;
using Unity.Burst;
using UnityEngine;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds.Checks {

    /// <summary>
    /// The listening and stutter check for the native effect-chain path: plays a real chain through the audio
    /// graph, lets you make the garbage collector do real work on demand, and shows on screen what happened.
    ///
    /// **Why this exists as a scene rather than as a test.** Everything about this port that can be decided by
    /// comparing numbers has been, and all of it passes. Two things cannot be: whether it SOUNDS right, and
    /// whether audio keeps flowing while the garbage collector is busy — which is the entire reason for the
    /// work. Both need a human with speakers, and the second needs a real build, because the editor is not the
    /// thing being shipped.
    ///
    /// **It generates its own sound and needs no assets**, so it works in an empty project and in a build
    /// without anything having to be included. A previous check in this codebase was made worthless by using a
    /// real asset that turned out to be a few dozen milliseconds long, so this one uses a two-second tone that
    /// sweeps, where a dropout is unmistakable.
    ///
    /// **Read the on-screen notice about the listener.** A missing audio listener produces perfect silence with
    /// no error of any kind, and has already cost one session in this codebase, so it is checked for and called
    /// out rather than left to be puzzled over.
    ///
    /// **One measurement caveat that invalidates results if ignored.** Once a managed audio callback has run in
    /// a process, that process's audio thread stays attached to the scripting runtime for good — so a run that
    /// has ALREADY played the old managed path cannot then give a valid reading for this one. Compare the two
    /// paths in two separate launches, never by switching inside one.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class ZoundsSapCheck : MonoBehaviour {

        [Header("Sound")]
        [Tooltip("Length of the generated tone, in seconds.")]
        public float toneSeconds = 2f;
        [Tooltip("The tone sweeps between these two frequencies, so a dropout is obvious.")]
        public float fromHz = 180f;
        public float toHz = 720f;

        [Header("Chain")]
        public bool useDelay = true;
        public bool useReverb = true;

        [Header("Garbage pressure")]
        [Tooltip("How many cross-linked objects to build per wave. A mark phase's cost follows the number of " +
                 "references it has to walk, not the number of bytes, so a big flat buffer would prove nothing.")]
        public int nodesPerWave = 1500000;

        private AudioSource source;
        private ZoundSapVoiceGenerator generator;

        private string status = "not started";
        private double lastCollectionMs;
        private int collections;
        private bool listenerMissing;
        private float liveSweep;
        private bool sweeping;

        private void Awake() {
            source = GetComponent<AudioSource>();
            generator = GetComponent<ZoundSapVoiceGenerator>();
            if (generator == null) generator = gameObject.AddComponent<ZoundSapVoiceGenerator>();
            listenerMissing = FindAnyObjectByType<AudioListener>() == null;
        }

        private void Start() {
            Play();
        }

        /// <summary>Starts (or restarts) the looping tone through the chain.</summary>
        public void Play() {
            var clip = BuildTone();
            var chain = BuildChain();
            var layout = chain.IsEmpty ? ChainLayout.Empty : ChainLayout.Build(chain, AudioSettings.outputSampleRate);

            generator.SetPlay(clip, layout, 0d, clip.frames, 1f, 0.8f,
                              (float)clip.frames / clip.frequency, loop: true,
                              tokenId: 1, heavyTier: true);

            source.generator = generator;
            source.Play();
            status = "playing";
        }

        private PcmClip BuildTone() {
            int rate = AudioSettings.outputSampleRate;
            int frames = Mathf.Max(1, (int)(toneSeconds * rate));
            var samples = new float[frames * 2];
            float phase = 0f;
            float peak = 0f;
            for (int i = 0; i < frames; i++) {
                float t = (float)i / frames;
                float hz = Mathf.Lerp(fromHz, toHz, t);
                phase += 2f * Mathf.PI * hz / rate;
                if (phase > 2f * Mathf.PI) phase -= 2f * Mathf.PI;
                // A gentle fade at both ends, so looping the tone does not click on every repeat and get
                // mistaken for the dropout this check is looking for.
                float edge = Mathf.Min(1f, Mathf.Min(t, 1f - t) * 40f);
                float s = 0.45f * edge * Mathf.Sin(phase);
                samples[i * 2] = s;
                samples[i * 2 + 1] = s * 0.85f;
                float a = s < 0 ? -s : s;
                if (a > peak) peak = a;
            }
            return new PcmClip {
                channels = 2, frequency = rate, frames = frames,
                samples = samples, valid = true, peak = peak,
            };
        }

        private ZoundEffectChain BuildChain() {
            var chain = new ZoundEffectChain();
            if (useDelay) {
                var d = new ZoundEffectNode(ZoundEffectType.Delay);
                d.p[0] = 260f; d.p[1] = 0.45f; d.p[2] = 0.35f; d.p[3] = 600f; d.p[4] = 0f;
                chain.nodes.Add(d);
            }
            if (useReverb) {
                var r = new ZoundEffectNode(ZoundEffectType.Reverb);
                r.p[0] = 0.6f; r.p[1] = 0.4f; r.p[2] = 1f; r.p[3] = 0.35f;
                chain.nodes.Add(r);
            }
            return chain;
        }

        /// <summary>
        /// Builds a wave of real garbage and forces a collection, reporting how long the collection itself took.
        /// If audio is on its own native thread, the sound continues through this untouched. If it is not, the
        /// sound stops for roughly as long as the number shown.
        /// </summary>
        public void ForceCollection() {
            var nodes = new Node[nodesPerWave];
            for (int i = 0; i < nodesPerWave; i++) nodes[i] = new Node { tag = i };
            var rng = new System.Random(12345);
            for (int i = 0; i < nodesPerWave; i++) {
                nodes[i].a = nodes[rng.Next(nodesPerWave)];
                nodes[i].b = nodes[rng.Next(nodesPerWave)];
            }
            kept.Add(nodes);

            var sw = Stopwatch.StartNew();
            System.GC.Collect(System.GC.MaxGeneration, System.GCCollectionMode.Forced, true, true);
            sw.Stop();
            lastCollectionMs = sw.Elapsed.TotalMilliseconds;
            collections++;
        }

        private sealed class Node { public object a, b; public int tag; }
        private readonly System.Collections.Generic.List<object> kept = new System.Collections.Generic.List<object>();

        private void Update() {
            if (Input.GetKeyDown(KeyCode.Alpha1)) Play();
            if (Input.GetKeyDown(KeyCode.Alpha2)) ForceCollection();
            if (Input.GetKeyDown(KeyCode.Alpha3)) { sweeping = !sweeping; }
            if (Input.GetKeyDown(KeyCode.Alpha4)) { generator.ReleaseLive(); status = "released, tail ringing"; }
            if (Input.GetKeyDown(KeyCode.Alpha5)) { generator.StopLive(); status = "stopped"; }

            // A slowly moving parameter, to hear whether a live change reaches a sound that is already playing
            // and whether it does so smoothly. The delay's mix is the first parameter of the first node when the
            // delay is in the chain; asking the layout for it rather than assuming keeps this honest.
            if (sweeping && useDelay) {
                liveSweep += Time.deltaTime * 0.4f;
                float mix = 0.5f + 0.5f * Mathf.Sin(liveSweep);
                generator.SetParameterLive(2, mix);   // source stage takes indexes 0 and 1; the delay's Time is 2
            }
        }

        private void OnGUI() {
            const int pad = 14;
            var area = new Rect(pad, pad, 640, 320);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + pad, area.y + pad, area.width - pad * 2, area.height - pad * 2));

            GUILayout.Label("Zounds native effect-chain check");
            GUILayout.Space(6);

            if (listenerMissing) {
                GUILayout.Label("NO AUDIO LISTENER IN THE SCENE — there will be perfect silence and no error. " +
                                "Add one to a camera.");
                GUILayout.Space(6);
            }

            GUILayout.Label("compiled audio path enabled: " + (BurstCompiler.IsEnabled ? "YES" : "NO — this will stutter"));
            GUILayout.Label("state: " + status + (generator != null && generator.IsPlaying ? "  (graph reports it playing)" : ""));
            GUILayout.Label("sounds registered as playing: " + SapVoiceRegistry.Count);
            GUILayout.Label("output rate: " + AudioSettings.outputSampleRate + " Hz");
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int numBuffers);
            GUILayout.Label("audio buffer: " + bufferLength + " frames x " + numBuffers +
                            (bufferLength >= 1024 ? "   (large — shrink it to make this a real test)" : ""));
            GUILayout.Label("managed heap: " + (System.GC.GetTotalMemory(false) / (1024f * 1024f)).ToString("F1") + " MB");
            GUILayout.Label("collections forced: " + collections +
                            (collections > 0 ? "   last one took " + lastCollectionMs.ToString("F0") + " ms" : ""));
            GUILayout.Space(6);
            GUILayout.Label("1 restart   2 force a collection   3 sweep a parameter live   4 release   5 stop");
            GUILayout.Label("The tone should continue unbroken through a forced collection. If it stops for about");
            GUILayout.Label("as long as the figure above, audio is still tied to the scripting runtime.");

            GUILayout.EndArea();
        }
    }
}
