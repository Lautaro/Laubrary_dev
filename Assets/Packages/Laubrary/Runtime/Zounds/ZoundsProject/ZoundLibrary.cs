using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

#if ADDRESSABLES_INSTALLED
using UnityEngine.AddressableAssets;
#endif

namespace Laubrary.Zounds
{

    [System.Serializable]
    public class ZoundLibrary
    {
        public List<Klip> klips = new List<Klip>();
        public List<Zequence> zequences = new List<Zequence>();
        public List<Tag> tags = new List<Tag>();

        private bool hasAnySoloZound = false;
        public bool soloStatusNeedsUpdate { get; set; } = true;

        public bool HasAnySoloZound() {
            if (soloStatusNeedsUpdate) {
                hasAnySoloZound = false;
                ForEachZound(z => {
                    if (z.solo) {
                        hasAnySoloZound = true;
                        return true;
                    }
                    return false;
                });
                soloStatusNeedsUpdate = false;
            }
            return hasAnySoloZound;
        }

        public bool TryGetTag(string name, out Tag tag)
        {
            tag = tags.Find(t => t.name == name);
            return tag != null;
        }

        public bool TryGetTag(int id, out Tag tag)
        {
            tag = tags.Find(t => t.id == id);
            return tag != null;
        }

        public Tag CreateNewTag(string name)
        {
            if (tags.Find(t => t.name == name) != null)
            {
                Debug.LogError("Tag already exists: " + name);
                return null;
            }
            Tag tag = new Tag();
            tag.name = name;
            do
            {
                tag.id = Random.Range(int.MinValue, int.MaxValue);
            } while (tags.Find(t => t.id == tag.id) != null);
            tags.Add(tag);
            return tag;
        }

        public int RemoveUnusedTags()
        {
            int removedCount = 0;
            removedCount += tags.RemoveAll(tag =>
            {
                int tagId = tag.id;
                return FindZound(z => z.tags.Contains(tagId)) == null;
            });
            return removedCount;
        }


        public static int GetUniqueZoundId()
        {
            var library = ZoundsProject.Instance.zoundLibrary;
            int id;
            do
            {
                id = Random.Range(int.MinValue, int.MaxValue);
            } while (id != 0 && ZoundIdExists(library, null, id));
            return id;
        }

        public void Validate()
        {
            bool dirty = false;
            if (ValidateZounds(klips)) dirty = true;
            if (ValidateZounds(zequences)) dirty = true;

#if UNITY_EDITOR
            if (dirty)
            {
                UnityEditor.EditorUtility.SetDirty(ZoundsProject.Instance);
            }
#endif
        }

        private bool ValidateZounds<TZound>(List<TZound> zounds) where TZound : Zound
        {
            bool dirty = false;
            foreach (var zound in zounds)
            {
                if (zound.id == 0 || ZoundIdExists(this, zound, zound.id))
                {
                    dirty = true;
                    zound.id = GetUniqueZoundId();
                }
            }
            return dirty;
        }

        public static bool ZoundIdExists(ZoundLibrary library, Zound self, int id)
        {
            if (id == 0) return false;
            bool exists = false;
            library.ForEachZound(z => {
                if (z.id == id && z != self) {
                    exists = true;
                    return true;
                }
                return false;
            });
            return exists;
        }

        public List<Zound> GetAllZounds()
        {
            List<Zound> allZounds = new List<Zound>();
            allZounds.AddRange(klips);
            allZounds.AddRange(zequences);

            return allZounds;
        }

        public Zound FindZound(System.Predicate<Zound> match)
        {
            Zound found = null;
            ForEachZound(z => {
                if (match(z)) {
                    found = z;
                    return true;
                }
                return false;
            });
            return found;
        }

        public List<Zound> FindAllZounds(System.Predicate<Zound> match)
        {
            var result = new List<Zound>();
            ForEachZound(z => {
                if (match(z)) {
                    result.Add(z);
                }
            });
            return result;
        }

        public void ForEachZound(System.Action<Zound> handler)
        {
            foreach (var z in klips) handler(z);
            foreach (var z in zequences)
            {
                handler(z);
                foreach (var klip in z.localKlips) handler(klip);
                foreach (var localZeq in z.localZequences)
                {
                    handler(localZeq.zequence);
                    foreach (var nestedKlip in localZeq.zequence.localKlips) handler(nestedKlip);
                }
            }
        }

