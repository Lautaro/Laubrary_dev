// ChunkTimeline — the authoring/playback backbone of a composed Chunks 2.0 effect (AgentHQ T-0038).
//
// Two jobs, deliberately kept in one object because they share one clock:
//   * WHEN each attached module fires, relative to burst-start (the `tracks` list, read through DelayFor —
//     the exact call ChunkModules.Dispatch and ChunkEmitter.ContainerLifetime already make);
//   * WHAT is fired along the way that is not a module (the `markers` list — a Code Event a game subscribes
//     to, and a Zound Event that plays a sound).
//
// The standalone-first rule is enforced by the ZERO defaults: a spec that says nothing about a module gets
// DelayFor == 0, which ChunkModules.Dispatch reads as "fire on the spot" — byte-identical to how a burst
// behaved before timelines existed. The timeline never has to be switched on for anything else to work.
//
// Why a Zound is stored as a NAME and never as an asset reference: that is how Zounds itself addresses one
// (ZoundEngine.PlayZound(name)), it is what the picker returns, and it is what PlayZoundEffect
// (Runtime/ZoetropeZounds) already stores — copying that shape means one Zound reference behaves the same
// everywhere in Laubrary. Playing goes through ChunkZoundHook so this assembly never references Zounds; the
// Runtime/ChunksZounds bridge fills the hook in, and a project with no audio tool simply plays no sound.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Chunks
{
    /// What a timeline marker fires when its moment arrives.
    public enum ChunkEventKind
    {
        /// A free-text hook name raised on ChunkTimelineEvents.CodeEvent for game code to react to.
        Code = 0,
        /// A Zound, played by name through ChunkZoundHook.
        Zound = 1,
    }

    /// One module's scheduled slot: "this module fires `delay` seconds after burst-start".
    ///
    /// `moduleName` is always one of the ChunkModules consts (ChunkModules.Splash / .PyreSpawn / .Formation /
    /// .Fragments) — never a hand-typed string. That is the project's "never type a reference string" rule
    /// applied to Chunks' own internals: the dispatcher looks a module up by the same const the UI wrote, so
    /// a typo cannot silently unschedule something.
    [Serializable]
    public class ChunkTimelineTrack
    {
        [Tooltip("Which module this slot schedules. Always a ChunkModules const, written by the timeline UI.")]
        public string moduleName = "";

        [Min(0f)]
        [Tooltip("Seconds after burst-start at which the module fires. 0 = on the spot.")]
        public float delay = 0f;
    }

    /// One event fired along the burst: a named code hook, or a Zound.
    [Serializable]
    public class ChunkTimelineMarker
    {
        [Tooltip("Whether this marker raises a named code event or plays a Zound.")]
        public ChunkEventKind kind = ChunkEventKind.Code;

        [Min(0f)]
        [Tooltip("Seconds after burst-start at which this marker fires.")]
        public float time = 0f;

        [Tooltip("The hook name raised on ChunkTimelineEvents.CodeEvent. This is where the name is DECLARED, " +
                 "so it is typed here and picked everywhere else.")]
        public string codeName = "";

        [Tooltip("Which Zound to play. Picked, never typed — a misspelt name fails silently at runtime.")]
        public string zoundName = "";

        /// True when this marker names nothing yet, so firing it would be a no-op.
        public bool IsEmpty => kind == ChunkEventKind.Zound
            ? string.IsNullOrEmpty(zoundName)
            : string.IsNullOrEmpty(codeName);

        /// What the marker is called on screen — its own name, or a placeholder while it has none.
        public string DisplayName
        {
            get
            {
                string n = kind == ChunkEventKind.Zound ? zoundName : codeName;
                return string.IsNullOrEmpty(n) ? (kind == ChunkEventKind.Zound ? "(no Zound)" : "(unnamed)") : n;
            }
        }
    }

    /// Schedules when each module of a composed burst fires, and fires the Code/Zound markers along the way.
    /// Off by default and free when off: every accessor short-circuits on <see cref="Enabled"/>.
    [Serializable]
    public class ChunkTimeline
    {
        [Tooltip("Schedule when each module fires relative to burst-start, and fire code/sound events along the way.")]
        public bool enabled = false;

        [Tooltip("One entry per SCHEDULED module. A module with no entry fires immediately, exactly as it did " +
                 "before timelines existed.")]
        public List<ChunkTimelineTrack> tracks = new List<ChunkTimelineTrack>();

        [Tooltip("Code and Zound events fired at their own moments along the burst.")]
        public List<ChunkTimelineMarker> markers = new List<ChunkTimelineMarker>();

        [Min(MinWindow)]
        [Tooltip("How much time the track view shows, in seconds. A VIEW range only — it never delays, cuts " +
                 "or shortens anything that was authored.")]
        public float windowSeconds = 2f;

        /// The narrowest and widest the authoring window is allowed to get. Public because the UI's slider
        /// bounds and this field's own clamp must not be able to disagree.
        public const float MinWindow = 0.25f;
        public const float MaxWindow = 30f;

        public bool Enabled => enabled;

        // ── schedule ────────────────────────────────────────────────────────────────────────────────────
        /// The delay, in seconds after burst-start, at which a named module should fire. 0 when the timeline
        /// is off or says nothing about that module — so an unscheduled burst behaves exactly as it did
        /// before timelines existed. Safe on a disabled timeline, a null track list and an unknown name.
        ///
        /// Called by ChunkModules.Dispatch (per module, per burst) and ChunkEmitter.ContainerLifetime.
        public float DelayFor(string moduleName)
        {
            if (!enabled || tracks == null || string.IsNullOrEmpty(moduleName)) return 0f;
            for (int i = 0; i < tracks.Count; i++)
            {
                var t = tracks[i];
                if (t != null && t.moduleName == moduleName) return Mathf.Max(0f, t.delay);
            }
            return 0f;
        }

        /// Whether the timeline holds an explicit slot for this module — distinct from "its delay is 0",
        /// which is also what an ABSENT module reports. The UI needs the difference to show an authored
        /// on-the-spot slot as authored.
        public bool Schedules(string moduleName)
        {
            if (tracks == null || string.IsNullOrEmpty(moduleName)) return false;
            for (int i = 0; i < tracks.Count; i++)
                if (tracks[i] != null && tracks[i].moduleName == moduleName) return true;
            return false;
        }

        /// Set (creating the slot on first use) a module's delay. Callers are the timeline UI's lane drags —
        /// they own the Undo record, this only mutates.
        public void SetDelayFor(string moduleName, float delay)
        {
            if (string.IsNullOrEmpty(moduleName)) return;
            tracks ??= new List<ChunkTimelineTrack>();
            delay = Mathf.Max(0f, delay);
            for (int i = 0; i < tracks.Count; i++)
            {
                var t = tracks[i];
                if (t != null && t.moduleName == moduleName) { t.delay = delay; return; }
            }
            tracks.Add(new ChunkTimelineTrack { moduleName = moduleName, delay = delay });
        }

        /// Drop a module's slot entirely, so it goes back to firing on the spot.
        public void ClearDelayFor(string moduleName)
        {
            if (tracks == null || string.IsNullOrEmpty(moduleName)) return;
            for (int i = tracks.Count - 1; i >= 0; i--)
                if (tracks[i] == null || tracks[i].moduleName == moduleName) tracks.RemoveAt(i);
        }

        /// The last instant this timeline schedules anything — the latest module delay or marker time,
        /// whichever is later. 0 when the timeline is off or empty.
        ///
        /// This is what a container-lifetime calculation wants: a burst whose last blast is scheduled a
        /// second after its debris dies must not have its container destroyed at the debris' lifetime, or
        /// the tail of the effect is silently cancelled. See the note in this task's report — today
        /// ChunkEmitter.ContainerLifetime only consults DelayFor(ChunkModules.Formation), not this.
        public float Duration
        {
            get
            {
                if (!enabled) return 0f;
                float d = 0f;
                if (tracks != null)
                    for (int i = 0; i < tracks.Count; i++)
                        if (tracks[i] != null) d = Mathf.Max(d, Mathf.Max(0f, tracks[i].delay));
                if (markers != null)
                    for (int i = 0; i < markers.Count; i++)
                        if (markers[i] != null) d = Mathf.Max(d, Mathf.Max(0f, markers[i].time));
                return d;
            }
        }

        // ── marker playback ─────────────────────────────────────────────────────────────────────────────
        /// Fire this timeline's EVENT markers for one burst. Module scheduling is not done here — that is
        /// ChunkModules.Dispatch's job, through DelayFor — so calling this twice would double the sounds but
        /// never double a module.
        ///
        /// Shaped to take a ChunkModuleContext so it can sit on the same line as every other module dispatch:
        /// the intended call site is one line at the end of ChunkModules.Run,
        /// <c>if (spec.timeline != null &amp;&amp; spec.timeline.Enabled) spec.timeline.Fire(ctx);</c>
        /// (see this task's report — that one line is not this task's to add, and until it exists markers are
        /// fired by calling <see cref="Play"/> from game code).
        public void Fire(in ChunkModuleContext ctx)
        {
            // `in` parameters cannot be captured by a coroutine or a lambda — copy out first.
            Vector3 origin = ctx.Origin;
            Transform container = ctx.Container;
            var runner = ctx.Runner;
            Schedule(origin, container, runner);
        }

        /// Fire a spec's event markers standalone, without going through ChunkModules — the entry point game
        /// code (or a preview) uses to play a timeline on its own. `container` may be null, in which case a
        /// throwaway host is made to carry the delayed markers (play mode only; in edit mode only the markers
        /// at time 0 fire, since there is no coroutine host to wait on).
        public static void Play(ChunkSpec spec, Vector3 origin, Transform container = null)
        {
            var timeline = spec != null ? spec.timeline : null;
            if (timeline == null || !timeline.enabled) return;
            timeline.Schedule(origin, container, null);
        }

        /// The one place markers actually get dispatched. A marker at time &lt;= 0 fires on the spot; a later
        /// one is waited out on a coroutine host. Safe with a null runner and a null container.
        public void Schedule(Vector3 origin, Transform container, ChunkModuleRunner runner)
        {
            if (!enabled || markers == null || markers.Count == 0) return;

            bool needsHost = false;
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (m != null && !m.IsEmpty && m.time > 0f) { needsHost = true; break; }
            }

            if (runner == null && needsHost) runner = ResolveRunner(container, Duration);

            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (m == null || m.IsEmpty) continue;   // an unnamed marker fires nothing, silently
                if (m.time <= 0f) { Raise(m, origin); continue; }
                if (runner == null) continue;           // no host (edit mode, no container) — nothing to wait on
                runner.StartCoroutine(FireAfter(m, m.time, origin, container, container != null));
            }
        }

        /// A coroutine host for the delayed markers: the burst's own runner when there is a container (so the
        /// markers die with the burst, exactly like ChunkModules' own delayed dispatch), else a throwaway
        /// object that cleans itself up. Null in edit mode with no container — deliberately, because a
        /// self-destructing object needs a running game loop to destroy itself with.
        static ChunkModuleRunner ResolveRunner(Transform container, float duration)
        {
            if (container != null)
            {
                var r = container.GetComponent<ChunkModuleRunner>();
                return r != null ? r : container.gameObject.AddComponent<ChunkModuleRunner>();
            }
            if (!Application.isPlaying) return null;

            var go = new GameObject("Chunk Timeline");
            var runner = go.AddComponent<ChunkModuleRunner>();
            UnityEngine.Object.Destroy(go, Mathf.Max(0f, duration) + 0.5f);
            return runner;
        }

        static IEnumerator FireAfter(ChunkTimelineMarker m, float delay, Vector3 origin, Transform container,
                                     bool requireContainer)
        {
            yield return new WaitForSeconds(delay);
            // Same guard ChunkModules.FireAfter uses: a burst whose container died (short life, scene change)
            // must not still be making noise or raising events for something that is no longer on screen.
            if (requireContainer && container == null) yield break;
            Raise(m, origin);
        }

        static void Raise(ChunkTimelineMarker m, Vector3 origin)
        {
            if (m == null) return;
            if (m.kind == ChunkEventKind.Zound)
            {
                // Null hook = a project with no audio tool. Silence is the correct behaviour, not an error.
                if (!string.IsNullOrEmpty(m.zoundName)) ChunkZoundHook.Play?.Invoke(m.zoundName);
                return;
            }
            if (!string.IsNullOrEmpty(m.codeName)) ChunkTimelineEvents.Raise(m.codeName, origin);
        }
    }
}
