using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Zoetrope;

namespace Laubrary.ZoetropeLaunimator
{
    /// <summary>
    /// The <see cref="IAnimatedView"/> for a COMPOSITE body — the whole character, addressed as one thing,
    /// fanned out across the independently-timed parts <see cref="CompositeZonedPlayer"/> built.
    ///
    /// Without it a composite Zoe had no IAnimatedView on its root AT ALL, and the consequences were total and
    /// silent: <see cref="ReactionFxPlayer"/>'s view resolved to null, so hurt, death and every named
    /// <c>Raise()</c> reported "nothing to play" no matter what was authored; its frame handler was never
    /// subscribed, so every OnFrame effect on every reaction was dead and only Immediate ones fired; the root
    /// <see cref="AnimationArbiter"/> had no view either, so every claim rolled back and it arbitrated nothing;
    /// and an effect placed at a painted MetaPoint (a muzzle flash) fell back to the sprite's centre because
    /// there was nothing to sample the layer from. That was the state of the project's own demo character.
    ///
    /// Two rules make the fan-out honest rather than a guess:
    ///
    /// 1. **Only parts that KNOW the clip take part.** A part whose lauminary has no animation by that name is
    ///    left alone and keeps doing whatever it was doing. That is what makes "walking legs under a firing
    ///    torso" fall out of this for free instead of needing a mechanism, and it is the same seam per-part
    ///    reaction targeting plugs into later (ZOE_PALETTE_BUILD_PLAN.md task 6) — naming a subset is a
    ///    parameter here, not a redesign.
    /// 2. **Every part claims through its OWN arbiter, at the root claim's priority** (see
    ///    <see cref="IArbitratedView"/>). Calling the parts' views directly would go around the per-part
    ///    arbiters that each part's <see cref="MotionPoseAnimator"/> claims on, and the walk cycle would take
    ///    the part back on its very next direction change — the exact stomping the arbiter exists to stop.
    ///
    /// It reports frames from the parts that ACCEPTED the current clip and from nobody else. Forwarding every
    /// part's frames unconditionally would let a part still walking its own cycle fire a reaction's OnFrame
    /// effect at a frame number that means nothing in the reaction's clip.
    /// </summary>
    public class CompositeAnimatedView : MonoBehaviour, IAnimatedView, IArbitratedView
    {
        class Part
        {
            public string name;
            public IAnimatedView view;
            public AnimationArbiter arbiter;
        }

        readonly List<Part> _parts = new List<Part>();
        // Parts whose frame events are currently being forwarded — the ones that accepted the clip playing now.
        readonly List<Part> _sourcing = new List<Part>();

        /// <summary>Collect the parts this facade speaks for. Called by <see cref="CompositeLauminaryView.Build"/>
        /// straight after the parts themselves are built, so every part already has its view and its
        /// arbiter.</summary>
        public void Bind(CompositeZonedPlayer player)
        {
            _parts.Clear();
            if (player == null) return;
            var objects = player.PartObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                var go = objects[i];
                if (go == null) continue;
                var view = go.GetComponent<IAnimatedView>();
                if (view == null) continue;   // a part with nothing to play (a plain SpriteView part) is not a claimant
                // go.name IS the part's authored name — CompositeZonedPlayer.Build names each child GameObject
                // directly from ZoeBodyPart.name, so reading it back here needs no extra wiring or lookup.
                _parts.Add(new Part { name = go.name, view = view, arbiter = go.GetComponent<AnimationArbiter>() });
            }
        }

        // ── IAnimatedView ─────────────────────────────────────────────────────

        /// Forwarded from whichever parts accepted the clip currently playing through this facade — see the
        /// class doc for why not from all of them.
        public event Action<string, int> OnFrameEvent;
        /// Same rule as <see cref="OnFrameEvent"/>: only the parts actually playing the current clip.
        public event Action<int> OnFrameEntered;

        /// True if ANY part knows the name, matching what <see cref="PlayClip"/> will actually do (it plays on
        /// every part that knows it, and reports success if at least one started).
        public bool HasClip(string clip)
        {
            for (int i = 0; i < _parts.Count; i++)
                if (_parts[i].view.HasClip(clip)) return true;
            return false;
        }

        /// The LONGEST of the parts that know this clip. The event's own timebase has to cover the whole body:
        /// timing a death to the shorter of two parts would cut the other one off mid-animation, and everything
        /// riding the event (a body flash, a timed effect) is measured against this answer. 0 when no part
        /// knows it, or when every part that does is a zoned strip with no fixed end — the established
        /// "unknown, degrade to a single play" value.
        public float GetClipSeconds(string clip)
        {
            float longest = 0f;
            for (int i = 0; i < _parts.Count; i++)
            {
                float s = _parts[i].view.GetClipSeconds(clip);
                if (s > longest) longest = s;
            }
            return longest;
        }