        public void ForEachZound(System.Func<Zound, bool> handler)
        {
            foreach (var z in klips) if (handler(z)) return;
            foreach (var z in zequences)
            {
                if (handler(z)) return;
                foreach (var klip in z.localKlips) if (handler(klip)) return;
                foreach (var localZeq in z.localZequences)
                {
                    if (handler(localZeq.zequence)) return;
                    foreach (var nestedKlip in localZeq.zequence.localKlips) if (handler(nestedKlip)) return;
                }
            }
        }
        public int CountRenderedPathUsages(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            int count = 0;
            ForEachZound(z =>
            {
                if (z is Klip klip && klip.renderedClipPath == path)
                {
                    count++;
                }
                else if (z is Zequence zeq && zeq.renderedClipPath == path)
                {
                    count++;
                }
            });
            return count;
        }



        [System.Serializable]
        public class Tag
        {
            public int id;
            public string name;
        }
    }


    [System.Serializable]
    public class Zound
    {
        /// <summary>How the gap between authored retriggers is measured.</summary>
        public enum RetriggerGap
        {
            /// <summary>A fixed rhythm based on the first play's actual length plus the gap.</summary>
            Steady,
            /// <summary>The gap is measured between the starts of consecutive plays, so they may overlap.</summary>
            FromStart,
            /// <summary>The gap is silence after each play has ended.</summary>
            FromEnd
        }

        internal const float MinVolumeRange = 0f;
        internal const float MaxVolumeRange = 1f;
        internal const float MinPitchRange = 0.1f;
        internal const float MaxPitchRange = 2f;
        internal const float MinChanceRange = 0f;
        internal const float MaxChanceRange = 1f;

        public int id;
        public int originalId; // Tracks the shared zound this was broken from, for "reconnect to shared" functionality.
        public int parentId;
        public string name;
        public float minVolume = 1f;
        public float maxVolume = 1f;
        public float minPitch = 1f;
        public float maxPitch = 1f;
        public float chance = 1f;
        public List<int> tags = new List<int>();
        public bool mute;
        public bool solo;

        /// <summary>When enabled, every request to play this Klip or Zequence starts a short authored burst instead.</summary>
        public bool retriggerEnabled;
        /// <summary>Total plays in an authored retrigger burst, including the first.</summary>
        public int retriggerCount = 4;
        /// <summary>Seconds between retriggers, interpreted by <see cref="retriggerGapMode"/>.</summary>
        public float retriggerGap = 0.5f;
        public RetriggerGap retriggerGapMode = RetriggerGap.FromEnd;

        /// <summary>Enables retriggering and upgrades old saved Zounds, whose newly added fields deserialize as zero.</summary>
        public void EnableRetrigger() {
            if (retriggerCount < 2) {
                retriggerCount = 4;
                retriggerGap = 0.5f;
                retriggerGapMode = RetriggerGap.FromEnd;
            }
            retriggerEnabled = true;
        }

        public AssetReference manuallySetMixerGroupRef;

        // Per-voice effect chain: inline, or a library preset by live reference plus sparse parameter
        // overrides. Shared/local follows the same pattern as originalId/parentId. This data is
        // pipeline-agnostic and is deliberately kept: it is currently unread at play time (the engine that
        // consumed it has been removed) and is the authored input the Burst generator path will consume.
        public ZoundEffectChain effectChain = new ZoundEffectChain();
        public int chainPresetId;
        /// <summary>The preset a Detach broke away from, so Reconnect can restore the link.</summary>
        public int detachedChainPresetId;
        public List<ChainParamOverride> chainOverrides = new List<ChainParamOverride>();

        /// <summary>Named, saved sets of this Zound's settings a playing sound can glide to (T-0498). Default is not stored.</summary>
        public List<ZoundSnapshot> snapshots = new List<ZoundSnapshot>();

        public Zound(int id) { this.id = id; }
        public Zound(int id, Zound source)
        {
            this.id = id;
            name = ZoundDictionary.EnsureUniqueZoundName(source.name);
            minVolume = source.minVolume;
            maxVolume = source.maxVolume;
            minPitch = source.minPitch;
            maxPitch = source.maxPitch;
            chance = source.chance;
            tags.AddRange(source.tags);
            mute = source.mute;
            solo = source.solo;
            retriggerEnabled = source.retriggerEnabled;
            retriggerCount = source.retriggerCount;
            retriggerGap = source.retriggerGap;
            retriggerGapMode = source.retriggerGapMode;
            manuallySetMixerGroupRef = source.manuallySetMixerGroupRef;
            effectChain = source.effectChain != null ? source.effectChain.DeepCopy() : new ZoundEffectChain();
            chainPresetId = source.chainPresetId;
            detachedChainPresetId = source.detachedChainPresetId;
            chainOverrides = source.chainOverrides != null ? new List<ChainParamOverride>(source.chainOverrides) : new List<ChainParamOverride>();
            snapshots = new List<ZoundSnapshot>();
            if (source.snapshots != null) foreach (var sn in source.snapshots) if (sn != null) snapshots.Add(sn.DeepCopy());
        }

