using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
#if ADDRESSABLES_INSTALLED
using UnityEngine.AddressableAssets;
#endif

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// One thing the picker can list: an audio clip in the workspace (for a new Klip, or a Klip's source), or a sound of the
    /// library (for a Zequence track). Everything the list, the filters and the details pane show is read once into this
    /// record, so a list of thousands costs no asset lookups while it is scrolled or searched.
    /// </summary>
    public sealed class ZoundPickerItem {
        public enum Kind { Clip, Klip, Zequence }

        /// <summary>Stable across sessions: the clip's asset GUID, or "zound:" + the sound's id. Recents and favourites key on it.</summary>
        public string key;
        public string name;
        public Kind kind;
        /// <summary>Where it lives, as the old menu showed it ("" for the library root, "Sources/", "Sources/Steps/"); "Klips" / "Zequences" for sounds.</summary>
        public string folder = "";
        public string[] tags = Array.Empty<string>();
        public float lengthSeconds;
        public int channels, frequency;
        public string assetPath = "";
        public AudioClip clip;
        public Zound zound;
        /// <summary>Names of the sounds that play this clip, or the Zequences that play this sound.</summary>
        public List<string> usedBy = new List<string>();
        /// <summary>The clip cannot be loaded on this machine (an external file that is not here, a broken reference).</summary>
        public bool missing;
#if ADDRESSABLES_INSTALLED
        public AssetReferenceT<AudioClip> audioRef;
#endif
        /// <summary>When the owner last picked it here (ticks), from the picker's own memory; 0 when never.</summary>
        public long lastPicked;

        string search;
        /// <summary>Everything a search can match, lower-cased, built once.</summary>
        public string SearchText {
            get {
                if (search == null) {
                    var sb = new System.Text.StringBuilder(name.Length + 48);
                    sb.Append(name).Append(' ').Append(folder).Append(' ');
                    foreach (var t in tags) sb.Append(t).Append(' ');
                    if (!string.IsNullOrEmpty(assetPath)) sb.Append(Path.GetFileName(assetPath));
                    search = sb.ToString().ToLowerInvariant();
                }
                return search;
            }
        }

        public string KindLabel => kind == Kind.Clip ? "Clip" : kind == Kind.Klip ? "Klip" : "Zeq";

        public string LengthText => lengthSeconds <= 0f ? "–" : lengthSeconds < 10f ? lengthSeconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s"
                                   : lengthSeconds < 60f ? lengthSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s"
                                   : Mathf.FloorToInt(lengthSeconds / 60f) + ":" + Mathf.FloorToInt(lengthSeconds % 60f).ToString("00");

        public string FormatText => frequency <= 0 ? "" : (channels == 1 ? "mono" : channels == 2 ? "stereo" : channels + " ch") + " · " + (frequency / 1000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " kHz";
    }

    /// <summary>What a call site asks the picker for: the listing, how many may be picked, what to do with them, and the
    /// actions that belong beside the list (import, external file, empty placeholder).</summary>
    public sealed class ZoundPickerRequest {
        public enum Listing { Clips, Zounds }

        public string title = "Pick";
        public Listing listing;
        public List<ZoundPickerItem> items = new List<ZoundPickerItem>();
        /// <summary>Several may be picked at once (a Zequence takes many tracks; the browser takes many new Klips).</summary>
        public bool multi = true;
        /// <summary>The verb on the confirm button: "Add", "Use".</summary>
        public string pickLabel = "Add";
        public Action<List<ZoundPickerItem>> onPick;
        /// <summary>Extra actions along the bottom (label, tooltip, what it does). The picker closes before one runs
        /// when <c>closes</c> is set, so a modal file dialog never opens under it.</summary>
        public List<(string label, string tooltip, Action run, bool closes)> actions = new List<(string, string, Action, bool)>();
        /// <summary>The editor window auditions are owned by (its Play buttons stop ours and vice versa); null uses the picker.</summary>
        public EditorWindow previewOwner;
        /// <summary>Which remembered state (filters, sort, view, recents, favourites) this listing shares: "clips" or "zounds".</summary>
        public string stateKey = "clips";
        /// <summary>Items with these keys are shown first and ticked ("already in this Zequence"); null for none.</summary>
        public HashSet<string> alreadyPicked;
    }

    /// <summary>Builds the picker's listings from the project. Shared by every call site so each lists the same things the same way.</summary>
    internal static class ZoundPickerCatalog {

        /// <summary>Every audio clip of the workspace (library, work and sources folders), with who plays it.</summary>
        public static List<ZoundPickerItem> Clips() {
            var items = new List<ZoundPickerItem>();
#if ADDRESSABLES_INSTALLED
            if (ZoundsProject.Instance == null) return items;
            AudioAssetUtility.FindAllAudioReferencesInWorkspace(out var libraryRefs, out var workRefs, out var sourcesRefs, out _);
            var users = ClipUsers();
            var seen = new HashSet<string>();
            void Add(List<AssetReferenceT<AudioClip>> refs, string parentFolder) {
                if (refs == null) return;
                foreach (var r in refs) {
                    if (r == null) continue;
                    var clip = r.editorAsset;
                    if (clip == null) continue;
                    string path = AssetDatabase.GetAssetPath(clip);
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    if (string.IsNullOrEmpty(guid) || !seen.Add(guid)) continue;
                    var it = new ZoundPickerItem {
                        key = guid, name = clip.name, kind = ZoundPickerItem.Kind.Clip, folder = FolderOf(path, parentFolder),
                        lengthSeconds = clip.length, channels = clip.channels, frequency = clip.frequency, assetPath = path, clip = clip, audioRef = r,
                    };
                    if (users.TryGetValue(guid, out var names)) it.usedBy = names;
                    items.Add(it);
                }
            }
            Add(libraryRefs, "");
            Add(workRefs, "");
            Add(sourcesRefs, "Sources/");
#endif
            return items;
        }

        /// <summary>The sounds of the library, filtered by <paramref name="include"/> (null for all), with the Zequences that play each.</summary>
        public static List<ZoundPickerItem> Zounds(Predicate<Zound> include = null) {
            var items = new List<ZoundPickerItem>();
            if (ZoundsProject.Instance == null) return items;
            var lib = ZoundsProject.Instance.zoundLibrary;
            var users = ZoundUsers(lib);
            foreach (var z in lib.GetAllZounds()) {
                if (z == null || z is ClipZound) continue;
                if (include != null && !include(z)) continue;
                var it = new ZoundPickerItem { key = "zound:" + z.id, name = z.name, zound = z, tags = TagNames(z) };
                if (z is Klip k) {
                    it.kind = ZoundPickerItem.Kind.Klip; it.folder = "Klips";
                    FillFromKlip(it, k);
                }
                else if (z is Zequence q) {
                    it.kind = ZoundPickerItem.Kind.Zequence; it.folder = "Zequences";
                    try { it.lengthSeconds = CompositeZoundEditing.CalculateCompositeDuration(q, 1f); } catch { it.lengthSeconds = 0f; }
                }
                if (users.TryGetValue(z.id, out var names)) it.usedBy = names;
                items.Add(it);
            }
            return items;
        }

        static void FillFromKlip(ZoundPickerItem it, Klip k) {
            AudioClip clip = null;
#if ADDRESSABLES_INSTALLED
            try { if (k.audioClipRef != null && k.audioClipRef.RuntimeKeyIsValid()) clip = k.audioClipRef.editorAsset as AudioClip; } catch { clip = null; }
#endif
            bool external = !string.IsNullOrEmpty(k.externalSourcePath);
            if (clip != null) {
                it.clip = clip; it.channels = clip.channels; it.frequency = clip.frequency;
                it.assetPath = AssetDatabase.GetAssetPath(clip);
                it.lengthSeconds = k.trimEnabled && k.trimEnd > k.trimStart ? k.trimEnd - k.trimStart : clip.length;
            }
            else if (external) {
                it.assetPath = k.externalSourcePath;
                it.lengthSeconds = k.trimEnd > k.trimStart ? k.trimEnd - k.trimStart : 0f;
                it.missing = !File.Exists(k.externalSourcePath);
            }
            else it.missing = true;
        }

        static string[] TagNames(Zound z) {
            if (z.tags == null || z.tags.Count == 0) return Array.Empty<string>();
            var all = ZoundsProject.Instance.zoundLibrary.tags;
            var names = new List<string>(z.tags.Count);
            foreach (int id in z.tags) { var t = all.Find(x => x.id == id); if (t != null && !string.IsNullOrEmpty(t.name)) names.Add(t.name); }
            return names.ToArray();
        }

        /// <summary>The folder shown for a clip: relative to the library root, or "Sources/" plus its subfolder, as the old menu listed it.</summary>
        static string FolderOf(string assetPath, string parentFolder) {
            var ps = ZoundsProject.Instance.projectSettings;
            string p = assetPath.Replace("\\", "/");
            string Sub(string root) {
                string s = p.Substring(root.Length).TrimStart('/');
                int slash = s.LastIndexOf('/');
                return slash < 0 ? "" : s.Substring(0, slash + 1);
            }
            if (!string.IsNullOrEmpty(ps.libraryFolderPath) && p.StartsWith(ps.libraryFolderPath)) return Sub(ps.libraryFolderPath);
            if (!string.IsNullOrEmpty(ps.sourcesFolderPath) && p.StartsWith(ps.sourcesFolderPath)) return "Sources/" + Sub(ps.sourcesFolderPath);
            if (!string.IsNullOrEmpty(ps.workFolderPath) && p.StartsWith(ps.workFolderPath)) return "Work/" + Sub(ps.workFolderPath);
            return parentFolder ?? "";
        }

        /// <summary>Clip GUID → the names of every Klip (library or local to a Zequence) that plays it.</summary>
        static Dictionary<string, List<string>> ClipUsers() {
            var d = new Dictionary<string, List<string>>();
#if ADDRESSABLES_INSTALLED
            var lib = ZoundsProject.Instance.zoundLibrary;
            void Note(Klip k, string shownAs) {
                if (k?.audioClipRef == null) return;
                string guid = k.audioClipRef.AssetGUID;
                if (string.IsNullOrEmpty(guid)) return;
                if (!d.TryGetValue(guid, out var l)) d[guid] = l = new List<string>();
                if (!l.Contains(shownAs)) l.Add(shownAs);
            }
            foreach (var k in lib.klips) Note(k, k.name);
            void Walk(CompositeZound c) {
                if (c == null) return;
                foreach (var k in c.localKlips) Note(k, c.name + " › " + k.name);
                foreach (var lz in c.localZequences) Walk(lz?.zequence);
            }
            foreach (var q in lib.zequences) Walk(q);
#endif
            return d;
        }

        /// <summary>Sound id → the names of every Zequence whose tracks play it (shared entries only).</summary>
        static Dictionary<int, List<string>> ZoundUsers(ZoundLibrary lib) {
            var d = new Dictionary<int, List<string>>();
            void Walk(CompositeZound c, string shownAs) {
                if (c == null) return;
                foreach (var e in c.zoundEntries) {
                    if (e == null || e.local) continue;
                    if (!d.TryGetValue(e.zoundId, out var l)) d[e.zoundId] = l = new List<string>();
                    if (!l.Contains(shownAs)) l.Add(shownAs);
                }
                foreach (var lz in c.localZequences) Walk(lz?.zequence, shownAs + " › " + (lz?.zequence?.name ?? "?"));
            }
            foreach (var q in lib.zequences) Walk(q, q.name);
            return d;
        }
    }

    /// <summary>
    /// Search as you type, forgiving: a query matches when its characters appear in order ("ftstp" finds "footstep"), and it
    /// ranks a whole-word or prefix hit above a scattered one, a hit in the name above one in the folder or tags. Several
    /// words in a query must each match.
    /// </summary>
    internal static class ZoundPickerFuzzy {

        /// <summary>A score above zero when <paramref name="query"/> (lower-case) matches <paramref name="item"/>; higher is better.</summary>
        public static int Score(string query, ZoundPickerItem item) {
            if (string.IsNullOrEmpty(query)) return 1;
            int total = 0;
            int start = 0;
            while (start < query.Length) {
                int sp = query.IndexOf(' ', start);
                int end = sp < 0 ? query.Length : sp;
                if (end > start) {
                    int s = ScoreWord(query, start, end, item);
                    if (s <= 0) return 0;
                    total += s;
                }
                start = end + 1;
            }
            return total;
        }

        static int ScoreWord(string q, int from, int to, ZoundPickerItem item) {
            string name = item.name;
            int best = 0;
            // Exact substring in the name wins; at a word start more so.
            int idx = name.IndexOf(q.Substring(from, to - from), StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) best = 1000 - idx + (idx == 0 || !char.IsLetterOrDigit(name[idx - 1]) ? 300 : 0);
            else {
                int sub = Subsequence(q, from, to, name.ToLowerInvariant());
                if (sub > 0) best = 400 + sub;
            }
            // Then the folder, tags and file name.
            string all = item.SearchText;
            int idx2 = all.IndexOf(q.Substring(from, to - from), StringComparison.Ordinal);
            if (idx2 >= 0) best = Math.Max(best, 200);
            else if (best == 0) { int sub = Subsequence(q, from, to, all); if (sub > 0) best = 50 + sub / 4; }
            return best;
        }

        /// <summary>The characters of q[from..to) in order inside text: 0 when not, else a score favouring runs and word starts.</summary>
        static int Subsequence(string q, int from, int to, string text) {
            int qi = from, score = 0, last = -2;
            for (int i = 0; i < text.Length && qi < to; i++) {
                if (text[i] != q[qi]) continue;
                score += i == last + 1 ? 12 : (i == 0 || !char.IsLetterOrDigit(text[i - 1])) ? 8 : 2;
                last = i; qi++;
            }
            return qi == to ? score : 0;
        }
    }

    /// <summary>
    /// What the picker remembers per machine and listing: the last filters, sort, grouping and view, the window's place, the
    /// favourites and the most recent picks. Kept in EditorPrefs under the project's path, so two projects never share one.
    /// </summary>
    [Serializable]
    internal sealed class ZoundPickerMemory {
        public string search = "";
        public int sort, group, view = 1, kindFilter;
        public bool onlyFavourites, onlyRecent, onlyUnused, auditionOnSelect, detailsShown = true;
        public List<string> favourites = new List<string>();
        public List<string> recentKeys = new List<string>();
        public List<long> recentTicks = new List<long>();
        public Rect window = new Rect(0, 0, 0, 0);

        const int MaxRecent = 40;

        static string Key(string stateKey) => "Zounds.Picker." + stateKey + "." + Application.dataPath.GetHashCode();

        public static ZoundPickerMemory Load(string stateKey) {
            try {
                string json = EditorPrefs.GetString(Key(stateKey), "");
                if (!string.IsNullOrEmpty(json)) { var m = JsonUtility.FromJson<ZoundPickerMemory>(json); if (m != null) return m; }
            }
            catch { }
            return new ZoundPickerMemory();
        }

        public void Save(string stateKey) {
            try { EditorPrefs.SetString(Key(stateKey), JsonUtility.ToJson(this)); } catch { }
        }

        public bool IsFavourite(string key) => favourites.Contains(key);
        public void ToggleFavourite(string key) { if (!favourites.Remove(key)) favourites.Add(key); }

        public long RecentTicks(string key) { int i = recentKeys.IndexOf(key); return i < 0 ? 0 : recentTicks[i]; }

        public void NotePicked(string key) {
            int i = recentKeys.IndexOf(key);
            if (i >= 0) { recentKeys.RemoveAt(i); recentTicks.RemoveAt(i); }
            recentKeys.Insert(0, key); recentTicks.Insert(0, DateTime.Now.Ticks);
            while (recentKeys.Count > MaxRecent) { recentKeys.RemoveAt(recentKeys.Count - 1); recentTicks.RemoveAt(recentTicks.Count - 1); }
        }
    }
}
