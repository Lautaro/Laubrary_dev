// ChunkWindow.Slicer — the FRAGMENT SLICER section (Chunks 2.0 module #4, AgentHQ T-0036).
//
// A switchable module section in the PyreWindow.CherryFraming shape: Z.Section + SetHeaderToggle, and while
// the toggle is off NOTHING below it is built — a plain debris spec keeps reading as the short window it
// always was.
//
// WHY THIS SECTION DRAWS ITS OWN PREVIEW. A cut is a visual thing, so it has to be shown. ChunkWindow.Preview
// already renders a slicing stage, but it is wired end-to-end to SampledChunkSprites (fixed six sample cells,
// its own out-cutRect outlines, its own seed) and offers no hook a second cutter could render into — hosting
// the fragment cut there would mean editing that file, which this task does not own. So the fragments get a
// small self-contained canvas here instead: the sanctioned bespoke-painting island, disposed on detach so a
// rebuild never leaks its compose texture.
//
// The preview is deterministic. With a seed pinned it shows exactly the cut the runtime will make; with
// seed 0 ("reroll every play") it shows one representative cut from an editor-only seed that Reshuffle bumps,
// so dialling Pieces or Min Area shows the DIAL's effect rather than a fresh roll each keystroke.
using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        // Only the picker MECHANICS live here (click / right-click); what the field is FOR is composed per
        // state at the call site, because it has to say whether this picker is currently winning.
        const string SlicerVisualPickerTip =
            "Any asset that can hand over frames — a Zoe, a Pyre blast, anything implementing IChunkAnimation. " +
            "Click to pick, right-click for New / Edit / Clear.";

        const float SlicerStage = 200f;         // square preview canvas, matching the sampled-debris stage
        const float SlicerGhostAlpha = 0.22f;   // how faintly the un-cut original shows behind the pieces
        const float SlicerPieceTint = 0.5f;     // how far each piece is pushed toward its identifying hue

        // PREVIEW-ONLY state (window-scoped, never written into the ChunkSpec asset).
        int _slicerPreviewSeed = 20260824;      // stands in while the module's own seed is 0 (reroll-every-play)
        IMGUIContainer _slicerImage;
        Label _slicerHint;
        Texture2D _slicerTex;                   // pooled compose target, HideAndDontSave, killed on detach

        // ── mutation helpers ────────────────────────────────────────────────────────────────────────────
        // Every data edit still goes through ChunkWindow.Dial (which owns the Undo.RecordObject contract);
        // this only adds the fragment preview refresh, which Dial has no way to know about.
        void SlicerDial(string undoLabel, System.Action apply)
        {
            Dial(undoLabel, apply);
            RefreshSlicerPreview();
        }

        VisualElement SlicerNum(string label, string tooltip, float value, System.Action<float> set,
                                float width = Num)
            => Z.Field(label, tooltip, Z.Float(value, tooltip, v => SlicerDial(label, () => set(v)), width));

        VisualElement SlicerInt(string label, string tooltip, int value, System.Action<int> set,
                                float width = Num)
            => Z.Field(label, tooltip, Z.Int(value, tooltip, v => SlicerDial(label, () => set(v)), width));

        void BuildFragmentSlicer(VisualElement root, ChunkSpec c)
        {
            var m = c != null ? c.fragmentSlicer : null;
            if (m == null) return;   // ChunkSpec.OnValidate re-creates it; nothing sane to draw meanwhile

            _slicerImage = null;
            _slicerHint = null;

            const string sectionTip =
                "Cuts one picture — a plain sprite, or the first frame of a Zoe / Pyre blast — into a few " +
                "large pieces that still read as parts of the original thing: a character or a hull splitting " +
                "into three chunks, not confetti. Each piece starts where it sat in the picture " +
                "and flies out from there. Works on its own: nothing else in this Chunk has to be switched on — " +
                "with no Layer Stack declared the pieces just draw at the emitter's own order, in cut sequence.";
            var s = Z.Section("Fragment Slicer", sectionTip, "chunks.slicer");
            s.SetHeaderToggle(m.enabled,
                "Cut the source picture into a few large, still-recognisable pieces that fly apart.",
                v => DialAndRebuild("Fragment slicer", () => m.enabled = v));
            root.Add(s);
            if (!m.enabled) return;   // off → build no body at all

            // ── what gets cut ───────────────────────────────────────────────────────────────────────────
            // TWO ways in, drawn in precedence order: an animated Source Visual (a Zoe, a Pyre blast) wins
            // whenever it can hand over a frame, and the plain Source Sprite is the fallback beneath it.
            // Precedence is stated the way this window already states it in Pyre Spawn (single blast vs pool):
            // the LOSING field says so in its own tooltip and names the way to hand control back, so the two
            // fields never look equal while one silently beats the other. Both are always present — the empty
            // state offers both ways in rather than hiding one (the preview's own hint says so out loud).
            var resolvedSource = m.ResolveSource();
            bool visualWins = m.sourceVisual != null && resolvedSource != null && resolvedSource != m.source;

            string visualTip = visualWins
                ? "The animated content being fractured: its FIRST frame (" + resolvedSource.name + ") is the " +
                  "picture that gets cut, so a character comes apart in the pose it starts in. This is what " +
                  "the cut below is showing — clear it to fall back to the Source Sprite."
                : m.sourceVisual != null
                    ? "This asset hands over no usable frame yet (nothing built, or an empty animation), so the " +
                      "Source Sprite below is being cut instead. Pick a Zoe, a Pyre blast, or anything else " +
                      "implementing IChunkAnimation to fracture the animated art itself."
                    : "Animated content to fracture — a Zoe, a Pyre blast, anything implementing " +
                      "IChunkAnimation. Its FIRST frame is the picture that gets cut, so a character comes " +
                      "apart in the pose it starts in. Set this and it wins over the Source Sprite below.";
            s.Add(Z.Field("Source Visual", visualTip,
                LauAssetElement.Build(m.sourceVisual,
                    picked => DialAndRebuild("Fragment source visual", () => m.sourceVisual = picked),
                    typeof(IChunkAnimation), _visualThumbs, c.name, "Assets/Chunks/AnimationSources",
                    SlicerVisualPickerTip)));

            string srcTip = visualWins
                ? "The plain sprite that gets cut — currently OVERRIDDEN by the Source Visual above, which is " +
                  "handing over its own first frame instead. Clear that picker to cut this sprite again."
                : "The plain sprite that gets cut into fragments, used when no Source Visual is set. Leave both " +
                  "empty to use the Chunk's own Sample Source instead. Its texture needs Read/Write Enabled to " +
                  "be cut at all.";
            s.Add(Z.Field("Source Sprite", srcTip,
                Z.Object<Sprite>(m.source, srcTip, v => SlicerDial("Fragment source", () => m.source = v), 200f)));

            s.Add(Z.Row(
                Z.MicroSlider("Pieces", m.pieceCount, FragmentCutter.MinPieces, 10f,
                    "How many pieces the sprite is cut into. 2–6 is what this is built for: few enough that " +
                    "each piece still reads as a part of the original.",
                    v => SlicerDial("Fragment pieces", () => m.pieceCount = Mathf.RoundToInt(v)),
                    Wide, showValue: true, decimals: 0),
                Z.HSpace(),
                SlicerInt("Min Area px", "Smallest piece, in source pixels. A cell below this is merged into " +
                    "its nearest neighbour instead of becoming a 1px 'fragment' nobody can see.",
                    m.minPieceAreaPx, v => m.minPieceAreaPx = Mathf.Max(1, v))));

            // Reshuffle reads for the CURRENT seed state: with a seed pinned it rerolls the SAVED cut; with
            // seed 0 ("reroll every play") there is no saved cut to change, so it only reshuffles what the
            // preview stands in with. The wording is re-composed whenever the seed changes rather than by
            // rebuilding the panel — a rebuild per keystroke would tear the field out from under the typing.
            var reshuffle = Z.Button("Reshuffle", ReshuffleTip(m.seed), () =>
            {
                if (m.seed != 0) SlicerDial("Fragment seed", () => m.seed = Random.Range(1, int.MaxValue));
                else { _slicerPreviewSeed = unchecked(_slicerPreviewSeed * 48271 + 1); RefreshSlicerPreview(); }
            });
            reshuffle.style.width = 84f;

            const string seedTip = "Fixes which pieces the cut produces so every play is identical. 0 = reroll every time.";
            var seedField = Z.Int(m.seed, seedTip, v =>
            {
                SlicerDial("Fragment seed", () => m.seed = v);
                reshuffle.tooltip = ReshuffleTip(v);
            }, Num);
            s.Add(Z.Row(Z.Field("Seed", seedTip, seedField), Z.HSpace(), reshuffle));

            s.Add(Z.VSpace(4f));

            // ── how it flies ────────────────────────────────────────────────────────────────────────────
            // Every flight dial here is a SHORT control (a Z.MinMax is a slider flanked by its own two numeric
            // fields), so they share rows instead of claiming one full-width row each — the space-economy
            // rule. The ranges' sliders are a little narrower than a solo range would be so a pair still sits
            // comfortably in a half-screen pane.
            const string speedTip = "Initial launch speed range, world units per second. Each piece picks its own.";
            const string spinTip = "Spin range, degrees per second. Each piece picks its own rate and direction.";
            s.Add(Z.Row(
                Z.Field("Speed", speedTip,
                    Z.MinMax(m.speedMin, m.speedMax, 0f, 20f, speedTip,
                        (lo, hi) => SlicerDial("Fragment speed", () => { m.speedMin = lo; m.speedMax = hi; }),
                        120f)),
                Z.HSpace(),
                Z.Field("Spin °/s", spinTip,
                    Z.MinMax(m.angularSpeedMin, m.angularSpeedMax, 0f, 720f, spinTip,
                        (lo, hi) => SlicerDial("Fragment spin", () => { m.angularSpeedMin = lo; m.angularSpeedMax = hi; }),
                        120f))));

            const string lifeTip = "Lifetime range, seconds. Each piece picks its own, then fades out on the curve below.";
            s.Add(Z.Row(
                Z.Field("Life s", lifeTip,
                    Z.MinMax(m.lifeMin, m.lifeMax, 0.05f, 8f, lifeTip,
                        (lo, hi) => SlicerDial("Fragment life", () => { m.lifeMin = lo; m.lifeMax = hi; }),
                        120f)),
                Z.HSpace(),
                SlicerNum("Gravity", "Downward acceleration. Higher = snappier arcs that fall fast.",
                    m.gravity, v => m.gravity = Mathf.Max(0f, v)),
                Z.HSpace(),
                Z.MicroSlider("Drag", m.drag, 0f, 5f,
                    "Air resistance. 0 = none, ~1 = noticeable, ~3 = soupy.",
                    v => SlicerDial("Fragment drag", () => m.drag = v), Wide, showValue: true)));

            // The tooltip is composed per-state: with Burst Aim ON there is no Direction ° control beside it to
            // refer to, so the wording must not point at one that isn't there.
            var aim = Z.Row(
                Z.Toggle("Burst Aim", m.useBurstDirection
                        ? "On: the pieces are aimed along the burst's own direction. Turn off to aim them at a "
                          + "fixed Direction ° instead."
                        : "Aim the pieces along the burst's own direction instead of the fixed Direction ° beside it.",
                    m.useBurstDirection, v => DialAndRebuild("Fragment burst aim", () => m.useBurstDirection = v)));
            if (!m.useBurstDirection)
            {
                aim.Add(Z.HSpace());
                aim.Add(Z.MicroSlider("Direction °", m.directionDeg, 0f, 360f,
                    "Centre direction of the cone. 0 = right, 90 = up.",
                    v => SlicerDial("Fragment direction", () => m.directionDeg = v), Wide,
                    showValue: true, decimals: 0));
            }
            aim.Add(Z.HSpace());
            aim.Add(Z.MicroSlider("Spread °", m.spreadDeg, 0f, 180f,
                "Cone half-angle around the aim. 180 = each piece flies straight out from where it sat in the " +
                "picture (the natural 'it came apart' look); 0 = every piece travels the same way.",
                v => SlicerDial("Fragment spread", () => m.spreadDeg = v), Wide, showValue: true));
            s.Add(aim);

            const string alphaTip = "Opacity across a fragment's life, left (spawn) to right (death).";
            s.Add(Z.Field("Alpha over life", alphaTip,
                Z.Curve(m.alphaOverLife, alphaTip,
                    v => SlicerDial("Fragment alpha over life", () => m.alphaOverLife = v))));

            // ── which layer slot ────────────────────────────────────────────────────────────────────────
            // A slot NAME is picked, never typed (the project's fundamental "never type a reference string"
            // rule): the owner of the names is the Chunk's own layer stack, so the options come from there.
            // With no stack declared there is nothing to pick from and nothing to pick FOR — draw order falls
            // back to the emitter's flat order — so the row is simply absent rather than degrading to a text
            // field, and the section tooltip carries the explanation.
            BuildSlicerLayerPick(s, c, m);

            s.Add(Z.VSpace(4f));
            s.Add(BuildSlicerStage());
            RefreshSlicerPreview();
        }

        static string ReshuffleTip(int seed) => seed != 0
            ? "Pick a different saved cut: rolls a new seed, so the pieces change permanently for this Chunk."
            : "Show a different cut here. The seed is 0, so every play rerolls anyway — this only changes which "
              + "cut the preview stands in with.";

        void BuildSlicerLayerPick(VisualElement s, ChunkSpec c, FragmentSlicerModule m)
        {
            var stack = c.layers;
            if (stack == null || stack.Count == 0) return;

            var options = new System.Collections.Generic.List<string>(stack.layers);
            int index = options.IndexOf(m.layerName);
            if (index < 0)
            {
                // A name left behind by a rename in the layer stack: show it, selected, rather than silently
                // snapping the fragments onto somebody else's slot.
                options.Add(string.IsNullOrEmpty(m.layerName) ? "(none)" : m.layerName);
                index = options.Count - 1;
            }

            const string layerTip = "Which slot of this Chunk's layer stack the fragments draw in. Several " +
                "fragments sub-order inside the one slot, in cut order.";
            s.Add(Z.Field("Layer", layerTip,
                Z.MiniRadio(index, options.ToArray(), layerTip,
                    v => Dial("Fragment layer", () => m.layerName = options[v]), wrap: true)));
        }

        // ── the bespoke cut canvas (sanctioned raw-painting island) ─────────────────────────────────────
        VisualElement BuildSlicerStage()
        {
            var stage = new VisualElement();
            stage.style.width = SlicerStage;
            stage.style.height = SlicerStage;
            stage.style.flexShrink = 0f;
            stage.style.alignItems = Align.Center;
            stage.style.justifyContent = Justify.Center;
            stage.style.backgroundColor = new Color(0.11f, 0.11f, 0.12f, 1f);
            var border = new Color(0f, 0f, 0f, 0.5f);
            stage.style.borderTopWidth = 1f; stage.style.borderBottomWidth = 1f;
            stage.style.borderLeftWidth = 1f; stage.style.borderRightWidth = 1f;
            stage.style.borderTopColor = border; stage.style.borderBottomColor = border;
            stage.style.borderLeftColor = border; stage.style.borderRightColor = border;
            stage.tooltip = "The cut, on whichever source is winning: each piece is drawn in its own colour over a " +
                "faint " +
                "ghost of the original, so you can see where the seams fall and that the pieces tile it exactly.";

            // An IMGUI island painting through ZuiPixel, NOT a UITK Image on ScaleToFit. ScaleToFit blows the
            // buffer up by whatever fraction happens to fit the box (196pt over a 15px cut is 13.07x), and a
            // fractional zoom rasterises a 1px seam as a ragged 13-or-14px band — which is precisely the thing
            // this stage exists to let you judge. ZuiPixel snaps to a whole-number zoom on a whole device
            // pixel instead. The same fault was found and fixed on the Cut Preview stage; this is its twin.
            var img = new IMGUIContainer(DrawSlicerStage);
            img.style.width = SlicerStage - 4f;
            img.style.height = SlicerStage - 4f;
            stage.Add(img);
            _slicerImage = img;

            var hint = new Label { pickingMode = PickingMode.Ignore };
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            hint.style.maxWidth = SlicerStage - 20f;
            stage.Add(hint);
            _slicerHint = hint;

            // The compose texture belongs to THIS element, not to the window: a rebuild detaches the old stage
            // and that is exactly when its texture must go. Captured per-element so a rebuild can never have
            // the outgoing stage destroy the incoming one's texture.
            stage.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                // Null the field BEFORE destroying, so a repaint that lands between the two cannot paint from
                // a destroyed texture — the island reads _slicerTex on every repaint now, not a cached image.
                var t = _slicerTex;
                _slicerTex = null;
                if (ReferenceEquals(_slicerImage, img)) _slicerImage = null;
                if (t != null) DestroyImmediate(t);
            });

            // Hand the previous texture entirely to the outgoing stage's own detach callback and start this
            // stage with none. Without this, a rebuild whose detach has not fired yet would let EnsureSlicerTex
            // REUSE the old texture — and the detach, arriving after, would destroy the one now on screen.
            _slicerTex = null;
            return stage;
        }

        /// Re-cut and re-compose the fragment preview. Cheap enough (one pixel read + one partition over a
        /// sprite-sized buffer) that every dial calls it, so the cut tracks the dials live.
        void RefreshSlicerPreview()
        {
            if (_slicerImage == null || _slicerHint == null) return;   // section not built (or module off)

            var c = Spec;
            var m = c != null ? c.fragmentSlicer : null;
            if (m == null) { SlicerHint("No Chunk selected."); return; }

            // ResolveSource() — never m.source — so the preview shows what the RUNTIME will cut: the Source
            // Visual's own first frame when one is picked, the plain sprite otherwise. Picking a Zoe therefore
            // shows that character's own pieces here, immediately.
            var resolved = m.ResolveSource();
            var src = resolved != null ? resolved : c.sampleSource;
            if (src == null)
            {
                // The honest empty state, naming BOTH ways in rather than only the sprite one.
                SlicerHint("Nothing to cut yet. Pick a Source Visual above (a Zoe, a Pyre blast) or a Source " +
                           "Sprite — or give the Chunk a Sample Source.");
                return;
            }
            if (src.texture == null) { SlicerHint("This sprite has no texture."); return; }
            if (!src.texture.isReadable) { SlicerHint("Enable Read/Write on this sprite's import settings to preview."); return; }

            var px = FragmentCutter.ReadSpritePixels(src, out int W, out int H);
            if (px == null) { SlicerHint("Could not read this sprite's pixels to preview."); return; }

            // cache:false — a preview key (especially the stand-in seed) would never repeat, so caching it
            // would only push real, seeded runtime cuts out of a deliberately small store.
            int seed = m.seed != 0 ? m.seed : _slicerPreviewSeed;
            var pieces = FragmentCutter.Cut(src, m.pieceCount, m.minPieceAreaPx, seed, cache: false);
            if (pieces == null || pieces.Count == 0) { SlicerHint("Nothing opaque in this sprite to cut."); return; }

            // A faint ghost of the whole original, so the pieces read as parts OF something.
            var canvas = new Color32[W * H];
            for (int i = 0; i < px.Length; i++)
            {
                var p = px[i];
                canvas[i] = new Color32(p.r, p.g, p.b, (byte)(p.a * SlicerGhostAlpha));
            }

            for (int k = 0; k < pieces.Count; k++)
            {
                var piece = pieces[k];
                var hue = Color.HSVToRGB(pieces.Count <= 1 ? 0.08f : k / (float)pieces.Count, 0.85f, 1f);
                for (int y = 0; y < piece.height; y++)
                    for (int x = 0; x < piece.width; x++)
                    {
                        var sp = piece.pixels[y * piece.width + x];
                        if (sp.a == 0) continue;
                        int cx = piece.rect.x + x, cy = piece.rect.y + y;
                        if (cx < 0 || cy < 0 || cx >= W || cy >= H) continue;
                        var blended = Color.Lerp(new Color32(sp.r, sp.g, sp.b, 255), hue, SlicerPieceTint);
                        canvas[cy * W + cx] = new Color32(
                            (byte)(blended.r * 255f), (byte)(blended.g * 255f), (byte)(blended.b * 255f), sp.a);
                    }
            }

            EnsureSlicerTex(W, H);
            _slicerTex.SetPixels32(canvas);
            _slicerTex.Apply(false);
            _slicerImage.MarkDirtyRepaint();
            _slicerImage.style.display = DisplayStyle.Flex;
            _slicerHint.style.display = DisplayStyle.None;
        }

        /// The stage's paint. Local rect, because ZuiPixel adds the island's own panel position itself, and
        /// where the island sits in the window is half of whether a local coordinate lands on a whole device
        /// pixel. Repaint only — GUI.DrawTexture is a repaint-time API. Painting straight from `_slicerTex`
        /// rather than from an assigned `image` is what lets the hint path simply stop drawing.
        void DrawSlicerStage()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (_slicerImage == null || _slicerTex == null) return;
            var r = _slicerImage.contentRect;
            if (float.IsNaN(r.width) || r.width < 1f || r.height < 1f) return;
            Z.DrawPixels(new Rect(0f, 0f, r.width, r.height), _slicerImage, _slicerTex);
        }

        void SlicerHint(string message)
        {
            if (_slicerHint == null) return;
            _slicerHint.text = message;
            _slicerHint.style.display = DisplayStyle.Flex;
            if (_slicerImage != null)
            {
                _slicerImage.style.display = DisplayStyle.None;
                _slicerImage.MarkDirtyRepaint();
            }
        }

        // Pooled compose texture — resized only when the source geometry changes, Point-filtered so pixel art
        // stays crisp, never saved. Released by the stage's own DetachFromPanel callback.
        void EnsureSlicerTex(int W, int H)
        {
            if (_slicerTex != null && _slicerTex.width == W && _slicerTex.height == H) return;
            if (_slicerTex != null) DestroyImmediate(_slicerTex);
            _slicerTex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }
    }
}
