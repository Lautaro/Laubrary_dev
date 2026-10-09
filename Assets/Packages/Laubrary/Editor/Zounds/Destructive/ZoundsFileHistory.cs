using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds.Destructive {

    /// <summary>
    /// Undo for audio files (destructive editing, 2026-10-09). Unity's Undo only knows objects, not files, so each file an
    /// edit rewrites or creates gets a folder of its versions, and the Undo step records which version the file should be
    /// at; when an undo or redo moves that record, the matching version is copied back into place and re-imported.
    ///
    /// Where: <c>&lt;system folder&gt;/Versions~/&lt;file id&gt;/&lt;n&gt;.wav</c> (and its <c>.meta</c>). The trailing "~" keeps
    /// Unity from importing the copies, and the folder is git-ignored: they are a safety net, not the work. The last
    /// <see cref="Keep"/> versions of each file are kept; an undo reaching further back than that leaves the file as it
    /// is and says so.
    ///
    /// A file an edit CREATED (the copy written when the original is protected) is recorded the same way, with "absent"
    /// before the edit: undoing the edit removes the file again (its last version stays in the folder), and redoing it
    /// puts it back under the same id, so the sound that plays it finds it.
    /// </summary>
    internal sealed class ZoundsFileHistory : ScriptableObject {

        public const int Keep = 5;

        [Serializable]
        public class FileState {
            public string key;       // the file's asset id when first recorded
            public string path;      // project path
            public int version;      // the version that should be on disk
            public bool created;     // made by an edit (absent before it)
        }

        public List<FileState> files = new List<FileState>();

        static ZoundsFileHistory s_instance;
        // What is actually on disk now, per key; rebuilt from the record after a script reload (the disk is then current).
        static readonly Dictionary<string, int> onDisk = new Dictionary<string, int>();
        static bool hooked;

        /// <summary>Raised after an undo or redo put a different version of some files in place (their project paths).</summary>
        public static event Action<List<string>> filesRestored;

        public static ZoundsFileHistory Instance {
            get {
                if (s_instance == null) {
                    foreach (var h in Resources.FindObjectsOfTypeAll<ZoundsFileHistory>()) { s_instance = h; break; }
                    if (s_instance == null) {
                        s_instance = CreateInstance<ZoundsFileHistory>();
                        s_instance.hideFlags = HideFlags.DontSave;   // as the project object: recorded by Undo, never saved
                    }
                    onDisk.Clear();
                    foreach (var f in s_instance.files) onDisk[f.key] = f.version;
                }
                Hook();
                return s_instance;
            }
        }

        [InitializeOnLoadMethod]
        static void Hook() {
            if (hooked) return;
            hooked = true;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        static string Root => ZoundsProject.Instance.projectSettings.systemFolderPath + "/Versions~";
        static string Dir(string key) => ZoundsProtection.Absolute(Root + "/" + key);
        static string VersionFile(string key, int v) => Path.Combine(Dir(key), v + ".wav");

        FileState Find(string key) => files.Find(f => f.key == key);

        static int NextVersion(string key) {
            int n = 0;
            string d = Dir(key);
            if (Directory.Exists(d)) foreach (var f in Directory.GetFiles(d, "*.wav")) if (int.TryParse(Path.GetFileNameWithoutExtension(f), out int v) && v >= n) n = v + 1;
            return n;
        }

        static void Snapshot(string key, string projectPath, int v) {
            Directory.CreateDirectory(Dir(key));
            string abs = ZoundsProtection.Absolute(projectPath);
            File.Copy(abs, VersionFile(key, v), true);
            if (File.Exists(abs + ".meta")) File.Copy(abs + ".meta", VersionFile(key, v) + ".meta", true);
        }

        static void Prune(string key, int keepFrom) {
            string d = Dir(key);
            if (!Directory.Exists(d)) return;
            foreach (var f in Directory.GetFiles(d, "*.wav"))
                if (int.TryParse(Path.GetFileNameWithoutExtension(f), out int v) && v < keepFrom) {
                    try { File.Delete(f); if (File.Exists(f + ".meta")) File.Delete(f + ".meta"); } catch (Exception e) { Debug.LogWarning("[Zounds] Could not remove an old version: " + e.Message); }
                }
        }

        /// <summary>
        /// Rewrites a file Zounds owns with <paramref name="wav"/>, keeping the version it had. Call inside the edit's Undo
        /// group: this records the history object, so the same Undo step that reverts the project reverts the file.
        /// </summary>
        public void Rewrite(string projectPath, byte[] wav, string undoName) {
            string key = AssetDatabase.AssetPathToGUID(projectPath);
            var st = Find(key);
            if (st == null || !onDisk.ContainsKey(key)) {
                // First time this file is edited (or first since a reload): its current content is version n.
                int v0 = NextVersion(key);
                Snapshot(key, projectPath, v0);
                if (st == null) { st = new FileState { key = key, path = projectPath, version = v0 }; files.Add(st); }
                else st.version = v0;
                onDisk[key] = v0;
            }
            Undo.RecordObject(this, undoName);
            WriteFile(projectPath, wav);
            int v1 = NextVersion(key);
            Snapshot(key, projectPath, v1);
            st.version = v1; st.path = projectPath;
            onDisk[key] = v1;
            Prune(key, v1 - Keep + 1);
        }

        /// <summary>Records a file an edit just created and imported (absent before the edit). Call inside the edit's Undo group.</summary>
        public void Created(string projectPath, string undoName) {
            Undo.RecordObject(this, undoName);
            string key = AssetDatabase.AssetPathToGUID(projectPath);
            int v = NextVersion(key);
            Snapshot(key, projectPath, v);
            files.RemoveAll(f => f.key == key);
            files.Add(new FileState { key = key, path = projectPath, version = v, created = true });
            onDisk[key] = v;
        }

        /// <summary>Writes bytes to a project file and imports it, so the editor and playback hear the new audio.</summary>
        public static void WriteFile(string projectPath, byte[] wav) {
            string abs = ZoundsProtection.Absolute(projectPath);
            SavWav.ClearReadOnlyIfPresent(abs);
            File.WriteAllBytes(abs, wav);
            AssetDatabase.ImportAsset(projectPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            ZoundsAudioEdits.Invalidate(projectPath);
        }

        static void OnUndoRedo() {
            if (s_instance == null) return;
            var h = s_instance;
            var changed = new List<string>();
            var present = new HashSet<string>();
            foreach (var st in h.files) {
                present.Add(st.key);
                if (onDisk.TryGetValue(st.key, out int now) && now == st.version) continue;
                if (!File.Exists(VersionFile(st.key, st.version))) {
                    Debug.LogWarning("[Zounds] '" + Path.GetFileName(st.path) + "' cannot go back that far: only its last " + Keep + " versions are kept. The file is left as it is.");
                    onDisk[st.key] = st.version;
                    continue;
                }
                bool existed = File.Exists(ZoundsProtection.Absolute(st.path));
                Restore(st.key, st.version, st.path, !existed);
                onDisk[st.key] = st.version;
                changed.Add(st.path);
            }
            // A created file whose record the undo took away: the edit that made it is undone, so it goes.
            var gone = new List<string>();
            foreach (var kv in onDisk) if (!present.Contains(kv.Key) && kv.Value >= 0) gone.Add(kv.Key);
            foreach (var key in gone) {
                string path = AssetDatabase.GUIDToAssetPath(key);
                if (!string.IsNullOrEmpty(path) && File.Exists(ZoundsProtection.Absolute(path))) {
                    ZoundsAudioEdits.ForgetAddressable(key);
                    AssetDatabase.DeleteAsset(path);
                    changed.Add(path);
                }
                onDisk[key] = -1;
            }
            // Redo of a creation: the record is back, the file is not (onDisk says -1): handled above, since -1 != version.
            if (changed.Count > 0) {
                foreach (var p in changed) ZoundsAudioEdits.Invalidate(p);
                try { filesRestored?.Invoke(changed); } catch (Exception e) { Debug.LogException(e); }
            }
        }

        static void Restore(string key, int v, string projectPath, bool withMeta) {
            string abs = ZoundsProtection.Absolute(projectPath);
            Directory.CreateDirectory(Path.GetDirectoryName(abs));
            SavWav.ClearReadOnlyIfPresent(abs);
            File.Copy(VersionFile(key, v), abs, true);
            // A file coming back after being removed keeps its id (from the kept .meta), so the sound that plays it finds it.
            if (withMeta && File.Exists(VersionFile(key, v) + ".meta")) File.Copy(VersionFile(key, v) + ".meta", abs + ".meta", true);
            AssetDatabase.ImportAsset(projectPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>For the kept check: forget everything recorded (the files and versions it made are its own to clean).</summary>
        internal static void ForgetForCheck(string key) {
            if (s_instance != null) s_instance.files.RemoveAll(f => f.key == key);
            onDisk.Remove(key);
        }

        internal static string VersionsFolderFor(string key) => Dir(key);
    }
}
