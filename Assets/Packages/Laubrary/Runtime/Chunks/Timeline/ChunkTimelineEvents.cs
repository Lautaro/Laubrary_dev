// ChunkTimelineEvents — the subscription point for a Chunks timeline's CODE events (AgentHQ T-0038).
//
// A Code Event marker carries a free-text hook name. That text field is correct here and nowhere else: this
// is where the name is DECLARED. Every place that REFERENCES a name (a Zound, a laumination, a MetaLayer)
// gets a picker instead — the project's "never type a reference string" rule cuts exactly along that line.
//
// One static event rather than a per-name registry on purpose: a game usually wants a single handler that
// switches on the name (right next to the rest of its FX reaction code), and a registry would need a
// subscribe/unsubscribe API per name plus a leak story for names nobody ever fires. Filtering on a string in
// one handler is cheaper to write and impossible to get half-wired.
using System;
using UnityEngine;

namespace Laubrary.Chunks
{
    /// Where game code hears a Chunks timeline's Code Event markers.
    ///
    /// <code>
    /// void OnEnable()  => ChunkTimelineEvents.CodeEvent += OnChunkEvent;
    /// void OnDisable() => ChunkTimelineEvents.CodeEvent -= OnChunkEvent;
    /// void OnChunkEvent(string name, Vector3 origin)
    /// {
    ///     if (name == "ShakeCamera") Camera.main.SendMessage("Shake");
    /// }
    /// </code>
    ///
    /// ⚠️ A static event outlives a scene load, so a subscriber that never unsubscribes keeps its whole
    /// object alive and will be called again in the next scene. Unsubscribe in OnDisable, as above.
    public static class ChunkTimelineEvents
    {
        /// Raised once per Code Event marker, with (the marker's hook name, the burst's world origin). Never
        /// raised for an unnamed marker.
        public static event Action<string, Vector3> CodeEvent;

        /// Whether anything is listening. Not needed to fire (an unheard event is harmless) — it exists so a
        /// tool can honestly report "nothing in this scene reacts to these events yet."
        public static bool HasListeners => CodeEvent != null;

        /// Fire a named code event. Public rather than internal so a game can replay one by hand (a cutscene
        /// re-triggering the same reaction a burst would have), but ChunkTimeline is its normal caller.
        ///
        /// One subscriber throwing must not stop the rest of the burst, so each is invoked separately and its
        /// exception is logged rather than propagated back into the coroutine that scheduled the marker.
        public static void Raise(string eventName, Vector3 origin)
        {
            if (string.IsNullOrEmpty(eventName)) return;
            var handlers = CodeEvent;
            if (handlers == null) return;

            var list = handlers.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try { ((Action<string, Vector3>)list[i])(eventName, origin); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
