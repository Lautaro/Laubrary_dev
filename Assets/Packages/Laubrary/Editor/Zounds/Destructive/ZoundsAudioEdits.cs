using System;
using System.Collections.Generic;
using System.IO;
using Laubrary.Zounds.Dsp;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds.Destructive {

    /// <summary>
    /// Cut, copy, paste, insert, duplicate-insert and duplicate-paste on a sound's audio file, from the Klip editor
    /// (destructive editing, 2026-10-09; the owner's design).
    ///
    /// <b>Where the edit lands.</b> A file Zounds made for this one sound is edited in place; its earlier versions are kept
    /// and Undo brings them back (<see cref="ZoundsFileHistory"/>). A protected file (<see cref="ZoundsProtection"/>) is
    /// never rewritten: the edited audio goes to a new file under the work folder's <c>Edits</c> folder, with the original's
    /// import settings, and the sound plays that from then on. A shared sound is first switched to a copy
    /// (<see cref="ZoundsEditGuard"/>), so the sounds that use it keep the original. Everything is one Undo step: the copy,
    /// the new file (removed again on undo) or the file's new content, and the ripple.
    ///
    /// <b>What the verbs do</b>, all at the edit cursor or on the selection, in seconds of the file:
    /// Cut -- removes the selection (later audio moves up) and keeps it to paste. Delete -- removes it the same way
    /// without keeping it (what is kept stays as it was). Copy -- keeps the selection to paste.
    /// Paste -- writes what was kept over the audio from the cursor on (nothing moves; the file grows only past its end).
    /// Insert -- puts what was kept in at the cursor (later audio moves on). Duplicate insert -- puts a copy of the
    /// selection in right after it. Duplicate paste -- writes a copy of the selection over the audio right after it.
    /// After an insert or a paste the cursor sits at the end of the new audio, and the new audio is selected, so
    /// duplicate-insert pressed again repeats it again.
    ///
    /// <b>Kept audio</b> lives in memory only, in its own format; pasting it into a file of another format converts it by
    /// the rule in <see cref="AudioPcm.ConvertedTo"/>.
    ///
    /// <b>Everything that points at seconds of the file follows the edit</b> (<see cref="AudioRipple"/>): the sound's
    /// trim, its curves on the waveform, a time-stretch region, and every Zequence track's own excerpt of it.
    /// </summary>
    internal static class ZoundsAudioEdits {

        public enum Verb { Cut, Copy, Paste, Insert, DuplicateInsert, DuplicatePaste, Delete }

        static AudioPcm clipboard;
        static string clipboardFrom;

        public static bool HasClipboard => clipboard != null && clipboard.Frames > 0;
        public static double ClipboardSeconds => clipboard != null ? clipboard.Seconds : 0d;
        public static string ClipboardFrom => clipboardFrom;
        /// <summary>For the kept check: what is kept, so it can be put back as it was.</summary>
        internal static (AudioPcm audio, string from) Clipboard { get => (clipboard, clipboardFrom); set { clipboard = value.audio; clipboardFrom = value.from; } }

        /// <summary>Raised after a file's audio changed on disk (an edit, or an undo putting a version back): its project path.</summary>
        public static event Action<string> changed;

        public struct Request {
            public Verb verb;
            public double cursor, selA, selB;
            public bool HasSelection => selB > selA + 1e-9;
        }

        public sealed class Result {
            public bool done;
            /// <summary>One line for the editor's readout.</summary>
            public string message;
            /// <summary>Set (in the "Tell me" mode) when a copy was made: the notice to show, and how to edit the original instead.</summary>
            public string notice;
            public Action editOriginal;
            public ZoundsEditGuard.Swap swap;
            public double cursor, selA, selB;
        }

        static Result Fail(string why) => new Result { done = false, message = why };

        public static string Seconds(double s) => s.ToString(s < 10d ? "0.000" : "0.00", System.Globalization.CultureInfo.InvariantCulture) + " s";

        /// <summary>Reads the sound's file as audio (exact bytes for a WAV file; Unity's decoding for anything else).</summary>
        static AudioPcm Load(Klip k, in ZoundsProtection.FileInfo f, out string error) {
            error = null;
            if (f.kind == ZoundsProtection.FileKind.None) { error = "This sound has no audio file."; return null; }
            if (f.IsWav) return AudioPcm.ReadWav(ZoundsProtection.Absolute(f.path), out error);
            AudioClip clip = null;
#if ADDRESSABLES_INSTALLED
            try { clip = k.audioClipRef != null ? k.audioClipRef.editorAsset as AudioClip : null; } catch { }
#endif
            var pcm = AudioPcm.FromClip(clip);
            if (pcm == null) error = "Unity could not decode '" + f.Name + "'. Set its Load Type to Decompress On Load and try again.";
            return pcm;
        }

        /// <summary>Applies <paramref name="r"/> to <paramref name="k"/>'s audio. Never throws; the result says what happened.</summary>
        public static Result Apply(Klip k, Request r) {
            try { return ApplyInner(k, r); }
            catch (Exception e) { Debug.LogException(e); return Fail("The edit failed: " + e.Message); }
        }

        static Result ApplyInner(Klip k, Request r) {
            if (k == null) return Fail("No sound.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) return Fail("Stop Play mode to edit audio files.");
            var file = ZoundsProtection.FileOf(k);
            var pcm = Load(k, file, out string error);
            if (pcm == null) return Fail(error);

            int a = pcm.FrameAt(r.selA), b = pcm.FrameAt(r.selB), c = pcm.FrameAt(r.cursor);
            bool sel = r.HasSelection && b > a;
            AudioPcm edited; AudioSpan span; string what; int newCursor, newA, newB;
            switch (r.verb) {
                case Verb.Copy:
                    if (!sel) return Fail("Select some audio to copy.");
                    clipboard = pcm.Slice(a, b); clipboardFrom = k.name;
                    return new Result { done = true, message = "Copied " + Seconds(clipboard.Seconds) + ".", cursor = r.cursor, selA = r.selA, selB = r.selB };
                case Verb.Cut:
                case Verb.Delete: {
                    bool keep = r.verb == Verb.Cut;
                    if (!sel) return Fail(keep ? "Select some audio to cut." : "Select some audio to delete.");
                    if (b - a >= pcm.Frames) return Fail((keep ? "Cutting" : "Deleting") + " all of it would leave no audio; select less.");
                    var cut = pcm.Slice(a, b);
                    edited = pcm.Spliced(a, b - a, null);
                    span = new AudioSpan { at = (double)a / pcm.rate, removed = (double)(b - a) / pcm.rate, inserted = 0d };
                    what = (keep ? "Cut " : "Deleted ") + Seconds(cut.Seconds);
                    newCursor = a; newA = newB = a;
                    if (keep) { clipboard = cut; clipboardFrom = k.name; }
                    break;
                }
                case Verb.Paste:
                case Verb.Insert: {
                    if (!HasClipboard) return Fail("Nothing to paste yet: cut or copy some audio first.");
                    var ins = clipboard.ConvertedTo(pcm);
                    bool insert = r.verb == Verb.Insert;
                    edited = insert ? pcm.Spliced(c, 0, ins) : pcm.Overwritten(c, ins);
                    span = insert ? new AudioSpan { at = (double)c / pcm.rate, removed = 0d, inserted = ins.Seconds }
                                  : new AudioSpan { at = (double)c / pcm.rate, removed = (double)Math.Min(ins.Frames, pcm.Frames - c) / pcm.rate, inserted = ins.Seconds, overwrite = true };
                    what = (insert ? "Inserted " : "Pasted ") + Seconds(ins.Seconds) + (clipboard.SameFormat(pcm) ? "" : " (converted from " + clipboard.Describe() + ")");
                    newCursor = c + ins.Frames; newA = c; newB = c + ins.Frames;
                    break;
                }
                case Verb.DuplicateInsert:
                case Verb.DuplicatePaste: {
                    if (!sel) return Fail("Select the audio to duplicate.");
                    var dup = pcm.Slice(a, b);
                    bool insert = r.verb == Verb.DuplicateInsert;
                    edited = insert ? pcm.Spliced(b, 0, dup) : pcm.Overwritten(b, dup);
                    span = insert ? new AudioSpan { at = (double)b / pcm.rate, removed = 0d, inserted = dup.Seconds }
                                  : new AudioSpan { at = (double)b / pcm.rate, removed = (double)Math.Min(dup.Frames, pcm.Frames - b) / pcm.rate, inserted = dup.Seconds, overwrite = true };
                    what = (insert ? "Repeated " : "Repeated over the next ") + Seconds(dup.Seconds);
                    newCursor = b + dup.Frames; newA = b; newB = b + dup.Frames;
                    break;
                }
                default: return Fail("Unknown edit.");
            }
            span.oldLength = pcm.Seconds; span.newLength = edited.Seconds;

            // ── who may be changed: the sound (a shared one goes to a copy first), then the file ──
            if (!ZoundsEditGuard.BeforeSoundEdit(k, out var swap)) return Fail("Cancelled.");
            if (swap.swapped) file = ZoundsProtection.FileOf(k);
            bool inPlace = !file.Protected && file.IsWav;
            if (file.Protected && !swap.swapped && !(EditsOriginalFile(file))) {
                var choice = ZoundsEditGuard.Ask("'" + file.Name + "' is " + file.Reason,
                    file.IsTrueSource
                        ? "Zounds never overwrites it. The edited audio goes to a new file, and '" + k.name + "' plays that one instead."
                        : "Changing it would change every sound that plays it. Make a copy: the edited audio goes to a new file that only '" + k.name + "' plays.",
                    file.IsTrueSource ? "Make the copy" : "Make a copy", !file.IsTrueSource && file.IsWav);
                if (choice == ZoundsEditGuard.Choice.Cancel) return Fail("Cancelled.");
                if (choice == ZoundsEditGuard.Choice.Original) { inPlace = true; editOriginalFiles.Add(file.guid); }
            }
            else if (EditsOriginalFile(file) && file.IsWav && !swap.swapped) inPlace = true;

            string newPath = null;
            var users = inPlace ? file.users : new List<Klip> { k };
            var rate = edited.rate;
            var spanCopy = span;
            ZoundsWindow.ModifyAndSaveZoundsProject(what.ToLowerInvariant(), () => {
                AudioRipple.Prepare(users);
                if (inPlace) {
                    ZoundsFileHistory.Instance.Rewrite(file.path, edited.ToWav(), what);
                    foreach (var u in users) ForgetOutputCopy(u);
                }
                else {
                    newPath = WriteNewFile(k, file, edited);
                    ZoundsFileHistory.Instance.Created(newPath, what);
                    Repoint(k, newPath);
                }
                AudioRipple.Apply(users, spanCopy, rate);
            });

            var res = new Result {
                done = true,
                swap = swap,
                cursor = (double)newCursor / rate, selA = (double)newA / rate, selB = (double)newB / rate,
            };
            if (r.verb == Verb.Cut || r.verb == Verb.Delete) res.selA = res.selB = res.cursor;
            res.message = what + "." + (inPlace ? " The previous version is kept: Undo brings it back." : " Written to a new file, '" + Path.GetFileName(newPath) + "'.");
            bool notify = ZoundsEditGuard.Mode == ZoundsProject.ProjectSettings.ProtectedEditPrompt.Notice;
            if (notify && (swap.swapped || !inPlace)) {
                string s = "";
                if (swap.swapped) s += "'" + swap.originalName + "' is " + swap.usedIn + ", so this edit went to a copy, '" + swap.copyName + "'. ";
                if (!inPlace) s += "'" + file.Name + "' is " + (file.Reason ?? "protected") + ", so the edited audio is in a new file, '" + Path.GetFileName(newPath) + "'.";
                res.notice = s.Trim();
                if (swap.swapped) { var sw = swap; res.editOriginal = () => ZoundsEditGuard.SwapBack(k, sw.originalId); }
            }
            return res;
        }

        // "Edit the original file" chosen in the Ask-first dialog: remembered per file until a script reload.
        static readonly HashSet<string> editOriginalFiles = new HashSet<string>();
        static bool EditsOriginalFile(in ZoundsProtection.FileInfo f) => !f.IsTrueSource && f.guid != null && editOriginalFiles.Contains(f.guid);

        /// <summary>
        /// A sound whose shipped copy (in the ZoundFiles folder, made by the save) holds the old audio: forget it and any old
        /// render, keeping their paths, so the next save makes them afresh from the edited file. (Playback reads the file
        /// itself; the shipped copy is only the fallback for a build that cannot load it.)
        /// </summary>
        static void ForgetOutputCopy(Klip u) {
#if ADDRESSABLES_INSTALLED
            string src = u.audioClipRef != null ? AssetDatabase.GUIDToAssetPath(u.audioClipRef.AssetGUID) : null;
            if (!string.IsNullOrEmpty(u.outputClipPath) && u.outputClipPath != src) u.outputClipRef = null;
            if (!string.IsNullOrEmpty(u.renderedClipPath) && u.renderedClipPath != src) u.renderedClipRef = null;
#endif
        }

        static string Sanitize(string name) {
            foreach (char ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            return name.Replace('[', '(').Replace(']', ')').Replace('*', '_').Trim();
        }

        /// <summary>The edited audio as a new file in the work folder's Edits folder, imported the way the original was.</summary>
        static string WriteNewFile(Klip k, in ZoundsProtection.FileInfo from, AudioPcm audio) {
            var settings = ZoundsProject.Instance.projectSettings;
            string folder = settings.workFolderPath + "/Edits";
            ZoundsProject.EnsureDirectoryExists(folder);
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Sanitize(string.IsNullOrEmpty(k.name) ? "Edit" : k.name) + ".wav");
            File.WriteAllBytes(ZoundsProtection.Absolute(path), audio.ToWav());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var dst = AssetImporter.GetAtPath(path) as AudioImporter;
            var src = from.kind != ZoundsProtection.FileKind.External && !string.IsNullOrEmpty(from.path) ? AssetImporter.GetAtPath(from.path) as AudioImporter : null;
            if (dst != null) {
                var s = src != null ? src.defaultSampleSettings : dst.defaultSampleSettings;
                if (src == null) s.compressionFormat = AudioCompressionFormat.PCM;
                // The engine reads a sound's samples, which Unity only exposes for Decompress On Load.
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                dst.defaultSampleSettings = s;
                if (src != null) { dst.forceToMono = src.forceToMono; dst.loadInBackground = src.loadInBackground; dst.ambisonic = src.ambisonic; }
                dst.SaveAndReimport();
            }
#if ADDRESSABLES_INSTALLED
            KlipEditorWindow.EnsureClipAddressable(path);
#endif
            Invalidate(path);
            return path;
        }

        /// <summary>Points a sound at a project audio file (its source), forgetting the shipped copy of the old one.</summary>
        internal static void Repoint(Klip k, string path) {
#if ADDRESSABLES_INSTALLED
            k.audioClipRef = new UnityEngine.AddressableAssets.AssetReference(AssetDatabase.AssetPathToGUID(path));
#endif
            ForgetOutputCopy(k);   // the shipped copy, if any, was of the old file; the next save makes a fresh one
            k.audioClipPath = path;
            k.externalSourcePath = string.Empty;
            AudioWaveformUtility.ClearCache(k);
            ZoundDspPlayback.InvalidateLayout(k);
        }

        /// <summary>Takes a file that is going away out of the Addressables groups (deleting the file does not).</summary>
        internal static void ForgetAddressable(string guid) {
#if ADDRESSABLES_INSTALLED
            var s = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            if (s != null && !string.IsNullOrEmpty(guid) && s.FindAssetEntry(guid) != null) s.RemoveAssetEntry(guid);
#endif
        }

        /// <summary>Drops everything held in memory about a file's samples (after it changed on disk), and tells the editors.</summary>
        public static void Invalidate(string projectPath) {
            var clip = string.IsNullOrEmpty(projectPath) ? null : AssetDatabase.LoadAssetAtPath<AudioClip>(projectPath);
            if (clip != null) ZoundPcmCache.Remove(clip);
            ZoundTimeStretcher.Clear();
            AudioWaveformUtility.ClearCache();
            ZoundDspPlayback.InvalidateLayouts();
            try { changed?.Invoke(projectPath); } catch (Exception e) { Debug.LogException(e); }
        }
    }
}
