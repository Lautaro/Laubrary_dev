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
            if (reaction != null) { reaction.HurtFinished += PlayIdle; reaction.DeathFinished += BeginRespawn; }
        }

        void OnDisable()
        {
            if (reaction != null) { reaction.HurtFinished -= PlayIdle; reaction.DeathFinished -= BeginRespawn; }
        }

        void PlayIdle()
        {
            SetBodyVisible(true);   // idle / respawn always re-shows the body (undoing a clip-less-death hide, #6)
            if (view == null) view = GetComponent<IAnimatedView>();
            if (view != null && !string.IsNullOrEmpty(idleClip))
                view.PlayClip(idleClip, loop: true);
        }

        void BeginRespawn()
        {
            // A death with NO death clip has nothing to animate — so show NOTHING while dead instead of leaving the
            // body sitting on its last idle frame until respawn (task #6). PlayIdle re-shows it on revive. (A death
            // that DOES play a clip keeps its final frame up, as before.)
            if (DeathClipEmpty()) SetBodyVisible(false);
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
