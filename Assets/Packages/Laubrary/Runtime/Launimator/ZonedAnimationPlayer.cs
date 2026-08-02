using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Launimator
{
    /// <summary>
    /// The "spine" from ANIMATION_CONTROLLER.md: plays a reel's animations by name and, when an animation
    /// is a zoned strip, walks its zones as a phased move. The owning game logic ("the brain") issues a tiny
    /// verb set — Play / EnterAt / Advance / EndIn — and can override at any time by just calling Play again.
    ///
    /// It deliberately does its OWN frame stepping (over <see cref="AnimationDef.frames"/>) rather than going
    /// through <see cref="AnimationPlayback"/>, because zones need sub-range looping (Hold) and retimed
    /// straight-to-end playback (EndIn) that the whole-clip player doesn't model. A plain (non-zoned) animation
    /// just loops, exactly like a normal clip.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class ZonedAnimationPlayer : MonoBehaviour
    {
        [Tooltip("The reel version whose animations this plays.")]
        public ReelVersion version;

        [Tooltip("Mirror horizontally (facing). Applied to the SpriteRenderer each frame.")]
        public bool flipX;

        [Tooltip("Global speed knob: 1 = authored fps, 0 = paused.")]
        public float speedScale = 1f;

        [Tooltip("Cap on how many distinct frames/second EndIn will show; above this it decimates (drops frames) " +
                 "while still finishing on time and always landing the final frame.")]
        public float maxFps = 24f;

        SpriteRenderer _sr;
        readonly Dictionary<string, AnimationDef> _byName = new Dictionary<string, AnimationDef>(StringComparer.OrdinalIgnoreCase);

        // Composable visible sprite layers (body + swappable weapons): one child SpriteRenderer each, stacked.
        readonly List<SpriteRenderer> _layerRenderers = new List<SpriteRenderer>();
        AnimationDef _layerBuiltFor;
        readonly HashSet<string> _hiddenLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AnimationDef _anim;
        List<AnimZone> _zones;     // sanitized copy when the current anim is zoned, else null
        int _zoneIdx = -1;
        int _i;                    // current frame index into _anim.frames
        float _t;                  // sub-frame accumulator [0..1)
        bool _playing;
        bool _loop;                // plain (non-zoned) mode loop flag
        string _wantZone;          // a zone the brain asked for; a Loop zone switches to it at its cycle END (smooth)

        // EndIn retime state
        bool _ending;
        float _endElapsed, _endDur, _endFrameTimer;
        int _endStart, _endLast;
        Action _endDone;

        /// <summary>Fires when a strip/clip finishes on its own (last zone's PlayThrough ends, or a one-shot ends).
        /// EndIn uses its own onDone callback instead.</summary>
        public event Action OnComplete;

        /// <summary>Fires (eventName, frame) when playback ENTERS a frame carrying an authored FrameEvent
        /// (e.g. "Lift Off"). The brain uses these to time gameplay to the animation — author them in the
        /// Animation Builder's "Frame events" section.</summary>
        public event Action<string, int> OnFrameEvent;

        /// <summary>Fires once per frame-entry (edge-triggered — same convention as <see cref="OnFrameEvent"/>)
        /// for every MetaLayer with painted data on the just-entered frame. worldPos is the same value-weighted
        /// centroid <see cref="TryGetMetaPoint"/> computes. Lets gameplay subscribe once ("when the animator
        /// paints a point on layer X, do Y") instead of sampling at play-time via
        /// <see cref="PlayAndCaptureMetaPoint"/> — the animator then controls exactly which frame the point
        /// fires on purely by where they paint it.</summary>
        public event Action<string /*layerId*/, Vector3 /*worldPos*/> OnMetaLayerReached;

        /// <summary>Fires once per frame-entry with the frame index (edge-triggered, same convention as
        /// <see cref="OnFrameEvent"/>) — for EVERY frame, annotated or not. Authored frame events answer
        /// "something named happens here"; this answers "which frame are we on", which is what lets a
        /// consumer time to the animation without the animator having to author an event first.</summary>
        public event Action<int> OnFrameEntered;

        public string CurrentClip => _anim != null ? _anim.name : null;
        public bool IsZoned => _zones != null;
        public bool IsPlaying => _playing;
        public int CurrentFrame => _i;
        public bool HasFrames => _anim != null && _anim.frames != null && _anim.frames.Count > 0;
        public Sprite CurrentSprite => (HasFrames && _i >= 0 && _i < _anim.frames.Count) ? _anim.frames[_i] : null;
        public string CurrentZoneName => (_zones != null && _zoneIdx >= 0 && _zoneIdx < _zones.Count) ? _zones[_zoneIdx].name : null;
        /// <summary>The current animation's meta-layers (empty if none) — for debug visualisation.</summary>
        public IReadOnlyList<MetaLayer> CurrentMetaLayers => (IReadOnlyList<MetaLayer>)_anim?.metaLayers ?? Array.Empty<MetaLayer>();
        /// <summary>The current animation's authored frame events (empty if none) — lets a listener re-look-up
        /// the specific FrameEvent behind an OnFrameEvent firing (e.g. to read its zoundName).</summary>
        public IReadOnlyList<FrameEvent> CurrentEvents => (IReadOnlyList<FrameEvent>)_anim?.events ?? Array.Empty<FrameEvent>();

        void Awake() { _sr = GetComponent<SpriteRenderer>(); Reindex(); }
        void Update() => Tick(Time.deltaTime);

        public void SetVersion(ReelVersion v) { version = v; Reindex(); Stop(); }

        void Reindex()
        {
            _byName.Clear();
            if (version == null || version.animations == null) return;
            foreach (var a in version.animations)
                if (a != null && !string.IsNullOrEmpty(a.name)) _byName[a.name] = a;
        }

        /// <summary>
        /// Starts <paramref name="clip"/> as a one-shot (<c>loop: false</c>) and captures a named MetaLayer's
        /// point at that exact instant — the shared definition of "trigger an effect at a point on this
        /// animation, captured once when the action starts" (a muzzle flash, a footstep dust cloud). Gameplay
        /// code and Pyre's own live preview (see the <c>Pyre.Launimator.Editor</c> bridge) both call THIS method
        /// rather than each separately calling <see cref="Play"/> then <see cref="TryGetMetaPoint"/> — so the
        /// two can never drift out of alignment with each other; whatever this does IS "where/when that point
        /// is," for both. Capturing once matters: re-sampling the point every frame instead reads as jumping
        /// between "aligned" and "not found" as the clip's later frames — which usually don't carry the same
        /// painted data — play out. Returns false (and leaves <paramref name="capturedWorldPos"/> at its
        /// default) if the clip is unknown or the layer has no painted point on the frame the clip starts on.
        /// </summary>
        public bool PlayAndCaptureMetaPoint(string clip, string metaLayerId, out Vector3 capturedWorldPos)
        {
            Play(clip, loop: false);
            return TryGetMetaPoint(metaLayerId, out capturedWorldPos, out _);
        }

        // ── verbs ────────────────────────────────────────────────────────────
        /// <summary>Start an animation from its beginning. Zoned strips begin at zone 0 and traverse; plain
        /// clips loop (or play once when <paramref name="loop"/> is false). Returns false if the name is unknown.</summary>
        public bool Play(string clip, bool loop = true)
        {
            if (string.IsNullOrEmpty(clip) || !_byName.TryGetValue(clip, out var def) || def.frames == null || def.frames.Count == 0)
                return false;

            _anim = def;
            _ending = false; _endDone = null; _wantZone = null;
            _t = 0f; _playing = true;

            if (def.zonesEnabled && def.zones != null && def.zones.Count > 0)
            {
                _zones = Sanitize(def);
                _zoneIdx = 0;
                _i = _zones[0].startFrame;
            }
            else
            {
                _zones = null; _zoneIdx = -1;
                _loop = loop;
                _i = 0;
            }
            PushSprite();
            FireFrameEvents(); // frame-0 events
            FireMetaLayerReached();
            return true;
        }

        /// <summary>Jump straight to a named zone (ledge-fall enters at "Fall"; a hover toggles back to "Air";
        /// resume after an interrupt). Idempotent: if already in that zone it keeps looping rather than restarting,
        /// so rapid toggles don't stutter. Returns false if not zoned or the zone is unknown.</summary>
        public bool EnterAt(string zoneName)
        {
            if (_zones == null) return false;
            int idx = _zones.FindIndex(z => string.Equals(z.name, zoneName, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return false;
            if (idx == _zoneIdx && !_ending) return true; // already here — don't reset
            _zoneIdx = idx; _i = _zones[idx].startFrame; _t = 0f;
            _ending = false; _playing = true; _wantZone = null;
            PushSprite();
            FireFrameEvents();
            FireMetaLayerReached();
            return true;
        }

        /// <summary>Ask to move to a named zone, but SMOOTHLY: a Loop zone keeps playing to its end (its last
        /// frame flows into the next zone), then switches there. Call it freely each frame — it just records the
        /// latest target. No-op if already in that zone, not zoned, or the zone is unknown. For an INSTANT cut
        /// use <see cref="EnterAt"/> instead.</summary>
        public void WantZone(string zoneName)
        {
            if (_zones == null) return;
            if (string.Equals(CurrentZoneName, zoneName, StringComparison.OrdinalIgnoreCase)) { _wantZone = null; return; }
            _wantZone = zoneName;
        }

        /// <summary>Leave the current zone for the next one (used to exit a Loop, e.g. Air → Fall). No-op past the
        /// last zone.</summary>
        public void Advance()
        {
            if (_zones == null) return;
            AdvanceZone();
            _t = 0f; PushSprite();
        }

        /// <summary>Retime from the current frame straight to the LAST frame so the strip finishes in
        /// <paramref name="seconds"/> (ignoring Hold loops), then call <paramref name="onDone"/>. Speeds up to fit;
        /// past <see cref="maxFps"/> it drops in-between frames but always lands the final frame on time.
        /// This is the "time-warp to a sync point" used for landings/attacks/deaths.</summary>
        public void EndIn(float seconds, Action onDone = null)
        {
            if (!HasFrames) { onDone?.Invoke(); return; }
            _ending = true; _playing = true;
            _endElapsed = 0f; _endDur = Mathf.Max(0.0001f, seconds);
            _endFrameTimer = 999f; // allow an immediate first sample
            _endStart = _i;
            _endLast = _zones != null ? _zones[_zones.Count - 1].endFrame : _anim.frames.Count - 1;
            if (_endLast < _endStart) _endLast = _endStart;
            _endDone = onDone;
            PushSprite();
        }

        public void Stop() { _playing = false; _ending = false; }

        /// <summary>Length of a named clip in seconds at this player's current <see cref="speedScale"/>, or 0
        /// when it cannot be known: an unregistered name, no frames, a non-positive rate — or a ZONED strip,
        /// whose zones can hold/loop indefinitely so it has no fixed length. Consumers (reaction effects timing
        /// themselves to a clip) treat 0 as "unknown" and degrade to a single play.</summary>
        public float GetClipSeconds(string clip)
        {
            if (string.IsNullOrEmpty(clip) || !_byName.TryGetValue(clip, out var def)) return 0f;
            if (def.frames == null || def.frames.Count == 0) return 0f;
            if (def.zonesEnabled && def.zones != null && def.zones.Count > 0) return 0f;
            float fps = def.fps * speedScale;
            return fps > 0f ? def.frames.Count / fps : 0f;
        }

        /// <summary>Stop playback AND blank every managed renderer (base + layers) — "show no clip", so a
        /// clip-less death can be replaced by an explosion. <see cref="Play"/>/<see cref="EnterAt"/> re-show it.
        /// The Stop is essential: without it, the next <see cref="Tick"/> would call <c>PushSprite</c> and
        /// re-enable the renderer, defeating the hide (the bug where a killed target flashed back to idle).</summary>
        public void Hide()
        {
            Stop();
            if (_sr != null) _sr.enabled = false;
            foreach (var r in _layerRenderers) if (r != null) r.enabled = false;
        }

        // ── stepping ─────────────────────────────────────────────────────────
        /// <summary>Advance playback. Called from Update; exposed so tests can step deterministically.</summary>
        public void Tick(float dt)
        {
            if (_sr != null && _sr.flipX != flipX) _sr.flipX = flipX;
            if (!_playing || !HasFrames) return;

            if (_ending) { TickEnding(dt); return; }

            float fps = _anim.fps * speedScale;
            if (fps <= 0f) return;
            _t += dt * fps;
            int guard = 0;
            while (_t >= 1f && guard++ < 512)
            {
                _t -= 1f;
                StepOne();
                if (!_playing) break;
            }
            PushSprite();
        }

        bool _warnedZone;

        void StepOne()
        {
            _i++;
            if (_zones != null && _zones.Count > 0)
            {
                if (_zoneIdx < 0 || _zoneIdx >= _zones.Count)
                {
                    if (!_warnedZone)
                    {
                        _warnedZone = true;
                        Debug.LogWarning($"ZonedAnimationPlayer: zoneIdx={_zoneIdx} out of range (zones={_zones.Count}, " +
                                         $"clip={CurrentClip}, i={_i}, frames={(_anim.frames != null ? _anim.frames.Count : 0)}) — recovered.");
                    }
                    _zoneIdx = Mathf.Clamp(_zoneIdx, 0, _zones.Count - 1);
                }
                var z = _zones[_zoneIdx];
                if (_i > z.endFrame)
                {
                    if (z.behavior == ZoneBehavior.Loop)
                    {
                        // Reached the cycle end: if the brain asked for another zone, switch NOW (smooth seam);
                        // otherwise loop. This is why Air finishes before Fall.
                        if (!TryEnterWanted()) _i = z.startFrame;
                    }
                    else AdvanceZone();                                       // PlayThrough → next zone
                }
            }
            else
            {
                int count = _anim.frames.Count;
                if (_i >= count)
                {
                    if (_loop) _i = 0;
                    else { _i = count - 1; _playing = false; FireComplete(); }
                }
            }
            FireFrameEvents(); // entered frame _i
            FireMetaLayerReached();
        }

        /// <summary>If a zone was requested via WantZone and it's a different zone, switch to its start now and
        /// return true (consuming the request). Used at a Loop zone's cycle end for a smooth boundary transition.</summary>
        bool TryEnterWanted()
        {
            if (string.IsNullOrEmpty(_wantZone)) return false;
            int idx = _zones.FindIndex(zz => string.Equals(zz.name, _wantZone, StringComparison.OrdinalIgnoreCase));
            _wantZone = null;
            if (idx >= 0 && idx != _zoneIdx) { _zoneIdx = idx; _i = _zones[idx].startFrame; return true; }
            return false;
        }

        void AdvanceZone()
        {
            if (_zones == null) return;
            if (_zoneIdx < _zones.Count - 1) { _zoneIdx++; _i = _zones[_zoneIdx].startFrame; }
            else { _i = _zones[_zoneIdx].endFrame; _playing = false; FireComplete(); }
        }

        void TickEnding(float dt)
        {
            _endElapsed += dt; _endFrameTimer += dt;
            float p = Mathf.Clamp01(_endElapsed / _endDur);
            int target = _endStart + Mathf.RoundToInt(p * (_endLast - _endStart));
            if (target != _i && _endFrameTimer >= 1f / Mathf.Max(1f, maxFps)) { _i = target; _endFrameTimer = 0f; PushSprite(); FireFrameEvents(); FireMetaLayerReached(); }
            if (p >= 1f)
            {
                _i = _endLast; PushSprite();
                _ending = false; _playing = false;
                var cb = _endDone; _endDone = null;
                cb?.Invoke();
            }
        }

        void FireComplete() { var cb = OnComplete; cb?.Invoke(); }

        /// <summary>Invoke OnFrameEvent for any authored event on the current frame _i.</summary>
        void FireFrameEvents()
        {
            // EVERY frame entry is announced, whether or not the animator authored an event on it. Authored
            // events answer "something named happens here"; this answers the plainer question "which frame
            // are we on", which a consumer timing to the animation needs even on frames nobody annotated.
            // Fired first so a frame-number listener and an event listener see the same frame in the same
            // order, and unconditionally — FireFrameEvents' own early-out below is about authored events.
            OnFrameEntered?.Invoke(_i);

            var evs = _anim != null ? _anim.events : null;
            if (evs == null || OnFrameEvent == null) return;
            for (int k = 0; k < evs.Count; k++)
                if (evs[k] != null && evs[k].frame == _i) OnFrameEvent.Invoke(evs[k].name, _i);
        }

        /// <summary>Invoke OnMetaLayerReached for every MetaLayer with painted data on the current frame _i.</summary>
        void FireMetaLayerReached()
        {
            if (OnMetaLayerReached == null || _anim == null || _anim.metaLayers == null) return;
            var spr = CurrentSprite;
            if (spr == null) return;
            foreach (var layer in _anim.metaLayers)
            {
                if (layer == null || layer.frames == null || _i < 0 || _i >= layer.frames.Count) continue;
                var mf = layer.frames[_i];
                if (!TryComputeCentroid(mf, out _, out double nx, out double ny)) continue;
                OnMetaLayerReached.Invoke(layer.id, MaskToWorld(spr, mf, (float)nx, (float)ny));
            }
        }

        void PushSprite()
        {
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            bool layered = _anim != null && _anim.spriteLayers != null && _anim.spriteLayers.Count > 0;
            if (layered) EnsureLayerRenderers();

            // Base renderer: shows the flat frames when NOT layered; hidden (kept for collision mapping) when layered.
            if (_sr != null)
            {
                _sr.enabled = !layered;
                if (!layered && HasFrames && _i >= 0 && _i < _anim.frames.Count) _sr.sprite = _anim.frames[_i];
            }
            if (!layered) return;

            for (int l = 0; l < _anim.spriteLayers.Count && l < _layerRenderers.Count; l++)
            {
                var lay = _anim.spriteLayers[l]; var r = _layerRenderers[l];
                if (r == null) continue;
                bool vis = !_hiddenLayers.Contains(lay.id);
                r.enabled = vis;
                r.flipX = flipX;
                if (vis && lay.frames != null && _i >= 0 && _i < lay.frames.Count) r.sprite = lay.frames[_i];
            }
        }

        void EnsureLayerRenderers()
        {
            if (_layerBuiltFor == _anim) return;
            foreach (var r in _layerRenderers) if (r != null) DestroyRenderer(r.gameObject);
            _layerRenderers.Clear();
            int baseOrder = _sr != null ? _sr.sortingOrder : 0;
            for (int l = 0; l < _anim.spriteLayers.Count; l++)
            {
                var go = new GameObject($"layer:{_anim.spriteLayers[l].id}");
                go.transform.SetParent(transform, false);
                go.hideFlags = HideFlags.DontSave;
                var r = go.AddComponent<SpriteRenderer>();
                if (_sr != null) { r.sortingLayerID = _sr.sortingLayerID; r.maskInteraction = _sr.maskInteraction; }
                r.sortingOrder = baseOrder + 1 + l;
                _layerRenderers.Add(r);
            }
            _layerBuiltFor = _anim;
        }

        static void DestroyRenderer(GameObject go)
        {
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        /// <summary>Show/hide a composable sprite layer (e.g. swap weapons). No-op if the layer id isn't present.</summary>
        public void SetLayerVisible(string id, bool visible)
        {
            if (visible) _hiddenLayers.Remove(id); else _hiddenLayers.Add(id);
            PushSprite();
        }
        public bool IsLayerVisible(string id) => !_hiddenLayers.Contains(id);
        /// <summary>The current animation's composable sprite layers (empty if it's a flat animation).</summary>
        public IReadOnlyList<SpriteLayer> CurrentSpriteLayers =>
            (IReadOnlyList<SpriteLayer>)_anim?.spriteLayers ?? Array.Empty<SpriteLayer>();

        /// <summary>Clamp/sort a strip's zones into a safe working copy (never mutates the asset).</summary>
        List<AnimZone> Sanitize(AnimationDef def)
        {
            int last = def.frames.Count - 1;
            var list = new List<AnimZone>();
            foreach (var z in def.zones)
            {
                if (z == null) continue;
                int s = Mathf.Clamp(z.startFrame, 0, last);
                int e = Mathf.Clamp(z.endFrame, s, last);
                list.Add(new AnimZone { name = z.name, startFrame = s, endFrame = e, behavior = z.behavior });
            }
            list.Sort((a, b) => a.startFrame.CompareTo(b.startFrame));
            if (list.Count == 0) list.Add(new AnimZone { name = "All", startFrame = 0, endFrame = last, behavior = ZoneBehavior.PlayThrough });
            return list;
        }

        // ── consumer ergonomics (feet-planted height fit, mirrors ReelPlayer) ──
        public Sprite RestingSprite
        {
            get
            {
                if (_anim != null && _anim.frames != null && _anim.frames.Count > 0) return _anim.frames[0];
                // Fall back to the version's FIRST animation — use the ordered list, not _byName (a Dictionary
                // whose enumeration order varies across sessions, which made FitToHeight non-deterministic).
                if (version != null && version.animations != null)
                    foreach (var a in version.animations)
                        if (a != null && a.frames != null && a.frames.Count > 0) return a.frames[0];
                return null;
            }
        }
        public float NativeHeight { get { var s = RestingSprite; return s != null ? s.bounds.size.y : 1f; } }

        /// <summary>Scale so the resting frame is <paramref name="worldUnits"/> tall. With feet-baked pivots this
        /// keeps the feet planted at the transform origin.</summary>
        public void FitToHeight(float worldUnits)
        {
            float h = NativeHeight;
            if (h > 1e-4f) transform.localScale = new Vector3(worldUnits / h, worldUnits / h, 1f);
        }

        // ── meta-layer sampling (the "where / how-much") ─────────────────────────────
        /// <summary>
        /// Sample a meta-layer at the CURRENT frame: outputs the value-weighted centroid of its painted cells in
        /// WORLD space, plus the peak intensity (0..1). Use it to read a "muzzle" exit point on the shot frame, or a
        /// "sword" blade position + strength each frame (the consumer derives velocity from successive points).
        /// Returns false if the layer has no painted cells this frame (or there's no current sprite). Honors the
        /// sprite pivot, pixels-per-unit, transform scale/rotation and <see cref="flipX"/>.
        /// </summary>
        public bool TryGetMetaPoint(string layerId, out Vector3 worldPos, out float strength01)
        {
            worldPos = transform.position; strength01 = 0f;
            var spr = CurrentSprite;
            if (spr == null || _anim == null || _anim.metaLayers == null) return false;

            MetaLayer layer = null;
            foreach (var L in _anim.metaLayers)
                if (L != null && string.Equals(L.id, layerId, StringComparison.OrdinalIgnoreCase)) { layer = L; break; }
            if (layer == null || layer.frames == null || _i < 0 || _i >= layer.frames.Count) return false;

            var mf = layer.frames[_i];
            if (!TryComputeCentroid(mf, out strength01, out double nx, out double ny)) return false;
            worldPos = MaskToWorld(spr, mf, (float)nx, (float)ny);
            return true;
        }

        /// <summary>Value-weighted centroid of a MetaFrame's painted cells, in normalized mask-cell space
        /// (0..w, 0..h) plus peak intensity (0..1) — the shared math behind <see cref="TryGetMetaPoint"/> and
        /// <see cref="FireMetaLayerReached"/>. Returns false if the frame has no painted cells.</summary>
        static bool TryComputeCentroid(MetaFrame mf, out float strength01, out double nx, out double ny)
        {
            strength01 = 0f; nx = 0; ny = 0;
            // An unpainted frame's `cells` is null in memory, but Unity's serializer round-trips a null array
            // to a zero-length one (never back to null) — so an unpainted frame authored this way comes back
            // as cells.Length == 0, not null, and the old null-only guard let it through into the indexed loop
            // below, throwing IndexOutOfRangeException on the very first cell.
            if (mf == null || mf.cells == null || mf.cells.Length < mf.w * mf.h || mf.w <= 0 || mf.h <= 0) return false;

            double sx = 0, sy = 0, sw = 0; int peak = 0;
            for (int y = 0; y < mf.h; y++)
                for (int x = 0; x < mf.w; x++)
                {
                    int v = mf.cells[y * mf.w + x];
                    if (v <= 0) continue;
                    sx += (x + 0.5) * v; sy += (y + 0.5) * v; sw += v;
                    if (v > peak) peak = v;
                }
            if (sw <= 0) return false;
            strength01 = Mathf.Clamp01(peak / 10f);
            nx = sx / sw; ny = sy / sw;
            return true;
        }

        /// <summary>
        /// Fill <paramref name="buffer"/> with every painted meta-cell at the CURRENT frame, across ALL meta-layers,
        /// as world-space quads tinted by the layer colour and faded by cell value. Pure debug-visualisation helper
        /// (clears the buffer first). Returns the number of cells written.
        /// </summary>
        public int CollectMetaCellQuads(List<MetaCellQuad> buffer)
        {
            buffer.Clear();
            var spr = CurrentSprite;
            if (spr == null || _anim == null || _anim.metaLayers == null) return 0;

            foreach (var layer in _anim.metaLayers)
            {
                if (layer == null || layer.frames == null || _i < 0 || _i >= layer.frames.Count) continue;
                var mf = layer.frames[_i];
                if (mf == null || mf.cells == null || mf.cells.Length < mf.w * mf.h || mf.w <= 0 || mf.h <= 0) continue;

                Color baseC = layer.color; if (baseC.a <= 0f) baseC.a = 1f;
                for (int y = 0; y < mf.h; y++)
                    for (int x = 0; x < mf.w; x++)
                    {
                        int v = mf.cells[y * mf.w + x];
                        if (v <= 0) continue;
                        Color col = baseC; col.a = baseC.a * Mathf.Clamp01(v / 10f);
                        buffer.Add(new MetaCellQuad
                        {
                            bl = MaskToWorld(spr, mf, x, y),
                            br = MaskToWorld(spr, mf, x + 1, y),
                            tr = MaskToWorld(spr, mf, x + 1, y + 1),
                            tl = MaskToWorld(spr, mf, x, y + 1),
                            color = col,
                        });
                    }
            }
            return buffer.Count;
        }

        /// <summary>Is <paramref name="worldPoint"/> inside a painted cell of this layer at the CURRENT frame?
        /// Inverse of <see cref="MaskToWorld"/>. Outputs the painted value (0..10). PIXEL-accurate (no capsule fit).</summary>
        public bool IsMetaPainted(string layerId, Vector3 worldPoint, out int value)
        {
            value = 0;
            if (!TryGetLayerFrame(layerId, out _, out var mf, out var spr)) return false;

            Vector3 local = transform.InverseTransformPoint(worldPoint);
            if (flipX) local.x = -local.x;
            float ppu = spr.pixelsPerUnit <= 0f ? 16f : spr.pixelsPerUnit;
            Vector2 pivotPx = spr.pivot;
            float spx = local.x * ppu + pivotPx.x;
            float spy = local.y * ppu + pivotPx.y;
            int cx = Mathf.FloorToInt(spx / (spr.rect.width / mf.w));
            int cy = Mathf.FloorToInt(spy / (spr.rect.height / mf.h));
            if (cx < 0 || cx >= mf.w || cy < 0 || cy >= mf.h) return false;
            value = mf.cells[cy * mf.w + cx];
            return value > 0;
        }

        /// <summary>Append the world-space CENTRE of every painted cell of one layer (current frame) to the buffer
        /// (cleared first). Returns the count. Used by pixel-accurate collision.</summary>
        public int CollectMetaCellCenters(string layerId, List<Vector3> outCenters)
        {
            outCenters.Clear();
            if (!TryGetLayerFrame(layerId, out _, out var mf, out var spr)) return 0;
            for (int y = 0; y < mf.h; y++)
                for (int x = 0; x < mf.w; x++)
                    if (mf.cells[y * mf.w + x] > 0)
                        outCenters.Add(MaskToWorld(spr, mf, x + 0.5f, y + 0.5f));
            return outCenters.Count;
        }

        /// <summary>
        /// PIXEL-accurate overlap test: does THIS layer's painted region touch <paramref name="otherLayer"/> on
        /// <paramref name="other"/>, using the actual painted cells? Everything is resolved in WORLD space honoring
        /// each sprite's PPU, pivot and transform scale, so mixed pixel sizes ("mixels", e.g. different
        /// pixels-per-unit) just work — world overlap == on-screen overlap in a 2D orthographic camera. Sampling is
        /// done in BOTH directions (each side's cell centres against the other's mask) so a coarse cell on one side
        /// is densely sampled by the finer side. Outputs the first contact point.
        /// </summary>
        public bool PixelOverlaps(string myLayer, ZonedAnimationPlayer other, string otherLayer, out Vector3 point)
        {
            point = transform.position;
            if (other == null) return false;

            _centerBuf ??= new List<Vector3>();
            _centerBuf2 ??= new List<Vector3>();
            if (CollectMetaCellCenters(myLayer, _centerBuf) > 0)
                foreach (var c in _centerBuf)
                    if (other.IsMetaPainted(otherLayer, c, out _)) { point = c; return true; }
            if (other.CollectMetaCellCenters(otherLayer, _centerBuf2) > 0)
                foreach (var c in _centerBuf2)
                    if (IsMetaPainted(myLayer, c, out _)) { point = c; return true; }
            return false;
        }
        List<Vector3> _centerBuf, _centerBuf2;

        /// <summary>Locate a layer + its frame at the current index. Shared by the meta samplers.</summary>
        bool TryGetLayerFrame(string layerId, out MetaLayer layer, out MetaFrame mf, out Sprite spr)
        {
            layer = null; mf = null; spr = CurrentSprite;
            if (spr == null || _anim == null || _anim.metaLayers == null) return false;
            foreach (var L in _anim.metaLayers)
                if (L != null && string.Equals(L.id, layerId, StringComparison.OrdinalIgnoreCase)) { layer = L; break; }
            if (layer == null || layer.frames == null || _i < 0 || _i >= layer.frames.Count) return false;
            mf = layer.frames[_i];
            if (mf == null || mf.cells == null || mf.cells.Length < mf.w * mf.h || mf.w <= 0 || mf.h <= 0) { mf = null; return false; }
            return true;
        }

        /// <summary>mask cell coords (bottom-left origin) → world, honoring sprite px size, pivot, PPU, scale, flipX.</summary>
        Vector3 MaskToWorld(Sprite spr, MetaFrame mf, float maskX, float maskY) =>
            PixelToWorld(spr, mf.w, mf.h, maskX, maskY);

        /// <summary>Shared "a point in some W×H grid over this sprite" → world conversion, honoring sprite px
        /// size, pivot, PPU, scale, flipX. <paramref name="gridW"/>/<paramref name="gridH"/> let the SAME grid
        /// be either a (possibly coarser) MetaLayer mask (<see cref="MaskToWorld"/>) or the sprite's own full
        /// pixel resolution (<see cref="TryGetEventPoint"/>, where gridW/H == the sprite's actual pixel size,
        /// so px/py are already in that space with no extra scaling).</summary>
        Vector3 PixelToWorld(Sprite spr, int gridW, int gridH, float px, float py)
        {
            float spx = px * (spr.rect.width / gridW);
            float spy = py * (spr.rect.height / gridH);
            Vector2 pivotPx = spr.pivot;                       // px from the sprite rect's bottom-left
            float ppu = spr.pixelsPerUnit <= 0f ? 16f : spr.pixelsPerUnit;
            Vector3 local = new Vector3((spx - pivotPx.x) / ppu, (spy - pivotPx.y) / ppu, 0f);
            if (flipX) local.x = -local.x;                     // SpriteRenderer.flipX mirrors about the pivot
            return transform.TransformPoint(local);
        }

        /// <summary>
        /// Looks up <paramref name="eventName"/>'s authored pixel position (if any) among the CURRENT frame's
        /// events, converted to world space — the single-point analog of <see cref="TryGetMetaPoint"/>, for
        /// FrameEvents authored via the Animation Builder's pixel tool rather than MetaLayer painting (a much
        /// lighter "one pixel on one frame" signal, e.g. a muzzle/spawn point — MetaLayer stays the right tool
        /// for anything needing multiple pixels or multiple frames, like hit detection). Call this from an
        /// <see cref="OnFrameEvent"/> handler for <paramref name="eventName"/>, or any time after Play — it
        /// re-reads the CURRENT frame's events each call rather than caching. Returns false if the event isn't
        /// authored on the current frame, has no position set, or there's no current sprite.
        /// </summary>
        public bool TryGetEventPoint(string eventName, out Vector3 worldPos)
        {
            worldPos = transform.position;
            var evs = _anim != null ? _anim.events : null;
            if (evs == null) return false;
            var spr = CurrentSprite;
            if (spr == null) return false;

            for (int k = 0; k < evs.Count; k++)
            {
                var ev = evs[k];
                if (ev == null || ev.frame != _i || !ev.hasPosition) continue;
                if (!string.Equals(ev.name, eventName, StringComparison.OrdinalIgnoreCase)) continue;
                int gridW = Mathf.Max(1, Mathf.RoundToInt(spr.rect.width));
                int gridH = Mathf.Max(1, Mathf.RoundToInt(spr.rect.height));
                worldPos = PixelToWorld(spr, gridW, gridH, ev.position.x + 0.5f, ev.position.y + 0.5f);
                return true;
            }
            return false;
        }
    }
}
