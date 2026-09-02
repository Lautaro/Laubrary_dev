// ShaperGif — Pyre parity (T-0160/T-0149): a self-contained GIF89a encoder for Shaper documents, ported from
// Editor/Pyre/PyreGif.cs. It bakes every playback beat via ShaperBaker.RenderFrame (the SAME forwarder the
// real bake uses, over ShaperBaker.PlaybackOrder — the same cherry-aware beat sequence, blanks included), so
// the exported GIF matches what Bake produces and what the preview showed, never a second frame path.
//
// Determinism: pixels are gathered in scan order into a histogram whose keys are then sorted by packed RGB, and
// every median-cut split is a count-weighted median along the widest channel with a total-order sort — no
// randomness anywhere, so one document always encodes to the same bytes (BC-1.3).
//
// Composite rule: a source pixel with alpha >= 128 is drawn OPAQUE over its own RAW rgb (the renderer already
// composited onto the document's own layers, so no premultiply here); alpha < 128 becomes the GIF's single
// transparent index. No AssetDatabase work here — the caller supplies the path (a user Save dialog) and owns
// any import-refresh implications if it lands inside Assets/.
//
// Not shared with Pyre: PyreGif.cs is left untouched (read-only reference per the programme rules) and this
// file owns its own copy of the encoder rather than factoring out a third shared class — the two tools' frame
// sources (a Pyre spec's flat frameCount vs. a Shaper document's cherry-aware beat order) differ enough that a
// forced shared core would just relocate the divergence, not remove it.
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Laubrary.Shaper.Editor
{
    public static class ShaperGif
    {
        const int AlphaCutoff = 128;   // alpha >= this is opaque; below it maps to the transparent index
        const int MaxColors = 255;     // palette colours; leaves index MaxColors free for the transparent slot

        // GIF transparency is ONE BIT — a pixel is either fully opaque or fully transparent; the format has no
        // partial alpha at all. A single flat cutoff therefore SLICES every soft edge into a hard silhouette,
        // which is glaring on a feathered fill (soft rims, glows, frame fades) even though the rendered frames
        // themselves are perfectly smooth. Ordered (Bayer 4×4) dithering trades that hard cut for a stipple: a
        // pixel at alpha a is kept with probability ≈ a/255, patterned rather than random, so a gradient reads
        // as a dissolve instead of a cliff. The threshold is looked up in SOURCE-pixel space, so the stipple
        // stays at the art's own resolution and an upscaled export keeps whole, solid pixels. (Carried over
        // verbatim from PyreGif — T-0149's own gotcha note calls out this exact rationale.)
        static readonly byte[] Bayer4 =
        {
             0,  8,  2, 10,
            12,  4, 14,  6,
             3, 11,  1,  9,
            15,  7, 13,  5,
        };

        static bool IsOpaque(byte a, int bx, int by, bool dither)
        {
            if (!dither) return a >= AlphaCutoff;
            // 8..248, so alpha 0 is never kept and alpha 255 always is.
            int threshold = Bayer4[(by & 3) * 4 + (bx & 3)] * 16 + 8;
            return a >= threshold;
        }

        public static void Export(ShaperDocument doc, string path, int scale) => Export(doc, path, scale, false);

        public static void Export(ShaperDocument doc, string path, int scale, bool ditherAlpha)
        {
            if (doc == null || string.IsNullOrEmpty(path)) return;
            scale = Mathf.Clamp(scale, 1, 8);
            int W = Mathf.Max(1, doc.canvasWidth), H = Mathf.Max(1, doc.canvasHeight);
            int outW = W * scale, outH = H * scale;

            // 1) The playback order — the SAME beat sequence ShaperBaker.Bake writes to the sheet/clip, cherry
            //    framing (and its blanks) included. One GIF frame per beat, so a held slot repeats its frame in
            //    the export exactly as it repeats it on screen, and a blank beat encodes as a fully transparent
            //    frame rather than being skipped.
            var order = ShaperBaker.PlaybackOrder(doc);
            int frameCount = order.Count;
            if (frameCount == 0) return;

            // 2) Render each DISTINCT source frame once (cherry sequences routinely revisit or hold a frame),
            //    then map every beat onto its already-rendered pixels. A blank beat gets a shared, fully
            //    transparent buffer rather than a render call.
            var blank = new Color32[W * H];   // default Color32 is (0,0,0,0) — fully transparent
            var renderedOf = new Dictionary<int, Color32[]>();
            var frames = new Color32[frameCount][];
            for (int i = 0; i < frameCount; i++)
            {
                int f = order[i];
                if (f == ShaperCherry.BlankFrame) { frames[i] = blank; continue; }
                if (!renderedOf.TryGetValue(f, out var px))
                {
                    px = ShaperBaker.RenderFrame(doc, f);
                    renderedOf[f] = px;
                }
                frames[i] = px;
            }

            // 3) Global palette — median-cut over the union of every frame's opaque pixels; colorToIndex maps
            //    each unique opaque colour straight to its box (palette) index (each colour lives in exactly
            //    one box).
            var palette = BuildPalette(frames, W, ditherAlpha, out var colorToIndex);
            int paletteCount = palette.Count;                       // 1..255
            int transparentIndex = paletteCount;                    // the slot right after the colours
            int gctBits = Mathf.Max(1, CeilLog2(paletteCount + 1)); // enough entries for the colours + transparent
            int gctSize = 1 << gctBits;                             // global colour table length (a power of two)
            int minCodeSize = Mathf.Max(2, gctBits);                // GIF LZW minimum code size (>= 2)

            // 4) Per-frame delay in centiseconds (>= 2 so viewers don't clamp to their own minimum), from the
            //    document's own rate — the same clamp ShaperBaker applies before writing the AnimationClip.
            float fps = Mathf.Clamp(doc.frameRate, ShaperClock.MinFrameRate, ShaperClock.MaxFrameRate);
            int delay = Mathf.Max(2, Mathf.RoundToInt(100f / Mathf.Max(1f, fps)));

            using (var ms = new MemoryStream())
            {
                WriteHeader(ms, outW, outH, gctBits, transparentIndex);
                WriteGlobalColorTable(ms, palette, gctSize);
                WriteNetscapeLoop(ms);

                var indices = new byte[outW * outH];
                for (int i = 0; i < frameCount; i++)
                {
                    BuildIndices(frames[i], W, H, scale, outW, outH, colorToIndex, transparentIndex, indices, ditherAlpha);
                    WriteGraphicControl(ms, delay, transparentIndex);
                    WriteImageDescriptor(ms, outW, outH);
                    WriteImageData(ms, indices, minCodeSize);
                }

                ms.WriteByte(0x3B);   // GIF trailer
                WriteAllBytesNoOverwrite(path, ms.ToArray());
            }
        }

        // Guard rail carried from ShaperBaker.UniquePath: a GIF export never silently overwrites an existing
        // file at the user-chosen path — a repeat export gets a versioned name beside it instead. Unlike the
        // baker this path is not necessarily under Assets/ (a Save dialog can point anywhere), so this works
        // on the raw filesystem path rather than through AssetDatabase.
        static void WriteAllBytesNoOverwrite(string path, byte[] bytes)
        {
            if (!File.Exists(path)) { File.WriteAllBytes(path, bytes); return; }
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            int i = 1;
            string versioned;
            do { versioned = Path.Combine(dir, $"{name}_{i}{ext}"); i++; } while (File.Exists(versioned));
            Debug.Log($"[Shaper] '{path}' exists — writing '{versioned}' instead (guard rail: never overwrite user assets).");
            File.WriteAllBytes(versioned, bytes);
        }

        // ── frame → palette indices ──────────────────────────────────────────────────
        // Fill `indices` (top-down, upscaled) for one frame. ShaperDocumentRenderer's buffer is y-up (row 0 =
        // bottom, ShaperDocumentRenderer.cs:62-64) and GIF rows run top-down, so the encode pass flips
        // vertically — the exact same flip PyreGif applies to PyreRenderer's own y-up buffer.
        static void BuildIndices(Color32[] buf, int W, int H, int scale, int outW, int outH,
                                 Dictionary<int, int> colorToIndex, int transparentIndex, byte[] indices,
                                 bool ditherAlpha)
        {
            for (int oy = 0; oy < outH; oy++)
            {
                int srcTop = oy / scale;         // top-down source row (0 = top)
                int by = H - 1 - srcTop;         // buffer is y-up (row 0 = bottom) → flip to top-down
                int bufRow = by * W;
                int outRow = oy * outW;
                for (int ox = 0; ox < outW; ox++)
                {
                    int bx = ox / scale;
                    var c = buf[bufRow + bx];
                    // Keyed on the BUFFER coords (bx, by) — the same ones BuildPalette uses — so the palette
                    // pass and the encode pass agree on exactly which pixels end up opaque.
                    if (IsOpaque(c.a, bx, by, ditherAlpha))
                    {
                        int key = (c.r << 16) | (c.g << 8) | c.b;
                        indices[outRow + ox] = colorToIndex.TryGetValue(key, out int pi) ? (byte)pi : (byte)0;
                    }
                    else
                        indices[outRow + ox] = (byte)transparentIndex;
                }
            }
        }

        // ── median-cut quantization ───────────────────────────────────────────────────
        readonly struct Box
        {
            public readonly int start, end;   // [start, end) into the parallel r/g/b/cnt arrays
            public Box(int s, int e) { start = s; end = e; }
        }

        static List<Color32> BuildPalette(Color32[][] frames, int W, bool ditherAlpha,
                                          out Dictionary<int, int> colorToIndex)
        {
            // Histogram: packed RGB key → count, over every frame's opaque pixels in scan order. It MUST use the
            // same opacity test the encode pass does — with dithering on, edge pixels the flat cutoff would have
            // dropped now survive, and a colour missing from the palette would encode as index 0 (a wrong colour).
            var hist = new Dictionary<int, int>();
            for (int f = 0; f < frames.Length; f++)
            {
                var buf = frames[f];
                for (int i = 0; i < buf.Length; i++)
                {
                    var c = buf[i];
                    if (!IsOpaque(c.a, i % W, i / W, ditherAlpha)) continue;
                    int key = (c.r << 16) | (c.g << 8) | c.b;
                    hist.TryGetValue(key, out int n);
                    hist[key] = n + 1;
                }
            }

            colorToIndex = new Dictionary<int, int>(hist.Count);

            // No opaque pixels anywhere → a one-entry dummy palette (every pixel is transparent regardless).
            if (hist.Count == 0)
                return new List<Color32> { new Color32(0, 0, 0, 255) };

            // Deterministic colour list: sort the histogram keys by packed RGB so the initial order never depends
            // on Dictionary enumeration order. Parallel arrays carry each colour's channels + count.
            var keys = new List<int>(hist.Keys);
            keys.Sort();
            int m = keys.Count;
            var r = new byte[m]; var g = new byte[m]; var b = new byte[m]; var cnt = new int[m];
            for (int i = 0; i < m; i++)
            {
                int k = keys[i];
                r[i] = (byte)((k >> 16) & 0xFF);
                g[i] = (byte)((k >> 8) & 0xFF);
                b[i] = (byte)(k & 0xFF);
                cnt[i] = hist[k];
            }

            // Split boxes until we hit MaxColors or every box is a single unique colour. Each iteration picks the
            // box with the widest single-channel spread (ties keep the lowest index → stable), sorts it along
            // that channel, and cuts at the count-weighted median.
            var boxes = new List<Box> { new Box(0, m) };
            while (boxes.Count < MaxColors)
            {
                int pick = -1, bestRange = -1;
                for (int i = 0; i < boxes.Count; i++)
                {
                    var bx = boxes[i];
                    if (bx.end - bx.start < 2) continue;   // a single unique colour can't split
                    ChannelRanges(bx.start, bx.end, r, g, b, out int rr, out int gg, out int bb);
                    int range = Mathf.Max(rr, Mathf.Max(gg, bb));
                    if (range > bestRange) { bestRange = range; pick = i; }
                }
                if (pick < 0) break;   // all boxes are singletons — done (fewer than MaxColors unique colours)

                var box = boxes[pick];
                ChannelRanges(box.start, box.end, r, g, b, out int r2, out int g2, out int b2);
                int channel = (r2 >= g2 && r2 >= b2) ? 0 : (g2 >= b2 ? 1 : 2);
                SortRange(box.start, box.end, channel, r, g, b, cnt);
                int split = MedianSplit(box.start, box.end, cnt);
                boxes[pick] = new Box(box.start, split);
                boxes.Add(new Box(split, box.end));
            }

            // Representative colour per box = its count-weighted average, rounded. Record each source colour's
            // palette index as its box index — exact (every colour is in exactly one box), no nearest-search.
            var palette = new List<Color32>(boxes.Count);
            for (int bi = 0; bi < boxes.Count; bi++)
            {
                var bx = boxes[bi];
                long sr = 0, sg = 0, sb = 0, sw = 0;
                for (int i = bx.start; i < bx.end; i++)
                {
                    sr += (long)r[i] * cnt[i];
                    sg += (long)g[i] * cnt[i];
                    sb += (long)b[i] * cnt[i];
                    sw += cnt[i];
                    colorToIndex[(r[i] << 16) | (g[i] << 8) | b[i]] = bi;
                }
                if (sw == 0) sw = 1;
                palette.Add(new Color32((byte)((sr + sw / 2) / sw),
                                        (byte)((sg + sw / 2) / sw),
                                        (byte)((sb + sw / 2) / sw), 255));
            }
            return palette;
        }

        static void ChannelRanges(int start, int end, byte[] r, byte[] g, byte[] b,
                                  out int rRange, out int gRange, out int bRange)
        {
            int rmin = 255, rmax = 0, gmin = 255, gmax = 0, bmin = 255, bmax = 0;
            for (int i = start; i < end; i++)
            {
                if (r[i] < rmin) rmin = r[i]; if (r[i] > rmax) rmax = r[i];
                if (g[i] < gmin) gmin = g[i]; if (g[i] > gmax) gmax = g[i];
                if (b[i] < bmin) bmin = b[i]; if (b[i] > bmax) bmax = b[i];
            }
            rRange = rmax - rmin; gRange = gmax - gmin; bRange = bmax - bmin;
        }

        // Sort [start,end) of the parallel arrays by `channel`, tie-broken by packed RGB key (unique per colour),
        // so the result is a total order — fully deterministic regardless of the sort's stability.
        static void SortRange(int start, int end, int channel, byte[] r, byte[] g, byte[] b, int[] cnt)
        {
            int len = end - start;
            var order = new int[len];
            for (int i = 0; i < len; i++) order[i] = start + i;
            System.Array.Sort(order, (ia, ib) =>
            {
                int va = channel == 0 ? r[ia] : channel == 1 ? g[ia] : b[ia];
                int vb = channel == 0 ? r[ib] : channel == 1 ? g[ib] : b[ib];
                if (va != vb) return va - vb;
                int ka = (r[ia] << 16) | (g[ia] << 8) | b[ia];
                int kb = (r[ib] << 16) | (g[ib] << 8) | b[ib];
                return ka - kb;
            });
            var tr = new byte[len]; var tg = new byte[len]; var tb = new byte[len]; var tc = new int[len];
            for (int i = 0; i < len; i++)
            {
                int src = order[i];
                tr[i] = r[src]; tg[i] = g[src]; tb[i] = b[src]; tc[i] = cnt[src];
            }
            for (int i = 0; i < len; i++)
            {
                r[start + i] = tr[i]; g[start + i] = tg[i]; b[start + i] = tb[i]; cnt[start + i] = tc[i];
            }
        }

        // The split index (into [start,end)) at the count-weighted median, clamped so both sides are non-empty.
        static int MedianSplit(int start, int end, int[] cnt)
        {
            long total = 0;
            for (int i = start; i < end; i++) total += cnt[i];
            long half = total / 2;
            long acc = 0;
            int split = start + 1;
            for (int i = start; i < end; i++)
            {
                acc += cnt[i];
                if (acc >= half) { split = i + 1; break; }
            }
            return Mathf.Clamp(split, start + 1, end - 1);
        }

        static int CeilLog2(int n)
        {
            int bits = 0, v = 1;
            while (v < n) { v <<= 1; bits++; }
            return bits;   // smallest bits with (1<<bits) >= n; CeilLog2(1) == 0
        }

        // ── GIF block writers ─────────────────────────────────────────────────────────
        static void WriteU16(Stream s, int v)
        {
            s.WriteByte((byte)(v & 0xFF));
            s.WriteByte((byte)((v >> 8) & 0xFF));
        }

        // Header ("GIF89a") + Logical Screen Descriptor. bgIndex = the transparent slot, so disposal-to-background
        // restores to transparent between frames.
        static void WriteHeader(Stream s, int w, int h, int gctBits, int bgIndex)
        {
            s.WriteByte((byte)'G'); s.WriteByte((byte)'I'); s.WriteByte((byte)'F');
            s.WriteByte((byte)'8'); s.WriteByte((byte)'9'); s.WriteByte((byte)'a');
            WriteU16(s, w); WriteU16(s, h);
            // Packed: GCT flag(1)=1 | colour resolution(3) | sort(1)=0 | GCT size(3). Both 3-bit fields = gctBits-1.
            int fields = 0x80 | ((gctBits - 1) << 4) | (gctBits - 1);
            s.WriteByte((byte)fields);
            s.WriteByte((byte)bgIndex);   // background colour index
            s.WriteByte(0x00);            // pixel aspect ratio (none)
        }

        static void WriteGlobalColorTable(Stream s, List<Color32> palette, int gctSize)
        {
            for (int i = 0; i < gctSize; i++)
            {
                if (i < palette.Count) { var c = palette[i]; s.WriteByte(c.r); s.WriteByte(c.g); s.WriteByte(c.b); }
                else { s.WriteByte(0); s.WriteByte(0); s.WriteByte(0); }   // transparent slot + any pad (RGB unused)
            }
        }

        // NETSCAPE2.0 application extension — loop forever (loop count 0).
        static void WriteNetscapeLoop(Stream s)
        {
            s.WriteByte(0x21); s.WriteByte(0xFF); s.WriteByte(0x0B);
            byte[] app = System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0");
            s.Write(app, 0, app.Length);
            s.WriteByte(0x03);   // sub-block size
            s.WriteByte(0x01);   // sub-block id
            WriteU16(s, 0);      // 0 = loop forever
            s.WriteByte(0x00);   // block terminator
        }

        // Graphic Control Extension: disposal method 2 (restore to background) + transparency flag, per-frame delay.
        static void WriteGraphicControl(Stream s, int delayCentis, int transparentIndex)
        {
            s.WriteByte(0x21); s.WriteByte(0xF9); s.WriteByte(0x04);
            // Packed: reserved(3)=0 | disposal(3)=2 | user input(1)=0 | transparent colour flag(1)=1.
            s.WriteByte((byte)((2 << 2) | 0x01));
            WriteU16(s, delayCentis);
            s.WriteByte((byte)transparentIndex);
            s.WriteByte(0x00);   // block terminator
        }

        static void WriteImageDescriptor(Stream s, int w, int h)
        {
            s.WriteByte(0x2C);
            WriteU16(s, 0); WriteU16(s, 0);   // image left, top
            WriteU16(s, w); WriteU16(s, h);   // image width, height
            s.WriteByte(0x00);                // no local colour table, no interlace, no sort
        }

        // LZW minimum code size byte, then the LZW-compressed indices chunked into ≤255-byte sub-blocks, then the
        // zero-length block terminator.
        static void WriteImageData(Stream s, byte[] indices, int minCodeSize)
        {
            s.WriteByte((byte)minCodeSize);
            var lzw = LzwEncode(indices, minCodeSize);
            int pos = 0;
            while (pos < lzw.Count)
            {
                int chunk = Mathf.Min(255, lzw.Count - pos);
                s.WriteByte((byte)chunk);
                for (int i = 0; i < chunk; i++) s.WriteByte(lzw[pos + i]);
                pos += chunk;
            }
            s.WriteByte(0x00);   // block terminator
        }

        // ── the canonical GIF variable-width LZW (GIFCOMPR / ImageSharp semantics) ────
        // Clear = 1<<minCode, end = clear+1, first data code = end+1. Codes pack LSB-first; sub-block framing is
        // added by WriteImageData.
        //
        // The one thing every naive GIF encoder gets wrong: the CODE-WIDTH BUMP TIMING. The encoder assigns a
        // new dictionary code the instant it emits a prefix, so its `nextCode` runs exactly ONE AHEAD of the
        // decoder's table counter (the decoder only adds the matching entry when it reads the NEXT code). A
        // decoder widens its read size when ITS counter reaches 1<<width; to stay in lockstep the encoder must
        // therefore widen one code LATER than nextCode==1<<width — i.e. at nextCode==(1<<width)+1 (checked
        // post-increment). This is algebraically the classic GIFCOMPR `free_ent > maxcode` (checked
        // pre-increment) rule. Bumping at nextCode==1<<width instead desyncs the bitstream a few codes in.
        // Ported verbatim from PyreGif, whose SelfTest()/GifLzwDecode() round-trip proved this exact rule against
        // an independent decoder — not re-added here since this file changes no LZW logic, only the frame source.
        static List<byte> LzwEncode(byte[] indices, int minCodeSize)
        {
            int clearCode = 1 << minCodeSize;
            int endCode = clearCode + 1;
            var outBytes = new List<byte>();
            int bitBuffer = 0, bitCount = 0;

            void Emit(int code, int width)
            {
                bitBuffer |= code << bitCount;
                bitCount += width;
                while (bitCount >= 8)
                {
                    outBytes.Add((byte)(bitBuffer & 0xFF));
                    bitBuffer >>= 8;
                    bitCount -= 8;
                }
            }

            int codeWidth = minCodeSize + 1;
            int nextCode = endCode + 1;
            var dict = new Dictionary<int, int>();

            Emit(clearCode, codeWidth);

            if (indices.Length == 0)
            {
                Emit(endCode, codeWidth);
                if (bitCount > 0) outBytes.Add((byte)(bitBuffer & 0xFF));
                return outBytes;
            }

            int prefix = indices[0];
            for (int i = 1; i < indices.Length; i++)
            {
                int c = indices[i];
                int key = (prefix << 8) | c;
                if (dict.TryGetValue(key, out int existing))
                {
                    prefix = existing;   // still in the table — extend the current string
                    continue;
                }

                // (prefix, c) is new: emit the prefix code at the CURRENT width, then register the new pair.
                Emit(prefix, codeWidth);
                if (nextCode < 4096)
                {
                    dict[key] = nextCode;
                    nextCode++;
                    // Widen ONE code after the width fills — (1<<width)+1 post-increment — so the encoder's
                    // write size matches the decoder's read size (which lags the encoder's dictionary by one
                    // entry).
                    if (nextCode == (1 << codeWidth) + 1 && codeWidth < 12)
                        codeWidth++;
                }
                else
                {
                    // Dictionary full (codes 0..4095 all assigned) — emit a clear at the current (12-bit) width
                    // and rebuild from scratch, exactly where the decoder's own full table makes it expect one.
                    Emit(clearCode, codeWidth);
                    dict.Clear();
                    codeWidth = minCodeSize + 1;
                    nextCode = endCode + 1;
                }
                prefix = c;
            }

            Emit(prefix, codeWidth);   // the final pending string
            Emit(endCode, codeWidth);
            if (bitCount > 0) outBytes.Add((byte)(bitBuffer & 0xFF));   // flush the final partial byte
            return outBytes;
        }
    }
}
