using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Zoetrope;
using Laubrary.Launimator;

namespace Laubrary.ZoetropeLaunimator
{
    /// <summary>
    /// The real cue mechanism — implements <see cref="ICueSink"/> against a real
    /// <see cref="ZonedAnimationPlayer"/>. Dispatches on EITHER <see cref="ZonedAnimationPlayer.OnMetaLayerReached"/>
    /// or <see cref="ZonedAnimationPlayer.OnFrameEvent"/> (a binding's <see cref="CueBinding.eventName"/>, if
    /// set, picks the latter — see CueBinding's own doc comment), using the LIVE world position every time
    /// (not a static offset — this is what fixes the muzzle-FX position bug: the old weapon-fire path only
    /// used MetaLayer for timing, never for where the effect actually renders). Holds two independent sets of
    /// bindings: <see cref="Seed"/>'d ones (the Zoe's own, replaced wholesale) and <see cref="Register"/>'d
    /// ones (added under a stable key, e.g. one per equipped weapon, removed independently on
    /// <see cref="Unregister"/>).
    /// </summary>
    [RequireComponent(typeof(ZonedAnimationPlayer))]
    public class CueRelay : MonoBehaviour, ICueSink
    {
        readonly List<CueBinding> _seeded = new List<CueBinding>();
        readonly Dictionary<string, CueBinding> _dynamic = new Dictionary<string, CueBinding>();

        ZonedAnimationPlayer _player;

        void Awake() => _player = GetComponent<ZonedAnimationPlayer>();

        void OnEnable()
        {
            if (_player != null) { _player.OnMetaLayerReached += HandleLayerReached; _player.OnFrameEvent += HandleFrameEvent; }
        }

        void OnDisable()
        {
            if (_player != null) { _player.OnMetaLayerReached -= HandleLayerReached; _player.OnFrameEvent -= HandleFrameEvent; }
        }

        public void Seed(IReadOnlyList<CueBinding> cues)
        {
            _seeded.Clear();
            if (cues != null) _seeded.AddRange(cues);
        }

        public void Register(string key, string layerId, ICombatFx fx, string eventName = "")
        {
            if (string.IsNullOrEmpty(key)) return;
            _dynamic[key] = new CueBinding { layerId = layerId, eventName = eventName, fx = fx };
        }

        public void Unregister(string key)
        {
            if (!string.IsNullOrEmpty(key)) _dynamic.Remove(key);
        }

        void HandleLayerReached(string layerId, Vector3 worldPos)
        {
            foreach (var b in _seeded) TryFireLayer(b, layerId, worldPos);
            foreach (var b in _dynamic.Values) TryFireLayer(b, layerId, worldPos);
        }

        // FrameEvent-driven bindings resolve their own point (a single authored pixel, not a live-painted
        // mask) rather than trusting whatever position OnFrameEvent's caller happens to pass — there isn't
        // one, OnFrameEvent only carries (name, frame). TryGetEventPoint re-reads the CURRENT frame's events,
        // which is correct here since this fires synchronously from that same frame's event dispatch.
        void HandleFrameEvent(string eventName, int frame)
        {
            bool any = false;
            foreach (var b in _seeded) if (Matches(b, eventName)) { any = true; break; }
            if (!any) foreach (var b in _dynamic.Values) if (Matches(b, eventName)) { any = true; break; }
            if (!any || _player == null || !_player.TryGetEventPoint(eventName, out var worldPos)) return;

            foreach (var b in _seeded) TryFireEvent(b, eventName, worldPos);
            foreach (var b in _dynamic.Values) TryFireEvent(b, eventName, worldPos);
        }

        static bool Matches(CueBinding b, string eventName) =>
            b != null && !string.IsNullOrEmpty(b.eventName) && string.Equals(b.eventName, eventName, StringComparison.OrdinalIgnoreCase);

        // Instance methods, not static: firing may now RAISE A NAMED REACTION on this character, which needs
        // the ReactionFxPlayer sitting beside us. Resolved lazily because the relay and the player are added
        // in an order this component does not control.
        ReactionFxPlayer _reactions;
        ReactionFxPlayer Reactions =>
            _reactions != null ? _reactions : _reactions = GetComponent<ReactionFxPlayer>();

        /// Do whatever the binding says: spawn its FX, raise its named event, or both. Both halves are
        /// optional -- a cue that only raises an event is the whole point of raiseEvent, and an fx-only guard
        /// would silently skip it.
        void Fire(CueBinding b, Vector3 worldPos)
        {
            if (b.fx != null && !b.fx.IsEmpty) b.fx.Play(worldPos);
            if (string.IsNullOrEmpty(b.raiseEvent)) return;
            var r = Reactions;
            if (r == null || !r.Raise(b.raiseEvent))
                Debug.LogWarning($"[Cue] Frame cue tried to raise '{b.raiseEvent}' but this character " +
                                 $"declares no such event.", this);
        }

        void TryFireLayer(CueBinding b, string layerId, Vector3 worldPos)
        {
            if (b == null || !b.IsActionable) return;
            if (!string.IsNullOrEmpty(b.eventName)) return;   // event-driven bindings never fire off a layer
            if (!string.Equals(b.layerId, layerId, StringComparison.OrdinalIgnoreCase)) return;
            Fire(b, worldPos);
        }

        void TryFireEvent(CueBinding b, string eventName, Vector3 worldPos)
        {
            if (b == null || !b.IsActionable) return;
            if (!Matches(b, eventName)) return;
            Fire(b, worldPos);
        }
    }
}
