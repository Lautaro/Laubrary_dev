using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Laubrary.Zounds.Dsp.Native {

    /// <summary>
    /// The output buses of the native engine: the replacement for the bus GameObjects
    /// the managed graph built around <c>ZoundBusFilter</c>.
    ///
    /// Each bus is one small AudioMixer asset (see ZoundsNativeBusSetup) whose master
    /// group carries the "Zounds Bus N" native effect. The effect is where the DSP
    /// actually runs, on Unity's mixer thread, in native code. Because
    /// <see cref="AudioMixer.outputAudioMixerGroup"/> is settable at runtime, the bus
    /// can be pointed at whatever AudioMixerGroup the zound asked for — so this keeps
    /// the old GetOrCreateBus(group) contract without the game's own mixer needing to
    /// know anything about Zounds.
    ///
    /// The silent carrier AudioSource is not decoration: Unity suspends a mixer that
    /// nothing routes into, and a suspended mixer's effects are not ticked, so the bus
    /// would simply never render. The carrier keeps the bus awake and contributes
    /// nothing audible of its own.
    /// </summary>
    public sealed class ZoundNativeBuses {

        private sealed class Bus {
            public int index;
            public AudioMixer mixer;
            public AudioMixerGroup target;          // where this bus sends its output
            public AudioMixerGroup busGroup;        // the bus mixer's own master group
            public GameObject gameObject;
            public AudioSource carrier;
        }

        private readonly List<Bus> buses = new List<Bus>(ZoundDspConstants.MAX_BUSES);
        private readonly Transform parent;
        private readonly HideFlags hideFlags;
        private AudioClip carrierClip;
        private int sampleRate;

        /// <summary>Reserved for offline rendering; never handed out to a playing zound.</summary>
        private static readonly int reservedBus = ZoundNativeOffline.OFFLINE_BUS;

        public int Count => buses.Count;
        public string SetupError { get; private set; }

        public ZoundNativeBuses(Transform parent, HideFlags hideFlags, int sampleRate) {
            this.parent = parent;
            this.hideFlags = hideFlags;
            this.sampleRate = sampleRate;
            // A carrier left over from a graph that was never torn down would keep an
            // orphaned bus alive under the engine; clear them before building.
            for (int i = parent.childCount - 1; i >= 0; i--) {
                var child = parent.GetChild(i);
                if (child != null && child.name.StartsWith("ZoundBus ")) {
                    if (Application.isPlaying) Object.Destroy(child.gameObject);
                    else Object.DestroyImmediate(child.gameObject);
                }
            }
        }

        /// <summary>
        /// The bus index for a mixer group, creating the bus on first use. Returns -1 when
        /// no bus could be made available (the reason is in <see cref="SetupError"/> and is
        /// logged once).
        /// </summary>
        public int GetOrCreate(AudioMixerGroup group) {
            for (int i = 0; i < buses.Count; i++) if (buses[i].target == group) return buses[i].index;

            int index = NextFreeIndex();
            if (index < 0) {
                Debug.LogWarning("[Zounds] native DSP bus limit reached; routing to bus 0.");
                return buses.Count > 0 ? buses[0].index : -1;
            }

            var mixer = Resources.Load<AudioMixer>(ZoundsNativeBusPaths.ResourcePath(index));
            if (mixer == null) {
                SetupError = "the bus mixer '" + ZoundsNativeBusPaths.ResourcePath(index) + "' is missing. "
                           + "Run Heros Hour 2 > Zounds > Create native DSP bus mixers; without it nothing can be heard.";
                Debug.LogError("[Zounds] " + SetupError);
                return -1;
            }
            var groups = mixer.FindMatchingGroups(string.Empty);
            if (groups == null || groups.Length == 0) {
                SetupError = "the bus mixer '" + mixer.name + "' has no group to route through.";
                Debug.LogError("[Zounds] " + SetupError);
                return -1;
            }

            var bus = new Bus { index = index, mixer = mixer, target = group, busGroup = groups[0] };
            // Runtime routing: this is what makes one prebuilt mixer asset serve whatever
            // group the game asked for. Null means straight to the listener.
            mixer.outputAudioMixerGroup = group;

            var go = new GameObject("ZoundBus " + index + (group != null ? " -> " + group.name : " -> (listener)"));
            go.hideFlags = hideFlags;
            go.transform.parent = parent;
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.bypassEffects = false;
            source.bypassListenerEffects = false;
            source.bypassReverbZones = true;
            source.priority = 0;
            source.volume = 1f;
            source.pitch = 1f;
            source.outputAudioMixerGroup = bus.busGroup;
            source.clip = CarrierClip();
            bus.gameObject = go;
            bus.carrier = source;
            buses.Add(bus);
            source.Play();
            return index;
        }

        private int NextFreeIndex() {
            for (int i = 0; i < ZoundDspConstants.MAX_BUSES; i++) {
                if (i == reservedBus) continue;
                bool used = false;
                for (int b = 0; b < buses.Count; b++) if (buses[b].index == i) { used = true; break; }
                if (!used) return i;
            }
            return -1;
        }

        private AudioClip CarrierClip() {
            if (carrierClip == null) {
                // One second of silence, looped: it keeps the bus mixer out of Unity's
                // suspended state so the native effect on it keeps being ticked. The
                // effect adds Zounds' own output to whatever it is given, so the silence
                // costs nothing.
                carrierClip = AudioClip.Create("ZoundsBusCarrier", sampleRate, 1, sampleRate, false);
                carrierClip.hideFlags = HideFlags.HideAndDontSave;
                carrierClip.SetData(new float[sampleRate], 0);
            }
            return carrierClip;
        }

        /// <summary>Re-arms every carrier (an AudioSettings reset stops them).</summary>
        public void EnsurePlaying() {
            for (int i = 0; i < buses.Count; i++) {
                var b = buses[i];
                if (b.carrier != null && !b.carrier.isPlaying) b.carrier.Play();
            }
        }

        public int IndexAt(int slot) => slot >= 0 && slot < buses.Count ? buses[slot].index : -1;

        public void Teardown() {
            for (int i = 0; i < buses.Count; i++) {
                var b = buses[i];
                if (b.mixer != null) b.mixer.outputAudioMixerGroup = null;
                if (b.gameObject != null) {
                    if (Application.isPlaying) Object.Destroy(b.gameObject);
                    else Object.DestroyImmediate(b.gameObject);
                }
            }
            buses.Clear();
            if (carrierClip != null) {
                if (Application.isPlaying) Object.Destroy(carrierClip);
                else Object.DestroyImmediate(carrierClip);
                carrierClip = null;
            }
        }
    }

    /// <summary>Where the bus mixer assets live. Shared by the runtime and the editor setup utility.</summary>
    public static class ZoundsNativeBusPaths {
        public const string RESOURCE_PREFIX = "ZoundBuses/ZoundBus";
        public static string ResourcePath(int bus) => RESOURCE_PREFIX + bus;
    }
}
