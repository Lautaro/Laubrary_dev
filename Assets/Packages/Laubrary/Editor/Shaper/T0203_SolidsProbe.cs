// TEMP PROBE T-0203 — PM deletes after running
//
// Renders every Shaper Solid at its defaults beside Pyre's own six solids, taken through Pyre's window path
// (PyreRenderer.RenderFrame, the call PyreWindow.RenderFrameTexture makes), and sweeps all fourteen Solids
// dials to measure whether each one actually changes the picture. Writes contact sheets and returns a text
// report. No menu item, no window, nothing authored, nothing saved back to any user asset.

using System;
using System.IO;
using System.Text;
using UnityEngine;
using Laubrary.Shaper;
using Laubrary.Pyre;
using Laubrary.Pyre.Editor;

namespace Laubrary.Shaper.Editor
{
    public static class T0203_SolidsProbe
    {
        const int Tile = 96;
        const string OutDir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0203";

        static readonly ShaperSolidForm[] Forms =
        {
            ShaperSolidForm.Box, ShaperSolidForm.Pyramid, ShaperSolidForm.Can,
            ShaperSolidForm.Orb, ShaperSolidForm.Gem, ShaperSolidForm.Ring,
        };

        static readonly ShapeForm[] PyreForms =
        {
            ShapeForm.Box, ShapeForm.Pyramid, ShapeForm.Can,
            ShapeForm.Orb, ShapeForm.Gem, ShapeForm.Ring,
        };

        public static string RunAll()
        {
            var sb = new StringBuilder();
            try
            {
                Directory.CreateDirectory(OutDir);
                sb.AppendLine("T-0203 Solids probe");
                sb.AppendLine("===================");
                sb.AppendLine();
                sb.Append(ContactSheet());
                sb.AppendLine();
                sb.Append(DialSweep());
                sb.AppendLine();
                sb.Append(BorderAndFill());
            }
            catch (Exception e)
            {
                sb.AppendLine("PROBE THREW: " + e);
            }
            return sb.ToString();
        }

        // ── sheet 1: six Shaper solids over six Pyre solids ───────────────────────────────────────────────

        static string ContactSheet()
        {
            var sb = new StringBuilder();
            sb.AppendLine("-- contact sheet: Shaper (row 0) vs Pyre (row 1), Box Pyramid Can Orb Gem Ring --");

            var sheet = NewSheet(6, 2);

            for (int c = 0; c < 6; c++)
            {
                // Pyre first, so its converted steady material colour can be reused as Shaper's albedo — the
                // comparison is then about SHADING and not about two different base colours.
                Color mat;
                Color32[] pyre = RenderPyre(PyreForms[c], out mat);
                Color32[] shaper = RenderShaper(Forms[c], mat, null, out int shaperCount);

                Blit(sheet, 6, shaper, c, 0);
                Blit(sheet, 6, pyre, c, 1);

                sb.AppendLine($"  {Forms[c],-8} shaper: {Describe(shaper)}");
                sb.AppendLine($"  {"",-8} pyre  : {Describe(pyre)}");
                _ = shaperCount;
            }

            Save(sheet, 6, 2, "solids-vs-pyre.png");
            sb.AppendLine("  wrote " + Path.Combine(OutDir, "solids-vs-pyre.png"));
            return sb.ToString();
        }

        // ── sheet 2 + numbers: does every dial change the picture? ────────────────────────────────────────

        struct Sweep { public ShaperSolidDial dial; public ShaperSolidForm form; public float moved; public string label; }

