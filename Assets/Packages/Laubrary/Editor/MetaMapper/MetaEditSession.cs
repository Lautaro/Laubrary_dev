using System;
using Laubrary.PreviewKit;
using UnityEditor;
using UnityEngine;

namespace Laubrary.MetaMapper.Editor
{
    /// <summary>
    /// THE VISUAL-PROVIDER CONTRACT (design §7): what a HOST supplies so the generic MetaMapper window can
    /// draw its subject and place authored marks on it correctly, without the window knowing anything about
    /// the host's types.
    ///
    /// The whole contract is ONE affine:
    /// <code>  texturePx = originPx + mapPos × pixelsPerUnit  </code>
    /// with <b>+y UP on both sides</b> — texture pixels counted from the image's BOTTOM-left, map units
    /// counted from the subject's own origin. (The GUI's y-down flip is the stage's private business and
    /// never appears in this contract.) A host that describes its own picture honestly gets total coordinate
    /// sync for free; it never has to remember a translation step.
    ///
    /// Worked example — the Tileset Builder handing over a clump: it composes the clump's tiles into one
    /// texture. A clump's cells may sit at NEGATIVE offsets from its anchor, so the texture's bottom-left is
    /// the footprint's <c>Bounds.min</c>, and the host passes
    /// <c>originPx = (−bounds.xMin × cellPx, −bounds.yMin × cellPx)</c>, <c>pixelsPerUnit = cellPx</c>,
    /// <c>space = GridCells</c>. A mark the author drops on the ANCHOR cell's centre then reads back as
    /// <c>pos = (0.5, 0.5)</c> — already correct for <see cref="MetaMapCells"/> at stamp time.
    ///
    /// The window REFUSES a space mismatch loudly (<see cref="Validate"/>) rather than silently mis-placing
    /// every mark: a map authored in pixels cannot be edited on a picture described in cells.
    /// </summary>
    public class MetaSubjectVisual
    {
        [Tooltip("One texture per frame, index-aligned to the map's frameCount. Exactly one for the ordinary " +
                 "static case. All frames are assumed the same size; frame 0 sets the stage's content size.")]
        public Texture2D[] frames = Array.Empty<Texture2D>();

        [Tooltip("0 = no playback UI at all; > 0 enables the frame strip's play toggle at this rate.")]
        public float fps;

        [Tooltip("Where map-space (0,0) sits INSIDE the texture, in pixels, +y up from the image's " +
                 "bottom-left. Zero for a plain sprite (map space IS the rect); non-zero whenever the " +
                 "subject's own origin is not the picture's corner — the negative-offset clump case.")]
        public Vector2 originPx;

        [Tooltip("Texture pixels per ONE map unit: 1 for SpritePixels, the cell's pixel size for GridCells, the " +
                 "picture's pixels-per-unit for LocalUnits.")]
        public float pixelsPerUnit = 1f;

        [Tooltip("Which space the coordinates the author produces are in. MUST match the map being edited — " +
                 "the window refuses a mismatch loudly instead of mis-placing marks.")]
        public MapSpace space = MapSpace.SpritePixels;

        [Tooltip("The subject's size in MAP UNITS at hand-over time — the drift guard the map compares its " +
                 "own refSize against, and the native resolution a Mask layer paints at.")]
        public Vector2Int refSize;

        [Tooltip("Human name for the window title / breadcrumb: \"Shelf A\", \"imp_idle\".")]
        public string label = "";

        [Tooltip("True when the window may destroy `frames` once it is done with them (textures the provider " +
                 "generated). False for textures the host still owns and reuses.")]
        public bool ownsFrames;

        [Tooltip("True for a STAND-IN canvas invented because no provider could resolve the real subject. It " +
                 "is drawable and its coordinates are honest, but it knows nothing about the real art, so the " +
                 "window must never let it teach the map a refSize or a frame count.")]
        public bool isPlaceholder;

        [Tooltip("Set by the host when its subject CHANGED SHAPE under an already-authored map — see " +
                 "MetaSubjectDrift. Null (the ordinary case) means the picture still matches what the map was " +
                 "authored against. Non-null makes the window stop and ask the author instead of silently " +
                 "adopting the new geometry.")]
        public MetaSubjectDrift drift;

