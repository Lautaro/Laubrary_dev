// TEMP PROBE T-0198 — PM deletes after running
//
// Escalated Debug Phase 2 for T-0198: the predictions, executable. Every test is judged by a printed
// expectation, and the whole file is throwaway — it has no menu item, no window and no runtime reach.

using System;
using System.Text;
using Laubrary.Pyre.Forms.Kiln;
using Laubrary.PyreShaper;
using Laubrary.Shaper;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Shaper.EditorTools
{
    public static class T0198_CompositeSizeProbe
    {
        const string DemoDocPath = "Assets/Demos/ShaperDemo/ShaperDemoDoc.asset";

        public static string RunAll()
        {
            var sb = new StringBuilder("T-0198 composite size probe\n");
            sb.AppendLine(P1_NonSquareArcBurst());
            sb.AppendLine(P2_SquareControl());
            sb.AppendLine(P3_FitToPixelSize());
            sb.AppendLine(P4_BoxCoversCanvas());
            sb.AppendLine(P5_ThrowingSourceDegrades());
            sb.AppendLine(P6_UndoVsSerializeReference());
            sb.AppendLine(P7_NullHeightRenders());
            sb.AppendLine(P8_DemoDocDuplicate());
            return sb.ToString();
        }

        // ── predictions ──────────────────────────────────────────────────────────────────────────────────

        /// PREDICTION 1 (reproduce, pre-fix): a composite hosting a PyreForm on a NON-SQUARE canvas throws.
        /// Post-fix: renders, and puts ink on the canvas.
        static string P1_NonSquareArcBurst()
        {
            var doc = MakeDoc(96, 152, 2.48778f, ShaperNode.Composite(ArcBurst(), "ArcBurst"));
            return RenderVerdict("P1  non-square 96x152, pixelSize 2.48778, ArcBurst composite", doc,
                                 expectNoThrow: true, expectInk: true);
        }

        /// PREDICTION 2 (do NOT reproduce): the same generator on a SQUARE canvas at pixelSize 1 was always
        /// fine and must stay fine — the control that says the fix did not trade one shape of canvas for another.
        static string P2_SquareControl()
        {
            var doc = MakeDoc(96, 96, 1f, ShaperNode.Composite(ArcBurst(), "ArcBurst"));
            return RenderVerdict("P2  square 96x96, pixelSize 1, ArcBurst composite (control)", doc,
                                 expectNoThrow: true, expectInk: true);
        }

        /// PREDICTION 3: the derived bake box is in CANVAS UNITS, so it must scale with pixelSize; the bake
        /// raster stays one texel per sample and is never zero or fractional.
        static string P3_FitToPixelSize()
        {
            var def = PyreCompositeCatalog.Build(new ArcBurstForm(), PyreCompositeCatalog.ArcBurst);
            def.FitTo(96, 152, 2.48778f);
            bool ok = Approx(def.halfExtentX, 96 * 0.5f * 2.48778f)
                   && Approx(def.halfExtentY, 152 * 0.5f * 2.48778f)
                   && def.bakeWidth == 96 && def.bakeHeight == 152;

            var tiny = PyreCompositeCatalog.Build(new ArcBurstForm(), PyreCompositeCatalog.ArcBurst);
            tiny.FitTo(0, -3, 0f);
            bool clamped = tiny.bakeWidth >= 1 && tiny.bakeHeight >= 1
                        && tiny.halfExtentX > 0f && tiny.halfExtentY > 0f;

            var sb = new StringBuilder("P3  FitTo carries pixelSize and clamps\n");
            sb.AppendLine($"      halfExtent = ({def.halfExtentX:F3}, {def.halfExtentY:F3})  expected "
                        + $"({96 * 0.5f * 2.48778f:F3}, {152 * 0.5f * 2.48778f:F3})");
            sb.AppendLine($"      bake       = {def.bakeWidth}x{def.bakeHeight}  expected 96x152");
            sb.AppendLine($"      FitTo(0,-3,0) clamped to {tiny.bakeWidth}x{tiny.bakeHeight}, "
                        + $"half ({tiny.halfExtentX:F2},{tiny.halfExtentY:F2})  expected >= 1 and > 0");
            sb.Append("      RESULT: " + Verdict(ok && clamped));
            return sb.ToString();
        }

        /// PREDICTION 4: after a render, the fitted box must cover the canvas the grid actually spans — the
        /// number that decides whether the generator fills the document or sits shrunken in the middle.
        static string P4_BoxCoversCanvas()
        {
            var node = ShaperNode.Composite(ArcBurst(), "ArcBurst");
            var doc = MakeDoc(96, 152, 2.48778f, node);
            SafeRenderDoc(doc, out _, out _);

            float gridHalfW = 0.5f * (96 - 1) * 2.48778f;
            float gridHalfH = 0.5f * (152 - 1) * 2.48778f;
            float ratioX = node.composite.halfExtentX / gridHalfW;
            float ratioY = node.composite.halfExtentY / gridHalfH;
            bool ok = ratioX > 0.99f && ratioX < 1.05f && ratioY > 0.99f && ratioY < 1.05f;

            var sb = new StringBuilder("P4  fitted box vs the canvas the grid spans\n");
            sb.AppendLine($"      box/grid = ({ratioX:F4}, {ratioY:F4})  expected ~1.00 "
                        + "(pre-fix this was ~0.40 — the composite covered 40% of the canvas)");
            sb.Append("      RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        /// PREDICTION 5: a generator that throws must degrade to an EMPTY node with a status message, never
        /// take the frame down. This one throws on purpose, so it tests the guard rather than any real form.
        static string P5_ThrowingSourceDegrades()
        {
            var def = PyreCompositeCatalog.BuildSource(new ThrowingSource(), PyreCompositeCatalog.ArcBurst);
            var node = ShaperNode.Composite(def, "Broken");
            var doc = MakeDoc(64, 64, 1f, node);

            SafeRenderDoc(doc, out string err, out float ink);
            string status = ShaperCompositeDef.FirstError(node);
            bool ok = err == null && ink <= 0f && !string.IsNullOrEmpty(status);

            var sb = new StringBuilder("P5  a throwing generator degrades to empty + a status line\n");
            sb.AppendLine("      render threw: " + (err ?? "no") + "  expected no");
            sb.AppendLine($"      ink: {ink:F4}  expected 0");
            sb.AppendLine("      status: " + (status ?? "(none)") + "  expected a message");
            sb.Append("      RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        /// PREDICTION 6 (the height-nulling mechanism, measured rather than argued): Undo.RecordObject does not
        /// snapshot the managed-reference registry, so undoing an edit made after it nulls every
        /// [SerializeReference] on the document. RegisterCompleteObjectUndo does snapshot it and must not.
        static string P6_UndoVsSerializeReference()
        {
            string recorded = UndoRoundTrip(useCompleteUndo: false);
            string complete = UndoRoundTrip(useCompleteUndo: true);

            var sb = new StringBuilder("P6  Undo vs [SerializeReference] — how a height stage disappears\n");
            sb.AppendLine("      Undo.RecordObject          -> height after undo: " + recorded);
            sb.AppendLine("      Undo.RegisterCompleteObjectUndo -> height after undo: " + complete);
            sb.AppendLine("      expected: RecordObject may null it (that is the defect); complete undo must NOT");
            sb.Append("      RESULT: " + Verdict(complete == "alive"));
            return sb.ToString();
        }

        static string UndoRoundTrip(bool useCompleteUndo)
        {
            var doc = ScriptableObject.CreateInstance<ShaperDocument>();
            try
            {
                doc.canvasWidth = 32; doc.canvasHeight = 32;
                var layer = new ShaperLayer { name = "L", root = Disc(), height = new ShaperHeightDef() };
                doc.layers.Clear();
                doc.layers.Add(layer);

                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                if (useCompleteUndo) Undo.RegisterCompleteObjectUndo(doc, "probe");
                else Undo.RecordObject(doc, "probe");
                doc.canvasWidth = 48;
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(group);
                Undo.PerformUndo();

                var l = doc.layers.Count > 0 ? doc.layers[0] : null;
                if (l == null) return "layer gone";
                return l.height != null ? "alive" : "NULLED";
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(doc);
            }
        }

        /// PREDICTION 7: a layer whose height stage is null (exactly the state the demo document was found in)
        /// renders without throwing — the flat-layer case, not a broken one.
        static string P7_NullHeightRenders()
        {
            var doc = MakeDoc(64, 64, 1f, Disc());
            doc.layers[0].height = null;
            return RenderVerdict("P7  a layer with no height stage renders flat", doc,
                                 expectNoThrow: true, expectInk: true);
        }

        /// PREDICTION 8: the owner's own document, on a DUPLICATE held only in memory, at his canvas and pixel
        /// size. Nothing here writes to the asset.
        static string P8_DemoDocDuplicate()
        {
            var original = AssetDatabase.LoadAssetAtPath<ShaperDocument>(DemoDocPath);
            if (original == null) return "P8  demo document not found at " + DemoDocPath + "\n      RESULT: SKIPPED";

            var copy = UnityEngine.Object.Instantiate(original);
            try
            {
                copy.name = "T0198 duplicate (never saved)";
                copy.canvasWidth = 96; copy.canvasHeight = 152; copy.pixelSize = 2.48778f;
                int nulls = 0;
                for (int i = 0; i < copy.layers.Count; i++)
                    if (copy.layers[i] != null && copy.layers[i].height == null) nulls++;

                var sb = new StringBuilder("P8  the demo document (duplicate) at 96x152 / pixelSize 2.48778\n");
                sb.AppendLine($"      layers: {copy.layers.Count}, of which with no height stage: {nulls}");
                sb.Append("      " + RenderVerdict("render", copy, expectNoThrow: true, expectInk: true));
                return sb.ToString();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(copy);
            }
        }

        // ── harness ──────────────────────────────────────────────────────────────────────────────────────

        static string RenderVerdict(string label, ShaperDocument doc, bool expectNoThrow, bool expectInk)
        {
            SafeRenderDoc(doc, out string err, out float ink);
            bool ok = (err == null) == expectNoThrow && (ink > 0f) == expectInk;
            var sb = new StringBuilder(label + "\n");
            sb.AppendLine("      threw: " + (err ?? "no") + "  expected " + (expectNoThrow ? "no" : "yes"));
            sb.AppendLine($"      mean alpha: {ink:F4}  expected " + (expectInk ? "> 0" : "0"));
            sb.Append("      RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        /// Render one frame and report (exception text, mean alpha) instead of letting either escape.
        static void SafeRenderDoc(ShaperDocument doc, out string error, out float ink)
        {
            error = null; ink = 0f;
            try
            {
                int n = ShaperDocumentRenderer.SampleCount(doc);
                var dst = new float[n * ShaperDocumentRenderer.FloatsPerSample];
                ShaperDocumentRenderer.RenderPhaseInto(doc, 0.5f, dst, 0);
                double sum = 0.0;
                for (int i = 0; i < n; i++) sum += dst[i * ShaperDocumentRenderer.FloatsPerSample + 3];
                ink = n > 0 ? (float)(sum / n) : 0f;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
            }
        }

        static ShaperDocument MakeDoc(int w, int h, float pixelSize, ShaperNode root)
        {
            var doc = ScriptableObject.CreateInstance<ShaperDocument>();
            doc.canvasWidth = w; doc.canvasHeight = h; doc.pixelSize = pixelSize;
            doc.frameCount = 4;
            doc.layers.Clear();
            doc.layers.Add(new ShaperLayer { name = "Layer", root = root });
            return doc;
        }

        static ShaperCompositeDef ArcBurst()
            => PyreCompositeCatalog.Build(new ArcBurstForm(), PyreCompositeCatalog.ArcBurst);

        static ShaperNode Disc()
        {
            return ShaperNode.Primitive(
                new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = 12f, ellipseRy = 12f },
                "Disc");
        }

        static bool Approx(float a, float b) => Mathf.Abs(a - b) < 1e-3f;
        static string Verdict(bool ok) => ok ? "PASS" : "FAIL";

        /// A generator that always throws — the only way to test the guard without waiting for a real form to
        /// meet a canvas it cannot draw.
        [Serializable]
        sealed class ThrowingSource : IShaperCompositeSource
        {
            public string SourceLabel => "Deliberately broken";
            public void Render(int width, int height, float phase01, uint seed, Color32[] target)
                => throw new IndexOutOfRangeException("probe: deliberate failure");
        }
    }
}
