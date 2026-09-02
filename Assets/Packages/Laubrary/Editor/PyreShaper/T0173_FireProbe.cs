// TEMP PROBE T-0173 — PM deletes after running
//
// Answers the three questions the task asks about the two stateful-sim composite sources, without opening the
// window: does a Fire document and a Fireball document actually render through the Shaper compiler, is the
// result the same every time, and does the picture genuinely change with phase (a sim that ignored the clock
// would still render, still be deterministic, and still be wrong).
//
// The determinism check is deliberately harder than "render twice". Both sources cache their replay state on
// the instance, so rendering the same document twice in a row could agree merely because the second call took
// the "same frame, unchanged content" branch and re-rendered the grid it already had. So a SECOND document is
// built with FRESH source instances and visited in the OPPOSITE phase order — that forces cold replays where
// the first document took warm steps, and vice versa. Agreement between those two is the claim worth making:
// the picture depends on the phase asked for, not on the route taken to it.
//
// No [MenuItem] — invoked once via the Unity CLI's `command eval_file`, per PROGRAMME_RULES.md's code-only
// posture. Nothing here is written into the project: the documents are in-memory ScriptableObjects, destroyed
// on the way out, and the only file produced is the contact sheet under the task's workspace folder.
using System;
using System.IO;
using System.Text;
using Laubrary.PyreShaper;
using Laubrary.Shaper;
using UnityEngine;

namespace Laubrary.PyreShaper.Editor
{
    public static class T0173_FireProbe
    {
        const int Canvas = 64;
        const int FireFrames = 16;
        const int FireballFrames = 8;
        const string OutDir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0173";

        public static string RunAll()
        {
            var sb = new StringBuilder();
            sb.AppendLine("T-0173 Fire + Fireball composite-source probe");
            sb.AppendLine();

            ShaperDocument fireA = null, fireB = null, ballA = null, ballB = null;
            try
            {
                fireA = BuildFire(); fireB = BuildFire();
                ballA = BuildFireball(); ballB = BuildFireball();

                // ── the three phases the task names ──────────────────────────────────────────────────────
                float[] phases = { 0f, 0.5f, 1f };

                var fireFwd = RenderAt(fireA, phases, forward: true, out string e1);
                var fireRev = RenderAt(fireB, phases, forward: false, out string e2);
                var ballFwd = RenderAt(ballA, phases, forward: true, out string e3);
                var ballRev = RenderAt(ballB, phases, forward: false, out string e4);

                string err = e1 ?? e2 ?? e3 ?? e4;
                if (err != null)
                {
                    sb.AppendLine("FAIL: a render threw —");
                    sb.AppendLine(err);
                    return sb.ToString();
                }

                Report(sb, "Fire", fireFwd, fireRev, phases);
                sb.AppendLine();
                Report(sb, "Fireball", ballFwd, ballRev, phases);
                sb.AppendLine();

                // ── the contact sheet ────────────────────────────────────────────────────────────────────
                // Every frame of both documents, not just the three checked phases: the point of a contact
                // sheet is that a human can see whether the flame MOVES, and three stills cannot show that.
                // Rows 1-2 are Fire's sixteen frames, row 3 is Fireball's eight.
                var rows = new Color32[3][];
                rows[0] = Strip(fireA, 0, 8, out string s1);
                rows[1] = Strip(fireA, 8, 8, out string s2);
                rows[2] = Strip(ballA, 0, 8, out string s3);
                string sErr = s1 ?? s2 ?? s3;
                if (sErr != null) sb.AppendLine("WARNING: contact-sheet render threw — " + sErr);
                else sb.AppendLine(WriteSheet(rows));
            }
            catch (Exception e)
            {
                sb.AppendLine("FAIL: probe threw —");
                sb.AppendLine(e.ToString());
            }
            finally
            {
                Destroy(fireA); Destroy(fireB); Destroy(ballA); Destroy(ballB);
            }

            return sb.ToString();
        }

        // ── documents ────────────────────────────────────────────────────────────────────────────────────

        static ShaperDocument BuildFire()
        {
            var def = PyreCompositeCatalog.BuildSource(new FireCompositeSource { simFrames = FireFrames },
                PyreCompositeCatalog.Fire,
                halfExtentX: Canvas * 0.5f, halfExtentY: Canvas * 0.5f,
                bakeWidth: Canvas, bakeHeight: Canvas);
            return Wrap(def, "Fire", FireFrames);
        }

        static ShaperDocument BuildFireball()
        {
            var def = PyreCompositeCatalog.BuildSource(
                new FireballCompositeSource { simFrames = FireballFrames, arms = 5, sharpness = new ZUIValue(0.45f) },
                PyreCompositeCatalog.Fireball,
                halfExtentX: Canvas * 0.5f, halfExtentY: Canvas * 0.5f,
                bakeWidth: Canvas, bakeHeight: Canvas);
            return Wrap(def, "Fireball", FireballFrames);
        }

        static ShaperDocument Wrap(ShaperCompositeDef def, string name, int frames)
        {
            var doc = ScriptableObject.CreateInstance<ShaperDocument>();
            doc.canvasWidth = Canvas;
            doc.canvasHeight = Canvas;
            doc.frameCount = frames;
            doc.frameRate = 12f;
            doc.seed = 1234u;
            doc.layers.Add(new ShaperLayer { name = name, root = ShaperNode.Composite(def, name) });
            return doc;
        }

        // ── rendering ────────────────────────────────────────────────────────────────────────────────────