        public int FrameCount => frames != null ? frames.Length : 0;

        public Texture2D FrameAt(int i)
            => (frames != null && frames.Length > 0) ? frames[Mathf.Clamp(i, 0, frames.Length - 1)] : null;

        /// <summary>Unscaled pixel size of the picture — the stage's content size. Zero when there is no
        /// frame to draw.</summary>
        public Vector2 TextureSize
        {
            get
            {
                var t = FrameAt(0);
                return t != null ? new Vector2(t.width, t.height) : Vector2.zero;
            }
        }

        /// <summary>THE affine. Map units → texture pixels, +y up on both sides.</summary>
        public Vector2 MapToTexture(Vector2 mapPos) => originPx + mapPos * pixelsPerUnit;

        /// <summary>Where the picture's BOTTOM-LEFT corner sits in map space — the affine, inverted at (0,0).
        /// Zero whenever map space starts at the picture's corner (a sprite); negative on an axis whose subject
        /// origin is not that corner (a clump anchors at its TOP-left cell, so this reads (0, -(height-1))).
        /// This is what a mask anchors to: <see cref="MetaMapData.footprintMin"/> is set FROM it.</summary>
        public Vector2 MapMin => TextureToMap(Vector2.zero);

        /// <summary>THE affine, inverted. Texture pixels (+y up) → map units.</summary>
        public Vector2 TextureToMap(Vector2 texPx)
            => pixelsPerUnit > 0f ? (texPx - originPx) / pixelsPerUnit : Vector2.zero;

        /// <summary>Is this visual coherent, and does it agree with the map it is about to edit? Returns the
        /// human-readable reason when not — the window shows it instead of drawing anything, because a
        /// silently mis-placed mark is far worse than a refused edit.</summary>
        public bool Validate(MetaMapData data, out string problem)
        {
            problem = null;
            if (frames == null || frames.Length == 0 || FrameAt(0) == null)
            { problem = "The subject provided no picture (zero frames)."; return false; }
            if (pixelsPerUnit <= 0f)
            { problem = $"pixelsPerUnit is {pixelsPerUnit} — it must be positive; the affine is undefined otherwise."; return false; }
            if (data == null) { problem = "No map data to edit."; return false; }
            if (data.space != space)
            {
                problem = $"SPACE MISMATCH — the map is authored in {data.space} but the subject describes " +
                          $"itself in {space}. Editing would mis-place every mark, so this is refused. " +
                          "A map's space is set from its subject kind at creation and is not hand-editable.";
                return false;
            }
            return true;
        }

        /// <summary>Destroy the textures this visual owns. Safe to call twice.</summary>
        public void Dispose()
        {
            if (!ownsFrames || frames == null) return;
            for (int i = 0; i < frames.Length; i++)
                if (frames[i] != null) UnityEngine.Object.DestroyImmediate(frames[i]);
            frames = Array.Empty<Texture2D>();
            ownsFrames = false;
        }

        // ── the providers the CORE can build without knowing any other tool ──────────

        /// <summary>A Sprite subject: the one type this module can name, so the window never needs a provider
        /// for it. Map space IS the sprite rect, bottom-left origin, one pixel per unit (§7).</summary>
        public static MetaSubjectVisual ForSprite(Sprite sprite)
        {
            if (sprite == null) return null;
            var tex = PreviewTex.CropSprite(sprite);
            if (tex == null) return null;
            tex.hideFlags = HideFlags.HideAndDontSave;
            return new MetaSubjectVisual
            {
                frames = new[] { tex },
                fps = 0f,
                originPx = Vector2.zero,
                pixelsPerUnit = 1f,
                space = MapSpace.SpritePixels,
                refSize = new Vector2Int((int)sprite.textureRect.width, (int)sprite.textureRect.height),
                label = sprite.name,
                ownsFrames = true,
            };
        }