        /// The raw, UN-arbitrated channel, kept faithful to what PlayClip means everywhere else in the
        /// codebase: play it, now, on every part that knows it, over the top of whatever those parts were
        /// doing. In-project everything goes through <see cref="AnimationArbiter"/>, which takes the arbitrated
        /// path below; this exists for a consumer holding a bare IAnimatedView reference, and is deliberately
        /// not quietly upgraded into a claim — a claim it never asked for is a claim it would never know to
        /// release, and an unreleasable claim is exactly the failure this whole class of bug is made of.
        public bool PlayClip(string clip, bool loop, Action onComplete = null) =>
            Fan(clip, loop, onComplete, arbitrated: false, priority: 0f, targetPart: null);

        // ── IArbitratedView ───────────────────────────────────────────────────

        public bool PlayClipArbitrated(string clip, bool loop, float priority, Action onComplete, string targetPart = null) =>
            Fan(clip, loop, onComplete, arbitrated: true, priority, targetPart);

        public void ReleaseFannedClaims()
        {
            StopForwarding();
            for (int i = 0; i < _parts.Count; i++)
            {
                // Release ignores an owner that isn't the current holder, so a part we never took (or that has
                // since been taken from us) is untouched — this can never yank a part away from whoever
                // legitimately has it now.
                if (_parts[i].arbiter != null) _parts[i].arbiter.Release(this);
            }
        }

        /// <paramref name="arbitrated"/> false = write straight to each part's view (see PlayClip's own note);
        /// true = each part submits a claim to its own arbiter at <paramref name="priority"/>, owned by THIS
        /// facade, so the whole body is one logical claimant that a part's locomotion has to outrank.
        /// <paramref name="targetPart"/> — empty/null fans to every knowing part (unchanged); a name narrows
        /// the candidate set to that ONE named part before the clip/priority filters run, so a part-targeted
        /// reaction that also names an unknown clip fails the same "refused" way an unknown clip always has.
        bool Fan(string clip, bool loop, Action onComplete, bool arbitrated, float priority, string targetPart)
        {
            if (string.IsNullOrEmpty(clip)) return false;

            // Decide the participants BEFORE releasing or playing anything. If nothing can take the clip we
            // must leave the previous fan-out exactly as it was: the caller (AnimationArbiter.Play) rolls its
            // own claim back on a false return, and having already dropped the sub-claims would leave the
            // reaction that IS still running holding a root claim over parts that no longer answer to it.
            //
            // A LOCAL list, not a reused scratch field, and that is not fussiness: this method can genuinely
            // re-enter. The player fires frame 0's events synchronously from inside Play, so an effect
            // authored on a reaction's first frame runs while this loop is still going — and if that effect is
            // itself a clip-playing one, it comes straight back in here. A shared buffer would be cleared out
            // from under the outer call. (The forwarding list below still interleaves in that case; a clip
            // started from inside the first frame of the clip being started is pathological authoring and is
            // left honest rather than papered over.)
            var accepting = new List<Part>(_parts.Count);
            for (int i = 0; i < _parts.Count; i++)
            {
                var p = _parts[i];
                if (!string.IsNullOrEmpty(targetPart) && !string.Equals(p.name, targetPart, StringComparison.OrdinalIgnoreCase)) continue;
                if (!p.view.HasClip(clip)) continue;
                if (arbitrated && p.arbiter != null && !p.arbiter.WouldAccept(this, priority)) continue;
                accepting.Add(p);
            }
            if (accepting.Count == 0) return false;

            // Hand back EVERY part first, including ones not in this clip's set. A part the previous whole-body
            // clip played on but this one doesn't would otherwise keep a claim nobody will ever release, and
            // stay frozen on that older reaction's last frame while the rest of the body moves on.
            ReleaseFannedClaims();

            int outstanding = accepting.Count;
            bool completed = false;
            int started = 0;

            for (int i = 0; i < accepting.Count; i++)
            {
                var p = accepting[i];
                bool done = false;
                // Per-part, one-shot: a part that both completes and is later preempted must only count once,
                // or the whole-body completion fires early while another part is still playing.
                Action oneShot = () =>
                {
                    if (done) return;
                    done = true;
                    outstanding--;
                    if (outstanding > 0 || completed) return;
                    completed = true;
                    if (onComplete != null) onComplete();
                };

                // Subscribed BEFORE the play, never after: the player fires OnFrameEntered for frame 0
                // synchronously from inside Play, so subscribing afterwards silently drops any effect authored
                // on a reaction's FIRST frame. Undone immediately below if the clip didn't start.
                StartForwarding(p);

                bool ok = arbitrated && p.arbiter != null
                    // onInterrupted is oneShot as well, not null: a part taken from us mid-clip will never
                    // complete, and without counting it the whole reaction hangs forever on a signal that is
                    // no longer coming — which downstream means a body that never disposes.
                    ? p.arbiter.Play(this, priority, clip, loop, 0f, loop ? null : oneShot, oneShot)
                    : p.view.PlayClip(clip, loop, loop ? null : oneShot);

                if (ok) { started++; continue; }

                StopForwarding(p);
                done = true;        // nothing started, so there is nothing for this part to ever complete
                outstanding--;
            }

            if (started > 0) return true;

            // Pre-filtered by HasClip/WouldAccept above, so this is the belt-and-braces path rather than an
            // expected one. Nothing started, nothing was counted, and onComplete was never fired.
            StopForwarding();
            return false;
        }

