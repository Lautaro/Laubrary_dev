using Stopwatch = System.Diagnostics.Stopwatch;
using Unity.Burst;
using UnityEngine;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds.Checks {

    /// <summary>
    /// A small on-screen garbage-collection stress test that works in any build, editor or player: a button that forces a
    /// real collection while sound plays, and a verdict on whether the audio kept running through it.
    ///
    /// **Getting it onto the screen (nothing appears unless asked for).** Any one of: add this component to an object in
    /// a scene; launch the build with <c>-zoundsGcTest</c>; define the scripting symbol <c>ZOUNDS_GC_TEST</c> for a
    /// test build; for a build that shows nothing until asked, use <c>-zoundsGcGesture</c> or <c>ZOUNDS_GC_TEST_GESTURE</c>
    /// and hold a corner (or tick Hold Corner To Open on a scene component); or call <see cref="ZoundsGcTest.Show"/> from game code. It starts as a small tab in a corner and
    /// opens on click, so it can stay up during a play session.
    ///
    /// **The button must really collect, because this test has no known-bad run to compare against.** If the audio
    /// survives a "collection" that never happened, the result is a pass that means nothing, and nothing would reveal it.
    /// So every press proves the collection occurred before it says anything about the audio, using three independent
    /// signals: the runtime's own full-collection counter went up; an object that was made unreachable before the press
    /// is gone afterwards; and a finalizer on such an object ran. If the counter did not move, the press is reported as
    /// "NO COLLECTION HAPPENED" and the audio verdict is withheld. The object signals are listed separately because a
    /// conservative collector can legitimately keep one object alive by a stale reference, which is why the counter is
    /// the one that must hold and either object signal is enough alongside it.
    ///
    /// **What the audio verdict means.** Each audio block the engine renders is counted. Across a collection that the
    /// audio survives, about as many blocks are rendered as the running rate says were due; across one that freezes the
    /// audio thread, almost none are. The test needs sound playing to judge: its own looping tone, or any zound the game
    /// is playing (switch the tone off to test against the game's own audio).
    ///
    /// **Editor caveat.** Only a built player gives the final answer. In the editor the engine's compiled-code
    /// protection can be off and the editor adds managed work of its own, so a stall there says little about a build.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Laubrary/Zounds/GC Test Overlay")]
    public sealed class ZoundsGcTestOverlay : MonoBehaviour {

        public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

        [Header("Panel")]
        [Tooltip("Whether the panel starts opened. Otherwise it starts as a small tab that opens on click.")]
        public bool startOpen = false;
        [Tooltip("Which screen corner the panel sits in.")]
        public Corner corner = Corner.TopRight;
        [Tooltip("Draws the small \"GC test\" tab while the panel is closed. Turn it off to keep the screen clear and rely on the corner hold below (or the toggle key).")]
        public bool showTab = true;
        [Tooltip("Holding a press (mouse or finger) in a screen corner for a moment opens the panel, for builds with no way to pass arguments and no debug menu. Works even while the tab is hidden.")]
        public bool holdCornerToOpen = false;
        [Tooltip("Which corner to hold.")]
        public Corner holdCorner = Corner.TopLeft;
        [Tooltip("How long the press must be held, in seconds.")]
        public float holdSeconds = 1.5f;
        [Tooltip("Size of the hold area as a fraction of the screen's shorter side.")]
        [Range(0.05f, 0.4f)] public float holdAreaSize = 0.15f;
        [Tooltip("Pressing this key (while the panel is on screen) opens or closes it. None disables the shortcut.")]
        public KeyCode toggleKey = KeyCode.None;

        [Header("Test tone")]
        [Tooltip("Plays its own looping tone through a delay and reverb so there is something to interrupt. Turn it off to test against sound your game is already playing.")]
        public bool playToneOnStart = true;
        [Tooltip("Length of the generated tone, in seconds.")]
        public float toneSeconds = 2f;
        public float fromHz = 180f;
        public float toHz = 720f;
        public bool useDelay = true;
        public bool useReverb = true;

        [Header("Garbage pressure")]
        [Tooltip("Cross-linked objects built before each forced collection. A collection's cost follows how many references it must walk, not how many bytes, so a large flat buffer would prove little.")]
        public int nodesPerWave = 1500000;

        // ── state ──
        private AudioSource source;
        private ZoundSapVoiceGenerator generator;
        private bool open;
        private bool toneOn;
        private bool listenerMissing;
        private string witnessVerdict;
        private string lastVerdict;
        private string lastProof;
        private bool lastProofOk = true;
        private int presses, confirmed, notCollected, survived, stalls;
        private double lastCollectionMs;

        private double rateBlocksPerSecond;
        private long rateBlocks = -1;
        private float rateSince;

        private float holdStart = -1f;
        private GUIStyle okStyle, badStyle, warnStyle;

        private void Awake() {
            open = startOpen;
            source = GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            generator = GetComponent<ZoundSapVoiceGenerator>();
            if (generator == null) generator = gameObject.AddComponent<ZoundSapVoiceGenerator>();
        }

        private void Start() {
            listenerMissing = FindAnyObjectByType<AudioListener>() == null;
            if (playToneOnStart) PlayTone();
        }

        private void OnDestroy() {
            if (generator != null && toneOn) generator.StopLive();
        }

        // ── tone ──

        /// <summary>Starts (or restarts) the looping test tone through the chain.</summary>
        public void PlayTone() {
            var clip = BuildTone();
            var chain = BuildChain();
            var layout = chain.IsEmpty ? ChainLayout.Empty : ChainLayout.Build(chain, AudioSettings.outputSampleRate);
            generator.SetPlay(clip, layout, 0d, clip.frames, 1f, 0.8f, (float)clip.frames / clip.frequency,
                              loop: true, tokenId: 1, heavyTier: true);
            source.generator = generator;
            source.Play();
            toneOn = true;
        }

        public void StopTone() {
            if (generator != null) generator.StopLive();
            if (source != null) source.Stop();
            toneOn = false;
        }

        private PcmClip BuildTone() {
            int rate = AudioSettings.outputSampleRate;
            int frames = Mathf.Max(1, (int)(toneSeconds * rate));
            var samples = new float[frames * 2];
            float phase = 0f, peak = 0f;
            for (int i = 0; i < frames; i++) {
                float t = (float)i / frames;
                phase += 2f * Mathf.PI * Mathf.Lerp(fromHz, toHz, t) / rate;
                if (phase > 2f * Mathf.PI) phase -= 2f * Mathf.PI;
                // Faded at both ends so the loop point cannot click and be mistaken for the dropout being looked for.
                float edge = Mathf.Min(1f, Mathf.Min(t, 1f - t) * 40f);
                float s = 0.45f * edge * Mathf.Sin(phase);
                samples[i * 2] = s;
                samples[i * 2 + 1] = s * 0.85f;
                float a = s < 0 ? -s : s;
                if (a > peak) peak = a;
            }
            return new PcmClip { channels = 2, frequency = rate, frames = frames, samples = samples, valid = true, peak = peak };
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

        // ── the collection, and the proof that it happened ──

        private sealed class Node { public object a, b; public int tag; }
        private readonly System.Collections.Generic.List<object> kept = new System.Collections.Generic.List<object>();

        /// <summary>An object whose finalizer reports that the collector reclaimed it.</summary>
        private sealed class FinalizerCanary {
            public static int ran;
            ~FinalizerCanary() { System.Threading.Interlocked.Increment(ref ran); }
        }

        // Kept out of line so no local in the caller can still reference the objects when the collection runs.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static System.WeakReference MakeUnreachableCanaries() {
            new FinalizerCanary();
            return new System.WeakReference(new object());
        }

        /// <summary>
        /// Builds a wave of real garbage, forces a full blocking collection, proves it happened, and judges the audio.
        /// The audio is judged only if the collection is proven; otherwise the verdict says nothing was tested.
        /// </summary>
        public void ForceCollection() {
            presses++;
            // Garbage with many references to walk, kept alive until the collection so the pause is measurable.
            var nodes = new Node[nodesPerWave];
            for (int i = 0; i < nodesPerWave; i++) nodes[i] = new Node { tag = i };
            var rng = new System.Random(12345);
            for (int i = 0; i < nodesPerWave; i++) {
                nodes[i].a = nodes[rng.Next(nodesPerWave)];
                nodes[i].b = nodes[rng.Next(nodesPerWave)];
            }
            kept.Add(nodes);

            var weak = MakeUnreachableCanaries();
            int finalizedBefore = FinalizerCanary.ran;
            int gen0 = System.GC.CollectionCount(0), gen2 = System.GC.CollectionCount(System.GC.MaxGeneration);
            long blocksBefore = ZoundAudioThreadGuard.Blocks;

            var sw = Stopwatch.StartNew();
            System.GC.Collect(System.GC.MaxGeneration, System.GCCollectionMode.Forced, true, true);
            sw.Stop();
            long across = ZoundAudioThreadGuard.Blocks - blocksBefore;
            lastCollectionMs = sw.Elapsed.TotalMilliseconds;

            // Let finalizers run and collect again so the finalizer canary is observable. This is after the timed
            // collection, so it does not distort the pause or the blocks counted across it.
            System.GC.WaitForPendingFinalizers();
            System.GC.Collect(System.GC.MaxGeneration, System.GCCollectionMode.Forced, true, true);

            int fullRuns = System.GC.CollectionCount(System.GC.MaxGeneration) - gen2;
            int youngRuns = System.GC.CollectionCount(0) - gen0;
            bool weakGone = !weak.IsAlive;
            bool finalized = FinalizerCanary.ran > finalizedBefore;

            string objects = "unreachable object " + (weakGone ? "reclaimed" : "still alive")
                           + ", finalizer " + (finalized ? "ran" : "did not run");
            string counters = "full collections +" + fullRuns + ", all collections +" + youngRuns
                              + ", collector mode " + UnityEngine.Scripting.GarbageCollector.GCMode;

            // The counter must have moved, and at least one object must be proven gone. Both, or the press is void.
            bool happened = fullRuns >= 1 && (weakGone || finalized);
            lastProofOk = happened;
            lastProof = (happened ? "COLLECTION CONFIRMED: " : "NO COLLECTION HAPPENED — this press proves nothing: ")
                        + counters + "; " + objects + ".";

            if (!happened) {
                notCollected++;
                lastVerdict = "Audio not judged (no collection was proven).";
            } else {
                confirmed++;
                lastVerdict = Judge(across, lastCollectionMs);
            }
            Debug.Log("[Zounds] GC test press " + presses + ": " + lastProof + " " + lastVerdict);
        }

        private void SampleRate() {
            long b = ZoundAudioThreadGuard.Blocks;
            float now = Time.realtimeSinceStartup;
            if (rateBlocks < 0) { rateBlocks = b; rateSince = now; return; }
            if (now - rateSince < 0.5f) return;
            rateBlocksPerSecond = (b - rateBlocks) / (now - rateSince);
            rateBlocks = b; rateSince = now;
        }

        private string Judge(long blocksAcross, double ms) {
            double expected = rateBlocksPerSecond * ms / 1000.0;
            rateBlocks = -1;
            if (rateBlocksPerSecond <= 0)
                return "No audio was being rendered, so nothing could be judged. Start the tone (or a game sound) and press again.";
            if (expected < 3)
                return "Collection too short (" + ms.ToString("F0") + " ms) to judge the audio — press again.";
            bool ok = blocksAcross >= expected * 0.7;
            if (ok) survived++; else stalls++;
            return (ok ? "AUDIO KEPT RUNNING" : "AUDIO STALLED") + ": " + blocksAcross + " of ~" + expected.ToString("F0")
                   + " blocks rendered during the " + ms.ToString("F0") + " ms collection.";
        }

        // ── input and UI ──

        private void Update() { SampleRate(); }

        private void EnsureStyles() {
            if (okStyle != null) return;
            okStyle = new GUIStyle(GUI.skin.label) { wordWrap = true }; okStyle.normal.textColor = new Color(0.45f, 0.95f, 0.5f);
            badStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontStyle = FontStyle.Bold }; badStyle.normal.textColor = new Color(1f, 0.4f, 0.35f);
            warnStyle = new GUIStyle(GUI.skin.label) { wordWrap = true }; warnStyle.normal.textColor = new Color(1f, 0.8f, 0.3f);
        }

        private Rect HoldRect() {
            float side = Mathf.Min(Screen.width, Screen.height) * holdAreaSize;
            float x = (holdCorner == Corner.TopLeft || holdCorner == Corner.BottomLeft) ? 0 : Screen.width - side;
            float y = (holdCorner == Corner.TopLeft || holdCorner == Corner.TopRight) ? 0 : Screen.height - side;
            return new Rect(x, y, side, side);
        }

        // Read from the GUI event stream rather than the Input class, so it works with either input system and with
        // touch (a finger arrives as mouse events here).
        private void HandleCornerHold(Event e) {
            if (!holdCornerToOpen || open) { holdStart = -1f; return; }
            var rect = HoldRect();
            switch (e.type) {
                case EventType.MouseDown:
                    holdStart = rect.Contains(e.mousePosition) ? Time.unscaledTime : -1f;
                    break;
                case EventType.MouseDrag:
                    if (!rect.Contains(e.mousePosition)) holdStart = -1f;
                    break;
                case EventType.MouseUp:
                    holdStart = -1f;
                    break;
            }
            if (holdStart >= 0f && Time.unscaledTime - holdStart >= holdSeconds) { open = true; holdStart = -1f; }
        }

        private void OnGUI() {
            EnsureStyles();
            var e = Event.current;
            if (toggleKey != KeyCode.None && e.type == EventType.KeyDown && e.keyCode == toggleKey) { open = !open; e.Use(); }

            HandleCornerHold(e);
            const int margin = 10;
            float w = open ? 460f : 92f, h = open ? 330f : 26f;
            float x = (corner == Corner.TopLeft || corner == Corner.BottomLeft) ? margin : Screen.width - w - margin;
            float y = (corner == Corner.TopLeft || corner == Corner.TopRight) ? margin : Screen.height - h - margin;
            var area = new Rect(x, y, w, h);

            if (!open) {
                if (!showTab) return;
                if (GUI.Button(area, new GUIContent("GC test ▸", "Open the garbage-collection stress test."))) open = true;
                return;
            }

            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 8, area.y + 6, area.width - 16, area.height - 12));
            GUILayout.BeginHorizontal();
            GUILayout.Label("GC stutter test  ·  " + (Application.isEditor ? "EDITOR (not conclusive)" : "BUILT PLAYER"));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("▾", GUILayout.Width(26))) open = false;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Trigger GC", "Builds a wave of garbage and forces a full collection now, proves it ran, and judges whether audio kept flowing."), GUILayout.Height(30))) ForceCollection();
            if (GUILayout.Button(new GUIContent(toneOn ? "Stop tone" : "Play tone", "The test's own looping tone. Turn it off to test against sound your game is playing."), GUILayout.Height(30), GUILayout.Width(100))) {
                if (toneOn) StopTone(); else PlayTone();
            }
            GUILayout.EndHorizontal();

            if (lastProof == null) GUILayout.Label("Press Trigger GC while sound plays.");
            else GUILayout.Label(lastProof, lastProofOk ? okStyle : badStyle);
            if (lastVerdict != null)
                GUILayout.Label(lastVerdict, lastVerdict.StartsWith("AUDIO STALLED") ? badStyle : lastVerdict.StartsWith("AUDIO KEPT") ? okStyle : warnStyle);

            GUILayout.Label("presses " + presses + "  ·  collections proven " + confirmed + "  ·  not collected " + notCollected
                            + "  ·  audio kept running " + survived + "  ·  stalled " + stalls);
            GUILayout.Label("audio blocks run as managed code: " + ZoundAudioThreadGuard.ManagedBlocks + " of " + ZoundAudioThreadGuard.Blocks
                            + (ZoundAudioThreadGuard.ManagedBlocks > 0 ? "  — THE AUDIO THREAD IS ATTACHED, collections can be heard" : ""));
            if (lastCollectionMs > 0) GUILayout.Label("last collection took " + lastCollectionMs.ToString("F0") + " ms   ·   managed heap "
                                                      + (System.GC.GetTotalMemory(false) / (1024f * 1024f)).ToString("F1") + " MB");
            if (listenerMissing) GUILayout.Label("NO AUDIO LISTENER IN THE SCENE — silence with no error.", badStyle);
            GUILayout.Label("compiler reports itself enabled: " + (BurstCompiler.IsEnabled ? "yes" : "NO — this will stutter"), BurstCompiler.IsEnabled ? GUI.skin.label : badStyle);
            AudioSettings.GetDSPBufferSize(out int len, out int num);
            GUILayout.Label("audio buffer " + len + " × " + num + " @ " + AudioSettings.outputSampleRate + " Hz"
                            + (len >= 1024 ? "  (large — shrink it to make this a real test)" : ""));
            if (witnessVerdict == null) {
                if (GUILayout.Button(new GUIContent("Check the chain is REALLY compiled", "Renders the same chains through the ordinary and the compiled path; any difference proves the compiled path is genuinely compiled."))) {
                    var wit = ZoundsSapCompiledWitness.Run();
                    witnessVerdict = wit.compiled ? "REALLY COMPILED" : "NOT COMPILED — audio will freeze during collection";
                    Debug.Log("[Zounds] " + wit.report);
                }
            } else GUILayout.Label("chain is: " + witnessVerdict, witnessVerdict.StartsWith("REALLY") ? okStyle : badStyle);
            GUILayout.EndArea();
        }
    }

    /// <summary>Ways to get the overlay on screen without editing a scene.</summary>
    public static class ZoundsGcTest {

        /// <summary>Puts the overlay on screen (once), opened, and keeps it across scene loads.</summary>
        public static ZoundsGcTestOverlay Show(bool startOpen = true) => Show(startOpen, true, false);

        /// <summary>As <see cref="Show(bool)"/>, choosing whether the closed tab is drawn and whether holding a corner opens the panel.</summary>
        public static ZoundsGcTestOverlay Show(bool startOpen, bool showTab, bool holdCornerToOpen) {
            var existing = Object.FindAnyObjectByType<ZoundsGcTestOverlay>();
            if (existing != null) return existing;
            var go = new GameObject("Zounds GC Test");
            Object.DontDestroyOnLoad(go);
            var overlay = go.AddComponent<ZoundsGcTestOverlay>();
            overlay.startOpen = startOpen;
            overlay.showTab = showTab;
            overlay.holdCornerToOpen = holdCornerToOpen;
            return overlay;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoShow() {
            bool wanted = false;
#if ZOUNDS_GC_TEST
            wanted = true;
#endif
            if (!wanted) foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-zoundsGcTest") { wanted = true; break; }
            if (wanted) { Show(true); return; }

            // The quiet variant: nothing on screen until a corner is held.
            bool gesture = false;
#if ZOUNDS_GC_TEST_GESTURE
            gesture = true;
#endif
            if (!gesture) foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-zoundsGcGesture") { gesture = true; break; }
            if (gesture) Show(false, false, true);
        }
    }
}
