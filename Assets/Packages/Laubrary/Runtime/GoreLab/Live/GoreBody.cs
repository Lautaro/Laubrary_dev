using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Combat2D;
using Laubrary.Launimator;
using Laubrary.MetaMapper;

namespace Laubrary.GoreLab
{
    /// <summary>What a wound did, for game code that wants to react (sound, score, screen shake...).</summary>
    public struct GoreWoundEvent
    {
        public IWoundRecipe recipe;
        public int group;
        public Vector2 world;
        public int pixelsChanged;
    }

    /// <summary>
    /// Adds wounds to a spawned character without the character knowing. Attach it next to a character that plays its animation through a
    /// ZonedAnimationPlayer and hand it a GoreRig. It keeps the list of wounds as shapes in each body member's own coordinates, so one wound is
    /// right on every frame and direction, and shows them by swapping in a baked picture of the frame that is on screen.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    public sealed partial class GoreBody : MonoBehaviour
    {
        [Header("Rig")]
        [Tooltip("The gore data for this character: which body members exist on each drawn frame and which damage types it offers.")]
        public GoreRig rig;

        [Header("Hits")]
        [Tooltip("Also wound when this character's Health takes damage from elsewhere (uses the rig's first damage type, along the hit direction). Off by default: most games call ApplyWound themselves.")]
        public bool woundOnHealthDamage;

        [Header("Flying pieces")]
        [Tooltip("How fast a torn-off piece leaves, in world units per second.")]
        public float launchSpeed = 5.5f;
        [Tooltip("Extra upward kick given to pieces and crumbs, in world units per second.")]
        public float launchLift = 2f;
        [Tooltip("Gravity on pieces and blood, in world units per second squared.")]
        public float gravity = 22f;
        [Tooltip("How much a piece bounces off the floor (0 = dead stop, 1 = perfect bounce).")]
        [Range(0f, 1f)] public float bounciness = 0.25f;
        [Tooltip("How long pieces and crumbs last before fading, in seconds.")]
        public float pieceLife = 3f;

        [Header("Blood")]
        [Tooltip("Scales how many crumbs and blood drops there are. 0 = clean holes.")]
        [Range(0f, 1f)] public float gore = 0.6f;
        [Tooltip("How hard a fresh wound bleeds at first (the spray fades by itself).")]
        public float bleedPressure = 0.8f;
        [Tooltip("How long the bleeding takes to fade to a third, in seconds.")]
        public float bleedFade = 2.6f;
        [Tooltip("The fastest a blood drop leaves a wound, in world units per second.")]
        public float bleedSpeed = 7f;

        /// <summary>Hands out the bake work. The default bakes at once; swap in a queue or a faster bake without touching anything else.</summary>
        public IGoreBakeScheduler scheduler = new ImmediateGoreScheduler();

        public event Action<GoreWoundEvent> Wounded;

        public bool HasWounds { get { return _removers.Count > 0; } }

        ZonedAnimationPlayer _player;
        SpriteRenderer _sr;
        Health _health;
        readonly List<GoreRemover> _removers = new List<GoreRemover>();
        readonly Dictionary<(Sprite, bool), GoreViewSlot> _slots = new Dictionary<(Sprite, bool), GoreViewSlot>();
        readonly GoreFrameResult _result = new GoreFrameResult();
        int _version;
        int _shots;
        bool _swapped;
        string _clipCacheName;
        readonly List<Sprite> _clipSprites = new List<Sprite>();

        bool Ready { get { return rig != null && _player != null && _sr != null; } }

        /// <summary>Gives a character the wound look in code: adds the component (if missing) and points it at the rig. In the editor the same is one Add Component and one object field.</summary>
        public static GoreBody Attach(GameObject character, GoreRig rig)
        {
            var body = character.GetComponent<GoreBody>();
            if (body == null) body = character.AddComponent<GoreBody>();
            body.rig = rig;
            return body;
        }

        void Awake()
        {
            _player = GetComponent<ZonedAnimationPlayer>();
            if (_player == null) _player = GetComponentInChildren<ZonedAnimationPlayer>();
            _sr = _player != null ? _player.GetComponent<SpriteRenderer>() : GetComponent<SpriteRenderer>();
            _health = GetComponent<Health>();
        }

        void OnEnable() { if (_health != null) _health.Damaged += OnHealthDamaged; }
        void OnDisable() { if (_health != null) _health.Damaged -= OnHealthDamaged; }

        void OnDestroy()
        {
            foreach (var kv in _slots) if (kv.Value != null) kv.Value.Destroy();
            _slots.Clear();
            DestroyDebrisAssets();
        }

        void OnHealthDamaged(DamageInfo info)
        {
            if (!woundOnHealthDamage || rig == null || rig.recipes == null || rig.recipes.Count == 0) return;
            Vector2 dir = info.direction.sqrMagnitude > 1e-6f ? info.direction.normalized : Vector2.right;
            ApplyWound(rig.recipes[0], info.point - dir * 0.5f, info.point + dir * 0.5f);
        }

        // Runs after the animation player and the facing resolver have settled this frame's picture, so the swap is the last word before drawing.
        void LateUpdate()
        {
            TickBleed();
            if (!Ready) return;
            if (_removers.Count == 0 || !_sr.enabled) { RestoreDisplay(); return; }
            Sprite drawn = _player.CurrentSprite;
            if (drawn == null || !rig.TryGetFrame(drawn, out var tags)) { RestoreDisplay(); return; }
            bool flip = _player.flipX;
            var slot = GetSlot(drawn, flip, tags);
            if (slot == null) { RestoreDisplay(); return; }
            if (slot.bakedVersion != _version) scheduler.Request(new GoreBakeJob { body = this, slot = slot });
            Sprite wounded = slot.WoundedSprite;
            if (wounded == null || slot.bakedVersion < 0) { RestoreDisplay(); return; }
            _sr.sprite = wounded;
            _sr.flipX = false;                 // a mirrored view is already baked mirrored
            _swapped = true;
        }

        void RestoreDisplay()
        {
            if (!_swapped) return;
            _sr.sprite = _player.CurrentSprite;  // put the drawn frame back at once; a still pose would otherwise keep the wounded picture
            _sr.flipX = _player.flipX;
            _swapped = false;
        }

        // ------------------------------------------------------------------ wounds

        /// <summary>
        /// Wounds the character along a drag from worldFrom to worldTo using the recipe (a swipe for a slice or cut, an aim line for a shot).
        /// Returns false if nothing visible changed (the aim missed, the frame has no tags, or the cut lies on a side this frame does not show).
        /// seed: a fixed number makes the wound repeatable; -1 uses a counter so each wound differs.
        /// </summary>
        public bool ApplyWound(IWoundRecipe recipe, Vector2 worldFrom, Vector2 worldTo, int seed = -1, bool flipSide = false)
        {
            if (!Ready || recipe == null || !_sr.enabled) return false;
            Sprite drawn = _player.CurrentSprite;
            if (drawn == null || !rig.TryGetFrame(drawn, out var tags)) return false;
            bool flip = _player.flipX;
            var slot = GetSlot(drawn, flip, tags);
            if (slot == null) return false;

            Vector2 a = WorldToView(drawn, flip, worldFrom), b = WorldToView(drawn, flip, worldTo);
            int shot = seed >= 0 ? seed : _shots;
            var ctx = new WoundContext
            {
                sliceable = rig.SliceableFlags(),
                p0x = a.x, p0y = a.y, p1x = b.x, p1y = b.y,
                group = NextGroup(),
                seed = rig.cut.seed * 131 + shot,
                shotCounter = shot,
                grid = slot.grid,
                members = slot.members,
                targets = null,
                cut = rig.cut,
                flipSide = flipSide,
                holeVisibility = op => HoleVisibility(op, flip),
            };
            for (int i = 0; i < _removers.Count; i++) ctx.existing.Add(_removers[i]);

            var added = new List<GoreRemover>();
            recipe.Generate(ctx, added);
            if (added.Count == 0) return false;

            int before = _removers.Count;
            _removers.AddRange(added);
            _version++;
            BakeNow(slot);                       // now, not queued: what flew off is read from this result
            if (_result.chunkCount == 0)
            {
                _removers.RemoveRange(before, added.Count);   // nothing this view shows changed: the wound does not exist
                _version++;
                return false;
            }
            if (seed < 0) _shots++;

            SpawnDebris(slot, _result, flip);
            StartBleeding();
            var handler = Wounded;
            if (handler != null) handler(new GoreWoundEvent { recipe = recipe, group = ctx.group, world = worldTo, pixelsChanged = _result.changed });
            return true;
        }

        /// <summary>Puts the character back as drawn. Pieces and drops already in the air keep flying.</summary>
        public void ResetWounds()
        {
            _removers.Clear();
            _version++;
            StopBleeding();
            if (_player != null && _sr != null) RestoreDisplay();
        }

        int NextGroup()
        {
            int g = -1;
            for (int i = 0; i < _removers.Count; i++) if (_removers[i].group > g) g = _removers[i].group;
            return g + 1;
        }

        // ------------------------------------------------------------------ views and baking

        GoreViewSlot GetSlot(Sprite drawn, bool flip, GoreFrameTags tags)
        {
            if (_slots.TryGetValue((drawn, flip), out var existing)) return existing;
            GoreGrid grid = GoreSourcePixels.Read(drawn, flip);
            if (grid == null) { _slots[(drawn, flip)] = null; return null; }
            var slot = new GoreViewSlot
            {
                source = drawn, flipped = flip, tags = tags, w = grid.w, h = grid.h, grid = grid,
                members = GoreRigInputs.Build(tags, grid.w, grid.h, flip, rig.members.Count),
            };
            _slots[(drawn, flip)] = slot;
            return slot;
        }

        /// <summary>Bakes every wound into this view's picture. Called by the scheduler.</summary>
        public void BakeNow(GoreViewSlot s)
        {
            if (s == null) return;
            var input = new GoreFrameInput { grid = s.grid, sx = 0, sy = 0, members = s.members };
            GoreCut.CutFrame(input, _removers, rig.cut, rig.style, _result);
            s.missing = _result.missing;
            s.changed = _result.changed;
            s.bleed = _result.bleed.ToArray();
            s.EnsureTexture(s.source.pixelsPerUnit);
            var body = _result.body;
            for (int y = 0; y < s.h; y++) Array.Copy(body, y * s.w, s.upload, (s.h - 1 - y) * s.w, s.w);   // texture rows run bottom-up
            s.texture.SetPixelData(s.upload, 0);
            s.texture.Apply(false);
            s.bakedVersion = _version;
        }

        // ------------------------------------------------------------------ space conversion

        // Sprite pixels of the VIEW with the engine's convention (origin top-left, y down). A mirrored view's pixel x runs the other way.
        Vector2 WorldToView(Sprite drawn, bool flip, Vector2 world)
        {
            Vector2 m = MetaMapSprite.ToMap(drawn, _sr.transform, flip, new Vector3(world.x, world.y, 0f));
            float w = drawn.rect.width, h = drawn.rect.height;
            return new Vector2(flip ? w - m.x : m.x, h - m.y);
        }

        Vector3 ViewToWorld(Sprite drawn, bool flip, double x, double y)
        {
            float w = drawn.rect.width, h = drawn.rect.height;
            return MetaMapSprite.ToWorld(drawn, _sr.transform, flip, new Vector2(flip ? w - (float)x : (float)x, h - (float)y));
        }

        // ------------------------------------------------------------------ hole visibility (for recipes that choose a spot)

        // The share of this walk direction's frames that would show a hole at this remover, so a bullet is not wasted on a spot most frames hide.
        readonly List<GoreGrid> _scoreGrids = new List<GoreGrid>();
        readonly List<GoreMemberInput[]> _scoreMembers = new List<GoreMemberInput[]>();

        double HoleVisibility(GoreRemover op, bool flip)
        {
            List<Sprite> frames = CurrentClipSprites();
            _scoreGrids.Clear(); _scoreMembers.Clear();
            for (int i = 0; i < frames.Count; i++)
            {
                Sprite spr = frames[i];
                if (spr == null || !rig.TryGetFrame(spr, out var tags)) continue;
                var slot = GetSlot(spr, flip, tags);
                if (slot == null) continue;
                _scoreGrids.Add(slot.grid);
                _scoreMembers.Add(slot.members);
            }
            return GoreHoleVisibility.Score(op, _scoreGrids, _scoreMembers);
        }

        List<Sprite> CurrentClipSprites()
        {
            string clip = _player.CurrentClip;
            if (clip == _clipCacheName) return _clipSprites;
            _clipCacheName = clip;
            _clipSprites.Clear();
            if (clip != null && _player.version != null)
                foreach (var anim in _player.version.animations)
                    if (anim != null && anim.name == clip && anim.frames != null) { _clipSprites.AddRange(anim.frames); break; }
            return _clipSprites;
        }
    }
}