        static string DialSweep()
        {
            var sb = new StringBuilder();
            sb.AppendLine("-- dial sweep: changed pixels, default vs moved (the form each dial is LIVE on) --");

            var sweeps = new[]
            {
                new Sweep { dial = ShaperSolidDial.Size,        form = ShaperSolidForm.Box,  moved = 34f,   label = "Size 20 -> 34" },
                new Sweep { dial = ShaperSolidDial.Centre,      form = ShaperSolidForm.Box,  moved = 12f,   label = "Centre X 0 -> 12" },
                new Sweep { dial = ShaperSolidDial.Aspect,      form = ShaperSolidForm.Box,  moved = 0.4f,  label = "Aspect 1 -> 0.4" },
                new Sweep { dial = ShaperSolidDial.Depth,       form = ShaperSolidForm.Box,  moved = 0.4f,  label = "Depth 1 -> 0.4" },
                new Sweep { dial = ShaperSolidDial.GemSides,    form = ShaperSolidForm.Gem,  moved = 8f,    label = "Gem sides 6 -> 8" },
                new Sweep { dial = ShaperSolidDial.GemCrown,    form = ShaperSolidForm.Gem,  moved = 1.4f,  label = "Gem crown 0.55 -> 1.4" },
                new Sweep { dial = ShaperSolidDial.GemPavilion, form = ShaperSolidForm.Gem,  moved = 1.6f,  label = "Gem pavilion 0.85 -> 1.6" },
                new Sweep { dial = ShaperSolidDial.RingInner,   form = ShaperSolidForm.Ring, moved = 0.85f, label = "Ring inner 0.55 -> 0.85" },
                // Moved values must differ from Pose()'s: the first run set Yaw to 35 on a pose that was
                // already yaw 35 and reported the dial INERT, which was the probe lying, not the engine.
                new Sweep { dial = ShaperSolidDial.Yaw,         form = ShaperSolidForm.Box,  moved = 75f,   label = "Yaw 35 -> 75" },
                new Sweep { dial = ShaperSolidDial.Tilt,        form = ShaperSolidForm.Box,  moved = -40f,  label = "Tilt 28 -> -40" },
                new Sweep { dial = ShaperSolidDial.Roll,        form = ShaperSolidForm.Box,  moved = 22f,   label = "Roll 12 -> 22" },
                new Sweep { dial = ShaperSolidDial.LineWidth,   form = ShaperSolidForm.Gem,  moved = 4f,    label = "Line width 1.1 -> 4" },
                new Sweep { dial = ShaperSolidDial.EdgeGlow,    form = ShaperSolidForm.Gem,  moved = 1f,    label = "Edge glow 0 -> 1" },
                new Sweep { dial = ShaperSolidDial.InnerGlow,   form = ShaperSolidForm.Gem,  moved = 1f,    label = "Inner glow 0 -> 1" },
            };

            var sheet = NewSheet(sweeps.Length, 2);
            var mat = new Color(1f, 0.62f, 0.24f, 1f);

            for (int i = 0; i < sweeps.Length; i++)
            {
                var s = sweeps[i];

                // A rotated base for every sweep: an axis-aligned Box hides yaw/aspect/depth behind its own
                // symmetry, which is exactly how the five silent inert dials went unnoticed before T-0155.
                Color32[] a = RenderShaper(s.form, mat, def => Pose(def), out _);
                Color32[] b = RenderShaper(s.form, mat, def => { Pose(def); Move(def, s.dial, s.moved); }, out _);

                Blit(sheet, sweeps.Length, a, i, 0);
                Blit(sheet, sweeps.Length, b, i, 1);

                int diff = Differing(a, b);
                sb.AppendLine($"  {s.label,-34} on {s.form,-8} {diff,6} px changed {(diff == 0 ? "  <-- INERT" : "")}");
            }

            Save(sheet, sweeps.Length, 2, "solids-dial-sweep.png");
            sb.AppendLine("  wrote " + Path.Combine(OutDir, "solids-dial-sweep.png"));
            return sb.ToString();
        }

        static void Pose(ShaperSolidDef d)
        {
            d.yaw = new ZUIValue(35f); d.tilt = new ZUIValue(28f); d.roll = new ZUIValue(12f);
        }

