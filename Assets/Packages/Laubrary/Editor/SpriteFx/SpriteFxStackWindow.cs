// SpriteFxStackWindow — the dedicated browse / create / tag authoring window for a SpriteFxSpec (the "SpriteFx
// Stack" asset). Same AssetKit base every other Laubrary tool uses (ZuiAssetWindow<T>: assign / New / Duplicate /
// Rename / Delete / thumbnail browser for free), so this window only has to lay out the per-asset editor.
//
// It hosts the reusable SpriteFxStackView control (slice 3) for the effect stack itself, a live input-sprite
// Preview (pick a sprite, scrub the loop's timeline bar, Play) that reuses the runtime SpriteFxFilter.Apply
// so what you see is what plays, and a small Hashing section for the seed.
//
// ONE CLOCK. The timebase used to be spread over unrelated dials in two sections, joined by a 0→1 "Life"
// scrub slider that could not express any of them — so reading "what does one loop actually look like" was
// guesswork, and there was no way to scrub the switched-off stretch between loops at all. Duration and Idle
// gap now sit in ONE row directly under a ZuiTimeline bar that draws the loop they describe — Active | Gap,
// to scale — and the scrub, the playhead and Play all run on that single seconds clock, resolved in exactly
// one place (ResolveAt) so scrubbing and playback cannot disagree.
//
// The bar shows the REAL behaviour plus one deliberate fiction. Active is what the effect does in game; Gap
// is the preview's own invention, the quiet stretch where the stack is switched off, which is what the game
// is left with once a pass finishes. Nothing else is invented — to see the finished state, scrub to the end
// of Active.
//
// Every dial routes through Dial(...) (Undo.RecordObject BEFORE the mutation, then SetDirty), matching ChunkWindow
// so a whole session's edits don't coalesce into one Undo step.
using System.Collections.Generic;
using Laubrary.AssetKit.Editor;
using Laubrary.SpriteFx;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.SpriteFx.Editor
{
    /// <summary>
    /// Authoring window for a <see cref="SpriteFxSpec"/> — the reusable colour/mask "SpriteFx Stack" asset.
    /// Embeds the host-agnostic <see cref="SpriteFxStackView"/> for the effect stack, and adds a preview whose
    /// timeline bar and the lengths that shape it are the window's own.
    /// </summary>
    public class SpriteFxStackWindow : ZuiAssetWindow<SpriteFxSpec>
    {
        [MenuItem("Laubrary/SpriteFx Stacks")]
        public static void Open() => GetWindow<SpriteFxStackWindow>("SpriteFx Stacks");

        /// Same entry-point shape as ChunkWindow.OpenFor / PyreWindow.OpenFor — lets a LauAssetField's Edit
        /// button (and the shared LauAssetEditors registry) jump straight into this SpriteFxSpec's own editor.
        public static void OpenFor(SpriteFxSpec spec)
        {
            var w = GetWindow<SpriteFxStackWindow>("SpriteFx Stacks");
            if (spec != null) w.SetAsset(spec);
        }

        protected override string TypeLabel => "SpriteFx Stack";
        protected override string NewAssetName => "New SpriteFx Stack";
        protected override string DefaultFolder => "Assets/SpriteFx";

        const float Num = 70f;
        const float Wide = 150f;
        const float PreviewBox = 200f;   // the scaled-up pixel-art preview stage, ~200px per slice-5 spec

        SpriteFxSpec Spec => Current;

        // ── PREVIEW-ONLY state (window-scoped, NEVER written into the SpriteFxSpec asset) ────────────────────
        // The input sprite the stack is applied to is a preview subject, not part of the recipe (Laubrary rule:
        // preview / Mirage-scoped config stays window-scoped). It is remembered across domain reloads in
        // EditorPrefs keyed by the spec's GUID — none of it touches the asset. None of these are [SerializeField]:
        // a domain reload should reset playback and re-load the remembered sprite from prefs, not carry live state.
        Sprite _previewSprite;              // the chosen input sprite (preview subject)
        SpriteFxPreviewSubject _subject;    // the Zoe event this stack is used by — re-resolved every rebuild
        Label _subjectLine;                 // permanently reserved line naming what is being previewed
        string _prevSpecGuid;               // which spec's remembered sprite is currently loaded into _previewSprite
        Texture2D _previewTex;              // pooled output texture we paint the filtered pixels into (Point-filtered)
        // ONE clock, in SECONDS along the whole loop (Active + Gap) — not a 0→1 progress, because the gap has
        // no progress to speak of. _scrubTime is where the playhead RESTS;
        // _playTime is the transient position while Play runs, so stopping returns to the scrub untouched.
        float _scrubTime;
        float _playTime;
        int _previewFrame;                  // frame counter advanced across play ticks (for hashing effects)
        bool _previewPlaying;
        bool _previewReversed;              // preview the stack against a reversed clock (the OnceReversed binding)
        bool _updateHooked;                 // guards the EditorApplication.update subscription
        double _lastTickTime;
        // What the bar's bands were last built from, so a repaint only happens when a length actually moved
        // (the hosted subject's length can change under us between rebuilds). -1 = "not built yet".
        float _bandActive = -1f, _bandGap = -1f;

        // live element refs (re-created every BuildPreview; nulled in OnBeforeRebuild)
        UnityEngine.UIElements.Image _previewImage;
        Label _previewHint;
        ZuiTimeline _timeline;         // the loop's scrub bar — Active | Gap, drawn to scale
        Button _playButton;
        VisualElement _previewStage;   // fixed reserved area — keeps the panel from jumping
        VisualElement _frameBox;   // drawn at the SPRITE's size; its border marks the sprite's own boundary
        VisualElement _overlayStrip;   // permanently reserved row of per-effect overlay toggles — added once, refilled

        // Which effects in the current stack offer a preview overlay, and what their toggles are. Re-collected
        // whenever the stack could have changed; read by every render. NOT a live query into the stack — the
        // entries carry the disambiguated label the strip was built with, and name the effect each toggle owns.
        List<SpriteFxPreviewOverlays.Entry> _overlayEntries = new List<SpriteFxPreviewOverlays.Entry>();

        const string PrevSpritePrefKey = "Laubrary.SpriteFx.Preview.Sprite.";

        // ── mutation helper (the Undo contract every dial routes through) ────────────────────
        void Dial(string undoLabel, System.Action apply)
        {
            var s = Spec;
            if (s == null) return;
            Undo.RecordObject(s, undoLabel);
            apply();
            EditorUtility.SetDirty(s);
        }

        // ── section toggle bar (T-0084) ───────────────────────────────────────────────────────
        // The roster the shared ZuiSectionToggleBar addresses, rebuilt from scratch on every BuildAsset.
        // Populated by Unit() as each section is built — same shape as ChunkWindow's adoption.
        readonly List<(string label, ZuiSection section)> _barUnits = new List<(string label, ZuiSection section)>();

        // Tallest layout the bar has taken at a given width, remembered for the window's lifetime so the
        // bar's own Sections↔Toggle Bar mode switch can never shrink the chrome above the workspace.
        float _barReservedW, _barReservedH;

        /// Run one section builder and register whatever top-level ZuiSection it added under `label`, so the
        /// toggle bar can address it. Both BuildPreview and BuildStack add exactly ONE section each — see
        /// ChunkWindow.Unit for the full rationale (reading the section back off the container rather than
        /// having each builder return it).
        void Unit(VisualElement body, SpriteFxSpec s, string label, System.Action<VisualElement, SpriteFxSpec> build)
        {
            int before = body.childCount;
            build(body, s);
            for (int i = before; i < body.childCount; i++)
                if (body[i] is ZuiSection sec) { _barUnits.Add((label, sec)); return; }
        }

        /// Stable-workspace rule: chrome ABOVE the workspace must never change the geometry of what is below
        /// it. See ChunkWindow.ReserveBarHeight — copied verbatim, no SpriteFx-specific changes needed.
        void ReserveBarHeight(VisualElement barHost, VisualElement bar)
        {
            bar.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float w = bar.resolvedStyle.width, h = bar.resolvedStyle.height;
                if (float.IsNaN(w) || float.IsNaN(h) || h <= 0f) return;
                if (Mathf.Abs(w - _barReservedW) > 0.5f) { _barReservedW = w; _barReservedH = 0f; }
                if (h <= _barReservedH + 0.5f) return;
                _barReservedH = h;
                barHost.style.minHeight = h;
            });
        }

        protected override void BuildAsset(VisualElement root, SpriteFxSpec spec)
        {
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;

            // Who hosts this stack is resolved BEFORE anything is drawn, because the answer decides what this
            // window is even allowed to own: on a Zoe event the duration and the visual are the event's, and
            // the preview's Duration must render as a stated fact rather than as a dial.
            EnsureSubject(spec);

            // T-0084 — the section toggle bar rides at the very top of the per-asset UI, spanning the full
            // window width (same placement as Pyre/Chunks). Its host is added FIRST (empty, so it reserves
            // space before anything below it builds) and filled LAST, once both sections below exist. Preview
            // stays included as a toggle-bar entry even though it is deliberately pinned outside the scroller
            // (see the BuildPreview call below) — the bar defaults to Sections mode, where nothing about that
            // pinning changes; a user only hides Preview by explicitly switching into Toggle Bar mode and
            // clicking it off, which is their own informed choice.
            var barHost = new VisualElement();
            barHost.style.flexShrink = 0f;
            if (_barReservedH > 0f) barHost.style.minHeight = _barReservedH;   // space reserved before anything paints
            root.Add(barHost);

            _barUnits.Clear();

            // The preview is PINNED above the scroller, not inside it. It is the thing being judged, so
            // scrolling a stack of a dozen effects must never take it off screen — which is exactly what
            // happened while all three sections shared one ScrollView.
            var pinned = new VisualElement();
            pinned.style.flexShrink = 0f;
            Unit(pinned, spec, "Preview", BuildPreview);
            root.Add(pinned);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            var body = scroll.contentContainer;

            Unit(body, spec, "Stack", BuildStack);

            root.Add(scroll);

            var bar = new ZuiSectionToggleBar("SpriteFx", _barUnits.ToArray());
            barHost.Add(bar);
            ReserveBarHeight(barHost, bar);
        }

        // ── who owns the timebase ───────────────────────────────────────────────────────────────────────────
        /// Which event uses this stack — resolved FRESH on every rebuild, never cached past one.
        ///
        /// The answer lives in project data edited somewhere else entirely: you wire the stack onto a Zoe
        /// event in the Zoe window and come back here. A remembered answer goes stale the moment that
        /// happens, and a remembered MISS is the worst of them — this window sat insisting the stack was on
        /// no event at all while the event pointed straight at it, and nothing short of a domain reload
        /// changed its mind. Resolving per rebuild costs one asset scan on a structural edit, which is
        /// cheaper than being wrong.
        void EnsureSubject(SpriteFxSpec spec)
        {
            // Load the remembered override sprite for THIS spec whenever the edited spec changes (incl. after
            // a domain reload, when _prevSpecGuid resets to null). On a plain rebuild (same spec) keep the pick.
            string guid = PreviewSpritePrefs.GuidOf(spec);
            if (_prevSpecGuid != guid)
            {
                _previewSprite = PreviewSpritePrefs.Load(PrevSpritePrefKey, guid);
                _prevSpecGuid = guid;
            }
            _subject = SpriteFxPreviewSubjects.Resolve(spec);
        }

        /// Coming back to this window is exactly when the answer is most likely to have just changed — the
        /// trip out was to go and hang this stack on an event. Rebuild only when it actually differs, so
        /// merely clicking the window never throws away scroll position or a fold.
        void OnFocus()
        {
            var spec = Spec;
            if (spec == null) return;
            var fresh = SpriteFxPreviewSubjects.Resolve(spec);
            if (Describes(fresh) == Describes(_subject)) return;
            _subject = fresh;
            Rebuild();
        }

        /// Everything about a subject this window's layout depends on, as one comparable string — a change in
        /// any of it means the panel has to be rebuilt, and a change in nothing else should not.
        static string Describes(SpriteFxPreviewSubject s)
            => s == null ? "" : $"{s.Label}|{s.Seconds}|{s.Fps}|{(s.Frames == null ? 0 : s.Frames.Length)}";

        /// True while this stack is being edited as part of an event that already fixed its length — the
        /// state in which this window stops owning duration, playback rate and the preview image.
        bool Hosted => _subject != null && _subject.OwnsTimebase;

        /// How long ONE play-through of the stack runs in the preview: the host event's length when there is
        /// a host, and only otherwise the stack's own fallback duration. This is the same precedence the
        /// runtime uses, which is what makes the preview honest.
        float PreviewSeconds(SpriteFxSpec spec)
            => Hosted ? _subject.Seconds : Mathf.Max(0.001f, spec != null ? spec.duration : 0.15f);

        // ── the loop clock ──────────────────────────────────────────────────────────────────────────────────
        // Two consecutive bands, and the bar under the preview draws exactly these:
        //   ACTIVE  the stack plays, life 0→1 — the real, in-game behaviour
        //   GAP     the stack is BYPASSED — the plain sprite, which is what the runtime is left with once a
        //           pass finishes and SpriteFxFilter.Restore puts the original sprite back
        // Gap is a viewing preference (EditorPrefs, shared across stacks); Active is the stack's own
        // Duration, or the host event's length when there is one.

        /// The ACTIVE band's length — never zero, because life = t / active divides by it.
        float ActiveSeconds(SpriteFxSpec spec) => Mathf.Max(0.001f, PreviewSeconds(spec));

        /// The whole loop, end to end. This is the bar's total and the clock's wrap point.
        float LoopSeconds(SpriteFxSpec spec)
            => ActiveSeconds(spec) + Mathf.Max(0f, IdleGap);

        /// <summary>Resolve ONE instant on the loop clock into what the stage must draw. The scrub and Play
        /// both go through this and nothing else, so the picture you scrub to and the picture Play shows at
        /// the same instant cannot disagree — which they could, and did, when the two ran on separate state.
        ///
        /// <paramref name="t"/> is CLAMPED, not wrapped: the far right of the bar has to mean "the end of the
        /// loop", and wrapping there would show the start instead. Playback wraps its own clock before it
        /// calls in.
        ///
        /// It hands back PROGRESS rather than life on purpose. Life is progress (the stack-wide remap that
        /// used to sit in between is gone — see SpriteFxSpec.SampleEnvelope), and RenderPreviewAt is the one
        /// place that turns progress into life, because it is also the place that inverts it for Reverse.
        /// Two definitions of life is exactly the drift this method exists to prevent. Progress also drives
        /// the underlying animation frame, which must keep running forward even when life runs backward.</summary>
        /// <param name="progress">0→1 through the ACTIVE band, reaching exactly 1 at its right edge; 0 during
        /// the gap, where the loop is back at rest waiting to fire again.</param>
        /// <param name="bypass">True only in the gap — apply no effects at all.</param>
        void ResolveAt(float t, out float progress, out bool bypass)
        {
            var spec = Spec;
            float active = ActiveSeconds(spec);
            float total = active + Mathf.Max(0f, IdleGap);
            t = Mathf.Clamp(t, 0f, total);

            // ACTIVE is INCLUSIVE of its own right edge, and the gap starts strictly after it. The boundary
            // instant is the one the user goes looking for — "scrub to the last part of active and see what
            // it ends up looking like" — so it has to read as the FINISHED effect (progress 1, stack applied),
            // not as the first instant of the switched-off gap. With no gap set, active IS the whole loop and
            // the bar's right edge means the same thing for the same reason.
            if (t <= active) { progress = Mathf.Min(1f, t / active); bypass = false; return; }   // ACTIVE
            progress = 0f; bypass = true;                                                        // GAP
        }

        void BuildStack(VisualElement root, SpriteFxSpec spec)
        {
            var s = Z.Section("Stack",
                "The colour / mask effects applied in order while the stack plays. Each effect's animatable " +
                "values are resolved at the current life every frame; drag the grip to reorder.");

            // The seed belongs HERE, at the head of the list it governs, not in a section of its own. It used
            // to share a "Timeline" section with Duration; Duration moved up beside the preview's timeline
            // bar, where the loop it lengthens is actually drawn, and a titled section left wrapping a lone
            // seed would be a box announcing one field — which the UI rules forbid, and rightly: the seed is
            // an attribute OF the effects below it, and reads as one sitting above them.
            const string seedTip = "Seed for any hashing effect below (LayerDissolve scatter, AlphaMask noise) " +
                "— the same seed always produces the same grain, so a dissolve is reproducible instead of " +
                "different on every play. Irrelevant for a plain Brightness / Tint flash.";
            s.Add(Z.Field("Seed", seedTip,
                Z.Int(spec.seed, seedTip, v => Dial("SpriteFx seed", () => spec.seed = v), Num)));
            s.Add(Z.VSpace());   // a boundary, so the seed does not read as the first row of the effect list

            var host = new SpriteFxStackView.Host
            {
                // Fires once per gesture, before the first mutation.
                OnBeforeChange = () => { var sp = Spec; if (sp != null) Undo.RecordObject(sp, "Edit SpriteFx Stack"); },
                // Fires after every value edit — mark the asset dirty (retained controls repaint themselves).
                // Repaint the stage on EVERY value change, not just on a structural one. With playback stopped
                // and Life parked somewhere, the whole point is to watch that frame respond as you drag — and
                // it was only marking the asset dirty, so the picture stayed at whatever it was before the
                // edit and tuning was done blind.
                OnChanged = () =>
                {
                    var sp = Spec;
                    if (sp != null) EditorUtility.SetDirty(sp);
                    // A value edit can change WHICH effects have something to mark on the picture (an effect
                    // could gate its overlay on any of its own dials), so the strip is re-collected here too —
                    // cheaply, and it only rebuilds itself when the set actually differs. Structural edits are
                    // already covered by Rebuild below; this closes the gap that route leaves open.
                    RefreshOverlays();
                    if (!_previewPlaying) RenderPreview();
                },
                // A structural change (add / remove / reorder / enable): the control already rebuilt its own
                // rows in place; re-run the whole panel so anything downstream stays in sync (safe — the
                // control's fold/curve state is keyed per effect instance and survives the rebuild).
                Rebuild = Rebuild,
                ControlWidth = Wide,
            };
            s.Add(SpriteFxStackView.Build(spec.modifiers, host));
            root.Add(s);
        }

        // ── PREVIEW (slice 5) ───────────────────────────────────────────────────────────────────────────────
        // A live WYSIWYG preview: pick an input sprite, scrub the timeline, and Play the stack applied to it.
        // It reuses the EXACT runtime routine (SpriteFxFilter.Apply, useBurst:false) so what the preview shows is
        // what plays. The input sprite is PREVIEW-ONLY (never stored on the asset) — see the field block above.
        void BuildPreview(VisualElement root, SpriteFxSpec spec)
        {
            var s = Z.Section("Preview",
                "The stack applied to the visual it will actually play on — scrub the timeline or press Play.");

            // 1) Input-sprite picker — ONLY when nothing else supplies a visual. On a Zoe event the event's own
            // clip IS the input, so a picker here would be a control whose value the preview ignores: the
            // subject already wins over it, and a dial that does nothing reads as a broken one.
            if (!Hosted)
            {
                const string spriteTip = "Preview the stack on THIS sprite. Used when the stack is not on a Zoe " +
                    "event, or that event resolves no frames. Preview-only, never part of the SpriteFx Stack " +
                    "asset — it is remembered per-asset just for authoring.";
                s.Add(Z.Field("Preview on", spriteTip,
                    Z.Object<Sprite>(_previewSprite, spriteTip, OnPickSprite, 200f)));
            }

            // Says what the stage is actually showing, and on whose clock. A PERMANENTLY reserved single line
            // whose text changes — never one that appears and disappears, which would shove the preview down
            // the moment a subject resolved and move the thing being looked at.
            _subjectLine = Z.Text("", ZuiText.Subtle,
                "Where the preview frames come from and how long the play-through runs. A stack used by a Zoe " +
                "event previews on that event's own visual, over that event's own length.");
            _subjectLine.style.height = 14f;
            _subjectLine.style.whiteSpace = WhiteSpace.NoWrap;
            _subjectLine.style.overflow = Overflow.Hidden;
            s.Add(_subjectLine);

            s.Add(Z.VSpace(4f));

            // 2) The preview stage — a bespoke pixel-art canvas island (sanctioned raw painting).
            //
            // TWO elements, and the split is the whole point. The STAGE is a fixed reserved area so the panel
            // never jumps when a stack gains or loses a margin. The FRAME inside it is drawn at exactly the
            // sprite's size and carries the border, so the border IS the sprite's own boundary — no overlay
            // needed, and the render underneath stays pixel-for-pixel what the game will draw.
            //
            // The image is positioned ABSOLUTELY within the frame. In a flex container an oversized child
            // just gets shrunk back to fit, which is what made a padded buffer squash the character again;
            // absolute placement lets it hang outside the border, which is exactly where the overflow goes.
            _previewStage = new VisualElement();
            _previewStage.style.width = PreviewBox;
            _previewStage.style.height = PreviewBox;
            _previewStage.style.flexShrink = 0f;
            _previewStage.style.alignItems = Align.Center;
            _previewStage.style.justifyContent = Justify.Center;
            _previewStage.tooltip = "The sprite with the current stack applied at the scrub position, scaled up " +
                "point-filtered (nearest-neighbour) so pixels stay crisp. The border marks the SPRITE's own " +
                "frame — anything an effect draws beyond it spills outside, exactly as it does in game.";

            _frameBox = new VisualElement();
            _frameBox.style.flexShrink = 0f;
            _frameBox.style.backgroundColor = new Color(0.11f, 0.11f, 0.12f, 1f);
            StageBorder(_frameBox);
            _previewStage.Add(_frameBox);

            _previewImage = new UnityEngine.UIElements.Image { scaleMode = ScaleMode.StretchToFill };
            _previewImage.style.position = Position.Absolute;
            _frameBox.Add(_previewImage);

            _previewHint = new Label { pickingMode = PickingMode.Ignore };
            _previewHint.style.whiteSpace = WhiteSpace.Normal;
            _previewHint.style.unityTextAlign = TextAnchor.MiddleCenter;
            _previewHint.style.maxWidth = PreviewBox - 20f;
            _previewStage.Add(_previewHint);


            // 3) Transport: Play/Stop plus the preview-only view toggles, packed beside the stage.
            _playButton = Z.Button(_previewPlaying ? "Stop" : "Play", PlayTooltip(), TogglePlay);
            _playButton.style.width = 60f;

            // Preview-only: the same stack against a reversed clock — what an attachment set to Once Reversed
            // shows. Nothing about the asset changes; this is how you check that one stack covers both
            // directions before hanging it on a departure event.
            var reverseToggle = Z.ToggleButton("Reverse",
                "Run the preview backwards (life 1→0), exactly as the Once Reversed playback binding does — so " +
                "one authored stack can be checked as both an arrival and a departure. The hashing grain still " +
                "counts forward, so a reversed dissolve un-dissolves through a different grain.",
                _previewReversed, on => { _previewReversed = on; RenderPreview(); });

            // Preview-only, like Reverse above: the per-effect diagnostic overlays. This window hardcodes NONE
            // of them — an effect that has something worth marking on the picture implements
            // ISpriteFxPreviewOverlay and its toggle appears here on its own, and disappears when the effect is
            // removed, disabled or configured into a mode with nothing to show. The row is permanently reserved
            // and empty when the stack offers nothing, so it can never shove the transport around by arriving.
            //
            // It goes on its own line under the Play/Reverse row rather than into it: that row's width is
            // already spoken for, and this one has to be free to hold several toggles for a stack with several
            // marked effects without wrapping the transport around.
            _overlayEntries = SpriteFxPreviewOverlays.Collect(spec != null ? spec.modifiers : null);
            _overlayStrip = SpriteFxPreviewOverlays.BuildStrip(_overlayEntries, RenderPreview);

            // The transport sits BESIDE the stage, not under it. The stage is a fixed 200pt square in a pane
            // that is realistically three times that wide, so a row underneath spent height to leave a large
            // empty rectangle to its right — and height is the scarce resource in a window whose whole point
            // is that the stack below stays reachable. Wraps back to stacked if the pane ever is that narrow.
            var controls = Z.Column(Z.Row(_playButton, reverseToggle), Z.VSpace(2f), _overlayStrip);
            controls.style.flexShrink = 1f;
            controls.style.minWidth = 0f;

            var stageRow = Z.Row(_previewStage, Z.HSpace(), controls);
            stageRow.style.flexWrap = Wrap.Wrap;
            stageRow.style.alignItems = Align.FlexStart;
            s.Add(stageRow);

            // 4) THE SCRUB, below the stage and FULL WIDTH. It goes under the preview rather than beside it
            // because a ruler needs length: the stage is a fixed 200pt square, and a bar squeezed into the
            // column next to it could not draw its bands to scale, let alone label them. This one control
            // answers "what does one loop actually look like", which separate numbers never could.
            _bandActive = _bandGap = -1f;   // a freshly built bar has no bands; force the first fill
            _timeline = Z.Timeline(_scrubTime, BarTooltip(), OnScrub);
            s.Add(Z.VSpace());
            s.Add(_timeline);

            // 5) The two lengths that shape the bar, in ONE row directly under it. They used to be dials in
            // different sections with nothing tying them together, which is the confusion this layout exists
            // to end — each now sits behind a chip in its own band's colour, so which stretch of the bar it
            // lengthens needs no explaining.
            //
            // WHO OWNS THE LENGTH. A stack is a shape over normalized life, not a schedule — its parameters
            // run 0→1 and mean nothing in seconds — so whatever plays it says how long that 0→1 takes. When
            // this stack is on a Zoe event the event already answered, and offering a Duration dial would be
            // offering a number the runtime ignores; it becomes a stated fact instead.
            //
            // (The stack-wide "Life remap" envelope and the "Step rate" own-clock that used to sit beside
            // Duration are GONE — see SpriteFxSpec.SampleEnvelope. Life is progress, and the only clock is
            // the host's. The remap in particular was a trapdoor: it sat between the host and every effect,
            // so a flattened one pinned life to a constant and silently collapsed every authored envelope in
            // the stack to a single value, which reads as "animation does not work" with nothing pointing at
            // the cause.)
            VisualElement durationControl;
            string durationTip;
            if (Hosted)
            {
                string hostedTip = $"Set by the event using this stack ({_subject.Label}), not here. A stack is a " +
                    "shape over its play-through, so the event it rides owns how long that takes — re-time the " +
                    "animation and this effect re-times with it. It is the ACTIVE band's length above.";
                durationTip = hostedTip;
                // Labelled "Duration (s)" like the dial it stands in for, and like the dial beside it — one
                // row of lengths should not name one of them differently just because this one is a stated
                // fact rather than a control. The unit moves out of the value for the same reason:
                // "Duration (s)   0.4 — from Death" instead of the label and the value both saying seconds.
                durationControl = Z.Field("Duration (s)", hostedTip,
                    Z.Text($"{_subject.Seconds:0.###} — from {_subject.Label}", ZuiText.Body, hostedTip));
            }
            else
            {
                const string durTip = "How long one play-through lasts, in seconds — the ACTIVE band above. Used " +
                    "only when nothing else says: put this stack on a Zoe event and that event's length wins " +
                    "instead. The one value in this row that IS saved into the stack.";
                durationTip = durTip;
                // Undo still goes through Dial (the asset-mutation contract); the band refresh rides after it
                // so the bar re-proportions on every drag increment rather than waiting for a rebuild.
                // A ZuiMicroSlider is a plain VisualElement, not a BaseField — it never sends a
                // ChangeEvent<float>, so a RegisterCallback here would silently never fire.
                durationControl = Z.MicroSlider("Duration (s)", spec.duration, 0.02f, 2f, durTip,
                    v => { Dial("SpriteFx duration", () => spec.duration = Mathf.Max(0.001f, v));
                           RefreshTimelineBands(); },
                    Wide, showValue: true);
            }

            // The rest between play-throughs. Preview-only and deliberately NOT on the asset: it is how you
            // want to WATCH the effect, not part of the effect. A real, stable range, so a slider rather than
            // a number — and shared across stacks, since a viewing habit belongs to no one of them.
            const string idleTip = "The GAP band above: how long the preview shows the PLAIN sprite with the stack " +
                "switched off before the loop starts again — what actually happens in game, where a finished pass " +
                "restores the original sprite. At 0 there is no gap and the effect loops straight back into " +
                "itself. Preview-only: never saved into the stack.";
            var idleSlider = Z.MicroSlider("Idle gap (s)", IdleGap, 0f, 2f, idleTip,
                v => { IdleGap = v; RefreshTimelineBands(); }, Wide, showValue: true);

            // Z.HSpace() at its own default, not a hand-picked number: these are two separate fields that
            // must not read as one run of dials, and the gap that marks a boundary is a sheet-wide value.
            var paramRow = Z.Row(Banded(ActiveBand, durationTip, durationControl), Z.HSpace(),
                                 Banded(GapBand, idleTip, idleSlider));
            paramRow.style.flexWrap = Wrap.Wrap;
            paramRow.style.alignItems = Align.Center;
            s.Add(Z.VSpace());
            s.Add(paramRow);

            root.Add(s);

            RefreshTimelineBands(rerender: false);   // bands first, so the scrub is clamped to a real total
            RenderPreview();                         // then paint the current scrub state
        }

        // The two bands' colours. Shared by the bar and by the chip in front of each band's own dial — the
        // chip is what ties "Idle gap (s)" to the dark stretch at the right of the bar without a word of
        // on-screen explanation. Active reads as the accent, gap as near-off, which is exactly what it is.
        static readonly Color ActiveBand = new Color(0.20f, 0.42f, 0.72f, 1f);
        static readonly Color GapBand = new Color(0.17f, 0.17f, 0.19f, 1f);

        /// <summary>The loop's shape in words. Assembled from the lengths that are ACTUALLY set rather than
        /// written out once, because a tooltip naming a band that is currently zero seconds long describes a
        /// picture the reader cannot find on screen — and a tooltip you can catch lying is worse than none.
        /// Re-run by RefreshTimelineBands, so dialling Idle gap to 0 rewrites the sentence with it.</summary>
        string LoopPhrase()
        {
            string head = Hosted
                ? $"the event's own {ActiveSeconds(Spec):0.###} s of stack"
                : "the stack's own Duration";
            return IdleGap > 0f
                ? head + ", then a gap with the stack switched off"
                : head + ", looping straight back into itself with no rest";
        }

        string PlayTooltip()
            => "Run the whole loop below — " + LoopPhrase() + " — over and over" +
               (Hosted ? ", on the event's own visual" : "") +
               ". Stop returns the playhead to where you left it.";

        string BarTooltip()
            => "One whole loop of the preview, drawn to scale: " + LoopPhrase() +
               ". Click or drag anywhere on it to scrub to that instant" +
               (IdleGap > 0f
                   ? " — in the gap band the stack is switched off entirely and you see the plain sprite, the " +
                     "same picture the game shows once a pass has finished."
                   : ". Raise Idle gap below to add the band where the stack is switched off entirely.");

        /// A dial with a small chip of its band's colour in front of it — a legend key, not decoration. The
        /// chip carries the dial's own tooltip, so hovering it explains the same thing the dial does (it is
        /// passed in rather than read off the dial: Z.Field puts the tooltip on its label and control, not on
        /// the wrapper it returns, so reading `dial.tooltip` would leave the hosted case's chip bare).
        static VisualElement Banded(Color band, string tooltip, VisualElement dial)
        {
            var chip = new VisualElement { tooltip = tooltip };
            chip.style.width = 8f;
            chip.style.height = 14f;
            chip.style.flexShrink = 0f;
            chip.style.backgroundColor = band;
            StageBorder(chip);
            var row = Z.Row(chip, Z.HSpace(4f), dial);
            row.style.alignItems = Align.Center;
            return row;
        }

        /// <summary>Re-paint the bar's two bands from the CURRENT Duration / Idle gap, and keep
        /// the scrub inside the new total. Cheap to over-call: it no-ops unless a length actually moved, which
        /// is what lets the render path call it to catch a hosted subject that re-timed itself under us.</summary>
        /// <param name="rerender">False when called from inside a render pass — the picture is already being
        /// painted, and re-entering RenderPreviewAt from within itself would recurse.</param>
        void RefreshTimelineBands(bool rerender = true)
        {
            if (_timeline == null) return;
            var spec = Spec;
            float active = ActiveSeconds(spec);
            float gap = Mathf.Max(0f, IdleGap);
            if (active == _bandActive && gap == _bandGap) return;
            _bandActive = active; _bandGap = gap;

            _timeline.SetSegments(
                new ZuiTimelineSegment("Active", active, ActiveBand,
                    (Hosted
                        ? $"The stack playing, life 0→1, over the event's own {active:0.###} s."
                        : "The stack playing, life 0→1, over the Duration set below.")
                    + " Its right edge is the finished effect — scrub there to see where it ends up."),
                new ZuiTimelineSegment("Gap", gap, GapBand,
                    "The stack switched OFF: the plain, unfiltered sprite, exactly what the game shows once a " +
                    "pass has finished — set by Idle gap below."));

            // A band just appeared or vanished, so the two tooltips that describe the loop have to be re-said.
            _timeline.tooltip = BarTooltip();
            if (_playButton != null) _playButton.tooltip = PlayTooltip();

            // The total just moved under the playhead; a scrub parked past the new end has to come back.
            if (_scrubTime > _timeline.Total) _scrubTime = _timeline.Total;
            if (!_previewPlaying) _timeline.SetSecondsWithoutNotify(_scrubTime);
            if (rerender && !_previewPlaying) RenderPreview();
        }

        // The rest between loops, in seconds. A viewing preference, so it lives in EditorPrefs and never
        // touches a SpriteFx Stack asset.
        const string IdleGapPrefKey = "Laubrary.SpriteFx.Preview.IdleGap";
        static float IdleGap
        {
            // Defaults NON-ZERO on purpose. The gap is the band this whole layout was asked for ("the timeline
            // also shows the configured delay gap in between loop"), and a 0 default would ship it invisible:
            // a first-run window would draw one unbroken band, the requested behaviour would never be
            // demonstrated, and finding it would depend on noticing a slider whose band is not on screen.
            // The empty state is the only state every user is guaranteed to see.
            get => EditorPrefs.GetFloat(IdleGapPrefKey, 0.35f);
            set => EditorPrefs.SetFloat(IdleGapPrefKey, Mathf.Clamp(value, 0f, 2f));
        }

        // ── per-effect preview overlays ──────────────────────────────────────────────────────────────────────
        // This window owns no overlay of its own and knows no effect by name. It collects whatever the current
        // stack offers (SpriteFxPreviewOverlays.Collect), keeps the reserved strip filled with their toggles, and
        // hands the buffer over after each render. Everything about WHAT gets drawn lives on the effect, and each
        // toggle's on/off state lives against the effect INSTANCE inside SpriteFxPreviewOverlays — this window
        // stores none of it, so switching stacks can never carry one stack's diagnostics onto another's.

        /// Recollect the overlays and, only if the SET changed, refill the strip.
        ///
        /// Every structural edit (add / remove / reorder / enable, and a mode switch, which ZUIShowIf promotes to
        /// a structural change because it reveals and hides dials) already re-runs the whole panel, which rebuilds
        /// the strip from scratch. This exists for the case that path does not cover: an effect whose answer to
        /// "have I anything to show?" turns on a plain VALUE edit, which only ever fires OnChanged. Collecting is
        /// a short scan over the stack, and the signature compare means a slider drag does not rebuild controls
        /// under the user's cursor forty times a second.
        void RefreshOverlays()
        {
            var spec = Spec;
            var fresh = SpriteFxPreviewOverlays.Collect(spec != null ? spec.modifiers : null);
            bool same = _overlayEntries != null && _overlayEntries.Count == fresh.Count;
            if (same)
                for (int i = 0; i < fresh.Count; i++)
                    // Compare the EFFECT each toggle belongs to, not just its label: two Fake Lights swapping
                    // places leaves the labels identical while the toggles now belong to different lights.
                    if (!ReferenceEquals(_overlayEntries[i].Overlay, fresh[i].Overlay) ||
                        _overlayEntries[i].Label != fresh[i].Label ||
                        _overlayEntries[i].Members.Count != fresh[i].Members.Count) { same = false; break; }

            // Always adopt the fresh list — Draw reads it, and its entries point at the modifiers as they are
            // now. Only the STRIP is left alone when nothing about the set changed.
            _overlayEntries = fresh;
            if (!same && _overlayStrip != null)
                SpriteFxPreviewOverlays.FillStrip(_overlayStrip, _overlayEntries, RenderPreview);
        }

        static void StageBorder(VisualElement v)
        {
            var c = new Color(0f, 0f, 0f, 0.5f);
            v.style.borderTopWidth = 1f; v.style.borderBottomWidth = 1f;
            v.style.borderLeftWidth = 1f; v.style.borderRightWidth = 1f;
            v.style.borderTopColor = c; v.style.borderBottomColor = c;
            v.style.borderLeftColor = c; v.style.borderRightColor = c;
        }

        // ── picker / scrub / transport callbacks ─────────────────────────────────────────────────────────────
        void OnPickSprite(Sprite s)
        {
            _previewSprite = s;
            PreviewSpritePrefs.Remember(PrevSpritePrefKey, _prevSpecGuid, s);
            RenderPreview();
        }

        /// The timeline bar hands back SECONDS along the whole loop, not a 0→1 progress — which is what makes
        /// scrubbing into the gap mean anything at all.
        void OnScrub(float seconds)
        {
            _scrubTime = seconds;
            if (_previewPlaying) StopPlay();   // grabbing the scrub takes manual control (StopPlay re-renders)
            else RenderPreview();
        }

        void TogglePlay()
        {
            if (_previewPlaying) StopPlay();
            else StartPlay();
        }

        void StartPlay()
        {
            if (Spec == null) return;
            _previewPlaying = true;
            _playTime = 0f;      // one clock, one reset — there is no separate gap state to clear any more
            _previewFrame = 0;
            _lastTickTime = EditorApplication.timeSinceStartup;
            if (!_updateHooked) { EditorApplication.update += OnPlayTick; _updateHooked = true; }
            UpdatePlayButton();
        }

        void StopPlay()
        {
            UnhookPlayUpdate();
            UpdatePlayButton();
            if (_timeline != null) _timeline.SetSecondsWithoutNotify(_scrubTime);   // back to the scrub position
            RenderPreview();
        }

        // Detach the update handler and clear the playing flag WITHOUT touching any UI element — safe to call from
        // OnDisable / OnAssetChanged when the elements may already be gone or about to be rebuilt.
        void UnhookPlayUpdate()
        {
            if (_updateHooked) { EditorApplication.update -= OnPlayTick; _updateHooked = false; }
            _previewPlaying = false;
        }

        void UpdatePlayButton()
        {
            if (_playButton != null) _playButton.text = _previewPlaying ? "Stop" : "Play";
        }

        void OnPlayTick()
        {
            var spec = Spec;
            if (spec == null) { StopPlay(); return; }        // asset unassigned / browsing — halt
            if (_previewImage == null) return;               // mid-rebuild: skip this tick, don't stop playback

            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - _lastTickTime);
            _lastTickTime = now;
            if (dt < 0f) dt = 0f;

            // ONE clock. The play-through and the switched-off gap used to be a multi-field state machine that
            // had to be kept in step with the scrub by hand; they are now two stretches of a single advancing
            // number, and ResolveAt is the only thing that knows where the boundary is. The gap band is what
            // the runtime does when a pass finishes —
            // SpriteFxFilter.Restore puts the original sprite back — so without it the preview loops from full
            // blast straight into full blast and the effect never appears to switch off at all.
            //
            // The HOST's length, not the stack's, decides the active stretch: a flash on a death event plays
            // over the death event. (LoopSeconds → ActiveSeconds → PreviewSeconds.)
            float total = LoopSeconds(spec);
            float active = ActiveSeconds(spec);
            _playTime += dt;
            if (_playTime >= total)
            {
                _playTime = total > 0f ? Mathf.Repeat(_playTime, total) : 0f;
                _previewFrame = 0;                        // the grain restarts with the loop
            }
            else if (_playTime < active) _previewFrame++; // the hashing grain only advances while the stack runs,
                                                          // so the gap does not shimmer under a bypassed stack

            ResolveAt(_playTime, out float progress, out bool bypass);
            if (_timeline != null) _timeline.SetSecondsWithoutNotify(_playTime);   // never the Seconds setter:
                                                                                   // that would re-enter OnScrub
            // RENDER EVERY TICK, unconditionally — including while in the gap. Not an optimisation left on the
            // table: anything that rebuilds the panel mid-loop (an edit, an undo, the window regaining focus)
            // re-creates the image element and repaints it at the SCRUB position, which mid-gap would flash the
            // effect back ON. Repainting from the play clock every tick is what makes a rebuild invisible.
            RenderPreviewAt(progress, _previewFrame, bypass);
        }

        // ── render ───────────────────────────────────────────────────────────────────────────────────────────
        /// The frame to filter when no sprite has been picked by hand: the character whose Zoe event uses
        /// this stack. Resolved through SpriteFxPreviewSubjects, so this window still knows nothing about
        /// Zoes — see that class for why the dependency has to run this way.
        ///
        /// Progress drives the frame, so scrubbing the stack's timeline walks the character's animation
        /// underneath it. That is the point: a death flash has to be judged against the death animation, not
        /// against one arbitrary still of it.
        ///
        /// The frame comes from ELAPSED TIME at the visual's OWN rate — progress through the event gives
        /// seconds, seconds at the subject's fps give a frame — so a three-loop event walks its clip three
        /// times and a 4-fps lauminary reads as a 4-fps lauminary. It used to index by the editor's tick counter, which
        /// made the character animate at whatever rate the editor happened to be repainting at; the fps the
        /// resolver went to the trouble of reporting was never read at all.
        Sprite SubjectFrame(float progress)
        {
            if (_subject == null || !_subject.HasFrames) return null;
            var f = _subject.Frames;
            if (f.Length == 1) return f[0];


            float p = Mathf.Clamp01(progress);
            // Without a rate there is no time-based answer, so fall back to spreading the frames evenly across
            // the play-through — still an animation, just not one claiming a rate it does not have.
            int i = _subject.Fps > 0f
                ? Mathf.FloorToInt(p * PreviewSeconds(Spec) * _subject.Fps)
                : Mathf.FloorToInt(p * f.Length);
            // At progress EXACTLY 1 the play-through has ENDED, and floor() lands one past its last frame —
            // which the wrap below turns into frame 0. Reachable at the right edge of the Active band, which
            // is exactly where someone scrubs to judge the finished effect; without this it snapped the
            // character back to the FIRST frame of its clip under a fully-finished effect. Step back one so
            // "the end" means the last frame actually shown. Multi-loop subjects are unaffected — i-1 still
            // wraps to the last frame of the final pass.
            if (p >= 1f) i = Mathf.Max(0, i - 1);
            return f[((i % f.Length) + f.Length) % f.Length];
        }

        void UpdateSubjectLine()
        {
            // The hosted subject's length is project data edited in another window entirely, so it can change
            // between rebuilds — and the ACTIVE band is that length. Re-check it wherever this line is
            // refreshed (which is every render), so the bar re-proportions with the fact it is describing
            // instead of quietly drawing the old one. It no-ops unless a length really moved, and it is told
            // NOT to re-render: it is already being called from inside a render.
            RefreshTimelineBands(rerender: false);

            if (_subjectLine == null) return;
            string t;
            if (_subject != null && _subject.HasFrames)
            {
                // Both halves of what the event fixed: what it looks like AND how long it takes. The length is
                // the half that used to be invisible, and it is the one that made the preview disagree with
                // the game without ever saying so.
                string rate = _subject.Fps > 0f ? $" at {_subject.Fps:0.#} fps" : " (a still)";
                t = _subject.OwnsTimebase
                    ? $"{_subject.Label} — {_subject.Frames.Length} frames{rate}, over {_subject.Seconds:0.###} s."
                    : $"{_subject.Label} — {_subject.Frames.Length} frames{rate}; the event states no length, so " +
                      "this stack's own Duration is used.";
            }
            else t = _previewSprite != null
                    ? "A picked sprite — this stack is not on any Zoe event yet."
                    : "Nothing to preview on — use this stack on a Zoe event, or pick a sprite.";
            if (_subjectLine.text != t) _subjectLine.text = t;
        }

        /// Paint whatever the RESTING scrub position resolves to. Frame 0 on purpose: a parked playhead gets a
        /// fixed hashing grain, so tuning a dissolve is not fighting a grain that changes under every edit.
        void RenderPreview()
        {
            ResolveAt(_scrubTime, out float progress, out bool bypass);
            RenderPreviewAt(progress, 0, bypass);
        }

        /// <param name="bypassStack">Draw the source frame with NO effects applied — the picture the runtime
        /// is left with once a pass ends and the original sprite is restored.</param>
        void RenderPreviewAt(float progress, int frame, bool bypassStack = false)
        {
            if (_previewImage == null || _previewHint == null) return;   // not built yet
            UpdateSubjectLine();
            var spec = Spec;
            if (spec == null) { ShowHint("No SpriteFx Stack selected."); return; }

            // THE SUBJECT WINS. It used to be the fallback -- `_previewSprite ?? SubjectFrame(...)` -- which
            // made the whole feature unreachable for anyone who had ever picked a sprite by hand: the pick is
            // remembered per-spec in EditorPrefs, so a stale choice from another session silently beat the
            // event's own clip forever. The requirement was "the input IS the clip", so the clip is the input
            // and the picker is an explicit OVERRIDE that only applies when there is nothing better.
            Sprite spr = SubjectFrame(progress) ?? _previewSprite;
            if (spr == null)
            {
                ShowHint("Pick a sprite to preview — or add this stack to a Zoe event and it will preview on " +
                         "that character's own clip.");
                return;
            }
            Texture2D tex = spr.texture;
            if (tex == null) { ShowHint("This sprite has no texture."); return; }

            Rect tr = spr.textureRect;
            int x = Mathf.RoundToInt(tr.x), y = Mathf.RoundToInt(tr.y);
            int W = Mathf.RoundToInt(tr.width), H = Mathf.RoundToInt(tr.height);
            if (W <= 0 || H <= 0) { ShowHint("This sprite has no pixels to preview."); return; }

            try
            {
                // Read through PreviewKit rather than GetPixels. GetPixels THROWS on a sprite whose importer
                // has Read/Write off, which is most pixel art in a real project -- this window used to detect
                // that and refuse with "Enable Read/Write on this sprite's import settings", telling the
                // author to go and change an IMPORT setting to look at an effect. PreviewTex blits through a
                // temporary RenderTexture and reads anything the GPU can sample, readable or not; it is the
                // same path the asset browsers already use for thumbnails, and the reason that gotcha is
                // written down. Requiring an import change to preview was never an acceptable answer.
                Color32[] px;
                var readable = Laubrary.PreviewKit.PreviewTex.CropSprite(spr);
                if (readable == null) { ShowHint("This sprite's pixels could not be read."); return; }
                px = readable.GetPixels32();
                W = readable.width; H = readable.height;
                Object.DestroyImmediate(readable);

                // A reversed pass flips the pass-local clock, exactly as SpriteFxFilter.Tick does — the hashing
                // frame keeps following the forward clock, so the preview reproduces the runtime's
                // (deliberate) non-mirroring grain too.
                float raw = Mathf.Clamp01(progress);
                progress = _previewReversed ? 1f - raw : raw;

                // Life IS progress — nothing sits in between. The stack-wide remap that used to is gone, for
                // the reason in SpriteFxSpec.SampleEnvelope.
                float life = Mathf.Clamp01(progress);

                // SHOW THE OVERFLOW. An outline or a glow draws past the silhouette, and on a tight sprite
                // that lands outside the frame — at runtime it goes to a companion renderer, but here the
                // stage is our own canvas with no Sprite semantics at all, so it can simply be drawn bigger.
                // Seeing it is the whole point: how much is spilling is invisible otherwise, and it is exactly
                // what decides whether an effect reads or gets cut in half.
                // Nothing draws outside the silhouette when the stack is off, so a bypassed frame takes the
                // unpadded path — and ShowImage scales from the SOURCE frame either way, so the character is
                // the same size on screen whether the effect is running or not.
                int pad = bypassStack ? 0 : SpriteFxStack.OutwardReach(spec.modifiers);
                if (pad > 0)
                {
                    int pw = W + pad * 2, ph = H + pad * 2;
                    var padded = new Color32[pw * ph];
                    for (int row = 0; row < H; row++)
                        System.Array.Copy(px, row * W, padded, (row + pad) * pw + pad, W);
                    SpriteFxStack.RunStack(padded, pw, ph, W, H, pad, pad,
                                           spec.modifiers, frame, life, spec.seed, useBurst: false);
                    // No bypass check here: pad is forced to 0 when bypassing, so this branch is only ever
                    // reached with the stack actually running.
                    SpriteFxPreviewOverlays.Draw(_overlayEntries, padded, pw, ph);
                    EnsurePreviewTex(pw, ph);
                    _previewTex.SetPixels32(padded);
                    _previewTex.Apply(false);
                    _previewImage.image = _previewTex;
                    _previewImage.MarkDirtyRepaint();
                    // Scale from the SOURCE frame, so the character is the same size on screen whether or not
                    // the stack needs a margin, and the overflow spills past the stage instead of shrinking it.
                    ShowImage(pw, ph, W, H);
                    return;
                }
                // The SAME routine Tick uses at runtime — inline (useBurst:false) so the preview matches WYSIWYG.
                if (!bypassStack)
                {
                    SpriteFxFilter.Apply(px, W, H, spec.modifiers, frame, life, spec.seed, useBurst: false);
                    SpriteFxPreviewOverlays.Draw(_overlayEntries, px, W, H);
                }

                EnsurePreviewTex(W, H);
                _previewTex.SetPixels32(px);
                _previewTex.Apply(false);
                _previewImage.image = _previewTex;
                _previewImage.MarkDirtyRepaint();
                ShowImage(W, H, W, H);   // same scale rule as the padded branch, so the two are comparable
            }
            catch (System.Exception e)
            {
                ShowHint("Could not read this sprite's pixels to preview.");
                Debug.LogWarning($"[SpriteFxStackWindow] Preview render failed: {e.Message}", spec);
            }
        }        void ShowHint(string msg)
        {
            _previewHint.text = msg;
            _previewHint.Shown(true);
            _previewImage.Shown(false);
        }

        void ShowImage() => ShowImage(0, 0, 0, 0);

        /// Show the stage at a scale fixed by the SPRITE, never by the buffer.
        ///
        /// The image element used to be a fixed square with ScaleToFit, which is right until an effect needs
        /// a margin — then the padded buffer is what gets fitted, and the character shrinks to make room for
        /// its own glow. That is the preview inventing a difference from the game, and a preview that does
        /// not match the runtime is worse than none: every judgement made in it is about a picture that will
        /// never exist.
        ///
        /// So the scale comes from the source frame and the element is sized to whatever the buffer needs at
        /// that scale. The sprite therefore occupies exactly the same pixels it always did, and the overflow
        /// simply extends past the stage — which is fine, and is the honest picture.
        void ShowImage(int texW, int texH, int srcW, int srcH)
        {
            float fit = PreviewBox - 4f;
            if (texW > 0 && srcW > 0 && srcH > 0)
            {
                float s = fit / Mathf.Max(srcW, srcH);   // screen px per source px — identical with or without a margin

                // The FRAME is the sprite. It does not change size when a stack gains a margin, so nothing in
                // the panel moves and the character never rescales — the jarring part of the old behaviour.
                _frameBox.style.width = srcW * s;
                _frameBox.style.height = srcH * s;

                // The IMAGE is the whole buffer, centred on the frame and hanging outside it by the margin.
                _previewImage.style.width = texW * s;
                _previewImage.style.height = texH * s;
                _previewImage.style.left = -(texW - srcW) * 0.5f * s;
                _previewImage.style.top = -(texH - srcH) * 0.5f * s;
            }
            else
            {
                _frameBox.style.width = fit;
                _frameBox.style.height = fit;
                _previewImage.style.width = fit;
                _previewImage.style.height = fit;
                _previewImage.style.left = 0f;
                _previewImage.style.top = 0f;
            }
            _previewHint.Shown(false);
            _previewImage.Shown(true);
        }

        // Pooled output texture — resized only when the source geometry changes, Point-filtered for crisp pixels,
        // never saved. Disposed on window disable/destroy.
        void EnsurePreviewTex(int W, int H)
        {
            if (_previewTex != null && _previewTex.width == W && _previewTex.height == H) return;
            DisposePreviewTex();
            _previewTex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        void DisposePreviewTex()
        {
            if (_previewTex != null) { DestroyImmediate(_previewTex); _previewTex = null; }
        }

        // Preview-sprite persistence lives in the shared PreviewSpritePrefs (AssetKit.Editor) — EditorPrefs keyed
        // by the spec's GUID, never the asset. Extracted 2026-08-02 so ChunkWindow's preview subject shares the
        // one canonical implementation; this window's key prefix is unchanged, so remembered sprites survive.

        // ── lifecycle ──────────────────────────────────────────────────────────────────────────────────────────
        protected override void OnAssetChanged()
        {
            base.OnAssetChanged();
            UnhookPlayUpdate();   // stop playback when the edited asset changes (elements are about to be rebuilt)
            _previewFrame = 0;
        }

        protected override void OnBeforeRebuild()
        {
            base.OnBeforeRebuild();   // AssetKit clears its thumbnail refs
            // Release element refs so a stale one is never touched between clearing the tree and rebuilding it.
            // Playback (an EditorApplication.update handler) intentionally survives a rebuild — OnPlayTick tolerates
            // a transiently-null image and the elements are re-created immediately in BuildPreview.
            _previewImage = null;
            _previewHint = null;
            _overlayStrip = null;   // rebuilt (and refilled) by BuildPreview
            // Not an element, but it points at modifiers and must not outlive the panel that collected them —
            // empty rather than null so nothing has to guard for it.
            _overlayEntries = new List<SpriteFxPreviewOverlays.Entry>();
            _timeline = null;
            _playButton = null;
            _previewStage = null;
            _frameBox = null;
        }

        protected override void OnDisable()
        {
            UnhookPlayUpdate();   // no leaked EditorApplication.update handler
            DisposePreviewTex();
            base.OnDisable();
        }

        void OnDestroy()
        {
            UnhookPlayUpdate();
            DisposePreviewTex();
        }
    }
}
