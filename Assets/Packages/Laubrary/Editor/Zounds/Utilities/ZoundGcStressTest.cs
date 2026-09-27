using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds.Dsp;
using Debug = UnityEngine.Debug;

namespace Laubrary.Zounds.EditorTools {

    /// <summary>
    /// The owner's stutter test on demand (T-0448): force a full garbage collection while a sound plays, and report
    /// what the audio side did about it — so "is the audio thread still immune?" is answered by a reading rather than
    /// by waiting for a collection to happen by itself.
    ///
    /// Also the other half of the guard T-0406 asked for: a scan for any component anywhere in the project that uses
    /// the managed per-sample audio callback, since a single one re-attaches the audio thread for the whole
    /// application. It runs whenever scripts load and says nothing unless it finds one.
    /// </summary>
    [InitializeOnLoad]
    public static class ZoundGcStressTest {

        static ZoundGcStressTest() {
            var found = FindManagedAudioCallbacks();
            if (found.Count > 0)
                Debug.LogWarning("[Zounds] " + found.Count + " component type(s) use the managed audio callback (OnAudioFilterRead): "
                                 + string.Join(", ", found) + ". Any one of them in a scene attaches the audio thread to the scripting "
                                 + "runtime for the whole application, so garbage collections can be heard as stutters again.");
        }

        /// <summary>Every loaded component type that declares the managed per-sample audio callback.</summary>
        public static List<string> FindManagedAudioCallbacks() {
            var list = new List<string>();
            const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                                                     | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
            foreach (var t in TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
                if (t.GetMethod("OnAudioFilterRead", any) != null) list.Add(t.FullName);
            return list;
        }

        /// <summary>The last result, for the button's tooltip.</summary>
        public static string lastResult = "Not run yet in this session.";

        public const string Tooltip =
            "Stress test: forces a full garbage collection right now, to check whether collections can be heard. Start a sound "
          + "(a long one, or several), press this while it plays, and listen for a click or gap.\n\n"
          + "Each press also reports how long the collection froze the editor, and whether any of this engine's audio has ever "
          + "run as ordinary managed code in this session — which is what would let a collection interrupt the sound.\n\n"
          + "Only a built Player gives the final answer: the editor runs extra managed work of its own and can hitch for reasons "
          + "a game never would.";

        /// <summary>Forces a full, blocking collection and reports on it. Also logged to the console.</summary>
        public static string Run(bool soundPlaying) {
            long blocksBefore = ZoundAudioThreadGuard.Blocks;
            long managedBefore = ZoundAudioThreadGuard.ManagedBlocks;
            var sw = Stopwatch.StartNew();
            System.GC.Collect(System.GC.MaxGeneration, System.GCCollectionMode.Forced, true, true);
            System.GC.WaitForPendingFinalizers();
            System.GC.Collect(System.GC.MaxGeneration, System.GCCollectionMode.Forced, true, true);
            sw.Stop();
            var callbacks = FindManagedAudioCallbacks();

            string audio = managedBefore == 0
                ? "all " + blocksBefore.ToString("N0") + " audio blocks so far ran as compiled code (the audio thread is not attached)"
                : managedBefore.ToString("N0") + " of " + blocksBefore.ToString("N0") + " audio blocks ran as MANAGED code — the audio thread is attached, so collections can be heard";
            string cb = callbacks.Count == 0 ? "no managed audio callbacks in the project" : "managed audio callbacks found: " + string.Join(", ", callbacks);
            lastResult = "Last press: the collection froze the editor for " + sw.Elapsed.TotalMilliseconds.ToString("0") + " ms"
                         + (soundPlaying ? " while a sound played" : " (nothing was playing)") + "; " + audio + "; " + cb + ".";
            Debug.Log("[Zounds] Forced GC: " + lastResult);
            return lastResult;
        }
    }
}