        public bool IsClipOrLocalZound() {
            return this is ClipZound || this.parentId != 0;
        }

        public virtual List<Zound> GetDependencies()
        {
            return new List<Zound>();
        }

        public virtual bool HasDirectDependency(Zound otherZound)
        {
            return false;
        }

        public virtual bool HasNestedDependency(Zound otherZound)
        {
            return false;
        }

        public virtual void RemoveDependency(Zound otherZound)
        {

        }

#if UNITY_EDITOR

        internal bool editor_hasManuallySetRouting => manuallySetMixerGroupRef != null && manuallySetMixerGroupRef.editorAsset != null;
#endif
    }


    public interface IZoundAudioClip
    {
#if ADDRESSABLES_INSTALLED
        AssetReference GetAudioClipReference();
#endif
    }

    internal class ClipZound : Zound
    {

        public AudioClip audioClip;
        public string audioPath;

        public ClipZound(AudioClip audioClip, string audioPath) : base(0)
        {
            this.name = audioClip.name;
            this.audioClip = audioClip;
            this.audioPath = audioPath;
            minVolume = 1f;
            maxVolume = 1f;
            minPitch = 1f;
            maxPitch = 1f;
            chance = 1f;
        }
    }

    [System.Serializable]
    public class Klip : Zound, IZoundAudioClip
    {

        /// <summary>Native-DSP pipeline only: duration change without pitch change, applied to the cached sample data ahead of the chain.</summary>
        public ZoundTimeStretch timeStretch = new ZoundTimeStretch();

        /// <summary>Looping (T-0473): when enabled this Klip is a Looper. See <see cref="ZoundLoop"/>.</summary>
        public ZoundLoop loop = new ZoundLoop();

        /// <summary>This Klip is a Looper: it plays its region over and over until stopped.</summary>
        public bool IsLooper => loop != null && loop.enabled;

        /// <summary>
        /// A fixed boost of how loud the sound goes into its effects (T-0521), from 1 (as recorded) to 10, in tenths: it
        /// multiplies Drive, whatever moves Drive, so the heard level into the chain is boost x Drive. Kept apart from Drive
        /// itself so Drive's range (0 to 4) and every binding, curve and game-code value on it stay exactly as they were;
        /// a sound saved before it existed reads 1 and sounds the same. Set in the Klip editor's top row.
        /// </summary>
        public float boost = 1f;
        /// <summary>The boost as the engine applies it: clamped to 1..10, and 1 for anything unset or invalid.</summary>
        public float BoostApplied => float.IsNaN(boost) || boost < 1f ? 1f : (boost > 10f ? 10f : boost);

        public float gain = 1f;
        public bool gainEnabled = false;
        public bool showRenderedWaveform = false;
        public bool trimEnabled = true;
        public float trimStart;
        public float trimEnd;
        public bool clampToTrim = true;
        public Envelope volumeEnvelope;
        public Envelope pitchEnvelope;

        public float subGain = 0f;
        public float lowGain = 0f;
        public float lowMidGain = 0f;
        public float midGain = 0f;
        public float highMidGain = 0f;
        public float highGain = 0f;
        public float airGain = 0f;
        public bool eqEnabled = false;
        public float lpFrequency = 22000f;
        public float hpFrequency = 10f;

        // Compression
        public bool compressionEnabled = false;
        public float compThreshold = -10f;
        public float compRatio = 4f;
        public float compAttack = 10f;
        public float compRelease = 100f;
        public float compMakeupGain = 0f;

        // Normalization
        public bool normalizationEnabled = false;
        public float normalizeTargetDB = -0.5f;

        // Fade In/Out
        public bool fadeEnabled = false;
        public float fadeInDuration = 0f;
        public float fadeOutDuration = 0f;
        public bool fadeUseSCurve = false;