        /// <summary>A blank canvas of the map's own recorded size, for a map whose subject cannot be resolved
        /// (no provider loaded, subject repicked away, or none ever set). Authoring identity — "this thing HAS
        /// a LootShelf layer" — must never be blocked just because the art is missing, and marks placed on a
        /// blank canvas of the right size are still in the right place.</summary>
        public static MetaSubjectVisual Blank(MapSpace space, Vector2Int refSize, string label)
            => Blank(space, refSize, Vector2.zero, label);

        /// <summary>The no-art canvas, honouring the map's own footprint anchor so a subject that sits in
        /// NEGATIVE map coordinates (a clump) still draws its marks inside the canvas rather than off it.</summary>
        public static MetaSubjectVisual Blank(MapSpace space, Vector2Int refSize, Vector2 footprintMin, string label)
        {
            int w = Mathf.Clamp(refSize.x, 1, 1024), h = Mathf.Clamp(refSize.y, 1, 1024);
            // Cells and local units need pixels to be visible at all; a sprite pixel IS one.
            float ppu = space == MapSpace.SpritePixels ? 1f : 16f;
            var tex = new Texture2D(Mathf.Max(1, (int)(w * ppu)), Mathf.Max(1, (int)(h * ppu)),
                TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            var clear = new Color32(0, 0, 0, 0);
            var px = new Color32[tex.width * tex.height];
            for (int i = 0; i < px.Length; i++) px[i] = clear;
            tex.SetPixels32(px);
            tex.Apply();
            return new MetaSubjectVisual
            {
                frames = new[] { tex },
                fps = 0f,
                originPx = -footprintMin * ppu,
                pixelsPerUnit = ppu,
                space = space,
                refSize = new Vector2Int(w, h),
                label = string.IsNullOrEmpty(label) ? "(no subject)" : label,
                ownsFrames = true,
                isPlaceholder = true,
            };
        }
    }

    /// <summary>
    /// THE SUBJECT MOVED UNDER THE MAP, described by the only party that can know it: the host.
    ///
    /// Marks and masks are stored relative to the subject's footprint, so a subject that changes shape
    /// silently re-points every authored number at different art. The MODEL can DETECT that
    /// (<see cref="MetaMapData.HasDrift(Vector2Int,Vector2)"/> compares the footprint it was authored against)
    /// but it cannot possibly know which WAY the art went: a Cartographer clump that grew one row is 3×5
    /// either way, and whether the existing tiles stayed put or slid down a cell depends on which END grew.
    /// Only the host has the before/after geometry, so the host computes <see cref="artShift"/> and the window
    /// asks the author which of the two honest answers they want.
    ///
    /// NOTHING IS APPLIED UNTIL THE AUTHOR CHOOSES. That is the whole point: the failure this replaces was a
    /// window that adopted the new footprint on open and left the marks behind, moving authored data with no
    /// event, no notice and no undo entry.
    /// </summary>
    public class MetaSubjectDrift
    {
        [Tooltip("How far the subject's EXISTING art moved in MAP UNITS since the map was authored. Zero is " +
                 "meaningful and common — a clump that grew at the bottom-right did not move its old cells at " +
                 "all, yet its footprint anchor still shifted, which moves every MASK.")]
        public Vector2 artShift;

        [Tooltip("The subject's footprint size now, in map units — what the map should end up recording.")]
        public Vector2Int newRefSize;

        [Tooltip("Where the subject's footprint corner sits in map space now — the new mask anchor.")]
        public Vector2 newFootprintMin;

        [Tooltip("One human sentence naming what changed, in the host's own vocabulary (\"grew 1 row on top\"). " +
                 "The window shows it verbatim: it cannot phrase this itself without knowing what a clump is.")]
        public string what = "";

        [Tooltip("Fired INSIDE the undoable edit once the author has chosen, so the host can update its own " +
                 "record of the footprint the metadata now belongs to. Without it the same drift is re-reported " +
                 "on every open and the author can never clear it.")]
        public Action onResolved;