        static void Move(ShaperSolidDef d, ShaperSolidDial dial, float v)
        {
            switch (dial)
            {
                case ShaperSolidDial.Size: d.size = new ZUIValue(v); break;
                case ShaperSolidDial.Centre: d.centreX = new ZUIValue(v); break;
                case ShaperSolidDial.Aspect: d.aspect = new ZUIValue(v); break;
                case ShaperSolidDial.Depth: d.depth = new ZUIValue(v); break;
                case ShaperSolidDial.GemSides: d.gemSides = new ZUIValue(v); break;
                case ShaperSolidDial.GemCrown: d.gemCrown = new ZUIValue(v); break;
                case ShaperSolidDial.GemPavilion: d.gemPavilion = new ZUIValue(v); break;
                case ShaperSolidDial.RingInner: d.ringInner = new ZUIValue(v); break;
                case ShaperSolidDial.Yaw: d.yaw = new ZUIValue(v); break;
                case ShaperSolidDial.Tilt: d.tilt = new ZUIValue(v); break;
                case ShaperSolidDial.Roll: d.roll = new ZUIValue(v); break;
                case ShaperSolidDial.LineWidth: d.lineWidth = new ZUIValue(v); break;
                case ShaperSolidDial.EdgeGlow: d.edgeGlow = new ZUIValue(v); break;
                case ShaperSolidDial.InnerGlow: d.innerGlow = new ZUIValue(v); break;
            }
        }

        // ── sheet 3: what the ORDINARY stages do to a Solid (task item 4) ─────────────────────────────────

        static string BorderAndFill()
        {
            var sb = new StringBuilder();
            sb.AppendLine("-- fill / border / rig on a Solid: does each one reach the picture? --");

            var mat = new Color(1f, 0.62f, 0.24f, 1f);
            var sheet = NewSheet(4, 1);

            Color32[] plain = RenderShaper(ShaperSolidForm.Gem, mat, Pose, out _);
            Color32[] tinted = RenderShaper(ShaperSolidForm.Gem, new Color(0.30f, 0.55f, 1f, 1f), Pose, out _);
            Color32[] bordered = RenderShaper(ShaperSolidForm.Gem, mat, Pose, out _, border: true);
            Color32[] rigLit = RenderShaper(ShaperSolidForm.Gem, mat, Pose, out _, addRigLight: true);

            Blit(sheet, 4, plain, 0, 0);
            Blit(sheet, 4, tinted, 1, 0);
            Blit(sheet, 4, bordered, 2, 0);
            Blit(sheet, 4, rigLit, 3, 0);

            // Reported against the COVERED count, not as a bare number: "336 changed" says nothing until you
            // know whether 336 is most of the solid or a tenth of it. A fill swap should reach nearly every
            // covered pixel EXCEPT the facet lines (whose albedo is substituted) and the glow-only fringe.
            int cov = Covered(plain);
            sb.AppendLine($"  covered pixels      : {cov,6}");
            sb.AppendLine($"  fill tints albedo   : {Differing(plain, tinted),6} px changed  ({Pct(Differing(plain, tinted), cov)})");
            sb.AppendLine($"  border draws        : {Differing(plain, bordered),6} px changed  ({Pct(Differing(plain, bordered), cov)})");
            sb.AppendLine($"  covered with border : {Covered(bordered),6} (a border should ADD covered pixels)");
            sb.AppendLine($"  one rig light wins  : {Differing(plain, rigLit),6} px changed  ({Pct(Differing(plain, rigLit), cov)})");
            sb.AppendLine($"  plain  : {Describe(plain)}");
            sb.AppendLine($"  rigLit : {Describe(rigLit)}");

            Save(sheet, 4, 1, "solids-fill-border-rig.png");
            sb.AppendLine("  wrote " + Path.Combine(OutDir, "solids-fill-border-rig.png"));
            return sb.ToString();
        }

        // ── renderers ─────────────────────────────────────────────────────────────────────────────────────

