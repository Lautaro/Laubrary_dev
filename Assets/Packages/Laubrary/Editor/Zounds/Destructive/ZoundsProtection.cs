using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds.Destructive {

    /// <summary>
    /// Which sounds and audio files an edit may change in place, and why not when it may not (destructive editing,
    /// 2026-10-09; the owner's design, the PM's decisions).
    ///
    /// <b>A sound is shared</b> when it is a library sound (not local to one Zequence) that other sounds use as a track.
    /// Changing its trim or curves would change those sounds too, so such an edit goes to a copy unless you say otherwise.
    /// A library sound nothing else uses is just itself: editing it in its own editor is the ordinary authoring act.
    ///
    /// <b>An audio file is protected</b> when it is a true source -- anything in the Sources or Library folders, a file
    /// outside the project, or any project file outside Zounds' own folders -- or when Zounds made it but several sounds
    /// play it, or a shared sound does. A protected file is never rewritten: an edit of its audio writes a new file and the
    /// edited sound plays that instead. Any other file is one Zounds made for this sound alone, and is edited in place,
    /// with its earlier versions kept (see <see cref="ZoundsFileHistory"/>).
    ///
    /// "Used by" is the same reckoning the delete dialog uses (the sounds that hold this one as a track), plus a scan of
    /// every sound's source for the file; both read the project in memory and nothing on disk, so they are cheap.
    /// </summary>
    internal static class ZoundsProtection {

        public enum FileKind { None, Source, Library, External, Outside, ZoundsMade }

        public struct FileInfo {
            public FileKind kind;
            /// <summary>Project path of the file ("Assets/..."), or the absolute path of an outside file.</summary>
            public string path;
            public string guid;
            /// <summary>Every sound that plays this file as its source.</summary>
            public List<Klip> users;
            /// <summary>Whether a shared sound is among them.</summary>
            public bool sharedUser;
            public bool IsTrueSource => kind == FileKind.Source || kind == FileKind.Library || kind == FileKind.External || kind == FileKind.Outside;
            public bool Protected => kind != FileKind.ZoundsMade || (users != null && users.Count > 1) || sharedUser;
            public bool IsWav => !string.IsNullOrEmpty(path) && path.EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase);
            public string Name => string.IsNullOrEmpty(path) ? "(no file)" : Path.GetFileName(path);

            /// <summary>Why the file may not be rewritten, as a sentence fragment ("a library file"); null when it may.</summary>
            public string Reason {
                get {
                    switch (kind) {
                        case FileKind.Source: return "a file in the Sources folder, which Zounds never overwrites";
                        case FileKind.Library: return "a library file, which Zounds never overwrites";
                        case FileKind.External: return "a file outside the project, which Zounds never overwrites";
                        case FileKind.Outside: return "a project file outside Zounds' folders, which Zounds never overwrites";
                        case FileKind.None: return "not a file";
                    }
                    if (users != null && users.Count > 1) return "played by " + users.Count + " sounds";
                    if (sharedUser) return "played by a sound that other sounds use";
                    return null;
                }
            }
        }

        // ─────────────────────────── sounds ───────────────────────────

        /// <summary>The sounds that hold <paramref name="z"/> as a track (the delete dialog's own reckoning).</summary>
        public static List<Zound> UsersOf(Zound z) => z == null ? new List<Zound>() : AudioAssetUtility.GetDirectZoundReferences(z);

        /// <summary>A library sound that other sounds use: editing its trim or curves would change them too.</summary>
        public static bool IsShared(Zound z) => z != null && z.parentId == 0 && !(z is ClipZound) && UsersOf(z).Count > 0;

        /// <summary>"used in 2 Zequences", "used in Boom and Crash" -- the users of a shared sound, short.</summary>
        public static string UsedIn(Zound z) {
            var users = UsersOf(z);
            if (users.Count == 0) return "used nowhere else";
            if (users.Count <= 2) return "used in " + string.Join(" and ", users.ConvertAll(u => "'" + u.name + "'"));
            return "used in " + users.Count + " Zequences";
        }

        // ─────────────────────────── files ───────────────────────────

        public static FileInfo FileOf(Klip k) {
            var info = new FileInfo { kind = FileKind.None, users = new List<Klip>() };
            if (k == null) return info;
            var settings = ZoundsProject.Instance.projectSettings;
            if (!string.IsNullOrEmpty(k.externalSourcePath)) {
                info.kind = FileKind.External; info.path = k.externalSourcePath;
            }
            else {
#if ADDRESSABLES_INSTALLED
                string guid = k.audioClipRef != null ? k.audioClipRef.AssetGUID : null;
                string path = string.IsNullOrEmpty(guid) ? null : AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) return info;
                info.path = path; info.guid = guid;
                switch (ZoundsClipClassifier.GetFolder(path, settings)) {
                    case ZoundsClipClassifier.ClipFolder.Sources: info.kind = FileKind.Source; break;
                    case ZoundsClipClassifier.ClipFolder.Library: info.kind = FileKind.Library; break;
                    case ZoundsClipClassifier.ClipFolder.Work:
                    case ZoundsClipClassifier.ClipFolder.ZoundFiles: info.kind = FileKind.ZoundsMade; break;
                    default: info.kind = FileKind.Outside; break;
                }
#else
                return info;
#endif
            }
            string extPath = info.kind == FileKind.External ? info.path : null, g = info.guid;
            ZoundsProject.Instance.zoundLibrary.ForEachZound(z => {
                if (!(z is Klip o)) return;
#if ADDRESSABLES_INSTALLED
                bool same = extPath != null ? o.externalSourcePath == extPath
                                            : string.IsNullOrEmpty(o.externalSourcePath) && o.audioClipRef != null && o.audioClipRef.AssetGUID == g;
#else
                bool same = extPath != null && o.externalSourcePath == extPath;
#endif
                if (!same) return;
                info.users.Add(o);
            });
            foreach (var u in info.users) if (IsShared(u)) { info.sharedUser = true; break; }
            return info;
        }

        /// <summary>
        /// The Klip editor's badge (short, on the source row) and its tooltip (the whole reason): what the file is, whether
        /// an edit of its audio rewrites it, and, for a shared sound, who else uses it.
        /// </summary>
        public static void Badge(Klip k, out string text, out string tooltip, out bool locked) {
            var f = FileOf(k);
            bool shared = IsShared(k);
            locked = f.Protected || shared;
            string file;
            switch (f.kind) {
                case FileKind.Source: file = "Source file"; break;
                case FileKind.Library: file = "Library file"; break;
                case FileKind.External: file = "Outside file"; break;
                case FileKind.Outside: file = "Project file"; break;
                case FileKind.ZoundsMade: file = f.users.Count > 1 ? "File shared by " + f.users.Count : f.sharedUser ? "File of a shared sound" : "Own file"; break;
                default: file = "No file"; break;
            }
            text = file + (f.kind == FileKind.None ? "" : f.Protected ? " · read-only" : " · edited in place") + (shared ? " · sound " + UsedIn(k) : "");
            var sb = new System.Text.StringBuilder();
            if (f.kind == FileKind.None) sb.Append("This sound has no audio file yet.");
            else if (f.Protected) sb.Append("'").Append(f.Name).Append("' is ").Append(f.Reason).Append(". Cut, paste and the other audio edits here write a new file, and this sound plays that one instead; the original file and everything else that plays it stay as they are.");
            else sb.Append("'").Append(f.Name).Append("' was made by Zounds and only this sound plays it, so audio edits here change the file itself. Its last few versions are kept, and Undo brings them back.");
            if (shared) sb.Append("\n\nThis sound is ").Append(UsedIn(k)).Append(". Changing its trim, curves or audio here goes to a copy of it, unless you choose to edit the original (Settings decides whether you are asked).");
            tooltip = sb.ToString();
        }

        /// <summary>Absolute path on disk for a project path or an outside file.</summary>
        public static string Absolute(string path) {
            if (string.IsNullOrEmpty(path)) return path;
            if (Path.IsPathRooted(path) && !path.StartsWith("Assets")) return path;
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        }
    }
}
