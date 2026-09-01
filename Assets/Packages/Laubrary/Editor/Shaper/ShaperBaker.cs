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
// Not by discipline, but by construction: RenderPhase below IS the only document→pixels path this file has,
// and it is the same call sequence the light audit already uses to render a real document
// (ShaperLightAudit.cs:2851-2868 — CompileDocument → per layer Resolve → BindLayer → PaintTile → Encode).
// A future preview window is expected to call RenderFrame/RenderPhase rather than grow a second renderer;
// the moment two exist, this guarantee is gone. That is why these two are public and documented as the
// shared entry point, not private helpers of the bake.
//
// ── WHAT THE ENGINE COULD NOT GIVE THIS BAKE (real findings, not papered over) ───────────────────────────
// 1. THERE IS NO LAYER COMPOSITOR. ShaperFillResolver paints ONE layer at a time into its own buffer; the
//    audits compare per-layer results and never combine them, and a search of Runtime/Shaper for any
//    layer-compositing / src-over step returns nothing. So a document with two layers had, until now, no
//    defined final image anywhere in the engine. CompositeOver below is that missing step, written here
//    because a bake cannot exist without it. It composites in the float destination — linear, premultiplied
//    — which is exactly where ShaperFillResolver.Encode's own doc comment says a further composite belongs
//    ("read the float destination directly ... this is what a light stage or a further composite should
//    do", ShaperFillResolver.cs:1507-1511). It is a candidate to MOVE into Runtime/Shaper later so a runtime
//    player composites identically; it lives here now only because this task owns the editor side.
// 2. THERE IS NO DOCUMENT ASSET. ShaperDocument is a plain [Serializable] class (ShaperLightRig.cs:379),
//    not a ScriptableObject — the only ScriptableObject in the whole runtime is ShaperHeightFieldPreset.
//    So "bake beside the source asset", which is how Pyre chooses its folder, has no source asset to be
//    beside. The caller supplies the folder instead (decision 3).
// 3. THERE IS NO PIXELS-PER-UNIT. Pyre's spec carries pixelsPerUnit; ShaperDocument does not, and its
//    pixelSize is canvas units per SAMPLE (LR-1.5) — a sampling density, not a display scale. Reusing it as
//    PPU would be a category error that silently mis-scales every baked sprite (decision 1).
using System;
using System.Collections.Generic;
using System.IO;
using Laubrary.AssetKit.Editor;
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
        /// DECISION 1 (pixels per unit). The document cannot answer this: it has no PPU field, and its
        /// <c>pixelSize</c> means canvas units per sample (LR-1.5), not screen pixels per world unit —
        /// feeding it in as PPU would mis-scale every baked sprite while looking plausible. So the project
        /// convention governs instead: 16, the PPU this project standardised on. Exposed as a Bake parameter
        /// so a caller with a different convention is never forced through this default.
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
            public string clipPath;
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
        /// DECISION 3 (where it lands). Pyre bakes beside its spec asset; Shaper has no document asset to be
        /// beside (see the file header), so the folder is the caller's to choose and defaults to "Assets".
        /// When the Phase C window exists and documents become real assets, the natural follow-up is an
        /// overload that derives the folder from the asset path exactly as PyreBaker does — the guard rails
        /// here (unique naming, never overwriting) already behave correctly under that change.
        ///
        /// Does not mutate authored data: the document's <c>phase01</c> is driven per frame and restored in a
        /// finally, so no Undo record is needed and an exception mid-bake cannot leave the document scrubbed
        /// to some arbitrary frame.
        /// </summary>
        public static BakeResult Bake(ShaperDocument doc, string folder = "Assets", string baseName = null,
                                      float pixelsPerUnit = DefaultPixelsPerUnit)
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
            var acc = new float[w * h * 4];
            var framePx = new Color32[w * h];
            for (int i = 0; i < distinct.Count; i++)
            {
                RenderPhaseInto(doc, doc.PhaseOfFrame(distinct[i]), acc);
                ShaperFillResolver.Encode(acc, framePx, w * h);
                BlitFrame(framePx, w, h, sheetPx, sheetW, i, cols, rows);
            }

            var sheet = new Texture2D(sheetW, sheetH, TextureFormat.RGBA32, false);
            sheet.SetPixels32(sheetPx);
            sheet.Apply(false, false);

            // 2) write the PNG, never clobbering an existing file.
            string dir = string.IsNullOrEmpty(folder) ? "Assets" : folder.Replace('\\', '/').TrimEnd('/');
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
            float fps = Mathf.Clamp(doc.frameRate, ShaperClock.MinFrameRate, ShaperClock.MaxFrameRate);
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

            string clipPath = UniquePath(dir, name, "anim");
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            result.ok = true;
            result.sheetPath = pngPath;
            result.clipPath = clipPath;
            result.sheetFrames = distinct.Count;
            result.clipKeys = keys.Count;
            result.columns = cols;
            result.rows = rows;
            result.message = $"Baked '{name}' → sheet: {pngPath} · clip: {clipPath} " +
                             $"({distinct.Count} frames in a {cols}x{rows} sheet, {keys.Count} keys over " +
                             $"{order.Count} beats @ {fps}fps{(doc.cherryEnabled ? ", cherry sequence" : "")})";
            Debug.Log("[Shaper] " + result.message);
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(pngPath));
            return result;
        }

        // ── the shared document→pixels path (see the file header's identical-to-preview note) ───────────

        /// <summary>Render one frame index of <paramref name="doc"/> to straight-alpha sRGB pixels.</summary>
        public static Color32[] RenderFrame(ShaperDocument doc, int frameIndex)
            => doc == null ? Array.Empty<Color32>()
             : RenderPhase(doc, doc.PhaseOfFrame(ShaperClock.WrapFrame(frameIndex, Mathf.Max(1, doc.frameCount))));

        /// <summary>
        /// Render <paramref name="doc"/> at an explicit phase. Row 0 of the returned array is the BOTTOM row,
        /// matching both <see cref="ShaperSampleGrid"/>'s +Y-up sampling and Texture2D.SetPixels32, so no flip
        /// is needed anywhere between the evaluator and the PNG.
        /// </summary>
        public static Color32[] RenderPhase(ShaperDocument doc, float phase01)
        {
            if (doc == null) return Array.Empty<Color32>();
            int w = Mathf.Max(1, doc.canvasWidth), h = Mathf.Max(1, doc.canvasHeight), n = w * h;
            var acc = new float[n * 4];
            RenderPhaseInto(doc, phase01, acc);
            var px = new Color32[n];
            ShaperFillResolver.Encode(acc, px, n);
            return px;
        }

        /// <summary>
        /// The layer walk. Mirrors ShaperLightAudit.cs:2851-2868 — the one place in the codebase that already
        /// renders a real document — and adds the layer composite the engine does not have (file header, 1).
        /// </summary>
        static void RenderPhaseInto(ShaperDocument doc, float phase01, float[] acc)
        {
            int w = Mathf.Max(1, doc.canvasWidth), h = Mathf.Max(1, doc.canvasHeight), n = w * h;
            Array.Clear(acc, 0, n * 4);
            if (doc.layers == null || doc.layers.Count == 0) return;

            // The rig is sampled on the DOCUMENT's clock (LR-1.8), so the document must BE at this phase while
            // its lights compile — driving only the per-layer Resolve would light every frame as if it were
            // the frame the user happened to be parked on. Restored in the finally: the bake is a read.
            float savedPhase = doc.phase01;
            try
            {
                doc.phase01 = phase01;
                var grid = doc.Grid();
                var prog = ShaperLightCompiler.CompileDocument(doc);
                float halfW = 0.5f * (w - 1) * doc.pixelSize;
                float halfH = 0.5f * (h - 1) * doc.pixelSize;

                for (int li = 0; li < doc.layers.Count; li++)
                {
                    var lay = doc.layers[li];
                    // A disabled layer is skipped entirely rather than painted and discarded: it must not cast
                    // shadows either, and BindLayer is index-addressed (li is explicit), so skipping cannot
                    // shift any other layer's binding.
                    if (lay == null || !lay.enabled || lay.root == null) continue;

                    var fdoc = ShaperFillResolver.Resolve(lay.root, phase01, doc.seed, halfW, halfH,
                                                          ShaperQuantitySet.ShippedShapeEngine);
                    var buf = new ShaperFillBuffers(n, Mathf.Max(1, fdoc.owners.Count));
                    var scene = ShaperLightCompiler.BindLayer(doc, li, prog, buf.sampleCapacity, buf.ownerCapacity);
                    ShaperFillResolver.PaintTile(fdoc, grid, 0, 0, w, h, buf,
                                                 new ShaperFillSheets { published = ShaperQuantitySet.ShippedShapeEngine },
                                                 scene);
                    CompositeOver(acc, buf.dst, n);
                }
            }
            finally { doc.phase01 = savedPhase; }
        }

        /// <summary>
        /// Premultiplied source-over, layer on top of everything already accumulated beneath it. Layers are
        /// ordered bottom-most first and are never reordered (ShaperResolve.cs:120), so walking the list in
        /// order and compositing each one OVER the accumulator is the document's stacking order by definition.
        ///
        /// Done in the float destination, which is linear and premultiplied (ShaperFillResolver.cs:241-242) —
        /// the encode to straight-alpha sRGB happens ONCE, after every layer has landed. Compositing after
        /// encoding instead would mean un-premultiplying and re-encoding per layer, losing an additive glow's
        /// colour at every step for exactly the reason Encode's own doc comment gives.
        /// </summary>
        static void CompositeOver(float[] acc, float[] src, int n)
        {
            for (int i = 0; i < n; i++)
            {
                int k = i * 4;
                float sa = src[k + 3];
                float inv = 1f - sa;
                acc[k + 0] = src[k + 0] + acc[k + 0] * inv;
                acc[k + 1] = src[k + 1] + acc[k + 1] * inv;
                acc[k + 2] = src[k + 2] + acc[k + 2] * inv;
                acc[k + 3] = sa + acc[k + 3] * inv;
            }
        }

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
