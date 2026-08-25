// ChunkWindow.Formation — the Spawn Formation module's section (AgentHQ T-0079, design doc "Standalone
// modules" #8). Placement + stagger only; WHAT gets spawned is the first blast's own job (BuildPyreSpawn,
// the "Blasts" section above this one) — this section never shows a blast/pool picker, only shape and timing.
//
// T-0081 polish pass. Two things changed here, both because the SAME shape dials are authored in two places:
//
//  1. The shape/stagger dials and the live point preview are now ONE builder, `BuildFormationDials`,
//     parameterised by (SpawnFormation, preview size, stagger-box key) instead of being hard-wired to
//     `c.spawnFormation`. `ChunkWindow.PyreSpawn.cs` calls it for every blast card whose "Several" is on, so
//     a per-blast ring finally has the same live preview the standalone module has always had (D-12) and the
//     two sets of dials can never drift apart again.
//  2. The single `_formationPreviewEl` field — the thing that made (1) impossible before, because several
//     cards would have fought over one element — is now a LIST of every preview canvas currently on screen.
//     `DialF` repaints them all and prunes the ones a rebuild detached (see RepaintFormationPreviews).
//
// It also states the supersede conflict (D-10) on THIS side of it: while this section is on it takes the
// first blast's effect and fires it at its own points, so that blast's own card is not firing. Said as a
// reserved one-line STATE readout, not as an element that appears and shoves the layout down.
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        const float FormationPreviewBox = 150f;   // the standalone section — a full-width host
        // Inside a blast card the host is a box inside a section, so the same 150px canvas plus a dial column
        // wide enough for a MicroSlider overflows a half-screen pane. Sized to the narrower host, not shrunk
        // arbitrarily: 120 + gutter still leaves a full Wide slider beside it.
        const float FormationPreviewCard = 120f;

        static readonly Color FormationOriginColor = new Color(1f, 1f, 1f, 0.35f);
        static readonly Color FormationOutlineColor = new Color(1f, 1f, 1f, 0.18f);
        static readonly Color FormationDotFirst = new Color(1f, 0.82f, 0.25f, 1f);   // fires first — bright/warm
        static readonly Color FormationDotLast = new Color(0.28f, 0.35f, 0.55f, 1f);  // fires last — dim/cool

        // EVERY preview canvas currently built, so a plain (non-rebuilding) Dial can ask them to redraw. A
        // single field would only ever serve one formation, which is exactly what stopped a blast card from
        // having a preview of its own.
        readonly List<IMGUIContainer> _formationPreviews = new List<IMGUIContainer>();

        // Scratch list reused by the preview draw callbacks — a preview repaints often (every drag tick), so this
        // avoids allocating a fresh List every frame the mouse merely moves over the window. Shared across all
        // of them safely: a repaint callback fills it and reads it back synchronously, and IMGUI never
        // re-enters one repaint from inside another.
        readonly List<SpawnPlacement> _formationPreviewScratch = new List<SpawnPlacement>();

        void BuildSpawnFormation(VisualElement root, ChunkSpec c)
        {
            var m = c.spawnFormation;

            var s = Z.Section("Spawn Formation",
                "Places several spawns in a shape and staggers when each one fires, instead of one spawn at " +
                "the burst's origin. While on, this REPLACES the FIRST blast in the Blasts section above — " +
                "WHAT gets spawned at each point is still that blast's own effect or pool; this section only " +
                "decides where the points sit and when each one fires. The other blasts are unaffected.",
                "chunks.formation");
            s.SetHeaderToggle(m.enabled,
                "Lay out several spawn points in a shape, each with its own firing delay, instead of one spawn " +
                "at the origin. Takes over the first blast while on.",
                v => DialAndRebuild("Spawn formation", () => m.enabled = v));
            // D-10: a COLLAPSED section must not hide that it is taking another section's blast over. The
            // suffix only draws while collapsed, so it never doubles up with the status line below.
            s.SetHeaderSuffix(() => m.enabled ? " — firing the first blast" : string.Empty);
            root.Add(s);

            // Off = nothing below it means anything, so don't build it at all (the Cherry Framing shape).
            if (!m.enabled) return;

            // D-10, this side of it. See BlastSupersededLine in ChunkWindow.PyreSpawn.cs for the other side
            // and for why this is a reserved line rather than an element that appears.
            s.Add(FormationSupersedeLine(c));

            BuildFormationDials(s, m.formation, "Formation", "the burst's origin",
                FormationPreviewBox, "chunks.formation.stagger");
        }

        /// What this section is doing to the first blast, RIGHT NOW, in the user's terms. Two real states:
        /// the first blast has an effect (so these points fire it), or it has none (so these points fire
        /// nothing at all — a silent dead end today, because SpawnFormationRunner.Fire returns early on
        /// `!spawner.HasSpawner` and nothing anywhere says so).
        static Label FormationSupersedeLine(ChunkSpec c)
        {
            var blast = c.pyreSpawn;
            bool armed = blast != null && blast.HasSpawner;
            return SupersedeStatusLine(
                armed
                    ? "Firing \"" + blast.DisplayName + "\" — the first blast above — at these points instead " +
                      "of its own placement."
                    : "Nothing to fire: the first blast above has no effect picked, so these points spawn nothing.",
                armed
                    ? "This section supplies WHERE and WHEN only. It takes the first blast's own effect (or " +
                      "its pool) and fires it at each of these points, in this section's layer slot — so that " +
                      "blast's card is not firing on its own, and its own Offset, Several and Layer slot are " +
                      "not in use. Switch this section off to hand it back."
                    : "This section supplies WHERE and WHEN only — the effect comes from the first blast in " +
                      "the Blasts section above. Pick one there and these points will fire it.");
        }

        /// One reserved, single-line STATE readout, used by BOTH sides of the Spawn-Formation-vs-first-blast
        /// conflict so they read as one statement made twice rather than two different warnings.
        ///
        /// Reserved, per the "Stable workspace" rule: the line is ALWAYS present and only its text changes.
        /// The version this replaced added a Z.Text when the conflict went live and left nothing there when
        /// it didn't, so switching Spawn Formation on grew a line inside a card and shoved everything under
        /// it down. Truncates rather than wraps for the same reason.
        static Label SupersedeStatusLine(string text, string tooltip)
        {
            var l = Z.Text(text ?? string.Empty, ZuiText.Subtle, tooltip);
            l.style.height = 18f;
            l.style.flexShrink = 0f;
            l.style.whiteSpace = WhiteSpace.NoWrap;
            l.style.overflow = Overflow.Hidden;
            return l;
        }

        /// <summary>
        /// The shape + stagger dials for ONE SpawnFormation, with its own live preview canvas beside them.
        ///
        /// Parameterised, not hard-wired: `f` is whichever formation is being edited (the standalone module's
        /// or a blast card's), `previewSize` sizes the canvas to its host, and `staggerBoxKey` decides how the
        /// stagger dials are grouped — a keyed sub-box when this builder owns a whole section, or an in-line
        /// labelled divider when the caller has already wrapped it in a titled box (a box called "Stagger"
        /// inside a box called "Shape &amp; stagger" would say the same word twice).
        ///
        /// Every preview built here registers itself in `_formationPreviews`, so any number of these can be on
        /// screen at once and each still repaints under a drag.
        /// </summary>
        void BuildFormationDials(VisualElement host, SpawnFormation f, string undoPrefix, string centreName,
                                 float previewSize, string staggerBoxKey)
        {
            bool line = f.shape == FormationShape.Line;

            // ── shape + count ─────────────────────────────────────────────────────
            host.Add(Z.Row(
                Z.Field("Shape",
                    "Line = points spread along a straight segment. Ring = points spread around a circle, or " +
                    "an arc of one.",
                    Z.Segmented((int)f.shape, new[] { "Line", "Ring" },
                        "Line = points along a straight segment. Ring = points around a circle or an arc.",
                        v => DialAndRebuild(undoPrefix + " shape", () => f.shape = (FormationShape)v))),
                Z.HSpace(),
                // Bounded int → a whole-number MicroSlider (decimals 0, rounded at the setter) — the same shape
                // ZuiReflect and every hand-written window in this package use for a bounded count.
                Z.MicroSlider("Count", f.count, 1, 64,
                    "How many spawn points this lays out. 1 = a single point on " + centreName + ".",
                    v => DialF(undoPrefix + " count", () => f.count = Mathf.RoundToInt(v)), Wide,
                    showValue: true, decimals: 0)));

            // ── shape dials, with the live preview beside them ────────────────────
            // The preview is a fixed square and the shape's own dials are all short — so they go in a column
            // BESIDE it rather than under it. Stacked under, the preview left a dead rectangle the width of
            // the panel and pushed every dial below the fold; vertical space is the scarce resource here, and
            // a row that already exists is free to fill. This is also what answers D-16 for a blast card: in
            // a column this narrow "Start angle °" is a column entry, not a near-empty full-pane row.
            var dials = Z.Column();
            dials.style.flexGrow = 1f;
            dials.style.flexShrink = 1f;
            dials.style.minWidth = 0f;

            if (line)
            {
                dials.Add(Num2F("Length",
                    "Line only: total length of the segment, world units, centred on " + centreName + ".",
                    f.length, v => f.length = v, Num));
                dials.Add(Z.MicroSlider("Angle °", f.angleDeg, 0f, 360f,
                    "Line only: the segment's direction in degrees. 0 = right, 90 = up.",
                    v => DialF(undoPrefix + " angle", () => f.angleDeg = v), Wide,
                    showValue: true, decimals: 0));
            }
            else
            {
                dials.Add(Num2F("Radius",
                    "Ring only: distance from " + centreName + " out to each point, world units.",
                    f.radius, v => f.radius = v, Num));
                dials.Add(Z.MicroSlider("Arc °", f.arcDeg, 0f, 360f,
                    "Ring only: how much of the circle to use. 360 = a full ring, evenly spaced with no " +
                    "doubled-up point where it closes. Less = an arc, with a point on each end.",
                    v => DialF(undoPrefix + " arc", () => f.arcDeg = v), Wide,
                    showValue: true, decimals: 0));
                dials.Add(Z.MicroSlider("Start angle °", f.startAngleDeg, 0f, 360f,
                    "Ring only: where the first point sits, in degrees. 0 = right, 90 = up.",
                    v => DialF(undoPrefix + " start angle", () => f.startAngleDeg = v), Wide,
                    showValue: true, decimals: 0));
            }

            dials.Add(Num2F("Scatter", "Random offset added to each point, world units, inside a disc of this " +
                "radius. 0 = a perfectly regular shape.", f.positionJitter,
                v => f.positionJitter = Mathf.Max(0f, v), Num));

            // flexShrink 0 is load-bearing: UITK defaults every flex child to shrinkable, so a fixed-size
            // preview sitting beside a growing column gets squeezed to nothing and the canvas silently
            // disappears — which is exactly what happened the first time this row was built.
            var preview = BuildFormationPreview(f, previewSize);
            preview.style.flexShrink = 0f;
            // Preview FIRST in the row. With the growing dial column first, the fixed-size preview kept landing
            // past the row's right edge and vanishing — measured, not guessed (its parent laid out at 150x150
            // but off the visible pane). Leading with the fixed-width element removes the whole class of
            // problem: it always gets its size, and the dials take whatever is left.
            host.Add(Z.Row(preview, Z.HSpace(), dials));

            // ── stagger ───────────────────────────────────────────────────────────
            const string staggerTip =
                "How long after burst-start each point fires. 0 seconds fires the whole shape at once and " +
                "costs nothing at all — no runner is created.";
            VisualElement staggerHost;
            if (!string.IsNullOrEmpty(staggerBoxKey))
            {
                var box = Z.BoxKeyed("Stagger", staggerTip, staggerBoxKey);
                host.Add(box);
                staggerHost = box;
            }
            else
            {
                // The lightweight grouping: the caller's own box already names this content, so a labelled
                // divider marks the boundary without a second fold level and a second title.
                host.Add(Z.Divider("Stagger", staggerTip));
                staggerHost = host;
            }

            // Three short numbers, ONE row — none of them is wide enough to earn a row of its own, and the
            // seed belongs with them because it is what makes the jitter below repeatable.
            staggerHost.Add(Z.Row(
                Num2F("Seconds", "Seconds between one point firing and the next, following the order below. " +
                    "0 = the whole shape fires at once.", f.staggerSeconds,
                    v => f.staggerSeconds = Mathf.Max(0f, v), Num),
                Z.HSpace(),
                Num2F("Jitter", "Random plus/minus wobble added to each point's delay, seconds. 0 = a " +
                    "perfectly even beat.", f.staggerJitter, v => f.staggerJitter = Mathf.Max(0f, v), Num),
                Z.HSpace(),
                // "Shape seed", not "Seed": inside a blast card there is already a "Seed" on the Scale row
                // (which fixes the pool pick, the angle and the scale), and two controls a few rows apart
                // both labelled "Seed" meaning different things is a bug generator. The longer label is
                // correct in the standalone section too — this is the SHAPE's randomness, not the spawn's.
                Int2F("Shape seed", "Fixes the random jitter, scatter and shuffle so the shape resolves " +
                    "identically every time. 0 = reroll every play.", f.seed, v => f.seed = v)));

            staggerHost.Add(Z.Field("Order",
                "Which point fires first: a wipe along the shape, the same backwards, outwards from the " +
                "middle (symmetric pairs fire together), or a shuffled order.",
                Z.MiniRadio((int)f.staggerOrder, new[] { "Sequential", "Reverse", "From Centre", "Random" },
                    "Sequential = point 0, 1, 2… · Reverse = the same backwards · From Centre = the middle " +
                    "point(s) first, spreading outwards · Random = a shuffled order.",
                    v => DialF(undoPrefix + " stagger order", () => f.staggerOrder = (FormationStaggerOrder)v),
                    wrap: true)));
        }

        /// Same undo contract as the window's own Dial, plus a repaint nudge for every live preview canvas — a
        /// plain Dial doesn't rebuild the panel, so nothing else would tell an IMGUIContainer a value it reads
        /// every frame just changed under a fast interactive drag.
        void DialF(string undoLabel, System.Action apply)
        {
            Dial(undoLabel, apply);
            RepaintFormationPreviews();
        }

        /// Repaint every preview on screen. The list maintains itself on attach/detach (see
        /// BuildFormationPreview) because the previews are built by two different sections — this one and
        /// every blast card — so no single builder knows when the list as a whole is stale. The null/panel
        /// check here is belt-and-braces for anything that slipped out of the hierarchy without notifying.
        void RepaintFormationPreviews()
        {
            for (int i = _formationPreviews.Count - 1; i >= 0; i--)
            {
                var el = _formationPreviews[i];
                if (el == null || el.panel == null) { _formationPreviews.RemoveAt(i); continue; }
                el.MarkDirtyRepaint();
            }
        }

        VisualElement Num2F(string label, string tooltip, float value, System.Action<float> set, float width = Num)
            => Z.Field(label, tooltip, Z.Float(value, tooltip, v => DialF(label, () => set(v)), width));

        VisualElement Int2F(string label, string tooltip, int value, System.Action<int> set, float width = Num)
            => Z.Field(label, tooltip, Z.Int(value, tooltip, v => DialF(label, () => set(v)), width));

        VisualElement BuildFormationPreview(SpawnFormation f, float size)
        {
            var stage = new VisualElement();
            stage.style.width = size;
            stage.style.height = size;
            stage.style.flexShrink = 0f;
            stage.style.alignSelf = Align.Center;
            stage.style.alignItems = Align.Center;
            stage.style.justifyContent = Justify.Center;
            stage.style.backgroundColor = new Color(0.11f, 0.11f, 0.12f, 1f);
            PreviewBorder(stage);
            stage.tooltip = "The resolved spawn points. Brighter/warmer = fires sooner, dimmer/cooler = fires " +
                "later — this is the only thing that makes a stagger order legible without pressing Play.";

            var imgui = new IMGUIContainer(() => DrawFormationPreview(stage.contentRect, f));
            imgui.style.width = size - 2f;
            imgui.style.height = size - 2f;
            stage.Add(imgui);
            // Registered on attach/detach rather than added here, so the list only ever holds canvases that
            // are really on a panel: a rebuild detaches last build's and they take themselves back out. That
            // makes it order-independent — it does not matter which section builds first, how many previews
            // one rebuild makes, or whether a card is folded — and it cannot leak a stale element for a
            // session's worth of rebuilds the way "add here, prune later" could.
            imgui.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (!_formationPreviews.Contains(imgui)) _formationPreviews.Add(imgui);
            });
            imgui.RegisterCallback<DetachFromPanelEvent>(_ => _formationPreviews.Remove(imgui));
            return stage;
        }

        void DrawFormationPreview(Rect rect, SpawnFormation f)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            rect = new Rect(0f, 0f, rect.width, rect.height);
            if (rect.width <= 1f || rect.height <= 1f) return;

            EditorGUI.DrawRect(rect, new Color(0.11f, 0.11f, 0.12f, 1f));

            // The preview resolves through the SAME method that fires the burst, so "what you see" cannot drift
            // from "what fires". A fixed placeholder seed stands in for an authored seed of 0 ("reroll every
            // play") so the preview stays STABLE across repaints instead of reshuffling under the mouse.
            f.Resolve(Vector3.zero, _formationPreviewScratch, f.seed != 0 ? f.seed : 12345);
            var pts = _formationPreviewScratch;

            Vector2 centre = rect.center;
            if (pts.Count == 0)
            {
                Handles.BeginGUI();
                Handles.color = FormationOriginColor;
                Handles.DrawSolidDisc(new Vector3(centre.x, centre.y, 0f), Vector3.forward, 3f);
                Handles.EndGUI();
                return;
            }

            float maxExtent = 0.001f;
            for (int i = 0; i < pts.Count; i++)
                maxExtent = Mathf.Max(maxExtent, new Vector2(pts[i].Position.x, pts[i].Position.y).magnitude);
            float margin = 16f;
            float scale = (Mathf.Min(rect.width, rect.height) * 0.5f - margin) / maxExtent;

            float maxDelay = 0.0001f;
            for (int i = 0; i < pts.Count; i++) maxDelay = Mathf.Max(maxDelay, pts[i].Delay);

            Handles.BeginGUI();
            var prevColor = Handles.color;

            // A faint shape reference — a full circle for Ring (so a partial arc reads against the whole it's
            // cut from), a straight spine for Line — drawn UNDER the dots.
            Handles.color = FormationOutlineColor;
            if (f.shape == FormationShape.Ring && f.radius * scale > 1f)
                Handles.DrawWireDisc(new Vector3(centre.x, centre.y, 0f), Vector3.forward, f.radius * scale);
            else if (f.shape == FormationShape.Line && pts.Count > 1)
            {
                float rad = f.angleDeg * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(rad), -Mathf.Sin(rad)) * (f.length * 0.5f * scale);
                Handles.DrawLine(new Vector3(centre.x - dir.x, centre.y - dir.y, 0f),
                    new Vector3(centre.x + dir.x, centre.y + dir.y, 0f));
            }

            Handles.color = FormationOriginColor;
            Handles.DrawSolidDisc(new Vector3(centre.x, centre.y, 0f), Vector3.forward, 2.5f);

            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                // Screen space is y-DOWN, world/local space here is y-UP — flip Y once, at the point of drawing.
                Vector2 screen = centre + new Vector2(p.Position.x, -p.Position.y) * scale;
                float t = p.Delay / maxDelay;
                Handles.color = Color.Lerp(FormationDotFirst, FormationDotLast, t);
                Handles.DrawSolidDisc(new Vector3(screen.x, screen.y, 0f), Vector3.forward, 5f);
            }

            Handles.color = prevColor;
            Handles.EndGUI();
        }
    }
}