        /// Render the given phases, visiting them forward or backward. The ORDER is the whole point: it decides
        /// which of the replay gate's three branches each render takes.
        static Color32[][] RenderAt(ShaperDocument doc, float[] phases, bool forward, out string error)
        {
            error = null;
            var outFrames = new Color32[phases.Length][];
            try
            {
                for (int k = 0; k < phases.Length; k++)
                {
                    int i = forward ? k : phases.Length - 1 - k;
                    outFrames[i] = ShaperDocumentRenderer.RenderPhase(doc, phases[i]);
                }
            }
            catch (Exception e) { error = e.ToString(); }
            return outFrames;
        }

        static Color32[] Strip(ShaperDocument doc, int firstFrame, int count, out string error)
        {
            error = null;
            var strip = new Color32[Canvas * count * Canvas];
            try
            {
                for (int c = 0; c < count; c++)
                {
                    int f = firstFrame + c;
                    var px = f < doc.frameCount ? ShaperDocumentRenderer.RenderFrame(doc, f) : null;
                    if (px == null) continue;
                    for (int y = 0; y < Canvas; y++)
                        for (int x = 0; x < Canvas; x++)
                            strip[y * (Canvas * count) + c * Canvas + x] = px[y * Canvas + x];
                }
            }
            catch (Exception e) { error = e.ToString(); }
            return strip;
        }

        // ── reporting ────────────────────────────────────────────────────────────────────────────────────

        static void Report(StringBuilder sb, string label, Color32[][] fwd, Color32[][] rev, float[] phases)
        {
            sb.AppendLine(label + " — canvas " + Canvas + "x" + Canvas);

            for (int i = 0; i < phases.Length; i++)
            {
                int lit = Lit(fwd[i]);
                sb.AppendLine("  phase " + phases[i].ToString("0.0") + ": " + lit + " lit px"
                    + (lit == 0 ? "  (nothing drawn)" : ""));
            }

            // Determinism: the forward pass and the reverse-order pass must agree at every phase.
            int worst = 0;
            for (int i = 0; i < phases.Length; i++) worst = Math.Max(worst, Diff(fwd[i], rev[i]));
            sb.AppendLine(worst == 0
                ? "  Determinism: PASS — forward and reverse-order renders are bit-identical at all three phases "
                  + "(warm-step and cold-replay routes agree)."
                : "  Determinism: FAIL — up to " + worst + " px differ between the forward and reverse-order "
                  + "renders, so the replay cache is changing the picture.");

            // Animation: consecutive phases must NOT be identical, or the sim is ignoring the clock.
            bool anySame = false;
            for (int i = 1; i < phases.Length; i++)
            {
                int d = Diff(fwd[i - 1], fwd[i]);
                sb.AppendLine("  phase " + phases[i - 1].ToString("0.0") + " vs " + phases[i].ToString("0.0")
                    + ": " + d + " px differ");
                if (d == 0) anySame = true;
            }
            sb.AppendLine(anySame
                ? "  Animation: FAIL — two phases render identically, so the picture is not following the clock."
                : "  Animation: PASS — every phase renders a different picture.");
        }

        static int Lit(Color32[] px)
        {
            if (px == null) return 0;
            int n = 0;
            for (int i = 0; i < px.Length; i++) if (px[i].a > 0) n++;
            return n;
        }

        static int Diff(Color32[] a, Color32[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return int.MaxValue;
            int n = 0;
            for (int i = 0; i < a.Length; i++)
                if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) n++;
            return n;
        }

        // ── the sheet ────────────────────────────────────────────────────────────────────────────────────

        static string WriteSheet(Color32[][] rows)
        {
            int w = Canvas * 8, h = Canvas * rows.Length;
            var sheet = new Color32[w * h];
            // A dark grey ground rather than transparency: a flame is mostly semi-transparent smoke, and on a
            // checkerboard or on white its edge is unreadable.
            var ground = new Color32(24, 24, 28, 255);
            for (int i = 0; i < sheet.Length; i++) sheet[i] = ground;

            // Row 0 of a Shaper render is the BOTTOM row (ShaperDocumentRenderer's own convention), and so is
            // row 0 of a Texture2D, so the FIRST strip is written to the TOP band to read top-to-bottom.
            for (int r = 0; r < rows.Length; r++)
            {
                int band = rows.Length - 1 - r;
                for (int y = 0; y < Canvas; y++)
                    for (int x = 0; x < w; x++)
                    {
                        var c = rows[r][y * w + x];
                        if (c.a == 0) continue;
                        int di = (band * Canvas + y) * w + x;
                        float a = c.a / 255f;
                        sheet[di] = new Color32(
                            (byte)(c.r * a + ground.r * (1f - a)),
                            (byte)(c.g * a + ground.g * (1f - a)),
                            (byte)(c.b * a + ground.b * (1f - a)), 255);
                    }
            }

            Texture2D tex = null;
            try
            {
                Directory.CreateDirectory(OutDir);
                tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                tex.SetPixels32(sheet);
                tex.Apply(false);
                byte[] png = ImageConversion.EncodeToPNG(tex);
                string path = Path.Combine(OutDir, "fire-fireball-contact-sheet.png");
                File.WriteAllBytes(path, png);
                return "Wrote " + path + " (" + w + "x" + h + ", " + png.Length + " bytes). "
                     + "Rows top to bottom: Fire frames 0-7, Fire frames 8-15, Fireball frames 0-7.";
            }
            catch (Exception e) { return "FAIL: contact-sheet write threw — " + e; }
            finally { if (tex != null) UnityEngine.Object.DestroyImmediate(tex); }
        }

        static void Destroy(ShaperDocument d)
        {
            if (d != null) UnityEngine.Object.DestroyImmediate(d);
        }
    }
}
