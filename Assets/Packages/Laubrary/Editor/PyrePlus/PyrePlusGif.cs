// PyrePlusGif — a self-contained GIF89a encoder for the PyrePlus prototype (see G3). It bakes every frame via
// PyrePlusRenderer.RenderFrame, nearest-neighbour upscales by `scale`, quantizes the union of every frame's
// OPAQUE pixels to a median-cut global palette (≤255 colours + one reserved transparent slot), and writes a
// looping animated GIF with a per-frame delay + disposal-to-background so a transparent-background animation
// plays correctly.
//
// Determinism: pixels are gathered in scan order into a histogram whose keys are then sorted by packed RGB, and
// every median-cut split is a count-weighted median along the widest channel with a total-order sort — no
// randomness anywhere, so one spec always encodes to the same bytes.
//
// Composite rule (per G3): a source pixel with alpha >= 128 is drawn OPAQUE over its own RAW rgb (the renderer
// already Over-composited onto the spec background, so no premultiply); alpha < 128 becomes the GIF's single
// transparent index. No AssetDatabase work here — the caller supplies the path (a user Save dialog) and owns any
// import refresh implications if it lands inside Assets/.
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Laubrary.PyrePlus.Editor
{
    public static class PyrePlusGif
    {
        const int AlphaCutoff = 128;   // alpha >= this is opaque; below it maps to the transparent index
        const int MaxColors = 255;     // palette colours; leaves index MaxColors free for the transparent slot

        public static void Export(PyrePlusSpec spec, string path, int scale)
        {
            if (spec == null || string.IsNullOrEmpty(path)) return;
            scale = Mathf.Clamp(scale, 1, 8);
            int W = spec.Width, H = spec.Height;
            int frameCount = Mathf.Max(1, spec.frameCount);
            int outW = W * scale, outH = H * scale;

            // 1) Bake every frame once. RenderFrame's buffer is y-UP (row 0 = BOTTOM); GIF rows run top-down, so the
            //    encode pass flips vertically. Kept in memory so the palette pass and the encode pass share pixels.
            var frames = new Color32[frameCount][];
            for (int f = 0; f < frameCount; f++)
                frames[f] = PyrePlusRenderer.RenderFrame(spec, f);

            // 2) Global palette — median-cut over the union of every frame's opaque pixels; colorToIndex maps each
            //    unique opaque colour straight to its box (palette) index (each colour lives in exactly one box).
            var palette = BuildPalette(frames, out var colorToIndex);
            int paletteCount = palette.Count;                       // 1..255
            int transparentIndex = paletteCount;                    // the slot right after the colours
            int gctBits = Mathf.Max(1, CeilLog2(paletteCount + 1)); // enough entries for the colours + transparent
            int gctSize = 1 << gctBits;                             // global colour table length (a power of two)
            int minCodeSize = Mathf.Max(2, gctBits);                // GIF LZW minimum code size (>= 2)

            // 3) Per-frame delay in centiseconds (>= 2 so viewers don't clamp to their own minimum), from previewFps.
            int delay = Mathf.Max(2, Mathf.RoundToInt(100f / Mathf.Max(1f, spec.previewFps)));

            using (var ms = new MemoryStream())
            {
                WriteHeader(ms, outW, outH, gctBits, transparentIndex);
                WriteGlobalColorTable(ms, palette, gctSize);
                WriteNetscapeLoop(ms);

                var indices = new byte[outW * outH];
                for (int f = 0; f < frameCount; f++)
                {
                    BuildIndices(frames[f], W, H, scale, outW, outH, colorToIndex, transparentIndex, indices);
                    WriteGraphicControl(ms, delay, transparentIndex);
                    WriteImageDescriptor(ms, outW, outH);
                    WriteImageData(ms, indices, minCodeSize);
                }

                ms.WriteByte(0x3B);   // GIF trailer
                File.WriteAllBytes(path, ms.ToArray());
            }
        }

        // ── frame → palette indices ──────────────────────────────────────────────────
        // Fill `indices` (top-down, upscaled) for one frame. The renderer's buffer is y-up (row 0 = bottom) and
        // GUI blits its highest row at the top of the preview, so GIF top row oy=0 = buffer row H-1 (the flip).
        static void BuildIndices(Color32[] buf, int W, int H, int scale, int outW, int outH,
                                 Dictionary<int, int> colorToIndex, int transparentIndex, byte[] indices)
        {
            for (int oy = 0; oy < outH; oy++)
            {
                int srcTop = oy / scale;         // top-down source row (0 = top)
                int by = H - 1 - srcTop;         // buffer is y-up (row 0 = bottom) → flip to top-down
                int bufRow = by * W;
                int outRow = oy * outW;
                for (int ox = 0; ox < outW; ox++)
                {
                    var c = buf[bufRow + ox / scale];
                    if (c.a >= AlphaCutoff)
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

        static List<Color32> BuildPalette(Color32[][] frames, out Dictionary<int, int> colorToIndex)
        {
            // Histogram: packed RGB key → count, over every frame's opaque pixels in scan order.
            var hist = new Dictionary<int, int>();
            for (int f = 0; f < frames.Length; f++)
            {
                var buf = frames[f];
                for (int i = 0; i < buf.Length; i++)
                {
                    var c = buf[i];
                    if (c.a < AlphaCutoff) continue;
                    int key = (c.r << 16) | (c.g << 8) | c.b;
                    hist.TryGetValue(key, out int n);
                    hist[key] = n + 1;
                }
            }

            colorToIndex = new Dictionary<int, int>(hist.Count);

            // No opaque pixels anywhere → a one-entry dummy palette (every pixel is transparent regardless).
            if (hist.Count == 0)
                return new List<Color32> { new Color32(0, 0, 0, 255) };

            // Deterministic colour list: sort the histogram keys by packed RGB so the initial order never depends on
            // Dictionary enumeration order. Parallel arrays carry each colour's channels + count.
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

            // Split boxes until we hit MaxColors or every box is a single unique colour. Each iteration picks the box
            // with the widest single-channel spread (ties keep the lowest index → stable), sorts it along that
            // channel, and cuts at the count-weighted median.
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

        // Sort [start,end) of the parallel arrays by `channel`, tie-broken by packed RGB key (unique per colour), so
        // the result is a total order — fully deterministic regardless of the sort's stability.
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
        // The one thing every naive GIF encoder gets wrong (and this one did): the CODE-WIDTH BUMP TIMING. The
        // encoder assigns a new dictionary code the instant it emits a prefix, so its `nextCode` runs exactly ONE
        // AHEAD of the decoder's table counter (the decoder only adds the matching entry when it reads the NEXT
        // code). A decoder widens its read size when ITS counter reaches 1<<width; to stay in lockstep the encoder
        // must therefore widen one code LATER than nextCode==1<<width — i.e. at nextCode==(1<<width)+1 (checked
        // post-increment). This is algebraically the classic GIFCOMPR `free_ent > maxcode` (checked pre-increment)
        // rule. Bumping at nextCode==1<<width instead desyncs the bitstream a few codes in — the exact garbage the
        // coordinator saw (a good top band, then streak noise, then a flood). Proven by SelfTest's round-trip.
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
                    // Widen ONE code after the width fills — (1<<width)+1 post-increment — so the encoder's write
                    // size matches the decoder's read size (which lags the encoder's dictionary by one entry).
                    if (nextCode == (1 << codeWidth) + 1 && codeWidth < 12)
                        codeWidth++;
                }
                else
                {
                    // Dictionary full (codes 0..4095 all assigned) — emit a clear at the current (12-bit) width and
                    // rebuild from scratch, exactly where the decoder's own full table makes it expect one.
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

        // ── self-test: encode → decode round-trip with an INDEPENDENT spec decoder ────
        // The orchestrator runs this via the editor to prove the LZW codec before trusting a real export. It builds
        // deterministic synthetic frames (runs + churn), frames them through the SAME WriteImageData the GIF uses
        // (so sub-block chunking is exercised too), decodes them with GifLzwDecode below — a from-scratch GIF89a
        // decoder, NOT sharing any code with the encoder — and byte-compares. Cases cover small & large code sizes,
        // runs longer than one 255-byte sub-block, the code-size ramp to 12 bits, and dictionary-full resets.
        public static string SelfTest()
        {
            var sb = new System.Text.StringBuilder();
            bool allPass = true;
            // (paletteCount, width, height). The bigger noisy frames force the width ramp to 12 and >4096 dictionary
            // churn (multiple table-full resets); every frame's LZW output far exceeds one 255-byte sub-block.
            var cases = new[] { (4, 64, 64), (130, 100, 100), (255, 128, 128) };
            foreach (var (pc, w, h) in cases)
            {
                var indices = new byte[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        // (x/7) makes horizontal runs (long output runs → multi sub-block); (x*y) forces heavy
                        // dictionary churn (table-full resets). Deterministic, no randomness.
                        indices[y * w + x] = (byte)(((x / 7) + y * 3 + x * y) % pc);

                int minCode = Mathf.Max(2, CeilLog2(pc + 1));   // the same rule Export uses (gctBits over pc+1)

                byte[] framed;
                using (var ms = new MemoryStream()) { WriteImageData(ms, indices, minCode); framed = ms.ToArray(); }

                var decoded = GifLzwDecode(framed, out string err);
                if (decoded == null)
                {
                    allPass = false;
                    sb.AppendLine($"FAIL pc={pc} {w}x{h} minCode={minCode}: decode error: {err}");
                    continue;
                }
                if (decoded.Length != indices.Length)
                {
                    allPass = false;
                    sb.AppendLine($"FAIL pc={pc} {w}x{h}: decoded {decoded.Length} px, expected {indices.Length}");
                    continue;
                }
                int mismatch = -1;
                for (int i = 0; i < indices.Length; i++)
                    if (decoded[i] != indices[i]) { mismatch = i; break; }
                if (mismatch >= 0)
                {
                    allPass = false;
                    sb.AppendLine($"FAIL pc={pc} {w}x{h} minCode={minCode}: at index {mismatch} got {decoded[mismatch]} "
                        + $"expected {indices[mismatch]} (framed {framed.Length} bytes)");
                }
                else
                {
                    sb.AppendLine($"PASS pc={pc} {w}x{h}: {indices.Length} px, minCode={minCode}, "
                        + $"framed {framed.Length} bytes ({((framed.Length - 2) / 255) + 1} sub-block(s))");
                }
            }
            return (allPass ? "PASS " : "FAIL ") + sb.ToString().TrimEnd();
        }

        // A from-scratch GIF89a image-data decoder (independent of the encoder above). Input: the exact bytes
        // WriteImageData produced — an LZW-minimum-code-size byte, then length-prefixed sub-blocks ending in a
        // zero-length block. Output: the decoded index array, or null (with `err` set). Written straight from the
        // GIF89a decoder description: read codes at the current width, maintain the string table, handle clear/end,
        // widen the read size when the table count reaches 1<<width, and handle the KwKwK (code == next) case.
        static byte[] GifLzwDecode(byte[] framed, out string err)
        {
            err = null;
            if (framed == null || framed.Length < 1) { err = "empty image data"; return null; }
            int minCode = framed[0];

            // De-chunk the sub-blocks into one contiguous LZW byte stream.
            var lzw = new List<byte>();
            int p = 1;
            while (p < framed.Length)
            {
                int len = framed[p++];
                if (len == 0) break;                     // block terminator
                if (p + len > framed.Length) { err = "sub-block overruns data"; return null; }
                for (int i = 0; i < len; i++) lzw.Add(framed[p + i]);
                p += len;
            }
            var data = lzw.ToArray();

            int clear = 1 << minCode, end = clear + 1;
            int codeWidth = minCode + 1;

            var table = new List<int[]>();
            void Reset()
            {
                table.Clear();
                for (int i = 0; i < clear; i++) table.Add(new[] { i });
                table.Add(null);   // clear slot
                table.Add(null);   // end slot
            }
            Reset();

            int bytePos = 0, bitPos = 0;
            int Read(int width)
            {
                int r = 0;
                for (int i = 0; i < width; i++)
                {
                    if (bytePos >= data.Length) return -1;
                    int bit = (data[bytePos] >> bitPos) & 1;
                    r |= bit << i;                       // LSB-first, matching the encoder's packing
                    if (++bitPos == 8) { bitPos = 0; bytePos++; }
                }
                return r;
            }

            var output = new List<int>();
            int prev = -1;
            while (true)
            {
                int code = Read(codeWidth);
                if (code < 0) { err = "unexpected end of code stream (no END code)"; return null; }
                if (code == clear) { Reset(); codeWidth = minCode + 1; prev = -1; continue; }
                if (code == end) break;

                int[] entry;
                if (prev == -1)
                {
                    // First code after a clear: must be a literal already in the base table.
                    if (code >= table.Count || table[code] == null) { err = $"first code {code} not a literal"; return null; }
                    output.AddRange(table[code]);
                    prev = code;
                    continue;
                }

                if (code < table.Count && table[code] != null)
                    entry = table[code];
                else if (code == table.Count)
                {
                    // KwKwK: the code isn't in the table yet — it's prev's string plus prev's first symbol.
                    var pe = table[prev];
                    entry = new int[pe.Length + 1];
                    System.Array.Copy(pe, entry, pe.Length);
                    entry[pe.Length] = pe[0];
                }
                else { err = $"code {code} out of range (table {table.Count})"; return null; }

                output.AddRange(entry);

                // Add prev + entry[0] as the next table entry (until the table is full), then widen when the table
                // count reaches 1<<width — the decoder-side counterpart of the encoder's (1<<width)+1 bump.
                if (table.Count < 4096)
                {
                    var pe = table[prev];
                    var ne = new int[pe.Length + 1];
                    System.Array.Copy(pe, ne, pe.Length);
                    ne[pe.Length] = entry[0];
                    table.Add(ne);
                    if (table.Count == (1 << codeWidth) && codeWidth < 12) codeWidth++;
                }
                prev = code;
            }

            var outBytes = new byte[output.Count];
            for (int i = 0; i < output.Count; i++) outBytes[i] = (byte)output[i];
            return outBytes;
        }
    }
}
