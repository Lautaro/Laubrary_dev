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

        // §6.2 requires a composite generator to DECLARE why it bypasses the shape/fill split, and requires that
        // declaration to read as technical debt rather than architecture. This is the honest sentence: a Pyre
        // layer paints its own lit, bordered, glowing picture in one pass, so there is no edge rule to hand
        // Shaper's shape stage and no paint recipe to hand its fill stage — splitting it would mean rewriting
        // sixteen renderers, which is a decision nobody has taken.
        const string Declaration =
            "Hosts a whole Pyre layer and lets Pyre's own renderer draw it, so animations authored in Pyre "
            + "reproduce exactly. Its silhouette and its paint are computed together in one pass (lighting, "
            + "facet lines, glows, border), so there is no edge rule to hand the shape stage and no paint "
            + "recipe to hand the fill stage. Splitting it would mean re-implementing every legacy Pyre form "
            + "against the shape/fill contract; until that is done this stays monolithic.";

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
                            reason = ShaperCompositeReason.NotYetSplit,
                            reasonNote = Declaration,
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
                    + "animates across the document's frames.",
                    "shaper.window.composite.pyrelayer");

                // How many frames the layer thinks it spans. It is the source's own dial, not the layer's, and
                // it decides what the Life-window row below is measured in — so it is drawn FIRST, above the
                // cards it scales, rather than lost among them.
                // The MicroSlider carries its own caption, so it is NOT wrapped in a Z.Field — a field labelled
                // the same as the control inside it is the redundant title the layout rules forbid.
                box.Add(Z.MicroSlider("Layer frames", src.frames, 1f, 120f,
                    "How many frames this layer's own animation spans. Match the document's Frames for exact "
                    + "one-to-one playback; a smaller number plays the layer's whole life out sooner.",
                    v => { ctx.Change(() => src.frames = Mathf.Max(1, Mathf.RoundToInt(v))); ctx.Rebuild(); },
                    170f, showValue: true, decimals: 0));

                var host = new CardHost(ctx, box, src);
                PyreShapeCards.BuildLifeWindow(host, src.layer);
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
                if (ctx.UndoTarget != null) Undo.RecordObject(ctx.UndoTarget, "Edit Shaper Document");
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
                    // T-0192 — grow off, same reasoning as ShaperWindow.Val (ShaperWindow.cs:1391-1417): a
                    // hosted Pyre layer's shape cards (PyreShapeCards.cs, read-only reference) land in this
                    // window's own ~360-500px ColumnFlow columns, not Pyre's own wide single-column body, so
                    // a solo Val growing to its 3.2× cap overflows exactly like the plain Shaper dials did.
                    controlWidth = 170f, grow = false,
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