        static Color32[] RenderShaper(ShaperSolidForm form, Color material, Action<ShaperSolidDef> tweak,
                                      out int sampleCount, bool border = false, bool addRigLight = false)
        {
            var doc = ScriptableObject.CreateInstance<ShaperDocument>();
            doc.name = "T0203Probe";
            doc.canvasWidth = Tile; doc.canvasHeight = Tile; doc.pixelSize = 1f;
            doc.frameCount = 1; doc.phase01 = 0f; doc.seed = 7u;
            doc.background = new Color(0f, 0f, 0f, 0f);

            if (addRigLight)
                doc.lightRig.lights.Add(new ShaperLight { name = "Key", enabled = true });

            var node = new ShaperNode
            {
                name = form.ToString(),
                kind = ShaperNodeKind.Solid,
                solid = new ShaperSolidDef { form = form },
                fill = new ShaperFillDef { kind = ShaperFillKind.Solid, solidColor = material },
            };
            tweak?.Invoke(node.solid);

            if (border)
            {
                node.border = new ShaperBorderDef { enabled = true };
                node.border.fill = new ShaperFillDef { kind = ShaperFillKind.Solid, solidColor = Color.white };
            }

            doc.layers.Add(new ShaperLayer { name = "L", enabled = true, root = node });

            // No effect applier: the probe document authors no SpriteFX, so the stage is a no-op either way
            // and passing null keeps this file off the PyreShaper bridge T-0202 is moving.
            var px = ShaperDocumentRenderer.RenderFrame(doc, 0);
            sampleCount = px != null ? px.Length : 0;
            UnityEngine.Object.DestroyImmediate(doc);
            return px;
        }

        static Color32[] RenderPyre(ShapeForm form, out Color material)
        {
            // Fully qualified: inside this namespace, with `using Laubrary.Pyre;` in scope, the bare name
            // `Pyre` is ambiguous between the namespace and the spec class.
            var spec = ScriptableObject.CreateInstance<Laubrary.Pyre.Pyre>();
            spec.name = "T0203PyreProbe";
            spec.canvasSize = Tile;
            spec.frameCount = 16;
            spec.background = new Color(0f, 0f, 0f, 0f);

            var layer = spec.layers[0];
            layer.shapeForm = form;
            layer.swarmEnabled = false;                 // Pyre.cs:652 — off means exactly one centred particle

            // SIZE PINNED TO SHAPER'S DEFAULT. Pyre's `size` is an envelope over the particle's own life
            // (Pyre.cs:326, DefaultSize) and at mid-life it resolves well above 20, so the first sheet compared
            // a radius-20 Shaper solid against a visibly larger Pyre one — every covered-pixel count differed
            // by 1.5x and it read as a lighting difference when it was a size difference. Pinning it makes the
            // two rows differ ONLY in what this task is about. Both glow radii scale with R
            // (max(2.5, 0.24R) / max(3, 0.30R)), so this also puts the two glows at the same proportion of the
            // solid rather than leaving Shaper's looking heavier.
            layer.size = new ZUIValue(20f);             // ShaperSolidDef.size's default
            layer.alpha = new ZUIValue(1f);             // and no life fade, so alpha is not a second variable

            // The window's own conversion, called rather than copied: a fresh 3D solid gets a STEADY Solid
            // material instead of the OverLife fire gradient (PyreShapeCards.cs:916), which is what makes a
            // Pyre solid read as light-driven. Without it the two rows would differ by the material, not the
            // shading, and the comparison would say nothing.
            PyreShapeCards.SteadyDefaultFillForSolid(layer);
            material = layer.shapeFill.color;

            var px = PyreRenderer.RenderFrame(spec, spec.frameCount / 2);
            UnityEngine.Object.DestroyImmediate(spec);
            return px;
        }

        // ── sheet plumbing ────────────────────────────────────────────────────────────────────────────────

        static Color32[] NewSheet(int cols, int rows)
        {
            var sheet = new Color32[cols * Tile * rows * Tile];
            var bg = new Color32(64, 64, 72, 255);
            for (int i = 0; i < sheet.Length; i++) sheet[i] = bg;
            return sheet;
        }