        public string audioClipPath;
        public string renderedClipPath;
        public string outputClipPath;
        public string externalSourcePath; // Absolute path to source file outside Unity (e.g. external FX repo).
        [FormerlySerializedAs("editor_needsRender")]
        [SerializeField] internal bool needsRender;

        /// <summary>
        /// Returns true when any edit is active — parameters that modify the audio waveform
        /// and require offline rendering to produce an output clip.
        /// Edits: trim, volume/pitch envelopes, boost gain, EQ, compression, normalization, fade.
        /// NOT included: min/max volume, min/max pitch, chance — these are settings
        /// applied in real-time by the AudioSource and do not require rendering.
        /// </summary>
        public bool HasActiveEdits() {
            if (trimEnabled) return true;
            if (volumeEnvelope.enabled) return true;
            if (pitchEnvelope.enabled) return true;
            // gain of 0.0 is the serialization default meaning "no boost" — render code
            // treats it as 1.0 (see AudioRenderUtility.ApplyGain). Only flag as active
            // when gain is a real non-unity value.
            if (gainEnabled && gain > 0.0001f && !Mathf.Approximately(gain, 1f)) return true;
            if (eqEnabled) return true;
            if (compressionEnabled) return true;
            if (normalizationEnabled) return true;
            if (fadeEnabled && (fadeInDuration > 0.001f || fadeOutDuration > 0.001f)) return true;
            return false;
        }

#if ADDRESSABLES_INSTALLED
        public AssetReference audioClipRef;
        public AssetReference renderedClipRef;
        public AssetReference outputClipRef;

        public AssetReference GetAudioClipReference()
        {
            // Output clip is the single runtime reference — always in ZoundFiles/.
            if (outputClipRef != null && outputClipRef.RuntimeKeyIsValid())
                return outputClipRef;
            // Legacy fallback for un-promoted Klips: original behavior.
            bool hasEdits = HasActiveEdits();
            bool hasRendered = renderedClipRef != null && renderedClipRef.RuntimeKeyIsValid();
            if (!hasEdits) return audioClipRef;
            return hasRendered ? renderedClipRef : audioClipRef;
        }
#endif
        public string GetAudioClipPath()
        {
            if (!string.IsNullOrEmpty(outputClipPath)) return outputClipPath;
            // Legacy fallback for un-promoted Klips.
            if (!HasActiveEdits()) return audioClipPath;
            return string.IsNullOrEmpty(renderedClipPath) ? audioClipPath : renderedClipPath;
        }

        public Klip(int id) : base(id) { }
        public Klip(int id, Klip source) : base(id, source)
        {
            timeStretch = source.timeStretch != null ? source.timeStretch.DeepCopy() : new ZoundTimeStretch();
            loop = source.loop != null ? source.loop.DeepCopy() : new ZoundLoop();

            boost = source.boost;
            gain = source.gain;
            gainEnabled = source.gainEnabled;
            showRenderedWaveform = source.showRenderedWaveform;
            trimEnabled = source.trimEnabled;
            trimStart = source.trimStart;
            trimEnd = source.trimEnd;
            clampToTrim = source.clampToTrim;
            volumeEnvelope = source.volumeEnvelope.DeepCopy();
            pitchEnvelope = source.pitchEnvelope.DeepCopy();
            // EQ
            subGain = source.subGain;
            lowGain = source.lowGain;
            lowMidGain = source.lowMidGain;
            midGain = source.midGain;
            highMidGain = source.highMidGain;
            highGain = source.highGain;
            airGain = source.airGain;
            eqEnabled = source.eqEnabled;
            lpFrequency = source.lpFrequency;
            hpFrequency = source.hpFrequency;
            // Compression
            compressionEnabled = source.compressionEnabled;
            compThreshold = source.compThreshold;
            compRatio = source.compRatio;
            compAttack = source.compAttack;
            compRelease = source.compRelease;
            compMakeupGain = source.compMakeupGain;
            // Normalization
            normalizationEnabled = source.normalizationEnabled;
            normalizeTargetDB = source.normalizeTargetDB;
            // Fade
            fadeEnabled = source.fadeEnabled;
            fadeInDuration = source.fadeInDuration;
            fadeOutDuration = source.fadeOutDuration;
            fadeUseSCurve = source.fadeUseSCurve;

            needsRender = true; // Duplicate should always start with a fresh render state to avoid inheriting previous paths.
#if ADDRESSABLES_INSTALLED
            audioClipRef = source.audioClipRef;
            renderedClipRef = null;
            outputClipRef = null; // Must be re-promoted after duplicate is rendered/saved.
#endif
            audioClipPath = source.audioClipPath;
            renderedClipPath = string.Empty;
            outputClipPath = string.Empty;
            externalSourcePath = source.externalSourcePath;
        }
    }

