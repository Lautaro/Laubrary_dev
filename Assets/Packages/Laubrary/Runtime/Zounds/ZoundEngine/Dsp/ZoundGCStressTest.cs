using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Laubrary.Zounds.Dsp {

    // Diagnostic-only: when armed, schedules a full GC collection at roughly the midpoint of
    // each triggered zound's playback (not at the trigger instant - forcing it right at trigger
    // completes before the voice starts rendering, which never overlaps a live voice and so
    // never reproduces the stutter; it just delays the start). See .agenthq/tasks/T-0079.md.
    // Toggle via Tools/Heros Hour 2/Force GC On Zound Trigger.
    public static class ZoundGCStressTest {
        public static bool ForceGCOnTrigger = false;

        static readonly List<double> pendingDueTimes = new List<double>();

        public static void MaybeSchedule(ZoundToken token) {
            if (!ForceGCOnTrigger || token == null) return;
#if UNITY_EDITOR
            double due = EditorApplication.timeSinceStartup + token.duration * 0.5;
            pendingDueTimes.Add(due);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
#endif
        }

#if UNITY_EDITOR
        static void Tick() {
            double now = EditorApplication.timeSinceStartup;
            for (int i = pendingDueTimes.Count - 1; i >= 0; i--) {
                if (now < pendingDueTimes[i]) continue;
                pendingDueTimes.RemoveAt(i);
                System.GC.Collect(System.GC.MaxGeneration, System.GCCollectionMode.Forced, true, true);
                Debug.Log("[GCWatch] Mid-playback forced GC fired.");
            }
            if (pendingDueTimes.Count == 0) EditorApplication.update -= Tick;
        }
#endif
    }
}
