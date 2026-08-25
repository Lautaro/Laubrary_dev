using UnityEngine;
using Laubrary.Chunks;

namespace Laubrary.Mirage
{
    /// <summary>
    /// The live driver behind a <see cref="ChunkSpec"/> previewable — Mirage's answer to "this content type
    /// does not loop".
    ///
    /// Every other kind of previewable Mirage realizes is CONTINUOUS: a Zoe cycles its clip list forever, and
    /// <see cref="MirageRig"/> realizes a Pyre blast with <c>loop = true</c> precisely because the window
    /// assumes something is always on screen to look at. A Chunks burst is the opposite shape — it fires, it
    /// flies, it is gone in a couple of seconds — so there is no loop flag to lean on. Left as-is it would be a
    /// preview you could watch exactly once per Play session, which is not a preview. Hence a driver instead of
    /// a flag: it fires once when it comes alive, and stands ready to fire again on demand
    /// (<see cref="MirageHud"/> draws the Replay control that calls <see cref="Fire"/>).
    ///
    /// <b>Auto-repeat is opt-in and off by default, deliberately.</b> An explosion re-detonating on a clock
    /// while you are trying to read one frame of it is the single thing that makes this kind of preview
    /// unreadable, so the default puts the burst on the author's beat. The option exists because authoring
    /// genuinely does want to see a burst more than once without clicking every time — it is just not the
    /// behaviour anyone should get without asking for it.
    ///
    /// <b>Play mode only, and not by choice.</b> <c>Chunk</c>, <c>ChunkModuleRunner</c> and Pyre's
    /// <c>PyreBlastPlayer</c> all drive off <c>Update</c> and coroutines, and none of them is
    /// <c>[ExecuteAlways]</c> — so nothing a burst spawns can move outside Play mode. Firing there would strew
    /// frozen, never-expiring debris across the stage (pooled <c>Chunk</c> instances that never reach their
    /// <c>Finished</c> callback and so never return to <c>ChunkPool</c>), which is worse than showing nothing.
    /// The driver therefore declines, and the HUD says so in words rather than offering a control that would
    /// visibly do nothing. This matches how the rest of Mirage already behaves — a Pyre entry is equally frozen
    /// in Edit mode, and <see cref="MirageHud"/>'s own placement panel already tells you to enter Play mode.
    ///
    /// <c>[ExecuteAlways]</c> is still on so the component exists and can be inspected/queried in Edit mode
    /// (the HUD walks the rig's children to build its panel in Edit mode too).
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Laubrary/Mirage/Mirage Chunk Burst")]
    public class MirageChunkBurst : MonoBehaviour
    {
        [Header("Content")]
        [Tooltip("The burst recipe to preview. Assigned by MirageRig.Realize from the previewable entry.")]
        public ChunkSpec spec;

        [Tooltip("Sorting order handed to every chunk this burst spawns. 500 is the same default the game-facing " +
                 "ChunkEmitter and the static Chunks.Burst API use, so the preview draws where a real burst would. " +
                 "A spec using the Chunks 2.0 Layer Stack overrides this per-layer on its own.")]
        public int sortingOrder = 500;

        [Header("Replay")]
        [Tooltip("Fire again automatically every 'Interval' seconds instead of only when Replay is pressed. OFF " +
                 "by default on purpose: a burst re-detonating on a clock while you are reading one frame of it " +
                 "is what makes an explosion preview unreadable.")]
        public bool autoRepeat;

        [Tooltip("Seconds between automatic replays. Only used while Auto-replay is on.")]
        [Min(0.25f)] public float repeatInterval = 2f;

        const float MinInterval = 0.25f;

        /// The container <see cref="ChunkEmitter.SpawnBurst"/> returned for the burst currently on screen —
        /// held so the next fire can clear it (see <see cref="ClearBurst"/>).
        Transform _burst;
        float _clock;
        bool _fired;

        /// Would pressing Replay right now actually produce something to look at? Drives the HUD's control the
        /// same way <c>MirageSubject.Capabilities</c> drives the character controls: the panel asks, it never
        /// assumes.
        public bool CanFire => spec != null && Application.isPlaying;

        /// <summary>Fire (or re-fire) the burst at this object's current position.
        ///
        /// The previous burst is torn down first so repeated replays cannot pile debris on top of debris — the
        /// container is destroyed outright rather than releasing its chunks back to <c>ChunkPool</c> by hand.
        /// That is the deliberate choice of the two: <c>ChunkEmitter.SpawnBurst</c> subscribes a one-shot
        /// <c>Finished</c> handler per chunk that releases it, so releasing early would leave that handler
        /// subscribed on a chunk the pool has already handed out again — a double release, i.e. the same
        /// instance sitting twice in the free list. Destroying instead simply means those chunks never come
        /// back to the pool; the pool's own <c>Get()</c> builds fresh ones, so the cost is a little bookkeeping
        /// drift in a preview scene, not a correctness bug in a live pool.</summary>
        public void Fire()
        {
            if (spec == null || !Application.isPlaying) return;
            ClearBurst();

            // Parent = this transform, so the burst rides with the entry and dies with it. Note SpawnBurst only
            // self-destructs its container when parent is null — with a parent, ownership is ours, which is
            // exactly what makes replay-clears-previous possible.
            _burst = ChunkEmitter.SpawnBurst(transform.position, spec, null, float.NaN, transform, sortingOrder);
            if (_burst != null) _burst.gameObject.hideFlags = HideFlags.DontSave;   // transient scaffolding, like the rig's own children
            _clock = 0f;
            _fired = true;
        }

        // Auto-fire happens on the first Update tick, NOT in Start() or OnEnable(). Start() is never called in
        // Edit mode for an ExecuteAlways script, and OnEnable() fires DURING MirageRig.Realize's AddComponent —
        // before `spec` has been assigned on the very next line. MirageSubject documents this exact trap and
        // solves it the same way; by the first Update tick the rig's synchronous field assignments are done.
        void Update()
        {
            if (!Application.isPlaying) return;
            if (!_fired) { Fire(); return; }
            if (!autoRepeat || spec == null) return;

            _clock += Time.deltaTime;
            if (_clock >= Mathf.Max(MinInterval, repeatInterval)) Fire();
        }

        void OnDisable()
        {
            ClearBurst();
            _fired = false;
            _clock = 0f;
        }

        void ClearBurst()
        {
            if (_burst == null) return;
            var go = _burst.gameObject;
            _burst = null;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }
    }
}
