using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Laubrary.ZTracker.Editor {
    /// <summary>Scans an external drum folder and imports selected files as project-owned samples.</summary>
    public static class ZTrackerDrumLibrary {
        const string RootPrefPrefix = "ZTracker.DrumLibrary.Root.";
        const string SampleFolder = "Assets/ZTracker/DrumSamples";
        const string ProvenanceKey = "ZTrackerSourceUri=";
        const string SupportedExtensions = ".wav;.ogg;.mp3;.aiff;.aif;.flac";

        [Serializable]
        public sealed class DrumFile {
            public string fullPath;
            public string name;
            public string parentFolderTag;
            public string extension;
        }

        [Serializable]
        public sealed class ScanError {
            public string path;
            public string message;
        }

        public sealed class ScanResult {
            public readonly List<DrumFile> files = new List<DrumFile>();
            public readonly List<ScanError> errors = new List<ScanError>();
        }

        public sealed class ImportResult {
            public AudioClip clip;
            public string sourceUrl;
            public string error;
            public bool Succeeded => clip != null && string.IsNullOrEmpty(error);
        }

        public sealed class ProjectClip {
            public AudioClip clip;
            public string assetPath;
            public string sourceUrl;
        }

        /// <summary>The external source root saved for this Unity project on this machine.</summary>
        static string RootPref {
            get {
                string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                using (var sha = SHA256.Create()) {
                    byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(projectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant()));
                    return RootPrefPrefix + BitConverter.ToString(hash, 0, 8).Replace("-", string.Empty);
                }
            }
        }

        public static string GetRoot() => EditorPrefs.GetString(RootPref, string.Empty);

        public static void SetRoot(string path) {
            if (string.IsNullOrWhiteSpace(path)) {
                EditorPrefs.DeleteKey(RootPref);
                return;
            }
            EditorPrefs.SetString(RootPref, Path.GetFullPath(path));
        }

        /// <summary>Canonical comparison form: case-insensitive, ignoring spaces, underscores and hyphens.</summary>
        public static string NormalizeTag(string value) {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var result = new StringBuilder(value.Length);
            foreach (char c in value.Trim())
                if (!char.IsWhiteSpace(c) && c != '_' && c != '-') result.Append(char.ToLowerInvariant(c));
            return result.ToString();
        }

        /// <summary>Recursively lists supported files. Search matches names and parent tags; no audio is imported.</summary>
        public static ScanResult Scan(string search = null) => ScanRoot(GetRoot(), search);

        /// <summary>Scans a supplied root so callers and focused checks can use the same complete traversal.</summary>
        internal static ScanResult ScanRoot(string root, string search = null) {
            var result = new ScanResult();
            if (string.IsNullOrWhiteSpace(root)) {
                result.errors.Add(new ScanError { path = string.Empty, message = "Choose a drum library folder first." });
                return result;
            }
            try { root = Path.GetFullPath(root); }
            catch (Exception e) {
                result.errors.Add(new ScanError { path = root, message = e.Message });
                return result;
            }
            if (!Directory.Exists(root)) {
                result.errors.Add(new ScanError { path = root, message = "The configured drum library folder does not exist." });
                return result;
            }

            var pending = new Stack<string>();
            pending.Push(root);
            var extensions = new HashSet<string>(SupportedExtensions.Split(';'), StringComparer.OrdinalIgnoreCase);
            string normalizedSearch = NormalizeTag(search);
            while (pending.Count > 0) {
                string directory = pending.Pop();
                string[] subdirectories = Array.Empty<string>();
                try { subdirectories = Directory.GetDirectories(directory); }
                catch (Exception e) {
                    result.errors.Add(new ScanError { path = directory, message = e.Message });
                }
                Array.Sort(subdirectories, StringComparer.OrdinalIgnoreCase);
                for (int i = subdirectories.Length - 1; i >= 0; i--) {
                    try {
                        if ((File.GetAttributes(subdirectories[i]) & FileAttributes.ReparsePoint) == 0)
                            pending.Push(subdirectories[i]);
                    }
                    catch (Exception e) {
                        result.errors.Add(new ScanError { path = subdirectories[i], message = e.Message });
                    }
                }

                string[] paths = Array.Empty<string>();
                try { paths = Directory.GetFiles(directory); }
                catch (Exception e) {
                    result.errors.Add(new ScanError { path = directory, message = e.Message });
                }
                Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
                foreach (string path in paths) {
                    string extension = Path.GetExtension(path);
                    if (!extensions.Contains(extension)) continue;
                    string folder = Path.GetFileName(Path.GetDirectoryName(path));
                    var record = new DrumFile {
                        fullPath = path,
                        name = Path.GetFileNameWithoutExtension(path),
                        parentFolderTag = folder,
                        extension = extension
                    };
                    if (normalizedSearch.Length == 0 || NormalizeTag(record.name).Contains(normalizedSearch) || NormalizeTag(record.parentFolderTag).Contains(normalizedSearch))
                        result.files.Add(record);
                }
            }
            result.files.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.fullPath, b.fullPath));
            return result;
        }

        /// <summary>Copies one selected source into Assets/ZTracker/DrumSamples and imports it for PCM access.</summary>
        public static ImportResult Import(DrumFile file) {
            var result = new ImportResult();
            if (file == null || string.IsNullOrWhiteSpace(file.fullPath)) {
                result.error = "No source file was supplied.";
                return result;
            }
            string source;
            try { source = Path.GetFullPath(file.fullPath); }
            catch (Exception e) { result.error = e.Message; return result; }
            if (!File.Exists(source)) {
                result.error = "Source file not found: " + source;
                return result;
            }
            try { return ImportPath(source, new Uri(source).AbsoluteUri); }
            catch (Exception e) { result.error = e.Message; return result; }
        }

        /// <summary>Copies a selected project clip into the drum library without changing its original importer.</summary>
        public static ImportResult ImportProjectClip(AudioClip sourceClip) {
            var result = new ImportResult();
            if (sourceClip == null) {
                result.error = "No project AudioClip was supplied.";
                return result;
            }
            string sourceAssetPath = AssetDatabase.GetAssetPath(sourceClip);
            if (string.IsNullOrEmpty(sourceAssetPath) || !sourceAssetPath.StartsWith("Assets/", StringComparison.Ordinal)) {
                result.error = "The selected AudioClip is not a project asset.";
                return result;
            }
            try {
                string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", sourceAssetPath));
                string sourceUrl = new Uri(fullPath).AbsoluteUri;
                if (TryGetSourceUri(sourceClip, out string existingSourceUrl)) sourceUrl = existingSourceUrl;
                if (sourceAssetPath.StartsWith(SampleFolder + "/", StringComparison.OrdinalIgnoreCase))
                    return ConfigureAndLoad(sourceAssetPath, sourceUrl);
                if (!File.Exists(fullPath)) {
                    result.error = "The selected AudioClip has no readable source file: " + sourceAssetPath;
                    return result;
                }
                return ImportPath(fullPath, sourceUrl);
            }
            catch (Exception e) {
                result.error = e.Message;
                return result;
            }
        }

        static ImportResult ImportPath(string source, string sourceUrl) {
            var result = new ImportResult();
            try {
                string extension = Path.GetExtension(source);
                if (!SupportedExtensions.Split(';').Contains(extension, StringComparer.OrdinalIgnoreCase)) {
                    result.error = "Unsupported audio format: " + extension;
                    return result;
                }
                byte[] bytes = File.ReadAllBytes(source);
                string hash = ContentIdentity(source, bytes);
                EnsureFolder("Assets", "ZTracker");
                EnsureFolder("Assets/ZTracker", "DrumSamples");
                string stem = SanitizeFileName(Path.GetFileNameWithoutExtension(source));
                string assetPath = SampleFolder + "/" + stem + "_" + hash.Substring(0, 20) + extension.ToLowerInvariant();
                string absoluteDestination = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));

                if (File.Exists(absoluteDestination)) {
                    if (!FilesEqual(absoluteDestination, bytes)) {
                        result.error = "A different file already occupies the content-addressed destination: " + assetPath;
                        return result;
                    }
                } else {
                    try {
                        using (var stream = new FileStream(absoluteDestination, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                            stream.Write(bytes, 0, bytes.Length);
                    }
                    catch (IOException) {
                        if (!File.Exists(absoluteDestination)) throw;
                        if (!FilesEqual(absoluteDestination, bytes)) {
                            result.error = "A different file already occupies the content-addressed destination: " + assetPath;
                            return result;
                        }
                    }
                }

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                return ConfigureAndLoad(assetPath, sourceUrl);
            }
            catch (Exception e) {
                result.error = e.Message;
            }
            return result;
        }

        static ImportResult ConfigureAndLoad(string assetPath, string sourceUrl) {
            var result = new ImportResult { sourceUrl = sourceUrl };
            var importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
            if (importer == null) {
                result.error = "Unity did not create an audio importer for " + assetPath;
                return result;
            }
            bool changed = importer.defaultSampleSettings.loadType != AudioClipLoadType.DecompressOnLoad;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            importer.defaultSampleSettings = settings;
            string updatedUserData = WriteSourceUri(importer.userData, sourceUrl);
            changed |= updatedUserData != importer.userData;
            if (changed) {
                importer.userData = updatedUserData;
                importer.SaveAndReimport();
            }
            result.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
            if (result.clip == null) result.error = "Unity imported the file but could not load it as an AudioClip: " + assetPath;
            return result;
        }

        /// <summary>Gets the original file URI persisted in an imported clip's AudioImporter.userData.</summary>
        public static bool TryGetSourceUri(AudioClip clip, out string sourceUrl) {
            sourceUrl = null;
            if (clip == null) return false;
            string path = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(path)) return false;
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) return false;
            foreach (string line in (importer.userData ?? string.Empty).Split('\n')) {
                string trimmed = line.TrimEnd('\r');
                if (!trimmed.StartsWith(ProvenanceKey, StringComparison.Ordinal)) continue;
                sourceUrl = Uri.UnescapeDataString(trimmed.Substring(ProvenanceKey.Length));
                return !string.IsNullOrEmpty(sourceUrl);
            }
            return false;
        }

        /// <summary>Lists project audio clips and their original source URIs when present.</summary>
        public static List<ProjectClip> GetProjectClips() {
            var clips = new List<ProjectClip>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" })) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!SupportedExtensions.Split(';').Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) continue;
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;
                TryGetSourceUri(clip, out string sourceUrl);
                clips.Add(new ProjectClip { clip = clip, assetPath = path, sourceUrl = sourceUrl });
            }
            clips.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.clip.name, b.clip.name));
            return clips;
        }

        static string WriteSourceUri(string userData, string sourceUrl) {
            var lines = (userData ?? string.Empty).Split('\n').Where(line => !line.TrimEnd('\r').StartsWith(ProvenanceKey, StringComparison.Ordinal)).ToList();
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            lines.Add(ProvenanceKey + Uri.EscapeDataString(sourceUrl));
            return string.Join("\n", lines);
        }

        static string ContentIdentity(string sourcePath, byte[] bytes) {
            using (var sha = SHA256.Create()) {
                byte[] pathBytes = Encoding.UTF8.GetBytes(sourcePath.ToLowerInvariant() + "\0");
                sha.TransformBlock(pathBytes, 0, pathBytes.Length, pathBytes, 0);
                sha.TransformFinalBlock(bytes, 0, bytes.Length);
                return BitConverter.ToString(sha.Hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        static bool FilesEqual(string path, byte[] bytes) {
            var info = new FileInfo(path);
            if (info.Length != bytes.Length) return false;
            using (var stream = File.OpenRead(path)) {
                var buffer = new byte[81920];
                int offset = 0, read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) {
                    for (int i = 0; i < read; i++) if (buffer[i] != bytes[offset + i]) return false;
                    offset += read;
                }
            }
            return true;
        }

        static void EnsureFolder(string parent, string name) {
            string path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
        }

        static string SanitizeFileName(string value) {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            string result = new string(chars).Trim().TrimEnd('.');
            return string.IsNullOrWhiteSpace(result) ? "Drum" : result;
        }
    }
}
