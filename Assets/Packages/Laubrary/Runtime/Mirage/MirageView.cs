using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Zoetrope;
using Laubrary.Choreographer;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Laubrary.Mirage
{
    /// <summary>
    /// A persisted preview arrangement — edited directly, no clone/discard (same workflow as Pyre's
    /// Pyre: field writes ARE the save). A <see cref="MirageRig"/> realizes this into live GameObjects,
    /// in Edit mode and at Play. Never write an entry here except through the "Add Previewable" flow
    /// (MirageWindow / MirageHud) — a Zoe's own muzzle/death VFX must stay a side effect of ITS entry,
    /// never its own entry.
    /// </summary>
    [CreateAssetMenu(menuName = "Laubrary/Mirage/Mirage View", fileName = "MirageView")]
    public class MirageView : ScriptableObject
    {
        [Tooltip("Every previewable is scaled so its source content's own PPU maps to this — the \"no mixels\" " +
                 "guarantee. A 16-PPU Zoe and a 64-PPU Pyre blast render at the same apparent pixel size. " +
                 "Default 16 matches the project's chosen PPU convention (see ppu-convention-gap memory).")]
        [Min(1f)] public float displayPixelsPerUnit = 16f;

        public List<PreviewableEntry> previewables = new List<PreviewableEntry>();

        [Tooltip("Recallable test backdrop — camera colour + a zoomable/positionable image. A private, " +
                 "owned COPY of a BackSplash preset's fields (not a shared asset reference) — editing it here " +
                 "only affects this view; use Recall/★ in the inline editor to pull from or push to a named " +
                 "shared preset.")]
        public Laubrary.BackSplash.BackSplashSettings backSplash = new Laubrary.BackSplash.BackSplashSettings();

        [Tooltip("Auto-captured from the Game view whenever the active view changes in Play mode — see " +
                 "MirageRig.CaptureThenSwap. Shown by MirageWindow's browser via RenderThumbnail.")]
        public Texture2D thumbnail;

        /// The one place a new entry gets constructed — used by both the asset-only path (MirageWindow,
        /// no live rig required) and MirageRig.AddEntry (which additionally realizes it live). Keeping
        /// construction here, not duplicated, is what keeps "only Add Previewable creates entries" true.
        public PreviewableEntry AddEntry(UnityEngine.Object content, Vector2 position)
        {
            var entry = new PreviewableEntry { content = content, position = position };
            previewables.Add(entry);
            return entry;
        }

#if UNITY_EDITOR
        /// Embeds/updates <paramref name="capture"/> as this asset's thumbnail sub-asset. First call embeds
        /// the texture itself (AddObjectToAsset); later calls overwrite its pixels in place instead of
        /// accumulating orphaned sub-assets. Takes ownership of `capture` — destroys it once its pixels have
        /// been copied over on a repeat capture.
        public void SetThumbnail(Texture2D capture)
        {
            string path = AssetDatabase.GetAssetPath(this);
            if (string.IsNullOrEmpty(path)) { UnityEngine.Object.DestroyImmediate(capture); return; }

            if (thumbnail == null)
            {
                capture.name = "Thumbnail";
                AssetDatabase.AddObjectToAsset(capture, path);
                thumbnail = capture;
            }
            else
            {
                if (thumbnail.width != capture.width || thumbnail.height != capture.height)
                    thumbnail.Reinitialize(capture.width, capture.height);
                thumbnail.SetPixels32(capture.GetPixels32());
                thumbnail.Apply();
                UnityEngine.Object.DestroyImmediate(capture);
            }
            EditorUtility.SetDirty(thumbnail);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }
#endif
    }

    /// <summary>
    /// One placed previewable. <see cref="content"/>'s actual runtime type IS the kind — a Zoe, a Pyre,
    /// or a Sprite (the background case) — deliberately no separate kind enum that could drift out of sync.
    /// </summary>
    [Serializable]
    public class PreviewableEntry
    {
        [Tooltip("Stable id so a live GameObject can be matched back to this entry across re-realizes.")]
        public string id = Guid.NewGuid().ToString("N");

        [Tooltip("A Zoe, a Pyre, or a Sprite (background). Kind is read from this object's type.")]
        public UnityEngine.Object content;

        public Vector2 position;
        [Tooltip("Author-chosen multiplier ON TOP OF the displayPixelsPerUnit normalization, not instead of it.")]
        public float scale = 1f;

        [Header("Zoe-only")]
        public WeaponDef weapon;
        [Tooltip("Drives this entry's OWN motion (matches MirageSubject's existing semantics).")]
        public Choreography choreography;
        [Tooltip("Cycles through these clips in order, forever (e.g. Idle, Shoot, repeat) -- always, there's no " +
                 "separate single-clip mode. A one-item list just replays that one clip forever, same effect " +
                 "the old standalone Clip+Loop fields had. Each step owns its OWN fire-weapon trigger (below) " +
                 "instead of one entry-wide layer -- a global trigger couldn't tell WHICH step's MetaLayer it " +
                 "meant once more than one clip could be playing, which is exactly the bug this replaced.")]
        public List<ClipStep> clips = new List<ClipStep>();
        [Tooltip("Stands in for whatever would normally drive Combatant.aimDirection in real gameplay (player " +
                 "input, AI) — Mirage has neither, so firing needs an explicit direction to preview with. NOT " +
                 "baked into any clip; one fixed value for this whole preview, same as each ClipStep's own " +
                 "fireWeapon/muzzleLayerId are stand-ins for real trigger logic.")]
        public Vector2 previewAimDirection = Vector2.right;

        [Tooltip("Freeze this entry onto one named, real game-state pose (\"Idle N\", \"Moving E\") instead of " +
                 "driving off Mirage's own stand-in input — picked from Laubrary.ZoetropeLaunimator.MotionPoseCatalog's " +
                 "auto-derived list for this Zoe (never typed free-hand). Only meaningful for a Zoe whose parts " +
                 "author a MotionPose. Empty = normal live-driven preview, unchanged.")]
        public string previewPose = "";

        [Tooltip("A Mirage-only testing convenience — opt in to a self-contained Idle -> (Hurt) -> Death -> " +
                 "respawn loop for THIS preview entry, replacing the Clips list above. Uses the real spawn " +
                 "path, Health, and the Zoe's own hitReaction for the hurt/death clips (same as a real level " +
                 "would), but the respawning-dummy behavior itself is Mirage-side, not saved onto the Zoe. " +
                 "Idle is always the Zoe's own view.idleClip (not a separate field here) -- redundant with " +
                 "data the Zoe already has, and Target Practice is meant for the simple case anyway.")]
        public bool targetPractice;
        [Tooltip("Seconds after the death reaction finishes (or immediately, if there's no death clip) before reviving.")]
        [Min(0f)] public float respawnDelay = 2f;
    }

    /// <summary>
    /// One step in a PreviewableEntry's Clips list — a clip name plus its OWN fire-weapon trigger. Per-step
    /// (not one entry-wide autoFire/muzzleLayerId pair) because different clips in the same sequence paint
    /// different MetaLayers under possibly-different ids ("Muzzle" on Shoot, nothing on Idle) — a single
    /// shared trigger couldn't express "fire during THIS step, not that one" and silently computed its
    /// available layers from whichever clip a separate, now-removed standalone Clip field happened to point
    /// at, not whatever was actually about to play.
    /// </summary>
    [Serializable]
    public class ClipStep
    {
        public string clip = "";
        public bool fireWeapon;
        public string muzzleLayerId = "";
        [Tooltip("Alternative to muzzleLayerId — the name of a FrameEvent (with an authored pixel position) " +
                 "that fires this step's weapon at its point instead. Takes priority over muzzleLayerId when set.")]
        public string muzzleEventName = "";
    }
}