        /// <summary>Straight-alpha source over the sheet's opaque backdrop, so an additive glow is visible.</summary>
        static void Blit(Color32[] sheet, int cols, Color32[] src, int col, int row)
        {
            if (src == null) return;
            int W = cols * Tile;
            for (int y = 0; y < Tile; y++)
            {
                for (int x = 0; x < Tile; x++)
                {
                    int si = y * Tile + x;
                    if (si >= src.Length) continue;
                    Color32 s = src[si];
                    int di = (row * Tile + y) * W + col * Tile + x;
                    Color32 d = sheet[di];
                    float a = s.a / 255f;
                    sheet[di] = new Color32(
                        (byte)Mathf.RoundToInt(s.r * a + d.r * (1f - a)),
                        (byte)Mathf.RoundToInt(s.g * a + d.g * (1f - a)),
                        (byte)Mathf.RoundToInt(s.b * a + d.b * (1f - a)),
                        255);
                }
            }
        }

        /// <summary>
        /// Nearest-neighbour upscaled on the way out. A 96px tile is too small to judge a bevel or a facet
        /// seam by eye at 1:1 — the first look at this sheet read a mid-brown front face as near-black, which
        /// the per-face numbers then contradicted. A contact sheet nobody can actually see is not evidence.
        /// </summary>
        const int Zoom = 3;

        static void Save(Color32[] sheet, int cols, int rows, string file)
        {
            // VERTICALLY FLIPPED on the way out, and this is not cosmetic. A Texture2D's row 0 is the BOTTOM
            // row, so the first save put row 0 (Shaper) along the bottom of the PNG while the report above it
            // read top-down — and the first look at that sheet drew exactly the wrong conclusion from it,
            // reading Pyre's contrasty solids as Shaper's. A sheet whose rows are not where its caption says
            // is worse than no sheet.
            int w = cols * Tile, h = rows * Tile;
            var big = new Color32[w * Zoom * h * Zoom];
            for (int y = 0; y < h * Zoom; y++)
                for (int x = 0; x < w * Zoom; x++)
                    big[y * w * Zoom + x] = sheet[(h - 1 - y / Zoom) * w + (x / Zoom)];

            var tex = new Texture2D(w * Zoom, h * Zoom, TextureFormat.RGBA32, false);
            tex.SetPixels32(big);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, file), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        // ── measurements ──────────────────────────────────────────────────────────────────────────────────

        static int Covered(Color32[] px)
        {
            if (px == null) return 0;
            int n = 0;
            for (int i = 0; i < px.Length; i++) if (px[i].a >= 8) n++;
            return n;
        }

        static string Pct(int part, int whole) => whole <= 0 ? "n/a" : (100f * part / whole).ToString("0") + "% of covered";

        static int Differing(Color32[] a, Color32[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return -1;
            int n = 0;
            for (int i = 0; i < a.Length; i++)
                if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) n++;
            return n;
        }

        /// <summary>
        /// The one number that decides "flat blob or lit solid": how many DISTINCT luminances the covered
        /// pixels carry. A flat one-colour silhouette lands at 1 or 2 (the shape plus its own edge line);
        /// anything shaded lands in the dozens.
        /// </summary>
        static string Describe(Color32[] px)
        {
            if (px == null) return "null";
            var seen = new System.Collections.Generic.HashSet<int>();
            int covered = 0, minL = 255, maxL = 0;
            for (int i = 0; i < px.Length; i++)
            {
                if (px[i].a < 8) continue;
                covered++;
                int l = (px[i].r * 30 + px[i].g * 59 + px[i].b * 11) / 100;
                seen.Add(l);
                if (l < minL) minL = l;
                if (l > maxL) maxL = l;
            }
            if (covered == 0) return "EMPTY (nothing covered)";
            return $"covered {covered,5}  distinct luminances {seen.Count,4}  range {minL}..{maxL}";
        }
    }
}
