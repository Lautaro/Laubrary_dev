// PyreLayerShaperUI — Pyre's legacy shapes joining Shaper's picker, drawn with Pyre's own cards (T-0183).
//
// Three small things live here, and NOTHING else does: the picker entries (one per surviving ShapeForm, in a
// "Pyre" column), the drawer that says "this source gets Pyre's Shape-section cards", and the adapter that
// lets those cards — which were written against a PyreWindow — draw into a ShaperWindow instead.
//
// This is the ONLY assembly that knows both sides. Shaper's editor assembly gained no reference to Pyre's
// editor assembly, and Pyre's window gained no knowledge of Shaper: both sides publish an extension point
// (ShaperShapeCatalog.Register, ShaperCompositeSourceUICatalog.Register, IPyreShapeCardHost) and this bridge
// is what pairs them, exactly as Runtime/PyreShaper pairs the two runtimes.
using System.Collections.Generic;
using Laubrary.Pyre;
using Laubrary.Pyre.Editor;
using Laubrary.Shaper;
using Laubrary.Shaper.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.PyreShaper.Editor
{
    [InitializeOnLoad]
    public static class PyreLayerShaperUI
    {
        static PyreLayerShaperUI()
        {
            ShaperShapeCatalog.Register(PyreShapes);
            ShaperCompositeSourceUICatalog.Register(new Drawer());
        }

        // ── the picker column ────────────────────────────────────────────────────────────────────────────

        // Which built-in ShapeForms Shaper offers, with Pyre's own labels and icons (PyreWindow.cs's Forms3D /
        // Forms2D / FormsSpecial tables). Playback3D is absent on purpose: it has no runtime bake in Pyre
        // either (PyreRenderer.cs:262-269), only an editor preview, so a hosted one would draw nothing. The two
        // [Obsolete] slots are absent for the reason they are retired in Pyre — Inferno and ForkBlast live on
        // as PyreForm plug-ins, which Shaper's Generators column already lists.
        static readonly (ShapeForm form, string label, string icon)[] Shapes =
        {
            (ShapeForm.Disc, "Disc", "circle"),
            (ShapeForm.Gem, "Gem", "diamond"),
            (ShapeForm.Box, "Box", "cube"),
            (ShapeForm.Pyramid, "Pyramid", "triangle"),
            (ShapeForm.Can, "Can", "cylinder"),
            (ShapeForm.Orb, "Orb", "sphere"),
            (ShapeForm.Ring, "Ring", "circle-dashed"),
            (ShapeForm.Crescent, "Crescent", "moon"),
            (ShapeForm.Star, "Star", "star"),
            (ShapeForm.Polygon, "Polygon", "polygon"),
            (ShapeForm.Streak, "Streak", "lightning"),
            (ShapeForm.Sparkle, "Sparkle", "sparkle"),
            (ShapeForm.Sprite, "Sprite", "image"),
            (ShapeForm.Text, "Text", "text-aa"),
            (ShapeForm.Fire, "Fire", "flame"),
            (ShapeForm.Fireball, "Fireball", "fire"),
        };

        // §6.2 requires a composite generator to bypass the shape/fill split only for a stated reason, which
        // used to be an authored sentence on every document (ShaperCompositeDef.reasonNote, retired T-0254).
        // The honest reason, kept here as a comment rather than a per-document field: a Pyre layer paints its
        // own lit, bordered, glowing picture in one pass, so there is no edge rule to hand Shaper's shape stage
        // and no paint recipe to hand its fill stage — splitting it would mean rewriting sixteen renderers,
        // which is a decision nobody has taken. Same reason (and the same NotYetSplit classification) as the
        // nine hosted PyreForms PyreCompositeCatalog declares.

        static IEnumerable<ShaperShapeEntry> PyreShapes()
        {
            foreach (var (f, label, icon) in Shapes)
            {
                var form = f;
                yield return new ShaperShapeEntry
                {
                    Category = "Pyre",
                    Label = label,
                    Icon = icon,
                    Tooltip = "Draw Pyre's " + label.ToLowerInvariant() + " — the real Pyre layer, with its own "
                        + "life envelopes for opacity, size, rotation and offset. Its dials appear below the "
                        + "picker as Pyre's own cards; the node's fill, border and transform are kept.",
                    IsCurrent = n => n.kind == ShaperNodeKind.Composite && n.composite != null
                        && n.composite.source is PyreLayerCompositeSource p && p.layer != null
                        && p.layer.form == null && p.layer.shapeForm == form,
                    Apply = n =>
                    {
                        n.kind = ShaperNodeKind.Composite;
                        // Switching BETWEEN Pyre shapes keeps the layer, so every envelope the author has
                        // already tuned survives changing a gem into an orb — the same continuity Pyre's own
                        // form picker gives (it sets shapeForm on the layer in place).
                        var existing = n.composite?.source as PyreLayerCompositeSource;
                        var src = existing ?? new PyreLayerCompositeSource();
                        if (src.layer == null) src.layer = new PyreLayer { matteEnabled = false };
                        // A built-in ShapeForm and a plug-in PyreForm are alternatives on a layer, and the form
                        // wins when both are set (PyreRenderer.cs:248) — so picking a legacy shape must clear it.
                        src.layer.form = null;
                        src.layer.shapeForm = form;

                        n.composite = new ShaperCompositeDef
                        {
                            source = src,
                            // The bake box is carried across so picking a different shape never silently
                            // resizes the node's footprint (the same rule ShaperShapeCatalog.ApplyForm follows).
                            halfExtentX = n.composite?.halfExtentX ?? 64f,
                            halfExtentY = n.composite?.halfExtentY ?? 64f,
                            bakeWidth = n.composite?.bakeWidth ?? 128,
                            bakeHeight = n.composite?.bakeHeight ?? 128,
                        };
                    },
                };
            }
        }

        // ── the card ─────────────────────────────────────────────────────────────────────────────────────

        sealed class Drawer : IShaperCompositeSourceUI
        {
            public bool CanDraw(IShaperCompositeSource source) => source is PyreLayerCompositeSource;

            public void Build(ShaperSourceUIContext ctx, IShaperCompositeSource source)
            {
                var src = (PyreLayerCompositeSource)source;
                if (src.layer == null) ctx.Change(() => src.layer = new PyreLayer { matteEnabled = false });

                var box = Z.BoxKeyed(src.SourceLabel,
                    "Pyre's own Shape-section cards for this layer — the same controls, in the same boxes, as "
                    + "the Pyre window shows. Every envelope is read at each frame's own phase, so the layer "
                    + "animates across the document's frames. Its own frame count and Life window are no "
                    + "longer authored here: they follow the SHAPER LAYER above, so a Pyre shape hosted in "
                    + "Shaper has exactly one lifetime.",
                    "shaper.window.composite.pyrelayer");

                // T-0204 — "Layer frames" and the Life-window row are GONE. The owner: "Pyre Box has its own
                // Life (frames)?! Each layer can have its own lifetime as long as it's not larger than canvas
                // framecount. What happens if a layer has 80 frames but the canvas 10? Makes no sense. This
                // shouldn't be more complex than in Pyre." A hosted layer used to author a SECOND, independent
                // frame axis and a SECOND Life window that could silently disagree with the Shaper layer's own
                // Lifetime (BuildLayersSection, ShaperWindow.cs). Both are now silently kept in lock-step with
                // the document/layer instead of exposed as a second pair of dials: `frames` always mirrors
                // ctx.FrameCount (the document's own Frames), which is what makes `phase01 · (frames − 1)`
                // land on the SAME frame index the document's own clock is on, and the hosted layer's own
                // startFrame/endFrame mirror the Shaper layer's Lifetime verbatim (same document-frame space,
                // same -1 "last frame" sentinel) once `frames` agrees. No Undo entry for this: it is bookkeeping
                // that keeps two numbers equal, not an authored edit — the actual edit happened on the layer's
                // own Lifetime row, which already recorded its own Undo.
                bool changed = false;
                if (ctx.FrameCount > 0 && src.frames != ctx.FrameCount) { src.frames = ctx.FrameCount; changed = true; }
                if (src.layer.startFrame != ctx.LayerStartFrame) { src.layer.startFrame = ctx.LayerStartFrame; changed = true; }
                if (src.layer.endFrame != ctx.LayerEndFrame) { src.layer.endFrame = ctx.LayerEndFrame; changed = true; }
                if (changed) ctx.Touch();

                var host = new CardHost(ctx, box, src);
                PyreShapeCards.BuildLegacyForm(host, src.layer);

                ctx.Body.Add(box);
            }
        }

        // ── the adapter ──────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Lets Pyre's Shape cards draw into a Shaper document. Every method is the Shaper equivalent of the
        /// PyreWindow member the card used to call, so the cards themselves needed no Shaper-aware branch.
        /// The Val / Val2D / FillRow / SlotFill options are copied verbatim from PyreWindow's
        /// (PyreWindow.cs's helpers section) so the controls look and behave the same in both windows.
        /// </summary>
        sealed class CardHost : IPyreShapeCardHost
        {
            readonly ShaperSourceUIContext ctx;
            readonly VisualElement body;
            readonly PyreLayerCompositeSource src;

            public CardHost(ShaperSourceUIContext ctx, VisualElement body, PyreLayerCompositeSource src)
            { this.ctx = ctx; this.body = body; this.src = src; }

            public VisualElement Body => body;

            // Pyre's canvas is one square edge; Shaper's is a rectangle, and the source renders at the larger
            // edge (PyreLayerCompositeSource.Render), so that is what the layer's own dial ranges are bounded by.
            public int CanvasSize => Mathf.Max(1, Mathf.RoundToInt(ctx.CanvasExtent));

            // The layer's OWN frame count, not the document's: the Life-window row is authored in the layer's
            // frames, which is exactly what the source's Frames dial declares.
            public int FrameCount => Mathf.Max(1, src.frames);

            public void Dirty(System.Action apply) => ctx.Change(apply);
            public void MarkDirty() => ctx.Touch();
            public void RecordUndo()
            {
                if (ctx.UndoTarget != null) Undo.RegisterCompleteObjectUndo(ctx.UndoTarget, "Edit Shaper Document");
            }
            public void RebuildShape() => ctx.Rebuild();
            // Shaper has no swarm section of Pyre's kind to rebuild; the whole card is rebuilt instead, which
            // is a superset and keeps the Fire "emitters from the swarm" toggle honest.
            public void RebuildSwarm() => ctx.Rebuild();

            public VisualElement Val(string label, string tooltip, ZUIValue v, float lo, float hi, bool cyclic = false)
                => Z.Value(label, v, new ZuiValueControl.Options
                {
                    absMin = lo, absMax = hi,
                    hideCurveTiming = true, hideCurveRange = true, hideLiveReadout = true,
                    // T-0192 — grow stays ON, matching Pyre's own Val() exactly (PyreWindow.cs:2589-2593):
                    // confirmed by eye against Pyre's reference captures (pyre_1col/2col/3col.png) that
                    // turning it off undershoots Pyre's own envelope size. Real fix for this window's part of
                    // T-0192 is ShaperWindow's own layer-row width budget, not Val's sizing.
                    controlWidth = 170f, grow = true,
                    cyclic = cyclic,
                    frameCount = FrameCount,
                }, tooltip, ctx.Touch, RecordUndo);

            public VisualElement Val2D(string label, string tooltip, ZUIValue x, ZUIValue y,
                                       ZuiValue2DControl.Options o)
                => Z.Value2D(label, x, y, o, tooltip, ctx.Touch, RecordUndo);

            public ZuiFillControl FillRow(string label, string tooltip, ZuiFill fill,
                                          ZuiFillControl.Options opt = null)
                => Z.Fill(label, fill, tooltip, ctx.Touch, RecordUndo, opt);

            public ZuiFillControl SlotFill(string label, string tooltip, ZuiFill fill)
                => FillRow(label, tooltip, fill, new ZuiFillControl.Options().WithWidth(96f));

            // Playback3D is not offered as a Shaper shape (it has no runtime bake at all), so this can only be
            // reached by an asset that stored that form some other way. Saying so beats drawing nothing.
            public void BuildPlaybackBox(PyreLayer s)
                => body.Add(Z.Text("Playback 3D is a Pyre-window-only preview form and has no bake, so it "
                    + "cannot be hosted here. Pick another shape.", ZuiText.Subtle,
                    "This layer holds Pyre's Playback 3D form, which renders nothing outside Pyre's own preview."));
        }
    }
}