    [System.Serializable]
    public class CompositeZound : Zound
    {

        public enum Mode
        {
            Parallel = 0,
            Randomizer = 1,
            RoundRobin = 2,
            Playlist = 3
        }

        /// <summary>Native-DSP pipeline only: how a repeating entry re-triggers from one voice (see Dsp/DspVoice).</summary>
        public enum RepeatMode
        {
            FixedCount = 0,
            FixedDuration = 1
        }

        public Mode mode = Mode.Parallel;
        public int noPlayWeight = 0;
        public List<ZoundEntry> zoundEntries = new List<ZoundEntry>();
        public List<Klip> localKlips = new List<Klip>();
        public List<LocalZequence> localZequences = new List<LocalZequence>();

        private HashSet<int> m_playedEntries;
        /// <summary>
        /// Used to track Round Robin.
        /// </summary>
        public HashSet<int> playedEntries
        {
            get
            {
                if (m_playedEntries == null) m_playedEntries = new HashSet<int>();
                return m_playedEntries;
            }
        }
        /// <summary>
        /// Used to track Playlist.
        /// </summary>
        public int currentEntryIndexToPlay { get; set; } = 0;
        /// <summary>Round Robin: the entry played last, excluded from the first pick after the set resets.</summary>
        public int lastRoundRobinIndex { get; set; } = -1;

        public CompositeZound(int id) : base(id) { }
        public CompositeZound(int id, CompositeZound source) : base(id, source)
        {
            mode = source.mode;
            noPlayWeight = source.noPlayWeight;
            foreach (var entry in source.zoundEntries)
            {
                var serialized = JsonUtility.ToJson(entry);
                var duplicate = JsonUtility.FromJson<ZoundEntry>(serialized);
                if (entry.local && source.TryGetEntryZound(entry, out var entryZound))
                {
                    if (entryZound is Klip entryKlip)
                    {
                        var duplicatedKlip = new Klip(ZoundLibrary.GetUniqueZoundId(), entryKlip);
                        duplicatedKlip.parentId = id;
                        duplicate.zoundId = duplicatedKlip.id;
                        localKlips.Add(duplicatedKlip);
                    }
                    else if (entryZound is Zequence entryZequence)
                    {
                        var duplicatedZequence = new Zequence(ZoundLibrary.GetUniqueZoundId(), entryZequence);
                        duplicatedZequence.parentId = id;
                        duplicate.zoundId = duplicatedZequence.id;
                        localZequences.Add(new CompositeZound.LocalZequence(duplicatedZequence));
                    }
                }
                zoundEntries.Add(duplicate);
            }
        }

        public bool HasLocalMuteOrSoloEntry() {
            foreach (var entry in zoundEntries) {
                if (entry.mute || entry.solo) {
                    return true;
                }
                if (entry.local && TryGetEntryZound(entry, out var childZound) && childZound is Zequence childZeq) {
                    if (childZeq.HasLocalMuteOrSoloEntry()) {
                        return true;
                    }
                }
            }
            return false;
        }

        public override List<Zound> GetDependencies()
        {
            var result = new List<Zound>();
            foreach (var entry in zoundEntries)
            {
                if (ZoundDictionary.TryGetZoundById(entry.zoundId, out var zound))
                {
                    result.Add(zound);
                }
            }
            foreach (var entry in zoundEntries)
            {
                if (ZoundDictionary.TryGetZoundById(entry.zoundId, out var zound))
                {
                    result.AddRange(zound.GetDependencies());
                }
            }
            result = result.Distinct().ToList();
            return result;
        }

        public override bool HasDirectDependency(Zound otherZound)
        {
            return zoundEntries.Find(entry => entry.zoundId == otherZound.id) != null;
        }

        public override bool HasNestedDependency(Zound otherZound)
        {
            foreach (var entry in zoundEntries)
            {
                if (ZoundDictionary.TryGetZoundById(entry.zoundId, out var zound))
                {
                    if (zound.HasDirectDependency(otherZound)) return true;
                    else if (zound.HasNestedDependency(otherZound)) return true;
                }
            }
            return false;
        }

        public override void RemoveDependency(Zound otherZound)
        {
            zoundEntries.RemoveAll(entry => entry.zoundId == otherZound.id);
        }