        void StartForwarding(Part p)
        {
            if (_sourcing.Contains(p)) return;
            _sourcing.Add(p);
            p.view.OnFrameEntered += RelayFrameEntered;
            p.view.OnFrameEvent += RelayFrameEvent;
        }

        void StopForwarding(Part p)
        {
            if (!_sourcing.Remove(p)) return;
            p.view.OnFrameEntered -= RelayFrameEntered;
            p.view.OnFrameEvent -= RelayFrameEvent;
        }

        void StopForwarding()
        {
            for (int i = _sourcing.Count - 1; i >= 0; i--) StopForwarding(_sourcing[i]);
        }

        // Several parts playing the same clip each announce the same frame. That is deliberately NOT deduped
        // here: ReactionFxPlayer already fires each authored OnFrame effect once per event, so the first part
        // to reach the frame is the one that triggers it — and any dedup here would need a notion of which
        // part is "the" timing source, which a body of independently-timed parts does not have.
        void RelayFrameEntered(int frame)
        {
            if (OnFrameEntered != null) OnFrameEntered(frame);
        }

        void RelayFrameEvent(string name, int frame)
        {
            if (OnFrameEvent != null) OnFrameEvent(name, frame);
        }

        /// The frame of the part currently driving the whole-body clip; the first part's otherwise. -1 when
        /// there is nothing to ask.
        public int CurrentFrame =>
            _sourcing.Count > 0 ? _sourcing[0].view.CurrentFrame :
            _parts.Count > 0 ? _parts[0].view.CurrentFrame : -1;

        public void Hide()
        {
            for (int i = 0; i < _parts.Count; i++) _parts[i].view.Hide();
        }

        public void SetPlaybackReversed(bool value)
        {
            for (int i = 0; i < _parts.Count; i++) _parts[i].view.SetPlaybackReversed(value);
        }

        // ── meta-layer sampling: first part that actually has the data wins ───
        // A layer id means one thing across a character (IAnimatedView.GetMetaLayerKind's own rule), so which
        // part carries it is an authoring detail the caller should not have to know. Walked in the AUTHORED
        // part order, so the answer is stable rather than dictionary-order luck.

        public bool TryGetMetaPoint(string layerId, out Vector2 worldPos)
        {
            for (int i = 0; i < _parts.Count; i++)
                if (_parts[i].view.TryGetMetaPoint(layerId, out worldPos)) return true;
            worldPos = default;
            return false;
        }

        public bool TryGetMetaPointNearest(string layerId, out Vector2 worldPos)
        {
            for (int i = 0; i < _parts.Count; i++)
                if (_parts[i].view.TryGetMetaPointNearest(layerId, out worldPos)) return true;
            worldPos = default;
            return false;
        }

        public bool TryGetMetaVector(string layerId, out Vector2 worldOrigin, out Vector2 worldDirection, out float worldLength)
        {
            for (int i = 0; i < _parts.Count; i++)
                if (_parts[i].view.TryGetMetaVector(layerId, out worldOrigin, out worldDirection, out worldLength)) return true;
            worldOrigin = default; worldDirection = Vector2.up; worldLength = 0f;
            return false;
        }

        public bool TryGetMetaVectorNearest(string layerId, out Vector2 worldOrigin, out Vector2 worldDirection, out float worldLength)
        {
            for (int i = 0; i < _parts.Count; i++)
                if (_parts[i].view.TryGetMetaVectorNearest(layerId, out worldOrigin, out worldDirection, out worldLength)) return true;
            worldOrigin = default; worldDirection = Vector2.up; worldLength = 0f;
            return false;
        }

        /// Vector wins over Point across the whole body, exactly as it does within one view — it carries
        /// strictly more information, so it is the safer answer for a muzzle when a character somehow declares
        /// both.
        public MetaLayerKind GetMetaLayerKind(string layerId)
        {
            var found = MetaLayerKind.None;
            for (int i = 0; i < _parts.Count; i++)
            {
                var kind = _parts[i].view.GetMetaLayerKind(layerId);
                if (kind == MetaLayerKind.Vector) return MetaLayerKind.Vector;
                if (kind == MetaLayerKind.Point) found = MetaLayerKind.Point;
            }
            return found;
        }
    }
}
