// ChunkWindow.Preview — the PREVIEW SUBJECT section (the ZOE_FX_SHELF "Chunks editor experience" slice).
//
// A ChunkSpec's sampled-debris settings were configured blind: nothing in the window showed what the slicing
// actually cuts. This section lets the author aim the recipe at a REAL sprite and see it immediately — the
// subject drawn with the preview cuts outlined on it, plus the actual debris those cuts produce. The debris is
// cut by the SAME SampledChunkSprites.Sample the runtime uses (tint, edge modes and the baked modifier stack
// included), so preview == runtime (the one-shared-core rule); the only editor-side addition is the
// out-cutRect overload that reports WHERE each cut came from, so the outlines cannot drift from the cuts.
//
// The subject is EDITOR-ONLY state — the "live preview subject (not baked)" pattern. It is NEVER serialized
// into the ChunkSpec asset: it lives in EditorPrefs keyed by the spec's GUID (the exact scheme
// SpriteFxStackWindow already uses for its preview input sprite, now shared as PreviewSpritePrefs), so each
// chunk remembers its subject across domain reloads without ever dirtying the asset or touching version
// control. When no subject is picked, the spec's own Sample Source stands in.
//
// Sampling is DETERMINISTIC per preview seed (UnityEngine.Random re-seeded around each Sample call, editor
// state restored after) so tuning a tint/size dial re-cuts the SAME six pieces — you see the dial's effect,
// not a reroll — and the Resample button is the explicit reroll.
using System.Collections.Generic;
using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        const string SubjectPrefKey = "Laubrary.Chunks.Preview.Subject.";
        const float StageBox = 200f;    // the scaled-up pixel-art subject stage (same size as SpriteFxStackWindow's)
        const int DebrisSamples = 6;    // cuts previewed per refresh — enough to read the spread, cheap to bake
        const float DebrisCell = 44f;   // one debris thumbnail's square size

        static readonly Color32 CutOutlineColor = new Color32(255, 210, 40, 255);   // readable on most art

        // ── PREVIEW-ONLY state (window-scoped, NEVER written into the ChunkSpec asset) ─────────────────────
        Sprite _subject;                  // the chosen subject sprite; EditorPrefs-remembered per spec
        string _subjectSpecGuid;          // which spec's remembered subject is currently loaded
        int _previewSeed = 12345;         // deterministic cuts per seed; Resample rerolls it
        Texture2D _stageTex;              // pooled compose target: subject pixels + cut outlines (Point-filtered)
        readonly List<Sprite> _debris = new List<Sprite>();   // owned sampled sprites (each owns its own texture)

        // The stage's zoom, in the package-wide sense (ZuiPixelStage): 1 draws a source pixel at the size it
        // has in game. 0 is the "not chosen yet" sentinel — the stage had no zoom control at all before this
        // and always blew the subject up to whatever fitted, so a newly-picked subject still ARRIVES fitted;
        // the difference is that the fit is now a real zoom number, written into the control the moment it is
        // chosen, instead of a magnification nothing on screen accounted for.
        int _stageZoom;
        ZuiMicroSlider _stageZoomSlider;

        // live element refs (re-created every BuildPreview; nulled in OnBeforeRebuild)
        IMGUIContainer _stageView;        // the bespoke pixel-art canvas island (see DrawStage)
        Label _stageHint;
        Label _stageReadout;              // fixed-height line: what is actually on screen, and at what zoom
        VisualElement _stageEl;
        readonly List<IMGUIContainer> _debrisCells = new List<IMGUIContainer>();
        readonly Texture2D[] _debrisTex = new Texture2D[DebrisSamples];   // what each cell draws, by index

        void BuildPreview(VisualElement root, ChunkSpec c)
        {
            // Load the remembered subject for THIS spec whenever the edited spec changes (incl. after a domain
            // reload, when _subjectSpecGuid resets to null). On a plain rebuild (same spec) keep the pick.
            string guid = PreviewSpritePrefs.GuidOf(c);
            if (_subjectSpecGuid == null || _subjectSpecGuid != guid)
            {
                _subject = PreviewSpritePrefs.Load(SubjectPrefKey, guid);
                _subjectSpecGuid = guid;
            }

            // Titled "Cut Preview", not "Preview": what it shows is where the SAMPLED cuts above it land, and
            // a bare "Preview" read as a rival to the window's own "Preview in Mirage" action. Keyed, so a
            // later reword cannot orphan its fold state (an unkeyed section falls back to title+tooltip).
            var s = Z.Section("Cut Preview",
                "Cut debris from a real sprite to see this chunk's slicing while configuring. Everything here " +
                "is preview-only — the subject sprite is never saved into the Chunk asset. At runtime debris " +
                "is cut from the Sample Source (or uses the sprite list / procedural squares when none is set).",
                "chunks.preview");

            // Subject picker + Resample share a row (both short; vertical space is the scarce resource).
            const string subjectTip = "The sprite the preview cuts debris from — a live preview subject, never " +
                "saved into this Chunk asset (remembered per asset in EditorPrefs). Empty = the Sample Source " +
                "stands in. Its texture must be Read/Write enabled to be sampled.";
            var resample = Z.Button("Resample",
                "Reroll the preview cuts. Between rerolls the same cuts are kept, so a tint or size tweak shows " +
                "its effect on the same pieces.",
                () => { _previewSeed = unchecked(_previewSeed * 48271 + 1); RefreshChunkPreview(); });
            resample.style.width = 80f;
            s.Add(Z.Row(
                Z.Field("Subject", subjectTip, Z.Object<Sprite>(_subject, subjectTip, OnPickSubject, 200f)),
                Z.HSpace(),
                resample));

            s.Add(Z.VSpace(4f));

            // The subject stage — a bespoke pixel-art canvas island (sanctioned raw painting). Fixed square box
            // (stable layout: it never resizes with content); a hint Label swaps in, inside that same fixed
            // box, when there is nothing to render.
            _stageEl = new VisualElement();
            _stageEl.style.width = StageBox;
            _stageEl.style.height = StageBox;
            _stageEl.style.flexShrink = 0f;
            _stageEl.style.alignItems = Align.Center;
            _stageEl.style.justifyContent = Justify.Center;
            _stageEl.style.backgroundColor = new Color(0.11f, 0.11f, 0.12f, 1f);
            PreviewBorder(_stageEl);
            _stageEl.tooltip = "The subject sprite with the preview cuts outlined — each outline is where one " +
                "debris piece below was cut from, blown up by a whole number of screen pixels with nearest-" +
                "neighbour sampling, so an authored pixel is an identical square block and a 1px cut outline " +
                "stays a clean line. The readout under it says how big the subject really is.";

            // The stage is a bespoke pixel-art CANVAS — the sanctioned raw-IMGUI island — drawn through
            // ZuiPixel rather than a UI Toolkit Image. The Image was blowing the subject up to fill this box
            // at whatever fractional ratio fell out of it: a 15x15 subject into a 196pt box is 13.07 points
            // per authored pixel, so identical pixels rasterized 13 or 14 device pixels wide, and the cut
            // outlines — which are ONE pixel thick — smeared into the art they exist to sit on top of. ZuiPixel
            // decides the size and the corner in DEVICE pixels, floors the zoom to a whole number, snaps the
            // rect onto a pixel boundary and restates FilterMode.Point on every draw. See ZuiPixel.cs's header
            // for the full points-versus-device-pixels trap; it is invisible at 100% display scaling.
            _stageView = new IMGUIContainer(DrawStage);
            _stageView.style.width = StageBox - 4f;
            _stageView.style.height = StageBox - 4f;
            _stageEl.Add(_stageView);

            _stageHint = new Label { pickingMode = PickingMode.Ignore };
            _stageHint.style.whiteSpace = WhiteSpace.Normal;
            _stageHint.style.unityTextAlign = TextAnchor.MiddleCenter;
            _stageHint.style.maxWidth = StageBox - 20f;
            _stageEl.Add(_stageHint);
            s.Add(_stageEl);

            // A permanently-reserved single line whose TEXT changes and whose geometry never does (fixed
            // height, no wrapping) — the stable-workspace shape for a status readout. It exists so a tiny
            // source can never masquerade as a detailed preview: it names the subject's real pixel size and
            // the whole-number zoom it is being shown at.
            _stageReadout = Z.Text("—", ZuiText.Subtle,
                "The subject's real size in its own pixels, the zoom the stage is showing it at, and how many " +
                "screen pixels one of its pixels therefore covers. A small size here means the preview is " +
                "coarse because the SOURCE is small, not because the preview is blurred.");
            _stageReadout.style.height = 14f;
            _stageReadout.style.whiteSpace = WhiteSpace.NoWrap;
            _stageReadout.style.overflow = Overflow.Hidden;
            _stageReadout.style.flexShrink = 0f;
            // Wider than the stage box it sits under: the line now carries three facts, and a width chosen for
            // the old two truncated the third mid-word (measured, 2026-09-08). It reserves the same fixed
            // height either way, so nothing moves.
            _stageReadout.style.width = 300f;
            s.Add(_stageReadout);

            // The zoom the rest of the package uses, on the stage that used to have none. Fit is the explicit
            // way back to "show me all of it"; the number it picks lands in the slider, so what the stage is
            // doing is always readable rather than implied.
            s.Add(ZuiPixelStage.ZoomControl(
                _stageZoom > 0 ? _stageZoom : ZuiPixelStage.MinZoom,
                z => { _stageZoom = z; _stageView?.MarkDirtyRepaint(); },
                StageFitZoom,
                "the subject", out _stageZoomSlider, 150f));

            s.Add(Z.VSpace(4f));

            // The debris strip: a FIXED row of thumbnail slots (space reserved up front — filling or emptying
            // them never reflows the section), one per previewed cut, in the same order as the outlines.
            var strip = Z.Row();
            strip.style.height = DebrisCell + 4f;
            strip.style.flexShrink = 0f;
            strip.tooltip = "The debris the outlined cuts produce, at the current sampling / tint / modifier " +
                "settings — cut by the same code the runtime uses.";
            _debrisCells.Clear();
            for (int i = 0; i < DebrisSamples; i++)
            {
                var cell = new VisualElement();
                cell.style.width = DebrisCell;
                cell.style.height = DebrisCell;
                cell.style.flexShrink = 0f;
                cell.style.marginRight = 4f;
                cell.style.alignItems = Align.Center;
                cell.style.justifyContent = Justify.Center;
                cell.style.backgroundColor = new Color(0.11f, 0.11f, 0.12f, 1f);
                PreviewBorder(cell);
                cell.tooltip = strip.tooltip;
                // Same pixel-exact island as the stage, for the same reason: a cut is a handful of authored
                // pixels, and a fractional blow-up is exactly where a few pixels stop reading as pixels.
                int idx = i;
                var view = new IMGUIContainer(() => DrawDebrisCell(idx));
                view.style.width = DebrisCell - 6f;
                view.style.height = DebrisCell - 6f;
                cell.Add(view);
                _debrisCells.Add(view);
                strip.Add(cell);
            }
            s.Add(strip);

            root.Add(s);
            RefreshChunkPreview();
        }

        /// The subject stage's paint. Local rect, because ZuiPixel adds the island's own panel position
        /// itself — where the island sits inside the window is half of whether a local coordinate lands on a
        /// whole device pixel. Repaint only: GUI.DrawTexture is a repaint-time API.
        void DrawStage()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (_stageView == null || _stageTex == null) return;
            var r = _stageView.contentRect;
            if (float.IsNaN(r.width) || r.width < 1f || r.height < 1f) return;
            var placement = ZuiPixelStage.Draw(new Rect(0f, 0f, r.width, r.height), _stageView, _stageTex,
                                               _stageZoom > 0 ? _stageZoom : ZuiPixelStage.MinZoom);
            SetStageReadout(_stageTex.width, _stageTex.height, placement.zoom);
        }

        /// The largest whole zoom the fixed stage box can hold the current subject at — the Fit answer. The
        /// box is a fixed square (it never resizes with content, by design), so this needs no laid-out
        /// geometry and can answer before the first repaint, which is what lets a newly-picked subject arrive
        /// already fitted.
        int StageFitZoom()
        {
            if (_stageTex == null) return ZuiPixelStage.MinZoom;
            float box = StageBox - 4f;
            return ZuiPixelStage.FitZoom(new Rect(0f, 0f, box, box), _stageTex.width, _stageTex.height);
        }

        /// Set the stage zoom and make the control say so. Used by the auto-fit on a new subject; a zoom the
        /// user dialled is never overwritten by it.
        void SetStageZoom(int zoom)
        {
            _stageZoom = ZuiPixelStage.Clamp(zoom);
            if (_stageZoomSlider != null) _stageZoomSlider.value = _stageZoom;
        }

        /// One debris thumbnail's paint, by index into the fixed strip. A slot with no cut simply paints
        /// nothing — the slot itself is always there, so filling or emptying the strip never reflows it.
        void DrawDebrisCell(int i)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (i < 0 || i >= _debrisTex.Length || i >= _debrisCells.Count) return;
            var tex = _debrisTex[i];
            var view = _debrisCells[i];
            if (tex == null || view == null) return;
            var r = view.contentRect;
            if (float.IsNaN(r.width) || r.width < 1f || r.height < 1f) return;
            Z.DrawPixels(new Rect(0f, 0f, r.width, r.height), view, tex);
        }

        /// Set the stage readout from what was ACTUALLY drawn — the buffer's own size and the screen pixels per
        /// source pixel it was drawn at. It names BOTH numbers, because they are different and the difference
        /// is the point: the Zoom control says 2, the screen shows 8 pixels per source pixel, and the factor
        /// between them is the game's own scale. Printing only the second (which is what it used to do) put a
        /// number on screen that contradicted the control right below it. Guarded on change so a repaint that
        /// re-states the same numbers does not dirty the label and ask for another repaint.
        void SetStageReadout(int w, int h, int devicePixelsPerPixel)
        {
            if (_stageReadout == null) return;
            int level = Mathf.Max(1, devicePixelsPerPixel / Mathf.Max(1, ZuiPixelStage.TargetScale));
            string t = devicePixelsPerPixel > 0
                ? w + " × " + h + " px · zoom " + level + " · " + devicePixelsPerPixel + " screen px each"
                : "—";
            if (_stageReadout.text != t) _stageReadout.text = t;
        }

        static void PreviewBorder(VisualElement v)
        {
            var c = new Color(0f, 0f, 0f, 0.5f);
            v.style.borderTopWidth = 1f; v.style.borderBottomWidth = 1f;
            v.style.borderLeftWidth = 1f; v.style.borderRightWidth = 1f;
            v.style.borderTopColor = c; v.style.borderBottomColor = c;
            v.style.borderLeftColor = c; v.style.borderRightColor = c;
        }

        void OnPickSubject(Sprite s)
        {
            _subject = s;
            _stageZoom = 0;   // a different subject is a different size: fit it, and say what zoom that is
            PreviewSpritePrefs.Remember(SubjectPrefKey, _subjectSpecGuid, s);
            RefreshChunkPreview();
        }

        /// Re-cut and re-compose the whole preview. Cheap (a handful of tiny GetPixels + one small compose), so
        /// every Dial calls it — the preview tracks every dial live, with the SAME cuts (deterministic per seed).
        void RefreshChunkPreview()
        {
            if (_stageView == null || _stageHint == null) return;   // section not built yet
            DisposeDebris();

            var c = Spec;
            if (c == null) { StageHint("No Chunk selected."); return; }

            Sprite src = _subject != null ? _subject : c.sampleSource;
            if (src == null) { StageHint("Pick a Subject sprite (or set a Sample Source) to preview slicing."); return; }
            Texture2D tex = src.texture;
            if (tex == null) { StageHint("This sprite has no texture."); return; }
            if (!tex.isReadable) { StageHint("Enable Read/Write on this sprite's import settings to preview."); return; }

            Rect tr = src.textureRect;
            int x = Mathf.RoundToInt(tr.x), y = Mathf.RoundToInt(tr.y);
            int W = Mathf.RoundToInt(tr.width), H = Mathf.RoundToInt(tr.height);
            if (W <= 0 || H <= 0) { StageHint("This sprite has no pixels to preview."); return; }

            try
            {
                // Read the subject's own pixels (sub-rect aware, mirroring SpriteFxStackWindow's read).
                Color32[] px;
                if (x == 0 && y == 0 && W == tex.width && H == tex.height)
                {
                    px = tex.GetPixels32();
                }
                else
                {
                    Color[] block = tex.GetPixels(x, y, W, H);
                    px = new Color32[block.Length];
                    for (int i = 0; i < block.Length; i++) px[i] = (Color32)block[i];
                }

                // Cut the preview debris through the REAL runtime sampler, deterministically per seed. The
                // editor's shared Random state is saved/restored so a preview refresh never perturbs anything
                // else that draws editor randomness.
                var cuts = new List<RectInt>(DebrisSamples);
                var savedState = Random.state;
                try
                {
                    for (int i = 0; i < DebrisSamples; i++)
                    {
                        Random.InitState(unchecked(_previewSeed * 7919 + i * 131));
                        Sprite piece = SampledChunkSprites.Sample(src, c.samplePxMin, c.samplePxMax,
                            c.pixelsPerUnit, out RectInt cut, c.tintMode, c.tintColor, c.tintStrength,
                            c.edgeThicknessPx, c.modifiers);
                        if (piece == null) continue;
                        _debris.Add(piece);
                        cuts.Add(cut);
                    }
                }
                finally { Random.state = savedState; }

                for (int i = 0; i < _debrisTex.Length; i++)
                    _debrisTex[i] = i < _debris.Count ? _debris[i].texture : null;
                for (int i = 0; i < _debrisCells.Count; i++) _debrisCells[i]?.MarkDirtyRepaint();

                foreach (var cut in cuts) OutlineCut(px, W, H, cut);

                EnsureStageTex(W, H);
                _stageTex.SetPixels32(px);
                _stageTex.Apply(false);
                if (_stageZoom <= 0) SetStageZoom(StageFitZoom());   // first sight of this subject
                _stageView.MarkDirtyRepaint();
                _stageHint.Shown(false);
                _stageView.Shown(true);
            }
            catch (System.Exception e)
            {
                StageHint("Could not read this sprite's pixels to preview.");
                Debug.LogWarning($"[ChunkWindow] Slicing preview failed: {e.Message}", c);
            }
        }

        void StageHint(string msg)
        {
            _stageHint.text = msg;
            _stageHint.Shown(true);
            _stageView.Shown(false);
            for (int i = 0; i < _debrisTex.Length; i++) _debrisTex[i] = null;
            for (int i = 0; i < _debrisCells.Count; i++) _debrisCells[i]?.MarkDirtyRepaint();
            SetStageReadout(0, 0, 0);
        }

        // 1px border of one cut, in the subject's own bottom-left-origin pixel space (the same space the
        // sampler reported the rect in, so outline and cut cannot disagree).
        static void OutlineCut(Color32[] px, int W, int H, RectInt r)
        {
            int x0 = Mathf.Clamp(r.xMin, 0, W - 1), x1 = Mathf.Clamp(r.xMax - 1, 0, W - 1);
            int y0 = Mathf.Clamp(r.yMin, 0, H - 1), y1 = Mathf.Clamp(r.yMax - 1, 0, H - 1);
            for (int xx = x0; xx <= x1; xx++) { px[y0 * W + xx] = CutOutlineColor; px[y1 * W + xx] = CutOutlineColor; }
            for (int yy = y0; yy <= y1; yy++) { px[yy * W + x0] = CutOutlineColor; px[yy * W + x1] = CutOutlineColor; }
        }

        // Pooled compose texture — resized only when the subject geometry changes, Point-filtered for crisp
        // pixels, never saved. Disposed on window disable/destroy and when the edited spec changes.
        void EnsureStageTex(int W, int H)
        {
            if (_stageTex != null && _stageTex.width == W && _stageTex.height == H) return;
            if (_stageTex != null) DestroyImmediate(_stageTex);
            _stageTex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        // The sampled debris sprites own freshly-created textures (SampledChunkSprites.Build makes one per
        // cut) — destroy both halves or every refresh leaks a texture.
        void DisposeDebris()
        {
            // Drop what the cells DRAW before destroying it — a painting island reads _debrisTex on every
            // repaint, so clearing after the destroy would leave one frame pointing at a dead texture.
            for (int i = 0; i < _debrisTex.Length; i++) _debrisTex[i] = null;
            for (int i = 0; i < _debris.Count; i++)
            {
                var s = _debris[i];
                if (s == null) continue;
                var t = s.texture;
                DestroyImmediate(s);
                if (t != null) DestroyImmediate(t);
            }
            _debris.Clear();
            for (int i = 0; i < _debrisCells.Count; i++) _debrisCells[i]?.MarkDirtyRepaint();
        }

        void DisposeChunkPreview()
        {
            DisposeDebris();
            if (_stageTex != null) { DestroyImmediate(_stageTex); _stageTex = null; }
        }

        protected override void OnBeforeRebuild()
        {
            base.OnBeforeRebuild();   // AssetKit clears its thumbnail refs
            // Release element refs so a stale one is never touched between clearing the tree and rebuilding it.
            _stageView = null;
            _stageHint = null;
            _stageReadout = null;
            _stageZoomSlider = null;
            _stageEl = null;
            _debrisCells.Clear();
        }

        void OnDestroy() => DisposeChunkPreview();
    }
}
