// ShaperBaker — T-0148. The first path by which a Shaper document leaves the editor as something a game can
// actually consume: a sliced sprite-sheet PNG plus an AnimationClip that steps through it at the document's
// own frame rate. Before this, Shaper could evaluate but not EMIT — a repo-wide grep for `AnimationClip`
// across Runtime/Shaper, Editor/Shaper, Runtime/PyreShaper and Editor/PyreShaper returned zero hits, and the
// only `EncodeToPNG` calls in the whole tool were audit classes writing verification contact sheets.
//
// Near-behaviour port of Pyre's own baker (Editor/Pyre/PyreBaker.cs in the main working copy): same output
// pair, same "never clobber a user asset" guard rail, same import-settings discipline. What is NOT ported is
// its entry shape, because Pyre bakes a ScriptableObject spec and Shaper has no asset to bake — see
// "WHAT THE ENGINE COULD NOT GIVE THIS BAKE" below.
//
// ── How "the bake is identical to what you saw" is guaranteed ────────────────────────────────────────────
// By construction, and as of T-0153 by a stronger construction than before: there is exactly ONE
// document→pixels implementation in the whole engine — ShaperDocumentRenderer, in Runtime/Shaper. This baker
// calls it and owns no pixel path of its own. Phase C's preview window must call the same one. The guarantee
// therefore holds because a second renderer does not exist, not because two implementations agree.
//
// ── WHAT THE ENGINE COULD NOT GIVE THIS BAKE (the original three findings, and what happened to them) ────
// 1. THERE WAS NO LAYER COMPOSITOR — FIXED (T-0153). ShaperFillResolver paints ONE layer at a time into its
//    own buffer; the audits compare per-layer results and never combine them. So a two-layer document had no
//    defined final image anywhere in the engine, and this file originally had to supply the missing step to
//    bake at all. That step is now ShaperDocumentRenderer.CompositeOver in Runtime, composited in the float
//    destination (linear, premultiplied) exactly where ShaperFillResolver.Encode's own doc comment says a
//    further composite belongs (ShaperFillResolver.cs:1507-1511).
// 2. THERE WAS NO DOCUMENT ASSET — FIXED (T-0152, commit a205a82b). ShaperDocument is now a ScriptableObject
//    with [CreateAssetMenu], so Pyre's "bake beside the source asset" is finally expressible; Bake now
//    derives its folder from the document's own asset path when the document is saved, and only falls back
//    to the caller's folder when it is not (decision 3, updated).
// 3. THERE WAS NO PIXELS-PER-UNIT — FIXED (T-0166). ShaperDocument.pixelsPerUnit now carries it, distinct from
//    pixelSize (a SAMPLING density, LR-1.5) by construction — a document author sets a display scale without
//    touching sampling. The caller-facing `pixelsPerUnit` parameter below still exists and still defaults to
//    DefaultPixelsPerUnit, for a caller with no document convention of its own to reach for; ShaperWindow's own
//    Bake call now passes document.pixelsPerUnit explicitly (decision 1).
using System;
using System.Collections.Generic;
using System.IO;
using Laubrary.AssetKit.Editor;
using Laubrary.PyreShaper;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Shaper.Editor
{
    /// <summary>
    /// Bakes a <see cref="ShaperDocument"/> to a sprite sheet + AnimationClip. See the file header for the
    /// three things the engine does not provide that this file had to decide or supply.
    /// </summary>
    public static class ShaperBaker
    {
        /// <summary>
        /// Written to the baked PNG's <c>TextureImporter.userData</c> — the shared marker convention
        /// (<see cref="LauAssetBrowser.BakedMarkerPrefix"/>) any Laubrary baker opts into, so a general
        /// raw-Sprite browse can exclude derived bake output. A baked Shaper sheet is a drag-and-drop export,
        /// not a source sprite anyone should be picking from a browser.
        /// </summary>
        public const string BakedMarker = LauAssetBrowser.BakedMarkerPrefix + "Shaper";

        /// <summary>
        /// DECISION 1 (pixels per unit), UPDATED by T-0166. <see cref="ShaperDocument.pixelsPerUnit"/> now
        /// answers this and <c>ShaperWindow</c>'s own Bake call passes it explicitly; this constant remains
        /// the fallback for a caller with no document convention of its own (the project's own standard, 16,
        /// matching <c>ShaperDocument.pixelsPerUnit</c>'s own default so the two never quietly disagree).
        /// </summary>
        public const float DefaultPixelsPerUnit = 16f;

        /// <summary>
        /// Sheet grid width cap. Frames are laid out left-to-right, top-to-bottom on a FIXED grid with ZERO
        /// padding: this sheet is imported point-filtered, uncompressed and mip-free, so there is no
        /// filtering that could bleed one frame into its neighbour and therefore no gutter to justify. Eight
        /// columns keeps a long animation squarish rather than one enormous strip, matching the "horizontal-ish
        /// sheet" shape Pyre's own baker produces.
        /// </summary>
        public const int MaxSheetColumns = 8;

        /// <summary>Safety stop for the cherry walk, so a pathological slot list cannot spin forever.</summary>
        public const int MaxCherryBeats = 4096;

        static readonly Vector2 CenterPivot = new Vector2(0.5f, 0.5f);

        public struct BakeResult
        {
            public bool ok;
            public string sheetPath;
            /// <summary>The Unity AnimationClip. Plays in any Animator; CANNOT express cherry framing.</summary>
            public string clipPath;
            /// <summary>The <see cref="ShaperClip"/> asset ShaperPlayer consumes. Preserves cherry framing.</summary>
            public string shaperClipPath;
            /// <summary>Distinct source frames written into the sheet.</summary>
            public int sheetFrames;
            /// <summary>Keyframes in the clip — more than <see cref="sheetFrames"/> when a cherry sequence
            /// revisits a frame, fewer when it holds one.</summary>
            public int clipKeys;
            public int columns, rows;
            public string message;
        }

        // ── the bake ────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bake <paramref name="doc"/> into <paramref name="folder"/> as a sliced sheet + clip.
        ///
        /// DECISION 3 (where it lands), UPDATED by T-0152. Pyre bakes beside its spec asset, and now that
        /// ShaperDocument is a real ScriptableObject that is finally possible: when <paramref name="doc"/> is
        /// a saved asset, the bake lands in that asset's own folder, matching PyreBaker. The
        /// <paramref name="folder"/> argument is the fallback for an unsaved, in-memory document (which is what
        /// a freshly created or window-owned document is) and remains an explicit override for a caller that
        /// wants the output elsewhere. The guard rails are unchanged and already behaved correctly under this:
        /// unique naming, never overwriting.
        ///
        /// Does not mutate authored data: the document's <c>phase01</c> is driven per frame and restored in a
        /// finally, so no Undo record is needed and an exception mid-bake cannot leave the document scrubbed
        /// to some arbitrary frame.
        /// </summary>
        /// <param name="bakeAnimationClip">T-0177 — the transport's Bake box lets the AnimationClip output be
        /// skipped. The sheet is never optional: both clip formats slice their sprites from it, so it is
        /// always produced regardless of these flags.</param>
        /// <param name="bakeShaperClip">T-0177 — same as <paramref name="bakeAnimationClip"/>, for the
        /// ShaperClip output (the only one of the two that preserves cherry framing).</param>
        public static BakeResult Bake(ShaperDocument doc, string folder = "Assets", string baseName = null,
                                      float pixelsPerUnit = DefaultPixelsPerUnit,
                                      bool bakeAnimationClip = true, bool bakeShaperClip = true)
        {
            var result = new BakeResult();
            if (doc == null)
            {
                result.message = "Bake skipped: no document.";
                Debug.LogWarning("[Shaper] " + result.message);
                return result;
            }

            int w = Mathf.Max(1, doc.canvasWidth), h = Mathf.Max(1, doc.canvasHeight);

            // The playback order: one entry per beat, each a source frame index or ShaperCherry.BlankFrame.
            var order = PlaybackOrder(doc);
            if (order.Count == 0)
            {
                result.message = "Bake skipped: the document has no frames to bake.";
                Debug.LogWarning("[Shaper] " + result.message);
                return result;
            }

            // Only the frames actually shown reach the sheet, in first-use order. For a plain document that is
            // simply 0..frameCount-1; for a cherry sequence it is the distinct picked set, so a sequence that
            // holds or revisits a frame does not pay for it twice in texture memory (the clip expresses the
            // holds and repeats instead — see decision 2).
            var distinct = new List<int>();
            var seen = new HashSet<int>();
            foreach (int f in order)
            {
                if (f == ShaperCherry.BlankFrame || !seen.Add(f)) continue;
                distinct.Add(f);
            }
            if (distinct.Count == 0)
            {
                result.message = "Bake skipped: every beat of this sequence is blank.";
                Debug.LogWarning("[Shaper] " + result.message);
                return result;
            }

            int cols = Mathf.Min(MaxSheetColumns, distinct.Count);
            int rows = Mathf.CeilToInt(distinct.Count / (float)cols);
            int sheetW = cols * w, sheetH = rows * h;

            // 1) render every distinct frame into the sheet buffer.
            var sheetPx = new Color32[sheetW * sheetH];   // default is (0,0,0,0) — transparent padding cells
            var framePx = new Color32[w * h];
            // T-0194 — the bake takes the SAME per-layer buffer cache the preview does, which is what keeps
            // "the bake is what the preview showed" true rather than merely likely: a cache hit returns the
            // arrays the miss path would have produced, so a sheet is byte-identical either way, and a still
            // layer across sixteen frames is resolved once instead of sixteen times. The cache is local to this
            // bake so nothing survives into a later one.
            var bakeLayers = new ShaperLayerBufferCache();
            var bakePool = new ShaperRenderBufferPool();
            for (int i = 0; i < distinct.Count; i++)
            {
                // The canonical Runtime renderer, not a local pixel path — see the file header. The
                // Color32-destination overload is used (not RenderPhaseInto + Encode) because effects are
                // 8-BIT PIXEL KERNELS that run AFTER the encode: the float accumulator path is pre-encode and
                // therefore has no effect hook at all, by design rather than omission. Using it here would
                // silently render every authored effect as a no-op — the exact "built but inert" failure the
                // effects work existed to fix (T-0156). This overload still writes into the hoisted framePx,
                // so the loop does not churn an output array per frame.
                ShaperDocumentRenderer.RenderPhase(doc, doc.PhaseOfFrame(distinct[i]), framePx,
                                                   ShaperEffectApplier.Instance, distinct[i],
                                                   bakePool, bakeLayers);
                BlitFrame(framePx, w, h, sheetPx, sheetW, i, cols, rows);
            }

            var sheet = new Texture2D(sheetW, sheetH, TextureFormat.RGBA32, false);
            sheet.SetPixels32(sheetPx);
            sheet.Apply(false, false);

            // 2) write the PNG, never clobbering an existing file.
            // Beside the document's own asset when it has one (Pyre parity, unblocked by T-0152); otherwise the
            // caller's folder. An in-memory document — which is what a window-owned or freshly created one is —
            // has an empty asset path, and that is the case `folder` exists to serve.
            string docAssetPath = AssetDatabase.GetAssetPath(doc);
            string dir = !string.IsNullOrEmpty(docAssetPath)
                ? Path.GetDirectoryName(docAssetPath).Replace('\\', '/')
                : (string.IsNullOrEmpty(folder) ? "Assets" : folder.Replace('\\', '/').TrimEnd('/'));
            if (!AssetDatabase.IsValidFolder(dir)) dir = "Assets";
            string name = SanitizeName(!string.IsNullOrEmpty(baseName) ? baseName
                                     : (!string.IsNullOrEmpty(doc.name) ? doc.name : "Shaper"));
            string pngPath = UniquePath(dir, name, "png");
            File.WriteAllBytes(pngPath, sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);

            // 3) DECISION 4 — import settings, set explicitly because every default here is wrong for pixel
            //    art and wrong SILENTLY: bilinear filtering blurs a 64px sprite, compression puts DXT blocks
            //    through a hard alpha edge, mipmaps fade it at distance, and NPOT scaling would RESIZE the
            //    sheet out from under the slice rects computed above. maxTextureSize is raised to fit rather
            //    than left at the 2048 default, which would quietly downscale a long animation's sheet.
            var importer = (TextureImporter)AssetImporter.GetAtPath(pngPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;      // Encode writes STRAIGHT alpha (ShaperFillResolver.cs:1516)
            importer.sRGBTexture = true;              // ...and sRGB-encoded bytes, via ShaperSrgb.EncodeToByte
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = MaxTextureSizeFor(Mathf.Max(sheetW, sheetH));
            importer.spritePixelsPerUnit = Mathf.Max(0.01f, pixelsPerUnit);
            importer.userData = BakedMarker;

            var meta = new SpriteMetaData[distinct.Count];
            for (int i = 0; i < distinct.Count; i++)
            {
                meta[i] = new SpriteMetaData
                {
                    name = name + "_" + distinct[i],
                    rect = FrameRect(i, cols, rows, w, h),
                    alignment = (int)SpriteAlignment.Custom,
                    pivot = CenterPivot,
                };
            }
#pragma warning disable CS0618 // TextureImporter.spritesheet is legacy but remains the documented slice-from-code path
            importer.spritesheet = meta;
#pragma warning restore CS0618
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            // 4) collect the sliced sprites, mapped back to the source frame index each one represents.
            var spriteOfFrame = new Dictionary<int, Sprite>();
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(pngPath))
            {
                if (!(obj is Sprite s)) continue;
                int idx = FrameIndexOf(s.name);
                if (idx >= 0) spriteOfFrame[idx] = s;
            }
            if (spriteOfFrame.Count == 0)
            {
                result.message = "Bake produced no sprites from " + pngPath;
                Debug.LogWarning("[Shaper] " + result.message);
                return result;
            }

            // 5) the clip. One key per CHANGE of displayed sprite, at the document's own rate — so a cherry
            //    slot that holds for three beats is one key followed by three beats of silence, which is what
            //    a hold IS in a sprite curve, and a blank beat is a key with a null sprite (a SpriteRenderer
            //    with no sprite draws nothing).
            //
            //    T-0177 — SKIPPABLE via bakeAnimationClip: the Bake box lets a user who only wants the
            //    cherry-preserving ShaperClip opt out of the AnimationClip Unity would otherwise also write.
            float fps = Mathf.Clamp(doc.frameRate, ShaperClock.MinFrameRate, ShaperClock.MaxFrameRate);
            string clipPath = null;
            int clipKeyCount = 0;
            if (bakeAnimationClip)
            {
                var clip = new AnimationClip { frameRate = fps };
                var keys = new List<ObjectReferenceKeyframe>(order.Count);
                Sprite previous = null;
                bool first = true;
                for (int beat = 0; beat < order.Count; beat++)
                {
                    int f = order[beat];
                    Sprite s = null;
                    if (f != ShaperCherry.BlankFrame) spriteOfFrame.TryGetValue(f, out s);
                    if (!first && ReferenceEquals(s, previous)) continue;
                    keys.Add(new ObjectReferenceKeyframe { time = beat / fps, value = s });
                    previous = s;
                    first = false;
                }

                var binding = new EditorCurveBinding { type = typeof(SpriteRenderer), path = "", propertyName = "m_Sprite" };
                AnimationUtility.SetObjectReferenceCurve(clip, binding, keys.ToArray());

                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);

                clipPath = UniquePath(dir, name, "anim");
                AssetDatabase.CreateAsset(clip, clipPath);
                clipKeyCount = keys.Count;
                // T-0282 — flush THIS asset, never the project. See the note after the ShaperClip block below.
                AssetDatabase.SaveAssetIfDirty(clip);
            }

            // 6) the ShaperClip (T-0154). The AnimationClip above and this are NOT equivalent outputs, and the
            //    difference is not cosmetic:
            //
            //      • A Unity AnimationClip has a FIXED keyframe order. Cherry framing's per-pass variation is
            //        drawn from ShaperCherryState.loopIndex, so pass 2 legitimately differs from pass 1 — an
            //        AnimationClip has nowhere to put that and freezes whatever pass 0 happened to be.
            //      • A ShaperClip carries the cherry DATA (enabled flag, slots, loop delay, seed) and rebuilds
            //        a playback document, so ShaperPlayer re-runs ShaperCherry.AdvanceOneBeat live and the
            //        variation survives.
            //
            //    So: use the AnimationClip for Animator-driven, Unity-native consumption; use the ShaperClip
            //    when the authored sequence must play as authored. Both are emitted because neither subsumes
            //    the other.
            //
            //    INDEXING TRAP, handled here: frames is indexed by SOURCE DOCUMENT FRAME, not by sheet order.
            //    A cherry slot's sourceIndex is a document frame index, and ShaperPlayer does
            //    frames[Mathf.Clamp(_frame, 0, frames.Length-1)] (ShaperPlayer.cs:196-197). If this array were
            //    packed to just the distinct frames, a slot pointing at document frame 7 in a 3-sprite sheet
            //    would CLAMP to sprite 2 and silently play the wrong picture. The array is therefore full
            //    frameCount length with unused slots left null — and a null draws nothing, which is already
            //    how the player renders an authored blank beat (:192).
            // T-0177 — SKIPPABLE via bakeShaperClip. Unlike the AnimationClip, this is the ONLY output that can
            // express cherry framing (file header, decision explained above) — the Bake box's tooltip says so.
            string shaperClipPath = null;
            if (bakeShaperClip)
            {
                var shaperClip = ScriptableObject.CreateInstance<ShaperClip>();
                var clipFrames = new Sprite[Mathf.Max(1, doc.frameCount)];
                foreach (var kv in spriteOfFrame)
                    if (kv.Key >= 0 && kv.Key < clipFrames.Length) clipFrames[kv.Key] = kv.Value;
                shaperClip.frames = clipFrames;
                shaperClip.frameRate = fps;
                shaperClip.seed = doc.seed;
                shaperClip.cherryEnabled = doc.cherryEnabled;
                shaperClip.cherryFrames = doc.cherryFrames != null
                    ? new List<ShaperCherryFrame>(doc.cherryFrames) : new List<ShaperCherryFrame>();
                shaperClip.cherryLoopDelaySeconds = doc.cherryLoopDelaySeconds;
                shaperClip.sourceDocumentName = name;

                shaperClipPath = UniquePath(dir, name + " Clip", "asset");
                AssetDatabase.CreateAsset(shaperClip, shaperClipPath);
                // T-0282 — flush only the asset THIS bake just made, never the whole project.
                // AssetDatabase.SaveAssets() writes every dirty asset there is (the same class of bug T-0276
                // fixed on the New/Duplicate/Rename path); a Bake press was doing it too, and this call plus
                // the AnimationClip's own SaveAssetIfDirty above replace BOTH the old AssetDatabase.SaveAssets()
                // and the AssetDatabase.Refresh() that used to sit after this block. The sheet PNG was already
                // written to disk and force-reimported earlier in this method (File.WriteAllBytes + ImportAsset
                // + importer.SaveAndReimport, :205-243) — that path never touched SaveAssets/Refresh and still
                // doesn't. Refresh() is dropped outright: everything this method writes (PNG, importer, clip,
                // ShaperClip) is already registered with the AssetDatabase by the explicit ImportAsset/
                // CreateAsset/SaveAndReimport calls, so a project-wide re-scan added nothing here — re-verified
                // against T-0275's bake-parity table (workspace/T-0275/bake-parity-table.md) after this change.
                AssetDatabase.SaveAssetIfDirty(shaperClip);
            }

            result.ok = true;
            result.sheetPath = pngPath;
            result.clipPath = clipPath;
            result.shaperClipPath = shaperClipPath;
            result.sheetFrames = distinct.Count;
            result.clipKeys = clipKeyCount;
            result.columns = cols;
            result.rows = rows;
            result.message = $"Baked '{name}' → sheet: {pngPath}" +
                             (clipPath != null ? $" · clip: {clipPath}" : "") +
                             (shaperClipPath != null ? $" · ShaperClip: {shaperClipPath}" : "") +
                             $" ({distinct.Count} frames in a {cols}x{rows} sheet, {clipKeyCount} keys over " +
                             $"{order.Count} beats @ {fps}fps" +
                             (doc.cherryEnabled
                                ? (bakeShaperClip
                                    ? ", cherry sequence — the ShaperClip preserves its per-pass variation, the AnimationClip freezes pass 0"
                                    : ", cherry sequence — NOT preserved by the outputs baked this time")
                                : "") + ")";
            Debug.Log("[Shaper] " + result.message);
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(pngPath));
            return result;
        }

        // ── document→pixels: FORWARDERS ONLY ────────────────────────────────────────────────────────────
        // T-0153 moved the real implementation to Runtime/Shaper/ShaperDocumentRenderer.cs. What used to live
        // here — the layer walk and CompositeOver — was the engine's missing document renderer, written in the
        // editor only because a bake could not exist without it. It belongs in Runtime so that a bake, a
        // preview and a runtime consumer all composite identically, and so that "the bake is what you saw" is
        // true because ONE renderer exists rather than because two agree.
        //
        // These two remain as named forwarders rather than being deleted: they are the shape an editor caller
        // reaches for, and keeping them means a future Phase C window that already found ShaperBaker.RenderFrame
        // lands on the canonical renderer instead of being tempted to write its own.

        // Both forwarders pass ShaperEffectApplier.Instance (T-0156). The applier lives in the PyreShaper
        // bridge because the effect kernels are SpriteFx and Runtime/Shaper cannot name them without an
        // assembly cycle; an EDITOR caller sits above both, so this is the layer where the two meet. Omitting
        // it is not a neutral default — it silently renders every authored effect as a no-op.

        /// <summary>Render one frame index to straight-alpha sRGB pixels, effects applied. Forwards to <see cref="ShaperDocumentRenderer"/>.</summary>
        public static Color32[] RenderFrame(ShaperDocument doc, int frameIndex)
            => ShaperDocumentRenderer.RenderFrame(doc, frameIndex, ShaperEffectApplier.Instance);

        /// <summary>Render at an explicit phase to straight-alpha sRGB pixels, effects applied. Forwards to <see cref="ShaperDocumentRenderer"/>.</summary>
        public static Color32[] RenderPhase(ShaperDocument doc, float phase01)
            => ShaperDocumentRenderer.RenderPhase(doc, phase01, ShaperEffectApplier.Instance);

        // ── playback order ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One entry per beat for a single pass of playback: a source frame index, or
        /// <see cref="ShaperCherry.BlankFrame"/>.
        ///
        /// DECISION 2 (what a cherry-enabled bake contains). When cherry framing is on, this walks the cherry
        /// sequence — NOT the plain 0..frameCount-1 order. The guard rail the bake inherits from Pyre is that
        /// the bake is identical to what the user was watching, and what they were watching IS the cherry
        /// sequence: that is the entire purpose of the feature's header toggle. Baking the plain order instead
        /// would silently discard the authored sub-sequence, its holds and its randomised lengths, and hand
        /// back an animation the user never previewed.
        ///
        /// The walk stops after exactly one full pass (including a trailing blank loop-delay gap, which is
        /// part of the loop), because the clip is authored with loopTime — Unity repeats it, so baking two
        /// identical passes would only double the asset. Note the consequence of the engine's deliberate
        /// per-pass variation: a min/max length or a multi-frame pick draws from
        /// <see cref="ShaperCherryState.loopIndex"/>, so pass 2 differs from pass 1. A baked clip is one
        /// frozen pass and cannot express that; the bake captures pass 0. Stated rather than hidden.
        /// </summary>
        public static List<int> PlaybackOrder(ShaperDocument doc)
        {
            var order = new List<int>();
            if (doc == null) return order;
            int frames = Mathf.Max(1, doc.frameCount);

            bool cherry = doc.cherryEnabled && doc.cherryFrames != null && doc.cherryFrames.Count > 0;
            if (!cherry)
            {
                for (int f = 0; f < frames; f++) order.Add(f);
                return order;
            }

            var st = ShaperCherry.Begin(doc);
            order.Add(st.frame);
            for (int beat = 1; beat < MaxCherryBeats; beat++)
            {
                st = ShaperCherry.AdvanceOneBeat(st, doc);
                // Pass 2 has begun the moment the state is back in a real slot with a bumped loop index; the
                // blank delay gap (loopIndex already bumped, delayActive still true) is still pass 1's tail.
                if (st.loopIndex > 0 && !st.delayActive) break;
                order.Add(st.frame);
            }
            return order;
        }

        // ── sheet geometry + asset plumbing ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The rect of sheet cell <paramref name="cell"/>, in texture space (origin bottom-left). Cells run
        /// left-to-right and top-to-bottom as a reader expects, which is why the row is flipped here: visual
        /// row 0 is the TOP one, and the top of a texture is its highest y.
        /// </summary>
        static Rect FrameRect(int cell, int cols, int rows, int w, int h)
        {
            int cx = cell % cols;
            int cy = cell / cols;
            return new Rect(cx * w, (rows - 1 - cy) * h, w, h);
        }

        static void BlitFrame(Color32[] src, int w, int h, Color32[] sheet, int sheetW, int cell, int cols, int rows)
        {
            int cx = cell % cols;
            int cy = cell / cols;
            int ox = cx * w;
            int oy = (rows - 1 - cy) * h;
            for (int y = 0; y < h; y++)
                Array.Copy(src, y * w, sheet, (oy + y) * sheetW + ox, w);
        }

        /// <summary>Smallest importer size step that still contains the sheet, so nothing is downscaled.</summary>
        static int MaxTextureSizeFor(int longestEdge)
        {
            int[] steps = { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384 };
            foreach (int s in steps) if (s >= longestEdge) return s;
            return 16384;
        }

        /// <summary>
        /// A path under <paramref name="dir"/> that does not exist yet, versioning the name if needed.
        /// Standing project guard rail: a bake never overwrites a user asset.
        /// </summary>
        static string UniquePath(string dir, string baseName, string ext)
        {
            string path = $"{dir}/{baseName}.{ext}";
            if (!File.Exists(path)) return path;
            int i = 1;
            while (File.Exists($"{dir}/{baseName}_{i}.{ext}")) i++;
            string versioned = $"{dir}/{baseName}_{i}.{ext}";
            Debug.Log($"[Shaper] '{path}' exists — writing '{versioned}' instead (guard rail: never overwrite user assets).");
            return versioned;
        }

        static int FrameIndexOf(string spriteName)
        {
            int u = spriteName.LastIndexOf('_');
            return u >= 0 && int.TryParse(spriteName.Substring(u + 1), out int n) ? n : -1;
        }

        static string SanitizeName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }
    }
}
