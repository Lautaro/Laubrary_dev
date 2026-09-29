using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zounds {

    /// <summary>
    /// One running list of every lookup that game code made and that found nothing: a Zound name that does not exist, a
    /// ZPOC id no part of a play declares, a track number past the last track, a snapshot name nobody has. Each distinct
    /// problem is ONE row, however often it happens, with a count and when it was first and last seen — so a call made
    /// every frame produces one row and one console warning, not thousands. Nothing here ever throws: a miss is reported
    /// and the call does nothing, which is the same promise the engine has always made for a missing Zound.
    ///
    /// Reporting a problem already on the list costs a dictionary lookup on a key made of values the caller already had,
    /// and allocates nothing. Main thread only.
    /// </summary>
    public static class ZoundDiagnostics {

        public enum Kind {
            /// <summary>A Zound was requested by name and no Zound has it.</summary>
            MissingZound = 0,
            /// <summary>A ZPOC id was sent to a play whose Zound, and everything it plays, declares no such id.</summary>
            MissingZpoc = 1,
            /// <summary>A track number past the last track of the Zound it was asked of.</summary>
            MissingTrack = 2,
            /// <summary>A snapshot name no Zound in a play has.</summary>
            MissingSnapshot = 3,
            /// <summary>A project-wide ZPOC value was set for an id no Zound in the project declares.</summary>
            UndeclaredGlobalZpoc = 4,
        }

        public sealed class Entry {
            public Kind kind;
            /// <summary>The Zound the request was made of, or empty for a project-wide one.</summary>
            public string zound;
            /// <summary>What was asked for: the name, id, track number or snapshot name, as the caller gave it.</summary>
            public string detail;
            /// <summary>What was asked for, spelled the way the caller first spelled it (<see cref="detail"/> may be the
            /// matching key, which is lowercased and stripped of spaces).</summary>
            public string shown;
            public string message;
            public int count;
            /// <summary>Unscaled real time since startup, in seconds.</summary>
            public float firstSeen, lastSeen;
        }

        static readonly Dictionary<(Kind, string, string), Entry> byKey = new Dictionary<(Kind, string, string), Entry>();
        static readonly List<Entry> ordered = new List<Entry>();

        /// <summary>Every problem seen, oldest first.</summary>
        public static IReadOnlyList<Entry> Entries => ordered;

        /// <summary>Bumped whenever a row is added, counted or cleared, so a display can tell when to redraw.</summary>
        public static int Revision { get; private set; }

        /// <summary>
        /// Records one occurrence. The first occurrence of a problem logs <paramref name="message"/> once as a warning;
        /// later ones only count. <paramref name="message"/> is only read the first time, so a caller building it on the
        /// fly should check <see cref="IsKnown"/> first if that matters.
        /// </summary>
        public static void Report(Kind kind, string zound, string detail, string message, string shown = null) {
            zound = zound ?? ""; detail = detail ?? "";
            float now = Time.realtimeSinceStartup;
            if (byKey.TryGetValue((kind, zound, detail), out var e)) {
                e.count++; e.lastSeen = now; Revision++;
                return;
            }
            e = new Entry { kind = kind, zound = zound, detail = detail, shown = shown ?? detail, message = message, count = 1, firstSeen = now, lastSeen = now };
            byKey.Add((kind, zound, detail), e);
            ordered.Add(e);
            Revision++;
            Debug.LogWarning("[Zounds] " + message);
        }

        public static bool IsKnown(Kind kind, string zound, string detail)
            => byKey.ContainsKey((kind, zound ?? "", detail ?? ""));

        /// <summary>Counts one more occurrence of a problem already on the list; false when it is not on it yet.</summary>
        public static bool Count(Kind kind, string zound, string detail) {
            if (!byKey.TryGetValue((kind, zound ?? "", detail ?? ""), out var e)) return false;
            e.count++; e.lastSeen = Time.realtimeSinceStartup; Revision++;
            return true;
        }

        public static void Clear() {
            byKey.Clear(); ordered.Clear(); Revision++;
        }

        public static void Remove(Entry e) {
            if (e == null) return;
            byKey.Remove((e.kind, e.zound, e.detail));
            ordered.Remove(e);
            Revision++;
        }
    }
}
