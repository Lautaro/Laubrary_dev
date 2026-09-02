// PyreFormShaperUI — the Fill / Alpha / Frames rows a hosted PyreForm needs, above its reflected dial dump (T-0202).
//
// ── what this closes ─────────────────────────────────────────────────────────────────────────────────────
// A hosted form's dials were drawn by the window's reflected dump over the FORM object
// (ShaperWindow.Sections.cs:1056-1057, 1117-1142). That is the right default for objects declaring ~775 fields
// between them and it stays the default here — this drawer runs it verbatim. But three of the things Pyre's own
// Shape section shows for a hosted form are NOT declared on the form: the layer's Shape Fill, the layer's Alpha
// envelope and the clip's frame count (PyreWindow.cs:1263-1267). A hosted form has no layer, so T-0202 moved
// those three onto PyreFormCompositeSource — and a field on the SOURCE is exactly what the reflected dump does
// not look at. Without this drawer they would be authored data with no control.
//
// ── why a whole drawer rather than a window edit ─────────────────────────────────────────────────────────
// Same reason PyreLayerShaperUI is a drawer: the registry (ShaperCompositeSourceUICatalog) is the published way
// for a generator family to bring its own card, and Shaper's core editor assembly is not allowed to learn this
// family by name. The cost is that a drawer replaces the reflected dump rather than sitting beside it, so the
// dump is reproduced below — with the window's own options, so the two cannot look different.
using Laubrary.Pyre;
using Laubrary.Shaper;
using Laubrary.Shaper.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.PyreShaper.Editor
{
    [InitializeOnLoad]
    public static class PyreFormShaperUI
    {
        static PyreFormShaperUI()
        {
            ShaperCompositeSourceUICatalog.Register(new Drawer());
        }

        sealed class Drawer : IShaperCompositeSourceUI
        {
            public bool CanDraw(IShaperCompositeSource source)
                => source is PyreFormCompositeSource s && s.form != null;

            public void Build(ShaperSourceUIContext ctx, IShaperCompositeSource source)
            {
                var src = (PyreFormCompositeSource)source;

                // A source authored before T-0202 deserializes with these null/zero, which reads on screen as a
                // control with nothing behind it. Repairing here — inside the window's own Change, so it is one
                // undoable step — is the same repair PyreLayerShaperUI makes for a null layer.
                if (src.shapeFill == null || src.alpha == null || src.frames <= 0)
                    ctx.Change(() =>
                    {
                        src.shapeFill ??= PyreFormCompositeSource.DefaultFill();
                        src.alpha ??= PyreFormCompositeSource.DefaultAlpha();
                        if (src.frames <= 0) src.frames = PyreFormCompositeSource.DefaultFrames;
                    });

                var box = Z.BoxKeyed(src.SourceLabel,
                    "The paint, opacity and clock this generator is hosted with — the same three rows Pyre's own "
                    + "Shape section shows above a form's dials.",
                    "shaper.window.composite.pyreform");

                // Frames first: it is the source's own dial and it decides what every envelope below is measured
                // against, so it is drawn above them rather than lost among the form's own fields. The
                // MicroSlider carries its own caption, so no Z.Field wrapper — a field labelled the same as the
                // control inside it is the redundant title the layout rules forbid.
                box.Add(Z.MicroSlider("Generator frames", src.frames, 1f, 120f,
                    "How many frames this generator's own animation spans. Match the document's Frames for exact "
                    + "one-to-one playback; a smaller number plays its whole life out sooner.",
                    v => { ctx.Change(() => src.frames = Mathf.Max(1, Mathf.RoundToInt(v))); ctx.Rebuild(); },
                    170f, showValue: true, decimals: 0));

                // Pyre draws no Fill row for a form that carries its own ramps (UsesFill == false,
                // PyreWindow.cs:1263) because the control would be dead. Same test, same reason: of the nine,
                // only Inferno and Fork Blast read it.
                if (src.form.UsesFill)
                    box.Add(Z.Fill("Fill", src.shapeFill,
                        $"The {src.form.DisplayName} generator's colour source. {src.form.Description}",
                        ctx.Touch, () => RecordUndo(ctx),
                        new ZuiFillControl.Options().WithWidth(190f).WithGrow(2.2f)));

                box.Add(Z.Value("Alpha", src.alpha, new ZuiValueControl.Options
                {
                    absMin = 0f, absMax = 1f,
                    hideCurveTiming = true, hideCurveRange = true, hideLiveReadout = true,
                    controlWidth = 170f, grow = true,
                    frameCount = Mathf.Max(1, src.frames),
                }, "Overall opacity across this generator's life — multiplied into the picture it paints.",
                   ctx.Touch, () => RecordUndo(ctx)));

                // The form's own dials, drawn exactly as the window's default path draws them
                // (ShaperWindow.Sections.cs:1117-1142) — same box key, same options — so replacing the dump with
                // this drawer changes nothing about how a form's ~775 fields appear.
                var dials = Z.BoxKeyed(src.SourceLabel + " dials",
                    "Every dial this generator declares, read straight off the generator itself — so it cannot "
                    + "drift out of date as the generator changes. A dial set to a curve is read at each frame's "
                    + "own phase, so animating one animates the picture.",
                    "shaper.window.composite.dials");
                var host = new VisualElement();
                ZuiReflect.FlowFields(host, src.form, new ZuiReflect.Options
                {
                    OnBeforeChange = () => RecordUndo(ctx),
                    OnChanged = ctx.Touch,
                    OnStructureChanged = ctx.Rebuild,
                    ControlWidth = 140f,
                });
                dials.Add(host);
                box.Add(dials);

                ctx.Body.Add(box);
            }

            // Complete-object, never RecordObject: a document is a graph of [SerializeReference]s and
            // RecordObject does not snapshot them, so undo would null them (T-0198, ShaperCompositeSourceUI.cs).
            static void RecordUndo(ShaperSourceUIContext ctx)
            {
                if (ctx.UndoTarget != null)
                    Undo.RegisterCompleteObjectUndo(ctx.UndoTarget, "Edit Shaper Document");
            }
        }
    }
}