        /// <summary>True when re-anchoring is provably lossless for this map: nothing authored ends up outside
        /// the new footprint. A shrink can push marks or painted cells out, and the author deserves to be told
        /// that BEFORE pressing the button, not by noticing something vanished.</summary>
        public bool WouldLoseContent(MetaMapData d)
        {
            if (d?.layers == null) return false;
            var box = new Rect(newFootprintMin.x, newFootprintMin.y,
                Mathf.Max(1, newRefSize.x), Mathf.Max(1, newRefSize.y));
            foreach (var L in d.layers)
            {
                if (L == null) continue;
                if (EntryEscapes(L.uniform, d, box)) return true;
                if (L.track != null)
                    foreach (var e in L.track) if (EntryEscapes(e, d, box)) return true;
            }
            return false;
        }

        bool EntryEscapes(MetaEntry e, MetaMapData d, Rect box)
        {
            if (e == null) return false;
            if (e.marks != null)
                foreach (var m in e.marks)
                    if (m != null && !box.Contains(m.pos + artShift)) return true;
            if (e.shapes != null)
                foreach (var sh in e.shapes)
                    if (sh != null && !box.Contains(sh.center + artShift)) return true;
            if (!e.MaskUsable) return false;
            Vector2 cs = d.MaskCellToMapScale(e);
            for (int y = 0; y < e.maskH; y++)
                for (int x = 0; x < e.maskW; x++)
                {
                    if (e.mask[y * e.maskW + x] == 0) continue;
                    var at = d.footprintMin + new Vector2((x + 0.5f) * cs.x, (y + 0.5f) * cs.y) + artShift;
                    if (!box.Contains(at)) return true;
                }
            return false;
        }
    }

    /// <summary>
    /// HOW THE EDIT RUNS (design §7). The window edits <see cref="data"/> <b>IN PLACE</b> — there is no
    /// copy-out/copy-back protocol for a host to get wrong, and no moment where the host's asset and the
    /// window disagree about what the data is.
    ///
    /// Every mutation goes through <see cref="Edit"/> (or the Begin/End pair for a continuous gesture), which
    /// calls <c>Undo.RecordObject(undoTarget, …)</c> BEFORE touching anything, dirties the target afterwards,
    /// and fires <see cref="onChanged"/> so the host can repaint its own views.
    /// </summary>
    public class MetaEditSession
    {
        [Tooltip("The model being edited, IN PLACE. Either a standalone MetaMap's `data` or a MetaMapData " +
                 "embedded in the host's own asset — the window cannot tell and does not care.")]
        public MetaMapData data;

        [Tooltip("The owning asset. Undo.RecordObject runs on THIS before every mutation, and it is what gets " +
                 "dirtied — which is why the data may live embedded in any asset at all.")]
        public UnityEngine.Object undoTarget;

        [Tooltip("Fired after every committed change so the host can repaint its own views (the Tileset " +
                 "Builder repainting its clump box, say). Optional.")]
        public Action onChanged;

        public bool IsValid => data != null;

        /// <summary>One discrete, undoable edit. The label is what appears in Edit ▸ Undo.</summary>
        public void Edit(string label, Action apply)
        {
            if (data == null || apply == null) return;
            Begin(label);
            apply();
            End();
        }

        /// <summary>Open a CONTINUOUS gesture (a paint stroke, a mark drag): records the pre-state ONCE, so
        /// the whole stroke is a single undo step instead of one per pointer-move event.</summary>
        public void Begin(string label)
        {
            if (undoTarget != null) Undo.RecordObject(undoTarget, label);
        }

        /// <summary>Close a gesture opened with <see cref="Begin"/> — dirty the target and notify the host.</summary>
        public void End()
        {
            if (undoTarget != null) EditorUtility.SetDirty(undoTarget);
            onChanged?.Invoke();
        }

        /// <summary>The session a standalone <see cref="MetaMap"/> asset edits under. The PULL path's half of
        /// the contract; the PUSH path builds its own.</summary>
        public static MetaEditSession ForAsset(MetaMap map, Action onChanged = null)
            => map == null ? null : new MetaEditSession { data = map.Data, undoTarget = map, onChanged = onChanged };
    }
}
