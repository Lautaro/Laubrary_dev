using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Laubrary.Launimator
{
    /// <summary>
    /// A reusable, versioned reel: the portable artifact other projects consume. A reel is a
    /// folder identified by a stable <see cref="reelId"/> (independent of any Unity asset GUID, so it
    /// survives being copied between projects). It owns one editable <c>draft</c> version plus a series of
    /// immutable snapshots <c>v1, v2, …</c>. This root asset carries only identity and bookkeeping; the
    /// animations themselves live on each version's <see cref="ReelVersion"/>.
    /// </summary>
    public class Reel : ScriptableObject
    {
        [FormerlySerializedAs("zoeId")]
        [Tooltip("Stable identity, set once at creation. Lets different projects reuse this reel safely. " +
                 "NOT the Unity asset GUID.")]
        public string reelId;

        [FormerlySerializedAs("zoeName")]
        [Tooltip("Display name. Drives the folder name only at creation; renaming updates this field and the " +
                 "asset file names, but the folder is frozen.")]
        public string reelName = "NewReel";

        [Tooltip("Highest committed version number (0 means only a draft exists so far).")]
        public int latestVersion;

        [Tooltip("Pixels-per-unit used when slicing/baking this reel's sprites.")]
        public float pixelsPerUnit = 16f;
    }

    /// <summary>
    /// One frame's source recipe: where its pixels come from and how it is registered. This is the editable
    /// source-of-truth; the baked atlas frame is derived from it. <see cref="cell"/> is a rect in the source
    /// sheet (texture px, bottom-left origin) and <see cref="pivot"/> is the normalized registration anchor
    /// within that cell.
    /// </summary>
    [System.Serializable]
    public class FrameRef
    {
        public string sourceTextureGuid;
        public Rect cell;
        public Vector2 pivot = new Vector2(0.5f, 0f);

        [Tooltip("Per-frame sprite edit (flip / rotate / squash-stretch), baked into the frame at registration " +
                 "time. Identity by default — only set by the Animation Builder's UI.4 tools.")]
        public CellTransform transform = CellTransform.Identity;
    }

    /// <summary>
    /// A per-sprite edit applied when the frame is baked: mirror, rotate (lossless 90° steps plus an optional
    /// arbitrary angle), and squash/stretch. It is metadata on the recipe — the source sheet is never touched,
    /// and the edit bakes in once (in <c>AtlasBaker.Compose</c>) so it renders identically everywhere. The
    /// all-zero struct (how old saved data deserializes, and a freshly added cell) IS identity: scale 0 is
    /// treated as 1, so nothing changes unless a tool sets a field.
    /// </summary>
    [System.Serializable]
    public struct CellTransform
    {
        public bool flipX, flipY;
        public int rot90;          // lossless quarter-turns (CCW); any integer, used mod 4
        public float angle;        // extra arbitrary rotation, degrees CCW (0 = none)
        public float scaleX, scaleY; // squash/stretch multipliers (0 or 1 = none)
        public bool smooth;        // bilinear sampling for angle/scale (else crisp nearest-neighbor)

        public static CellTransform Identity => new CellTransform { scaleX = 1f, scaleY = 1f };
        public float SX => scaleX <= 0f ? 1f : scaleX;
        public float SY => scaleY <= 0f ? 1f : scaleY;
        public bool IsIdentity =>
            !flipX && !flipY && (rot90 % 4) == 0 && Mathf.Abs(angle) < 0.01f
            && Mathf.Abs(SX - 1f) < 1e-4f && Mathf.Abs(SY - 1f) < 1e-4f;
    }

    /// <summary>
    /// An authored event on a specific frame of an animation — the metadata the consuming game reacts to.
    /// Canonical use is "hit" (sync a weapon's damage / projectile to the swing's contact frame), but it's
    /// a free-form name so projects can add "footstep", "sfx", "spawn", etc. <see cref="ReelPlayer"/>
    /// fires <c>OnFrameEvent(name, frame)</c> when playback ENTERS the frame (once per play-through / loop).
    /// </summary>
    [System.Serializable]
    public class FrameEvent
    {
        [Tooltip("Zero-based frame index this event fires on.")]
        public int frame;
        [Tooltip("Event name the consumer matches on (e.g. \"hit\", \"footstep\", \"sfx\").")]
        public string name = "hit";
        [Tooltip("Optional Zound (by name) auto-played when this event fires, via the Launimator.Zounds bridge. Empty = no sound.")]
        public string zoundName = "";
    }

    /// <summary>
    /// One composable VISIBLE sprite layer of an animation (e.g. body, or a swappable weapon), aligned 1:1 with
    /// the animation's frames. When an animation has sprite layers, the player renders them stacked (a child
    /// SpriteRenderer each, in list order) instead of the flat <see cref="AnimationDef.frames"/>, and they can be
    /// toggled at runtime (weapon swap). <see cref="AnimationDef.frames"/> is still kept as the flat composite
    /// (used for collision mask mapping + as the fallback when there are no layers).
    /// </summary>
    [System.Serializable]
    public class SpriteLayer
    {
        public string id = "layer";
        public List<Sprite> frames = new List<Sprite>();
    }

    /// <summary>
    /// A single named animation. <see cref="recipe"/> is the editable source-of-truth (per-frame source
    /// rects + registration). On build it is baked into a self-contained, uniform-size <see cref="atlas"/>
    /// texture whose sliced <see cref="frames"/> register exactly — so animations never share (and can never
    /// corrupt) each other's sprites, and the reel no longer depends on the original rip sheet to PLAY
    /// (only to re-edit). <see cref="clip"/> is the generated looping AnimationClip over <see cref="frames"/>.
    /// </summary>
    [System.Serializable]
    public class AnimationDef
    {
        public string name = "Idle";
        public float fps = 12f;

        [Tooltip("Editable source-of-truth: each frame's source rect + registration. Drives baking + re-editing.")]
        public List<FrameRef> recipe = new List<FrameRef>();

        [Tooltip("Authored per-frame events (e.g. the \"hit\" frame for attack→damage sync). Survive baking; " +
                 "ReelPlayer fires OnFrameEvent(name, frame) as playback enters each frame.")]
        public List<FrameEvent> events = new List<FrameEvent>();

        [Tooltip("Source sheet uses a solid background COLOR (no alpha) — bake it out to real transparency.")]
        public bool bgKeyEnabled;

        [Tooltip("The background color treated as transparent when baking (only if bgKeyEnabled).")]
        public Color32 bgKey = new Color32(0, 0, 0, 255);

        [Tooltip("Per-channel tolerance (0..255) when matching bgKey, for slightly noisy backgrounds.")]
        public int bgKeyTolerance = 12;

        [Tooltip("If true, every frame bakes into a fixed frameWidth×frameHeight box (manual placement). " +
                 "If false, the atlas auto-sizes to fit all frames around their shared registration point.")]
        public bool fixedFrame;

        [Tooltip("Fixed-frame box size in source px (only used when fixedFrame is true).")]
        public int frameWidth = 32;
        public int frameHeight = 32;

        [Tooltip("Where every frame registers within the box (normalized, bottom-left origin). The baked " +
                 "sprites' shared pivot. Default bottom-center.")]
        public Vector2 framePivot = new Vector2(0.5f, 0f);

        [Tooltip("Baked, uniform-size atlas frames (generated from the recipe). These play in the clip/preview. " +
                 "When spriteLayers is non-empty this is the flat COMPOSITE (kept for collision mapping + fallback).")]
        public List<Sprite> frames = new List<Sprite>();

        [Tooltip("Composable visible sprite layers (body + swappable weapons). When non-empty the player renders " +
                 "these stacked (toggleable) instead of the flat `frames`. Aligned 1:1 with the frames.")]
        public List<SpriteLayer> spriteLayers = new List<SpriteLayer>();

        [Tooltip("The generated, self-contained atlas texture for this animation.")]
        public Texture2D atlas;

        [Tooltip("Asset GUID of the primary source sheet (for re-editing convenience).")]
        public string sourceTextureGuid;

        [Tooltip("The generated, looping AnimationClip over the baked frames.")]
        public AnimationClip clip;

        [Tooltip("Whether this animation uses meta-layers (gameplay overlays). Off keeps the editor UI clean.")]
        public bool metaLayersEnabled;

        [Tooltip("Authored meta-layers (hitbox/muzzle/trail masks + per-frame params), aligned to the sequence.")]
        public List<MetaLayer> metaLayers = new List<MetaLayer>();

        [Tooltip("Whether this animation is a phased 'strip' divided into zones (e.g. a Jump's Start/Air/Fall/" +
                 "Land). When off it plays as a plain clip. See ANIMATION_CONTROLLER.md.")]
        public bool zonesEnabled;

        [Tooltip("Ordered, contiguous frame ranges that make this a phased move, traversed by ZonedAnimationPlayer.")]
        public List<AnimZone> zones = new List<AnimZone>();

        [Tooltip("Project path of this animation's editable .aseprite source, if it has been promoted to Aseprite " +
                 "editing (lives in the version's Source/ folder). Empty = rip-only (recipe is the source). Set by " +
                 "the Builder's 'Edit anim in Aseprite'; 'Sync from Aseprite' reads it back into the owned source.")]
        public string asepriteSourcePath = "";
    }
}
