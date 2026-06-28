using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>One layer of a parsed/authored Aseprite document.</summary>
    public class AseLayer { public string name; public bool visible = true; }

    /// <summary>An Aseprite animation tag = a named, contiguous frame range (→ maps to a zone).</summary>
    public class AseTag { public string name; public int from, to; }

    /// <summary>
    /// A minimal in-memory Aseprite document: canvas size, ordered layers (bottom→top), tags, and per
    /// (frame, layer) full-canvas RGBA pixels in BOTTOM-UP row order (row 0 = bottom, matching Unity + the
    /// existing meta masks). Null where a layer has no cel on a frame.
    /// </summary>
    public class AseDoc
    {
        public int width, height, frameCount;
        public List<AseLayer> layers = new List<AseLayer>();
        public List<AseTag> tags = new List<AseTag>();
        public Color32[][][] pixels;   // [frame][layer] full-canvas, bottom-up; null = empty cel
    }

    /// <summary>
    /// Reads and writes the `.aseprite`/`.ase` binary format (32bpp RGBA) directly — so Zoetrope can both
    /// produce editable source files AND parse user-edited ones without depending on importer internals (which
    /// tight-pack sprites and would break layer compositing). Writer uses RAW cels; reader handles RAW (0) and
    /// zlib-COMPRESSED (2) cels so it reads files Aseprite itself saved.
    /// </summary>
    public static class AsepriteIO
    {
        // ── Write ────────────────────────────────────────────────────────────
        public static byte[] Write(AseDoc d, int durMs = 100)
        {
            int W = d.width, H = d.height, F = d.frameCount, L = d.layers.Count;
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);

            long fileSizePos = ms.Position;
            w.Write((uint)0);                  // file size (backfill)
            w.Write((ushort)0xA5E0);
            w.Write((ushort)F);
            w.Write((ushort)W); w.Write((ushort)H);
            w.Write((ushort)32);               // RGBA
            w.Write((uint)1);                  // layer opacity valid
            w.Write((ushort)durMs);
            w.Write((uint)0); w.Write((uint)0);
            w.Write((byte)0); w.Write(new byte[3]);
            w.Write((ushort)0);
            w.Write((byte)1); w.Write((byte)1);
            w.Write((short)0); w.Write((short)0);
            w.Write((ushort)0); w.Write((ushort)0);
            w.Write(new byte[84]);             // → 128-byte header

            for (int f = 0; f < F; f++)
            {
                int cels = 0; for (int l = 0; l < L; l++) if (d.pixels[f][l] != null) cels++;
                int chunkCount = cels + (f == 0 ? L + (d.tags.Count > 0 ? 1 : 0) : 0);

                long frameStart = ms.Position;
                w.Write((uint)0);              // frame bytes (backfill)
                w.Write((ushort)0xF1FA);
                // Old 16-bit chunk count: write the real count (Aseprite reads THIS field; 0xFFFF is only a
                // sentinel meaning "use the 32-bit field", which Aseprite 1.3 treats as a literal 65535).
                w.Write((ushort)(chunkCount <= 0xFFFE ? chunkCount : 0xFFFF));
                w.Write((ushort)durMs);
                w.Write(new byte[2]);
                w.Write((uint)chunkCount);     // new 32-bit chunk count

                if (f == 0)
                {
                    foreach (var lay in d.layers)
                        Chunk(w, ms, 0x2004, () =>
                        {
                            w.Write((ushort)(lay.visible ? 3 : 2));   // visible+editable / editable
                            w.Write((ushort)0); w.Write((ushort)0);
                            w.Write((ushort)0); w.Write((ushort)0);
                            w.Write((ushort)0); w.Write((byte)255); w.Write(new byte[3]);
                            Str(w, lay.name);
                        });
                    if (d.tags.Count > 0)
                        Chunk(w, ms, 0x2018, () =>
                        {
                            w.Write((ushort)d.tags.Count); w.Write(new byte[8]);
                            foreach (var t in d.tags)
                            {
                                w.Write((ushort)t.from); w.Write((ushort)t.to);
                                w.Write((byte)0);            // forward
                                w.Write(new byte[8]);        // repeat(2)+reserved(6) or old reserved(8)
                                w.Write(new byte[3]);        // deprecated color
                                w.Write((byte)0);            // extra
                                Str(w, t.name);
                            }
                        });
                }

                for (int l = 0; l < L; l++)
                {
                    var px = d.pixels[f][l];
                    if (px == null) continue;
                    int li = l;
                    Chunk(w, ms, 0x2005, () =>
                    {
                        w.Write((ushort)li);
                        w.Write((short)0); w.Write((short)0);
                        w.Write((byte)255);
                        w.Write((ushort)0);                  // raw cel
                        w.Write(new byte[7]);
                        w.Write((ushort)W); w.Write((ushort)H);
                        // bottom-up Color32[] → top-down RGBA bytes
                        var buf = new byte[W * H * 4];
                        for (int y = 0; y < H; y++)
                            for (int x = 0; x < W; x++)
                            {
                                var c = px[(H - 1 - y) * W + x]; int i = (y * W + x) * 4;
                                buf[i] = c.r; buf[i + 1] = c.g; buf[i + 2] = c.b; buf[i + 3] = c.a;
                            }
                        w.Write(buf);
                    });
                }

                long frameEnd = ms.Position;
                ms.Position = frameStart; w.Write((uint)(frameEnd - frameStart)); ms.Position = frameEnd;
            }

            long end = ms.Position;
            ms.Position = fileSizePos; w.Write((uint)end); ms.Position = end;
            return ms.ToArray();
        }

        static void Chunk(BinaryWriter w, MemoryStream ms, ushort type, Action body)
        {
            long s = ms.Position; w.Write((uint)0); w.Write((ushort)type);
            body();
            long e = ms.Position; ms.Position = s; w.Write((uint)(e - s)); ms.Position = e;
        }
        static void Str(BinaryWriter w, string s) { var b = Encoding.UTF8.GetBytes(s ?? ""); w.Write((ushort)b.Length); w.Write(b); }

        // ── Read ─────────────────────────────────────────────────────────────
        public static AseDoc Read(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            r.ReadUInt32();                                  // file size
            if (r.ReadUInt16() != 0xA5E0) throw new Exception("Not an Aseprite file.");
            int F = r.ReadUInt16(), W = r.ReadUInt16(), H = r.ReadUInt16();
            int depth = r.ReadUInt16();
            if (depth != 32) throw new Exception($"Only 32bpp RGBA supported (got {depth}bpp). Set Color Mode = RGB in Aseprite.");
            r.ReadUInt32(); r.ReadUInt16(); r.ReadUInt32(); r.ReadUInt32();
            r.ReadByte(); r.ReadBytes(3); r.ReadUInt16(); r.ReadByte(); r.ReadByte();
            r.ReadInt16(); r.ReadInt16(); r.ReadUInt16(); r.ReadUInt16(); r.ReadBytes(84);

            var doc = new AseDoc { width = W, height = H, frameCount = F };
            doc.pixels = new Color32[F][][];

            for (int f = 0; f < F; f++)
            {
                long frameStart = r.BaseStream.Position;
                uint frameBytes = r.ReadUInt32();
                if (r.ReadUInt16() != 0xF1FA) throw new Exception("Bad frame magic.");
                int oldCount = r.ReadUInt16(); r.ReadUInt16(); r.ReadBytes(2);
                uint newCount = r.ReadUInt32();
                long chunkCount = newCount != 0 ? newCount : oldCount;

                if (doc.pixels[f] == null) doc.pixels[f] = new Color32[Mathf.Max(1, doc.layers.Count)][];

                for (long c = 0; c < chunkCount; c++)
                {
                    long cStart = r.BaseStream.Position;
                    uint cSize = r.ReadUInt32();
                    ushort type = r.ReadUInt16();
                    if (type == 0x2004)        // layer
                    {
                        ushort flags = r.ReadUInt16(); r.ReadUInt16(); r.ReadUInt16();
                        r.ReadUInt16(); r.ReadUInt16(); r.ReadUInt16(); r.ReadByte(); r.ReadBytes(3);
                        string name = ReadStr(r);
                        doc.layers.Add(new AseLayer { name = name, visible = (flags & 1) != 0 });
                        Grow(doc, f);
                    }
                    else if (type == 0x2018)   // tags
                    {
                        int nt = r.ReadUInt16(); r.ReadBytes(8);
                        for (int t = 0; t < nt; t++)
                        {
                            int from = r.ReadUInt16(), to = r.ReadUInt16();
                            r.ReadByte(); r.ReadBytes(8); r.ReadBytes(3); r.ReadByte();
                            string tn = ReadStr(r);
                            doc.tags.Add(new AseTag { name = tn, from = from, to = to });
                        }
                    }
                    else if (type == 0x2005)   // cel
                    {
                        Grow(doc, f);
                        int layer = r.ReadUInt16();
                        short x = r.ReadInt16(), y = r.ReadInt16();
                        r.ReadByte();                 // opacity
                        ushort celType = r.ReadUInt16();
                        r.ReadBytes(7);
                        if (celType == 0 || celType == 2)
                        {
                            int cw = r.ReadUInt16(), ch = r.ReadUInt16();
                            int dataLen = (int)(cSize - (r.BaseStream.Position - cStart));
                            byte[] raw = r.ReadBytes(dataLen);
                            byte[] rgba = celType == 0 ? raw : Inflate(raw);
                            PlaceCel(doc, f, layer, x, y, cw, ch, rgba);
                        }
                        else { r.BaseStream.Position = cStart + cSize; }   // linked/tilemap: skip
                    }
                    r.BaseStream.Position = cStart + cSize;
                }
                r.BaseStream.Position = frameStart + frameBytes;
            }
            return doc;
        }

        static void Grow(AseDoc d, int f)
        {
            int n = Mathf.Max(1, d.layers.Count);
            if (d.pixels[f] == null) { d.pixels[f] = new Color32[n][]; return; }
            if (d.pixels[f].Length < n) { var bigger = new Color32[n][]; Array.Copy(d.pixels[f], bigger, d.pixels[f].Length); d.pixels[f] = bigger; }
        }

        static void PlaceCel(AseDoc d, int f, int layer, int cx, int cy, int cw, int ch, byte[] rgba)
        {
            int W = d.width, H = d.height;
            var canvas = new Color32[W * H];                 // transparent
            for (int yy = 0; yy < ch; yy++)
                for (int xx = 0; xx < cw; xx++)
                {
                    int px = cx + xx, py = cy + yy;          // aseprite top-down
                    if (px < 0 || px >= W || py < 0 || py >= H) continue;
                    int si = (yy * cw + xx) * 4;
                    int di = ((H - 1 - py) * W + px);          // → bottom-up
                    canvas[di] = new Color32(rgba[si], rgba[si + 1], rgba[si + 2], rgba[si + 3]);
                }
            d.pixels[f][layer] = canvas;
        }

        static byte[] Inflate(byte[] data)
        {
            using var ms = new MemoryStream(data, 2, data.Length - 2);   // skip 2-byte zlib header
            using var ds = new DeflateStream(ms, CompressionMode.Decompress);
            using var outp = new MemoryStream();
            ds.CopyTo(outp);
            return outp.ToArray();
        }

        static string ReadStr(BinaryReader r) { int n = r.ReadUInt16(); return Encoding.UTF8.GetString(r.ReadBytes(n)); }
    }
}
