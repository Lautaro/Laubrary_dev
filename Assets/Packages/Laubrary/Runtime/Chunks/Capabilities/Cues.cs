using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Chunks
{
    /// What a cue fires when its moment arrives.
    public enum ChunkEventKind
    {
        /// A named hook raised on ChunkTimelineEvents.CodeEvent for game code to react to.
        Code = 0,
        /// A Zound, played by name through ChunkZoundHook.
        Zound = 1,
    }

    /// One moment along a recipe that fires something which is not a visual: a named code hook, or a sound.
    ///
    /// A Zound is stored as a NAME and never as an asset reference because that is how Zounds itself addresses
    /// one, it is what the picker returns, and it is what every other Laubrary reference to a Zound already
    /// stores. Playing goes through ChunkZoundHook so this assembly never references Zounds at all; a project
    /// with no audio tool simply plays no sound.
    [Serializable]
    public class ChunkCue
    {
        [Tooltip("Whether this raises a named code event or plays a Zound.")]
        public ChunkEventKind kind = ChunkEventKind.Code;

        [Min(0f)]
        [Tooltip("Seconds from the START of the recipe at which this fires.")]
        public float time = 0f;

        [Tooltip("The hook name raised for game code. This is where the name is DECLARED, so it is typed here " +
                 "and picked everywhere else.")]
        public string codeName = "";

        [Tooltip("Which Zound to play. Picked, never typed — a misspelt name fails silently at runtime.")]
        public string zoundName = "";

        /// True when this names nothing yet, so firing it would do nothing.
        public bool IsEmpty => kind == ChunkEventKind.Zound
            ? string.IsNullOrEmpty(zoundName)
            : string.IsNullOrEmpty(codeName);

        /// What the cue is called on screen — its own name, or a placeholder while it has none.
        public string DisplayName
        {
            get
            {
                string n = kind == ChunkEventKind.Zound ? zoundName : codeName;
                return string.IsNullOrEmpty(n) ? (kind == ChunkEventKind.Zound ? "(no Zound)" : "(unnamed)") : n;
            }
        }
    }

    /// The recipe's non-visual moments: sounds and named code hooks, each on the same clock as everything else.
    ///
    /// A COORDINATOR that occupies time only through its own markers — it has no delay of its own, because a
    /// cue already says exactly when it happens. That is why cue times feed the clock directly (see
    /// <see cref="ChunkClock"/>) rather than through this capability's duration.
    [System.Serializable]
    public class Cues : ChunkCapability
    {
        public override string KindName => "Cues";

        /// The capability itself has no moment; each cue has its own.
        public override bool OccupiesTime => false;

        [Tooltip("The moments this recipe fires a sound or a named code event at.")]
        public List<ChunkCue> cues = new List<ChunkCue>();

        public int CueCount => cues != null ? cues.Count : 0;

        /// The last instant this schedules anything. 0 when there is nothing to fire.
        public float LatestCueTime
        {
            get
            {
                float t = 0f;
                if (cues != null)
                    for (int i = 0; i < cues.Count; i++)
                        if (cues[i] != null) t = Mathf.Max(t, Mathf.Max(0f, cues[i].time));
                return t;
            }
        }

        public override void Fire(in ChunkModuleContext ctx)
        {
            // `in` parameters cannot be captured by a coroutine or a lambda — copy out first.
            Schedule(ctx.Origin, ctx.Container, ctx.Runner);
        }

        /// The one place cues are dispatched. A cue at or before 0 fires on the spot; a later one is waited out
        /// on the burst's own runner, so the cues die with the burst exactly as its visuals do. Safe with a
        /// null runner and a null container — in edit mode with neither, only the cues at 0 fire.
        public void Schedule(Vector3 origin, Transform container, ChunkModuleRunner runner)
        {
            if (!enabled || cues == null || cues.Count == 0) return;

            for (int i = 0; i < cues.Count; i++)
            {
                var cue = cues[i];
                if (cue == null || cue.IsEmpty) continue;   // an unnamed cue fires nothing, silently
                if (cue.time <= 0f) { Raise(cue, origin); continue; }
                if (runner == null) continue;               // no host to wait on
                runner.StartCoroutine(FireAfter(cue, cue.time, origin, container, container != null));
            }
        }

        static IEnumerator FireAfter(ChunkCue cue, float delay, Vector3 origin, Transform container,
                                     bool requireContainer)
        {
            yield return new WaitForSeconds(delay);
            // A burst whose container died (a short life, a scene change) must not still be making noise for
            // something that is no longer on screen.
            if (requireContainer && container == null) yield break;
            Raise(cue, origin);
        }

        static void Raise(ChunkCue cue, Vector3 origin)
        {
            if (cue == null) return;
            if (cue.kind == ChunkEventKind.Zound)
            {
                // A null hook is a project with no audio tool. Silence is correct there, not an error.
                if (!string.IsNullOrEmpty(cue.zoundName)) ChunkZoundHook.Play?.Invoke(cue.zoundName);
                return;
            }
            if (!string.IsNullOrEmpty(cue.codeName)) ChunkTimelineEvents.Raise(cue.codeName, origin);
        }
    }
}
