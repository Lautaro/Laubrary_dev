using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine.Audio;



#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Laubrary.Zounds {

    [ExecuteAlways]
    public class ZoundEngine : MonoBehaviour {

        public static event System.Action<ZoundToken> onNewTokenCreated;
        public static event System.Action<Zound, ZoundToken> onZoundStartedPlaying;

        // Warned once per session per key so a missing selector or an exhausted selector doesn't spam the
        // console on every combat event that hits it.
        internal static TextAsset editorLastOpenedProject;
        internal static event System.Action onLoadLastOpenedProject;

        private static bool initialized;
        public static bool IsInitialized() => initialized;

        private static ZoundEngine instance;
        internal static ZoundEngine Instance {
            get {
                if (instance == null) {
                    var go = new GameObject();
                    instance = go.AddComponent<ZoundEngine>();
                    if (Application.isPlaying) {
                        go.name = "ZoundEngine";
                        DontDestroyOnLoad(go);
                    }
                    else {
                        go.name = "ZoundEngine [EditMode|NonSavable]";
                        go.hideFlags = HideFlags.HideAndDontSave;
                    }

#if UNITY_EDITOR
                    DetermineUpdater();
#else //Use runtime updater in build
                    UseRuntimeUpdater();
#endif

                }
                return instance;
            }
        }

        [SerializeField] private ZoundPool pool = new ZoundPool();

        internal Dictionary<string, Zound> zoundDictionary = new Dictionary<string, Zound>();
        internal Dictionary<int, Zound> zoundDictionaryById = new Dictionary<int, Zound>();
        internal Dictionary<string, AudioClip> loadedClips = new Dictionary<string, AudioClip>(); // Key: AssetReference string key
        internal Dictionary<AudioClip, string> runtimeClipFolders = new Dictionary<AudioClip, string>();
        internal Dictionary<string, Zound> missingZounds = new Dictionary<string, Zound>();

        // Persists missing zound entries across play mode transitions.
        // Instance missingZounds are copied here on ExitingPlayMode before the engine is destroyed.
        private static Dictionary<string, Zound> s_persistedMissingZounds = new Dictionary<string, Zound>();

        private Dictionary<Zound, float> zoundLastPlayedTimes = new Dictionary<Zound, float>();
        private Dictionary<Zound, LinkedList<ZoundToken>> cullingGroups = new Dictionary<Zound, LinkedList<ZoundToken>>();
        private List<ZoundToken> tokens = new List<ZoundToken>();

        private const float missingZoundsDuration = 10f;

        internal bool hasAnySoloZoundThisFrame = false;

        internal static ZoundPool Pool => Instance.pool;
        // Read-only view. Must never create the engine as a side effect: editor windows and asset
        // postprocessors read this while drawing/importing, and an engine born mid-import misbehaves.
        private static readonly Dictionary<Zound, LinkedList<ZoundToken>> s_emptyCullingGroups = new Dictionary<Zound, LinkedList<ZoundToken>>();
        internal static Dictionary<Zound, LinkedList<ZoundToken>> CullingGroups => instance != null ? instance.cullingGroups : s_emptyCullingGroups;
        internal static Dictionary<string, Zound> MissingZounds {
            get {
                // Merge live instance entries into the persistent store so callers see both.
                if (instance != null) {
                    foreach (var kvp in instance.missingZounds) {
                        if (!s_persistedMissingZounds.ContainsKey(kvp.Key))
                            s_persistedMissingZounds.Add(kvp.Key, kvp.Value);
                    }
                }
                return s_persistedMissingZounds;
            }
        }

        // Called on ExitingPlayMode to snapshot instance missingZounds before the engine is destroyed.
        internal static void PersistMissingZounds() {
            if (instance == null) return;
            foreach (var kvp in instance.missingZounds) {
                if (!s_persistedMissingZounds.ContainsKey(kvp.Key))
                    s_persistedMissingZounds.Add(kvp.Key, kvp.Value);
            }
        }

        // Removes an entry from both the persistent store and the live instance dictionary.
        internal static void RemovePersistedMissingZound(string key) {
            s_persistedMissingZounds.Remove(key);
            instance?.missingZounds.Remove(key);
        }
        private static float masterVolume;

        private void OnDestroy() {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= OnEditorUpdateMode;
#endif
            if (instance == this) {
                instance = null;
                // Project-wide ZPOC values are runtime state: they end with the engine, like the plays they drove.
                ZpocGlobals.ClearAll();
            }
        }

        /// <summary>Every token the engine is currently updating (tracks inside a Zequence included), or null when there is
        /// no engine. Read-only use.</summary>
        internal static List<ZoundToken> LiveTokens => instance != null ? instance.tokens : null;

        /// <summary>
        /// Sets a project-wide ZPOC value (0..1) that every play follows for that id unless its own token has set it. Ids
        /// are matched the way Zound names are. Runtime-only: nothing is saved, and it is forgotten when the engine goes.
        /// An id that no Zound in the project declares is reported once (see <see cref="ZoundDiagnostics"/>), never thrown.
        /// </summary>
        public static void SetGlobalZpoc(string zpocId, float value) => ZpocGlobals.Set(zpocId, value);

        /// <summary>Removes a project-wide ZPOC value; plays that followed it go back to the ZPOC's resting value.</summary>
        public static void ClearGlobalZpoc(string zpocId) => ZpocGlobals.Clear(zpocId);

        /// <summary>The project-wide value set for an id, if any.</summary>
        public static bool TryGetGlobalZpoc(string zpocId, out float value) => ZpocGlobals.TryGet(ZpocKeys.Key(zpocId), out value);

        /// <summary>
        /// Drops the name/id lookup tables so the next lookup re-reads the live project. Called when the
        /// Zounds window edits the project while the engine is running, so structural edits (new zound,
        /// rename, retarget) are heard without restarting Play mode. Loaded clips and playing tokens are kept.
        /// </summary>
        public static void InvalidateLookups() {
            if (instance == null) return;
            instance.zoundDictionary.Clear();
            instance.zoundDictionaryById.Clear();
        }

        public static void ClearEngineCaches() {
            if (instance == null) return;
            instance.zoundDictionary.Clear();
            instance.zoundDictionaryById.Clear();
            instance.loadedClips.Clear();
            instance.runtimeClipFolders.Clear();
            instance.missingZounds.Clear();
            instance.cullingGroups.Clear();
            instance.tokens.Clear();
            instance.zoundLastPlayedTimes.Clear();
            // Tear down what is playing BEFORE throwing away what it is playing from. Without this, a cache clear
            // leaves sounds running from decoded audio and effect chains the engine believes it has dropped --
            // so an edit in the Zounds window appears to have done nothing until the old sound happens to end.
            //
            // Tearing down rather than merely asking to stop, deliberately: a stop request only takes effect a
            // block later, so the sounds would still be reading while the next lines free what they are reading
            // from. And tearing down is followed by WAITING until the audio side has visibly stopped, rather than
            // trusting that it has -- because the moment a sound's audio is shared between voices instead of copied
            // per voice, the difference between those two stops being a cosmetic inconsistency and becomes a read of
            // freed memory. Getting the barrier right while it is still harmless is the cheap moment to do it.
            Laubrary.Zounds.Dsp.SapVoiceRegistry.DestroyAllAndConfirm(out bool voicesStopped);
            if (!voicesStopped) {
                // The sounds were deliberately left playing rather than destroyed, so they carry on from data the
                // engine is about to drop. That is audible and wrong, and it is still the better outcome: destroying a
                // sound the mixer is still reading has been observed to take the editor down with it.
                Debug.LogWarning("[Zounds] The library was invalidated while a sound could not be seen to stop, so " +
                                 "that sound was left playing rather than destroyed. It will finish playing from " +
                                 "settings the engine has already dropped, so the edit will seem not to have taken " +
                                 "effect until it ends. Destroying it instead would have risked the editor.");
            }
            Laubrary.Zounds.Dsp.ZoundPcmCache.Clear();
            Laubrary.Zounds.Dsp.ZoundDspPlayback.InvalidateLayouts();
            // A stepping modulator's position is remembered per sound so that "advance one step per play" can work at
            // all. Those entries hold onto the sound objects, so they have to be dropped when the library is, or they
            // would both keep dead sounds alive and resume a sequence belonging to a project that is no longer loaded.
            Laubrary.Zounds.Dsp.SapVoiceSetup.ForgetStepPositions();
        }

        public static void Initialize() {
#if UNITY_EDITOR
            // In the Editor, play the project the Zounds window edits. The StreamingAssets copy is a build
            // artefact that can be stale, and loading it would swap it into ZoundsProject.Instance.
            if (ZoundsProject.isJSONLoaded) {
                InitializeEngine();
                return;
            }
#endif
            string defaultProjectPath = System.IO.Path.Combine(
                Application.streamingAssetsPath, "DefaultZoundsProject.json");
            if (System.IO.File.Exists(defaultProjectPath)) {
                var jsonContent = System.IO.File.ReadAllText(defaultProjectPath);
                Initialize(jsonContent);
            }
            else {
                Debug.LogError("ZoundEngine is initialized without passing a json project, but default zounds project is not available.");
            }
        }

        public static void Initialize(TextAsset jsonTextAsset) {
            if (jsonTextAsset != null) {
                Initialize(jsonTextAsset.text);
            }
        }

        public static void Initialize(string jsonContent) {
            if (string.IsNullOrEmpty(jsonContent)) {
                Debug.LogError("Zounds Project json content is empty.");
                return;
            }
            ZoundsProject.LoadFromJSON(jsonContent);
            InitializeEngine();
        }

        private static void InitializeEngine() {
            if (!Application.isPlaying) {
                Debug.LogError("Can't initialize ZoundEngine during edit mode.");
                return;
            }
            var inst = Instance;
            inst.zoundDictionary.Clear();
            inst.zoundDictionaryById.Clear();
            inst.loadedClips.Clear();
            inst.runtimeClipFolders.Clear();
            inst.missingZounds.Clear();

#if ADDRESSABLES_INSTALLED
            ZoundDictionary.Initialize();
#endif
            UpdateMasterVolume(ZoundsProject.Instance.projectSettings);
            initialized = true;
        }

        public static async Task InitializeAsync() {
#if UNITY_EDITOR
            // See Initialize() above.
            if (ZoundsProject.isJSONLoaded) {
                await InitializeEngineAsync();
                return;
            }
#endif
            string defaultProjectPath = System.IO.Path.Combine(
                Application.streamingAssetsPath, "DefaultZoundsProject.json");
            if (System.IO.File.Exists(defaultProjectPath)) {
                var jsonContent = await System.IO.File.ReadAllTextAsync(defaultProjectPath);
                await InitializeAsync(jsonContent);
            }
            else {
                Debug.LogError("ZoundEngine is initialized without passing a json project, but default zounds project is not available.");
            }
        }

        public static async Task InitializeAsync(TextAsset jsonTextAsset) {
            if (jsonTextAsset != null) {
                await InitializeAsync(jsonTextAsset.text);
            }
        }

        public static async Task InitializeAsync(string jsonContent) {
            if (string.IsNullOrEmpty(jsonContent)) {
                Debug.LogError("Zounds Project json content is empty.");
                return;
            }
            ZoundsProject.LoadFromJSON(jsonContent);
            await InitializeEngineAsync();
        }

        private static async Task InitializeEngineAsync() {
            if (!Application.isPlaying) {
                Debug.LogError("Can't initialize ZoundEngine during edit mode.");
                return;
            }
            var inst = Instance;
            inst.zoundDictionary.Clear();
            inst.zoundDictionaryById.Clear();
            inst.loadedClips.Clear();
            inst.runtimeClipFolders.Clear();
            inst.missingZounds.Clear();

#if ADDRESSABLES_INSTALLED
            await ZoundDictionary.InitializeAsync();
#endif
            UpdateMasterVolume(ZoundsProject.Instance.projectSettings);
            initialized = true;
        }

        public static void StopAllZounds(bool cleanupPool = false) {
            var inst = Instance;
            foreach (var token in inst.tokens) {
                token.Kill();
            }
            inst.tokens.Clear();
            foreach (var cullingGroup in inst.cullingGroups.Values) {
                cullingGroup.Clear();
            }
            inst.pool.StopAllSources(cleanupPool);

            // Also stop the chain voices. Killing a token and stopping its audio source covers a sound that plays a
            // file, but a sound playing through its effect chain is being rendered by a voice the audio graph owns,
            // and that voice needs telling directly — otherwise "stop everything" would leave chain-played sounds
            // running, which is the opposite of what anyone pressing it wants. Done here rather than at each call
            // site so every existing way of stopping everything picks it up.
            Laubrary.Zounds.Dsp.SapVoiceRegistry.StopAll();
        }

        /// <summary>Returns a token for this zound without playing it. Call token.Play() to start.</summary>
        public static ZoundToken GetZoundToken(string zoundName) {
            return PlayZound(zoundName, ZoundArgs.Deferred);
        }

        /// <summary>Plays a zound by name and optional fallback. Returns a token to control playback.</summary>
        public static ZoundToken PlayZound(string zoundName, string fallbackZoundName = null) {
            return PlayZound(zoundName, ZoundArgs.Default, fallbackZoundName);
        }

        public static ZoundToken PlayZound(string zoundName, ZoundArgs zoundArgs, string fallbackZoundName = null) {
            if (ZoundDictionary.TryGetZoundByName(zoundName, out Zound zound)) {
                return PlayZound(zound, zoundArgs);
            }
            // A name already known to be missing skips the library search: that search blocks on
            // Addressables lookups and a missing name would otherwise pay for it on every call.
            if (!IsKnownMissing(zoundName)) {
                var clipToken = TryPlayLibraryClipFallback(zoundName, zoundArgs);
                if (clipToken != null) return clipToken;
            }
            HandleMissingZound(zoundName);
            if (fallbackZoundName != null) PlayZound(fallbackZoundName, zoundArgs);
            return null;
        }

        private static bool IsKnownMissing(string zoundName) {
            if (instance == null) return false;
            return instance.missingZounds.ContainsKey(ZoundDictionary.ZoundNameToKey(zoundName));
        }

        /// <summary>Plays a zound. Returns a token to control playback.</summary>
        public static ZoundToken PlayZound(Zound zound) {
            return PlayZound(zound, ZoundArgs.Default);
        }

        public static ZoundToken PlayZound(Zound zound, ZoundArgs zoundArgs) {
            if (zound == null) return null;
            if (!zoundArgs.ignoreCooldown && IsCoolingDownAtTime(zound, Time.realtimeSinceStartup + zoundArgs.delay)) {
                return null;
            }

            float chance = zoundArgs.chanceOverride >= 0f ? zoundArgs.chanceOverride : zound.chance;
            float chanceResult = Random.Range(0f, 1f);
            if (chanceResult > chance + Mathf.Epsilon) {
                return null;
            }

            var inst = Instance;
            var projectSettings = ZoundsProject.Instance.projectSettings;

            // The play can begin inside this call now, before the next update refreshes the solo state.
            inst.hasAnySoloZoundThisFrame = ZoundsProject.Instance.zoundLibrary.HasAnySoloZound();

            var audioSource = inst.pool.RequestAudioSource();
            var token = new ZoundToken(zound, audioSource, zoundArgs);
            onNewTokenCreated?.Invoke(token);
            inst.tokens.Add(token);

            if (!inst.cullingGroups.TryGetValue(zound, out var zoundTokenList)) {
                zoundTokenList = new LinkedList<ZoundToken>();
                inst.cullingGroups.Add(zound, zoundTokenList);
            }
            if (zoundTokenList.Count >= projectSettings.maxPlayedZoundInstances) {
                var dequeuedToken = zoundTokenList.First.Value;
                zoundTokenList.RemoveFirst();
                dequeuedToken.Kill(projectSettings.cullFadeDuration);
            }
            zoundTokenList.AddLast(token);

            Laubrary.Zounds.Dsp.ZoundTriggerWatch.OnPlay(zound, token, in zoundArgs);

            if (zoundArgs.startImmediately) {
                token.Start();
            }
            global::Laubrary.Zounds.Dsp.ZoundGCStressTest.MaybeSchedule(token);
            return token;
        }

        // Tries to find an AudioClip in the library folder whose name matches zoundName.
        // If found, wraps it in a ClipZound, caches it in the dictionary, and plays it.
        // Returns the ZoundToken on success, null if no matching clip exists.
        private static ZoundToken TryPlayLibraryClipFallback(string zoundName, ZoundArgs zoundArgs) {
#if UNITY_EDITOR
            if (ZoundDictionary.editorAudioClipZoundsCache != null) {
                string editorKey = ZoundDictionary.ZoundNameToKey(zoundName);
                var cached = ZoundDictionary.editorAudioClipZoundsCache
                    .Find(c => ZoundDictionary.ZoundNameToKey(c.name) == editorKey);
                if (cached != null) {
                    ZoundDictionary.ValidateZoundRuntime(cached);
                    return PlayZound(cached, zoundArgs);
                }
            }
#endif
#if ADDRESSABLES_INSTALLED
            // At runtime: try common audio extensions under the library folder path.
            // Addressable addresses are set to their full asset path by ZoundsAssetPostProcessor.
            string libraryPath = ZoundsProject.Instance.projectSettings.libraryFolderPath;
            string[] extensions = { ".wav", ".mp3", ".ogg", ".aif", ".aiff" };
            foreach (var ext in extensions) {
                string address = libraryPath.TrimEnd('/') + "/" + zoundName + ext;
                var locHandle = UnityEngine.AddressableAssets.Addressables.LoadResourceLocationsAsync(address, typeof(AudioClip));
                locHandle.WaitForCompletion();
                if (locHandle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded
                    && locHandle.Result != null && locHandle.Result.Count > 0) {
                    var loadHandle = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<AudioClip>(address);
                    AudioClip clip = loadHandle.WaitForCompletion();
                    if (clip != null) {
                        var clipZound = new ClipZound(clip, address);
                        ZoundDictionary.ValidateZoundRuntime(clipZound);
                        return PlayZound(clipZound, zoundArgs);
                    }
                    break;
                }
            }
#endif
            return null;
        }

        // routedNote (optional): when the name-fallback selector routed this request to a real zound, a
        // human-readable "played X via rule Y" string. The dictionary key stays the requested name's key
        // either way — only the displayed placeholder name carries the note — so IsKnownMissing/removal
        // by key are unaffected by whether a route was found.
        private static void HandleMissingZound(string zoundName, string routedNote = null) {
            string key = ZoundDictionary.ZoundNameToKey(zoundName);
            var inst = Instance;
            if (!inst.missingZounds.ContainsKey(key)) {
                string displayName = string.IsNullOrEmpty(routedNote) ? zoundName : $"{zoundName} — {routedNote}";
                inst.missingZounds.Add(key, new Zound(0) {
                    name = displayName
                });
                // Warned once per name per engine lifetime; the full list stays in the browser's Missing view, and in the
                // diagnostics list beside every other lookup that found nothing (which prints the one warning).
                string warnSuffix = string.IsNullOrEmpty(routedNote) ? "" : $" ({routedNote})";
                ZoundDiagnostics.Report(ZoundDiagnostics.Kind.MissingZound, "", zoundName,
                    $"No zound named '{zoundName}'. Added to the Missing list.{warnSuffix}");
            }
            else ZoundDiagnostics.Count(ZoundDiagnostics.Kind.MissingZound, "", zoundName);
        }

        internal static bool IsCoolingDownAtTime(Zound zound, float time) {
            if (instance == null) return false;
            var inst = instance;
            var projectSettings = ZoundsProject.Instance.projectSettings;
            if (inst.zoundLastPlayedTimes.TryGetValue(zound, out float lastPlayedTime)) {
                float cooldownDuration = projectSettings.cooldownDuration;
                if (time - lastPlayedTime < cooldownDuration) {
                    return true;
                }
            }
            return false;
        }

        internal static void NotifyZoundStartedPlaying(Zound zound, ZoundToken token) {
            onZoundStartedPlaying?.Invoke(zound, token);
        }

        internal static void RecordLastPlayedTime(Zound zound) {
            var inst = Instance;
            if (inst.zoundLastPlayedTimes.ContainsKey(zound)) {
                inst.zoundLastPlayedTimes[zound] = Time.realtimeSinceStartup;
            }
            else {
                inst.zoundLastPlayedTimes.Add(zound, Time.realtimeSinceStartup);
            }
        }

        public static float GetMasterVolume() {
            return masterVolume;
        }

        private static float s_globalSpeed = 1f;

        /// <summary>
        /// A speed applied to every sound that has live speed switched on, on top of its own (T-0409) — the bullet-time
        /// control: set 0.35 and every such sound playing now, and every one started afterwards, slows to 0.35 of its
        /// speed without its pitch dropping. Sounds without live speed ignore it and play normally. 1 is unchanged.
        /// </summary>
        public static float globalSpeed {
            get => s_globalSpeed;
            set {
                s_globalSpeed = Mathf.Clamp(value, Dsp.SapStretch.MinSpeed, Dsp.SapStretch.MaxSpeed);
                Dsp.SapVoiceRegistry.RefreshAllSpeeds();
            }
        }

        public static float GetRemainingCooldownTime(Zound zound) {
            if (instance == null) return 0f;
            if (instance.zoundLastPlayedTimes.TryGetValue(zound, out float lastPlayedTime)) {
                float cooldownDuration = ZoundsProject.Instance.projectSettings.cooldownDuration;
                float delta = Time.realtimeSinceStartup - lastPlayedTime;
                float remainingTime = cooldownDuration - delta;
                if (remainingTime < 0) return 0f;
                else return remainingTime;
            }
            else {
                return 0f;
            }
        }

        private void OnEnable() {
            if (instance == null) {
                instance = this;
#if UNITY_EDITOR
                DetermineUpdater();
#else //Use runtime updater in build
                UseRuntimeUpdater();
#endif
            }
        }

        private void OnUpdate() {
            var zoundsProject = ZoundsProject.Instance;
            var projectSettings = zoundsProject.projectSettings;
            UpdateMasterVolume(projectSettings);

            hasAnySoloZoundThisFrame = zoundsProject.zoundLibrary.HasAnySoloZound();
            Laubrary.Zounds.Dsp.ZoundTriggerWatch.OnEngineUpdate();

            List<int> removedIndices = null; // only allocate the list if there's at least 1 token being killed.

            for (int i = 0; i < tokens.Count; i++) {
                ZoundToken token = tokens[i];
                if (token.state == ZoundToken.State.Killed) {
                    if (removedIndices == null) removedIndices = new List<int>();
                    removedIndices.Add(i);
                    continue;
                }

                token.OnUpdate();
            }

            if (removedIndices != null) {
                removedIndices.Reverse();
                foreach (var index in removedIndices) {
                    var token = tokens[index];
                    if (cullingGroups.TryGetValue(token.zound, out var zoundTokenList)) {
                        zoundTokenList.Remove(token);
                    }
                    pool.ReturnAudioSource(token.audioSource);
                    tokens.RemoveAt(index);
                }
            }

#if UNITY_EDITOR
            if (!Application.isPlaying && tokens.Count > 0) {
                EditorApplication.QueuePlayerLoopUpdate();
            }
#endif
        }

        private static void UpdateMasterVolume(ZoundsProject.ProjectSettings projectSettings) {
            masterVolume = Application.isPlaying ? projectSettings.playerVolume : projectSettings.editorVolume;
            masterVolume *= projectSettings.systemVolumeModifier;
        }

        private static void UseRuntimeUpdater() {
            RuntimeUpdater.Instance.onUpdate = Instance.OnUpdate;
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void InitializeEditMode() {
            AssemblyReloadEvents.afterAssemblyReload += DetermineUpdater;
            EditorApplication.playModeStateChanged += EditorApplication_playModeStateChanged;
            ZoundsProject.onProjectModified += InvalidateLookups;
            ZoundsProject.onProjectModified += Laubrary.Zounds.Dsp.ZoundDspPlayback.InvalidateLayouts;
        }

        private static void EditorApplication_playModeStateChanged(PlayModeStateChange stateChange) {
            if (stateChange == PlayModeStateChange.EnteredEditMode) {
                ZoundMixerCache.Clear();
            }
            if (instance == null) return;

            if (stateChange == PlayModeStateChange.ExitingEditMode || stateChange == PlayModeStateChange.EnteredEditMode) {
                System.Action<UnityEngine.Object> destroyHandler;
                if (Application.isPlaying) destroyHandler = GameObject.Destroy;
                else destroyHandler = GameObject.DestroyImmediate;

                // Unhook first: a deferred Destroy would otherwise leave this instance's tick on
                // EditorApplication.update, running against a dead engine every editor frame.
                EditorApplication.update -= instance.OnEditorUpdateMode;
                destroyHandler(instance.gameObject);
                instance = null;
            }

            if (stateChange == PlayModeStateChange.EnteredPlayMode) {
                //Debug.Log("Enter Play Mode, Is Playing: " + Application.isPlaying);
                DetermineUpdater();
            }
            else if (stateChange == PlayModeStateChange.EnteredEditMode) {
                initialized = false;
                //Debug.Log("Enter Edit Mode, Is Playing: " + Application.isPlaying);
                DetermineUpdater();
            }
        }

        internal static void DetermineUpdater() {
            if (instance == null) return;
            if (Application.isPlaying) {
                UnityEditor.EditorApplication.update -= instance.OnEditorUpdateMode;
                UseRuntimeUpdater();
            }
            else {
                // Remove-then-add keeps this idempotent: creation runs it twice (OnEnable + the getter).
                UnityEditor.EditorApplication.update -= instance.OnEditorUpdateMode;
                UnityEditor.EditorApplication.update += instance.OnEditorUpdateMode;
            }
        }

        private void OnEditorUpdateMode() {
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            OnUpdate();
        }
#endif

    }

    public struct ZoundArgs {
        public bool startImmediately;
        public float delay;
        public float volumeOverride;
        public float pitchOverride;
        public float chanceOverride;
        public bool useFixedAverageValues; // reserved for future use
        public bool isChild; // only for debugging purpose (to show white border when is not a child)
        internal bool overrideMixerGroup;
        internal AudioMixerGroup mixerGroupOverride;
        public float overrideDuration;
        public CompositeZound.ZoundEntry soloOverride;
        public bool bypassGlobalSolo;
        public bool ignoreCooldown;
        /// <summary>Native-DSP pipeline only: the Zequence track this play belongs to, when that track repeats (Repeater settings live on the entry).</summary>
        internal CompositeZound.ZoundEntry repeatEntry;
        /// <summary>Native-DSP pipeline only: the random pitch/volume factors the parent already rolled into the overrides (1 when none), so a retriggered repeat can re-roll them.</summary>
        internal float pitchRandomFactor;
        internal float volumeRandomFactor;

        /// <summary>Returns a default ZoundArgs ready for immediate playback with no overrides.</summary>
        public static ZoundArgs Default => new ZoundArgs {
            startImmediately = true,
            delay = 0f,
            volumeOverride = -1f,
            pitchOverride = -1f,
            chanceOverride = -1f
        };

        /// <summary>Returns a ZoundArgs configured to create a token without immediately starting it.</summary>
        public static ZoundArgs Deferred => new ZoundArgs {
            startImmediately = false,
            delay = 0f,
            volumeOverride = -1f,
            pitchOverride = -1f,
            chanceOverride = -1f
        };
    }

}
