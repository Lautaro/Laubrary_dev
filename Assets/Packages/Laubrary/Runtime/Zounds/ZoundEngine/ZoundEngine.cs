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
        internal static Dictionary<Zound, LinkedList<ZoundToken>> CullingGroups => Instance.cullingGroups;
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
            if (instance == this) instance = null;
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
        }

        public static void Initialize() {
            string defaultProjectPath = System.IO.Path.Combine(
                Application.streamingAssetsPath, "DefaultZoundsProject.json");
            if (System.IO.File.Exists(defaultProjectPath)) {
                var jsonContent = System.IO.File.ReadAllText(defaultProjectPath);
                Initialize(jsonContent);
            }
#if UNITY_EDITOR
            else if (ZoundsProject.isJSONLoaded) {
                InitializeEngine();
            }
#endif
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
            string defaultProjectPath = System.IO.Path.Combine(
                Application.streamingAssetsPath, "DefaultZoundsProject.json");
            if (System.IO.File.Exists(defaultProjectPath)) {
                var jsonContent = await System.IO.File.ReadAllTextAsync(defaultProjectPath);
                await InitializeAsync(jsonContent);
            }
#if UNITY_EDITOR
            else if (ZoundsProject.isJSONLoaded) {
                await InitializeEngineAsync();
            }
#endif
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
        }

        /// <summary>Returns a token for this zound without playing it. Call token.Play() to start.</summary>
        public static ZoundToken GetZoundToken(string zoundName) {
            return PlayZound(zoundName, ZoundArgs.Deferred);
        }

        /// <summary>Plays a zound by name and optional fallback. Returns a token to control playback.</summary>
        public static ZoundToken PlayZound(string zoundName, string fallbackZoundName = null) {
            if (ZoundDictionary.TryGetZoundByName(zoundName, out Zound zound)) {
                return PlayZound(zound, ZoundArgs.Default);
            }
            var clipToken = TryPlayLibraryClipFallback(zoundName, ZoundArgs.Default);
            if (clipToken != null) return clipToken;
            HandleMissingZound(zoundName);
            if (fallbackZoundName != null) PlayZound(fallbackZoundName);
            return null;
        }

        public static ZoundToken PlayZound(string zoundName, ZoundArgs zoundArgs, string fallbackZoundName = null) {
            if (ZoundDictionary.TryGetZoundByName(zoundName, out Zound zound)) {
                return PlayZound(zound, zoundArgs);
            }
            var clipToken = TryPlayLibraryClipFallback(zoundName, zoundArgs);
            if (clipToken != null) return clipToken;
            HandleMissingZound(zoundName);
            if (fallbackZoundName != null) PlayZound(fallbackZoundName, zoundArgs);
            return null;
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

            if (zoundArgs.startImmediately) {
                token.Start();
            }
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

        private static void HandleMissingZound(string zoundName) {
            //Debug.LogError("Error playing " + zoundName + ": Zound name doesn't exist.");
            string key = ZoundDictionary.ZoundNameToKey(zoundName);
            var inst = Instance;
            if (!inst.missingZounds.ContainsKey(key)) {
                inst.missingZounds.Add(key, new Zound(0) {
                    name = zoundName
                });
            }
        }

        internal static bool IsCoolingDownAtTime(Zound zound, float time) {
            var inst = Instance;
            var projectSettings = ZoundsProject.Instance.projectSettings;
            if (inst.zoundLastPlayedTimes.TryGetValue(zound, out float lastPlayedTime)) {
                float cooldownDuration = projectSettings.cooldownDuration;
                if (time - lastPlayedTime < cooldownDuration) {
                    return true;
                }
            }
            return false;
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

        public static float GetRemainingCooldownTime(Zound zound) {
            if (Instance.zoundLastPlayedTimes.TryGetValue(zound, out float lastPlayedTime)) {
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

                destroyHandler(instance.gameObject);
                instance = null;
                //Debug.Log("Destroyed: " + stateChange);
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