using System.Collections;
using UnityEngine;
using Laubrary.Combat2D;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// A self-contained respawning-target-dummy behavior: plays <see cref="idleClip"/> until hit, defers to
    /// <see cref="ReactionFxPlayer"/> for the hurt/death clip itself (this only reacts to ITS finished-events
    /// — the actual hurt/death clip choice is real Zoe data, <see cref="Zoe.hit"/>/<see cref="Zoe.death"/>, not owned here),
    /// then respawns (<see cref="Health.Revive"/> + resume Idle) <see cref="respawnDelay"/> seconds after the
    /// death reaction finishes. Uses the REAL Health system, so hit detection/damage/death are never
    /// reimplemented for preview purposes. Deliberately holds NO reference to <see cref="Zoe"/> — this is a
    /// Mirage-only testing convenience (added by <c>MirageSubject</c>, not <c>ZoeSpawner</c>), so its own
    /// config lives on the caller (Mirage's <c>PreviewableEntry</c>/<c>MirageSubject</c>), never on the Zoe
    /// asset itself.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class TargetPracticeController : MonoBehaviour
    {
        public string idleClip = "Idle";
        [Min(0f)] public float respawnDelay = 2f;

        Health health;
        IAnimatedView view;
        ReactionFxPlayer reaction;
        AnimationArbiter _arbiter;

        /// Resolved lazily, never cached at Awake — MirageSubject adds this controller to an already-built
        /// character, so the arbiter may or may not exist yet at that moment. Same rule LocomotionAnimator and
        /// ReactionFxPlayer follow, for the same spawn-ordering reason.
        AnimationArbiter Arbiter => _arbiter != null ? _arbiter : (_arbiter = GetComponent<AnimationArbiter>());

        void Awake()
        {
            health = GetComponent<Health>();
            view = GetComponent<IAnimatedView>();
            reaction = GetComponent<ReactionFxPlayer>();
        }

        // Start (not OnEnable) so the caller's post-AddComponent field assignments have already happened — the
        // same "OnEnable already ran before fields could be set" trap WeaponMuzzleCue's Configure() exists to
        // avoid.
        void Start() => PlayIdle();

        void OnEnable()
        {
            if (reaction == null) reaction = GetComponent<ReactionFxPlayer>();
            if (reaction != null) { reaction.HurtFinished += OnHurtEnded; reaction.DeathFinished += BeginRespawn; }
        }

        void OnDisable()
        {
            if (reaction != null) { reaction.HurtFinished -= OnHurtEnded; reaction.DeathFinished -= BeginRespawn; }
        }

        // Back to idle only when the hurt reaction actually reached its own end. INTERRUPTED means something
        // else — a death, a named state — has just taken the body and is about to show its own thing, so
        // pushing Idle here would flash over it. That mattered the moment death interruption started working:
        // a killing blow retires the hurt reaction from INSIDE the death's own arming, so this fires while the
        // death clip is still being set up, and it only ever looked correct because the death happened to play
        // a fraction of a millisecond later and overwrite it.
        //
        // The clip-less case is skipped for a different reason: HurtFinished now fires for EVERY hit, including
        // one whose reaction has no clip at all (the finished-signal contract on ReactionFxPlayer — a consumer
        // must never be left waiting). Nothing took the body in that case, so nothing needs putting back, and
        // re-issuing Idle would restart the animation from frame 0 on every single hit.
        void OnHurtEnded(bool interrupted)
        {
            if (interrupted || HurtClipEmpty()) return;
            PlayIdle();
        }

        bool HurtClipEmpty()
        {
            var hurt = reaction != null && reaction.def != null ? reaction.def.hit : null;
            return hurt == null || string.IsNullOrEmpty(hurt.clip);
        }

        void PlayIdle()
        {
            SetBodyVisible(true);   // idle / respawn always re-shows the body (undoing a clip-less-death hide, #6)
            if (view == null) view = GetComponent<IAnimatedView>();
            if (string.IsNullOrEmpty(idleClip)) return;

            var arbiter = Arbiter;
            if (arbiter == null) { if (view != null) view.PlayClip(idleClip, loop: true); return; }

            // Claim, then hand straight back. Two things are wrong with writing to the view directly, and the
            // claim fixes both: a dummy's idle must never be able to paint over a hurt or a death that is
            // holding the body (at 200/1000 this claim is simply refused, which is the correct outcome — the
            // reaction is showing), and a body played through the raw view would never reach a COMPOSITE
            // character's parts at all, since those arbitrate individually.
            //
            // Handing it back immediately, rather than holding, is deliberate: this is "put idle up now", not
            // a state to own. Holding it would starve a real steady-state claimant — a locomotion or motion-pose
            // animator claims at the same PriorityLocomotion rung and, since equal priority never steals, the
            // one that claimed first would keep the body forever. Since this component's Start() runs before
            // any animator's first LateUpdate, that first one would always be this, and a walking dummy would
            // stand frozen on its idle frame. Releasing also fires Reassert, which is exactly how a real
            // animator takes the body back on its very next frame.
            var claim = new object();
            if (arbiter.Play(claim, AnimationArbiter.PriorityLocomotion, idleClip, loop: true))
                arbiter.Release(claim);
        }

        void BeginRespawn(bool interrupted)
        {
            // A death with NO death clip has nothing to animate — so show NOTHING while dead instead of leaving the
            // body sitting on its last idle frame until respawn (task #6), so it can be replaced by an explosion FX.
            // PlayIdle re-shows it on revive. (A death that DOES play a clip keeps its final frame up, as before.)
            if (DeathClipEmpty())
            {
                // Order matters: STOP the animation first. A Zoned view's player re-asserts its renderer's
                // visibility every frame in PushSprite, so a bare SetBodyVisible(false) is undone next frame (the
                // reported "it flashes back to the idle frame on death" bug). Hide() stops the player AND blanks it;
                // SetBodyVisible(false) then also covers a plain SpriteView that has no IAnimatedView to hide.
                if (view == null) view = GetComponent<IAnimatedView>();
                view?.Hide();
                SetBodyVisible(false);
            }
            StartCoroutine(RespawnAfterDelay());
        }

        bool DeathClipEmpty()
        {
            var death = reaction != null && reaction.def != null ? reaction.def.death : null;
            return death == null || string.IsNullOrEmpty(death.clip);
        }

        // Toggle the whole body's SpriteRenderers (a Zoned view is several) — "show nothing" without disabling this
        // GameObject (which would kill the controller + its respawn coroutine).
        void SetBodyVisible(bool on)
        {
            foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
                sr.enabled = on;
        }

        IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(Mathf.Max(0f, respawnDelay));
            if (health != null) health.Revive();
            PlayIdle();
        }
    }
}