        public bool TryGetEntryZound(ZoundEntry entry, out Zound zound)
        {
            if (entry.local)
            {
                zound = localKlips.Find(k => k.id == entry.zoundId);
                if (zound == null)
                {
                    var localRandomizer = localZequences.Find(lr => lr.zequence.id == entry.zoundId);
                    if (localRandomizer != null)
                    {
                        zound = localRandomizer.zequence;
                    }
                }
                return zound != null;
            }
            else
            {
                if (ZoundDictionary.TryGetZoundById(entry.zoundId, out zound))
                {
                    return true;
                }
            }
            zound = null;
            return false;
        }


        [System.Serializable]
        public class ZoundEntry
        {
            public enum ZoundType
            {
                Klip, Zequence, Randomizer
            }
            public int zoundId;
            public bool local;
            public float delay;
            public float volume = 1f;
            public float pitch = 1f;
            public float chance = 1f;
            public bool overrideVolume;
            public bool overridePitch;
            public bool overrideChance;
            public bool mute;
            public bool solo;
            public Envelope volumeEnvelope = new Envelope(MinVolumeRange, MaxVolumeRange);
            /// <summary>
            /// Only used for Randomizer
            /// </summary>
            public int chanceWeight = 1;

            // Native-DSP pipeline only: Repeater, this track plays its zound N times in sequence from one
            // voice (see Dsp/DspVoice). Harmless, unread data when the managed pipeline is active.
            public bool repeatEnabled;
            /// <summary>false = identical repeats; true = each repeat re-rolls the zound's random pitch and volume.</summary>
            public bool repeatRetrigger;
            public CompositeZound.RepeatMode repeatMode = CompositeZound.RepeatMode.FixedCount;
            public int repeatCount = 3;
            public float repeatTotalDuration = 2f;
            /// <summary>Seconds between repeats: from each repeat's start, or from its end when repeatSpaceFromEnd is set.</summary>
            public float repeatInterval = 0.25f;
            public bool repeatSpaceFromEnd;

            /// <summary>
            /// An optional name game code can reach this track by through a play's token (ZPOC), as an alternative to
            /// its number. Unique within this Zequence only; matched the way Zound names are.
            /// </summary>
            public string zpocId = "";

            /// <summary>
            /// The track plays its own excerpt of its Klip (non-destructive editing, T-0565): <see cref="trimStart"/> to
            /// <see cref="trimEnd"/>, in seconds of the Klip's source file, instead of the Klip's own trim. This is how two
            /// pieces split from one sound share that sound's processing while each plays a different part of it. The
            /// Klip's curves are anchored to source seconds, so each piece hears the part of each curve over its own audio.
            /// Off (the default) means the Klip's own trim, exactly as before.
            /// </summary>
            public bool ownTrim;
            public float trimStart, trimEnd;

#if UNITY_EDITOR
            [HideInInspector] public int editor_instanceID;
            [HideInInspector] public bool editor_foldoutExpanded = true;
            [HideInInspector] public bool editor_isRenaming = false;
            /// <summary>This track's height in the Zequence editor, in points; 0 means the editor's default. View state,
            /// kept with the entry so every track can have its own.</summary>
            [HideInInspector] public float editor_height;
#endif
        }

        /// <summary>
        /// Unity doesn't support nested serialization so we use SerializeReference
        /// </summary>
        [System.Serializable]
        public class LocalZequence
        {
            [SerializeReference] private Zequence m_zequence;
            public Zequence zequence { get => m_zequence; set => m_zequence = value; }
            public LocalZequence(Zequence zequence) { this.zequence = zequence; }
        }

#if UNITY_EDITOR
        [HideInInspector] public float editor_maxDuration = 3f;
#endif
    }

    [System.Serializable]
    public class Zequence : CompositeZound
    {

        public Envelope masterVolumeEnvelope = new Envelope(MinVolumeRange, MaxVolumeRange);

        public string renderedClipPath;
#if ADDRESSABLES_INSTALLED
        public AssetReference renderedClipRef;
#endif

        public Zequence(int id) : base(id) { }
        public Zequence(int id, Zequence source) : base(id, source)
        {
            masterVolumeEnvelope = source.masterVolumeEnvelope.DeepCopy();
            renderedClipPath = string.Empty;
#if ADDRESSABLES_INSTALLED
            renderedClipRef = null;
#endif
        }

    }


}
