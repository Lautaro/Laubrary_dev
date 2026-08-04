// SpriteFxStackWindow — the dedicated browse / create / tag authoring window for a SpriteFxSpec (the "SpriteFx
// Stack" asset). Same AssetKit base every other Laubrary tool uses (ZuiAssetWindow<T>: assign / New / Duplicate /
// Rename / Delete / thumbnail browser for free), so this window only has to lay out the per-asset editor.
//
// It hosts the reusable SpriteFxStackView control (slice 3) for the effect stack itself, a small Timeline
// section (play-through duration / life-remap envelope / hashing seed), and a live input-sprite Preview
// (pick a sprite, scrub, Play) that reuses the runtime SpriteFxFilter.Apply so what you see is what plays.
//
// Every dial routes through Dial(...) (Undo.RecordObject BEFORE the mutation, then SetDirty), matching ChunkWindow
// so a whole session's edits don't coalesce into one Undo step.
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
    /// Embeds the host-agnostic <see cref="SpriteFxStackView"/> for the effect stack and adds a Timeline section.
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
        float _previewProgress;             // the resting scrub position (raw progress 0→1)
        float _playProgress;                // transient progress while Play animates (does not overwrite the scrub)
        int _previewFrame;                  // frame counter advanced across play ticks (for hashing effects)
        bool _previewPlaying;
        bool _previewHeld;                  // resting on the last frame between loops
        float _previewPause;                // seconds left of that rest
        bool _previewReversed;              // preview the stack against a reversed clock (the OnceReversed binding)
        bool _updateHooked;                 // guards the EditorApplication.update subscription
        double _lastTickTime;

        // live element refs (re-created every BuildPreview; nulled in OnBeforeRebuild)
        UnityEngine.UIElements.Image _previewImage;
        Label _previewHint;
        ZuiMicroSlider _lifeSlider;
        Button _playButton;
        VisualElement _previewStage;

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

        protected override void BuildAsset(VisualElement root, SpriteFxSpec spec)
        {
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;

            // Who hosts this stack is resolved BEFORE anything is drawn, because the answer decides what this
            // window is even allowed to own: on a Zoe event the duration and the visual are the event's, and
            // the Timeline section below must render them as facts rather than as dials.
            EnsureSubject(spec);

            // The preview is PINNED above the scroller, not inside it. It is the thing being judged, so
            // scrolling a stack of a dozen effects must never take it off screen — which is exactly what
            // happened while all three sections shared one ScrollView.
            var pinned = new VisualElement();
            pinned.style.flexShrink = 0f;
            BuildPreview(pinned, spec);
            root.Add(pinned);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            var body = scroll.contentContainer;

            BuildStack(body, spec);
            BuildTimeline(body, spec);

            root.Add(scroll);
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

        void BuildStack(VisualElement root, SpriteFxSpec spec)
        {
            var s = Z.Section("Stack",
                "The colour / mask effects applied in order while the stack plays. Each effect's animatable " +
                "values are resolved at the current life every frame; drag the grip to reorder.");

            var host = new SpriteFxStackView.Host
            {
                // Fires once per gesture, before the first mutation.
                OnBeforeChange = () => { var sp = Spec; if (sp != null) Undo.RecordObject(sp, "Edit SpriteFx Stack"); },
                // Fires after every value edit — mark the asset dirty (retained controls repaint themselves).
                OnChanged = () => { var sp = Spec; if (sp != null) EditorUtility.SetDirty(sp); },
                // A structural change (add / remove / reorder / enable): the control already rebuilt its own
                // rows in place; re-run the whole panel so anything downstream stays in sync (safe — the
                // control's fold/curve state is keyed per effect instance and survives the rebuild).
                Rebuild = Rebuild,
                ControlWidth = Wide,
            };
            s.Add(SpriteFxStackView.Build(spec.modifiers, host));
            root.Add(s);
        }

        void BuildTimeline(VisualElement root, SpriteFxSpec spec)
        {
            var s = Z.Section("Timeline",
                "How long one play-through lasts and how raw progress is remapped into the life value fed to " +
                "every effect's curves.");

            // WHO OWNS THE LENGTH. A stack is a shape over normalized life, not a schedule — its parameters
            // run 0→1 and mean nothing in seconds — so whatever plays it says how long that 0→1 takes. When
            // this stack is on a Zoe event, the event already answered, and offering a Duration dial here
            // would be offering a number the runtime ignores.
            if (Hosted)
            {
                string hostedTip = $"Set by the event using this stack ({_subject.Label}), not here. A stack is a " +
                    "shape over its play-through, so the event it rides owns how long that takes — re-time the " +
                    "animation and this effect re-times with it.";
                s.Add(Z.Field("Duration", hostedTip,
                    Z.Text($"{_subject.Seconds:0.###} s — from {_subject.Label}", ZuiText.Body, hostedTip)));
            }
            else
            {
                const string durTip = "How long one play-through lasts, in seconds. Used only when nothing else " +
                    "says: put this stack on a Zoe event and that event's length wins instead.";
                s.Add(Z.MicroSlider("Duration (s)", spec.duration, 0.02f, 2f, durTip,
                    v => Dial("SpriteFx duration", () => spec.duration = Mathf.Max(0.001f, v)), Wide, showValue: true));
            }

            const string envTip = "Optional easing / remap of raw progress (0→1 over Duration) into the LIFE " +
                "value fed to every effect's curves. Identity by default; a triangle (0→1→0) turns a " +
                "monotonic effect into a pulse, an ease softens the ends.";
            s.Add(Z.Field("Life remap", envTip,
                // ZuiEnvelope, not Z.Curve: that wrapper returns a raw Unity CurveField, which is a
                // native control in a ZUI window and opens Unity's own curve editor for our data.
                Z.Envelope(spec.Envelope, new ZuiEnvelopeOptions { yMin = 0f, yMax = 1f, anchorsLocked = true },
                    envTip, () => Dial("SpriteFx envelope", () => { }), null, 220f, 80f)));

            const string seedTip = "Seed for any hashing effect (LayerDissolve scatter, AlphaMask noise). " +
                "Irrelevant for a plain Brightness / Tint flash.";
            s.Add(Z.Field("Seed", seedTip,
                Z.Int(spec.seed, seedTip, v => Dial("SpriteFx seed", () => spec.seed = v), Num)));

            // Deliberately still a dial when hosted, unlike Duration: this is the EFFECT's own re-evaluation
            // grid, not the visual's frame rate. Riding a 4-fps reel with a 12-step flicker is the whole point
            // of it, so the host cannot own it — but the tooltip has to keep the two rates apart by name.
            string fpsTip = "Own clock: how many times per second THIS EFFECT's time advances while it plays. " +
                "0 = every rendered frame (continuous — the default). Set a rate to step the effect on its own " +
                "fixed grid, independent of the animation it rides — a fast flicker over a slow reel, or a " +
                "deliberately chunky retro fade. Not the same thing as the visual's frame rate" +
                (Hosted ? $", which is {_subject.Fps:0.#} fps and comes from the event." : ".") +
                " The preview above steps at this rate too.";
            s.Add(Z.MicroSlider("Step rate (fps)", spec.targetFps, 0f, 60f, fpsTip,
                v => Dial("SpriteFx step rate", () => spec.targetFps = Mathf.Max(0f, Mathf.Round(v))),
                Wide, showValue: true, decimals: 0));

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

            // 2) The preview stage — a bespoke pixel-art canvas island (sanctioned raw painting). Fixed square box,
            // Point-filtered texture scaled to fit; a hint Label swaps in when there is nothing to render.
            _previewStage = new VisualElement();
            _previewStage.style.width = PreviewBox;
            _previewStage.style.height = PreviewBox;
            _previewStage.style.flexShrink = 0f;
            _previewStage.style.alignItems = Align.Center;
            _previewStage.style.justifyContent = Justify.Center;
            _previewStage.style.backgroundColor = new Color(0.11f, 0.11f, 0.12f, 1f);
            StageBorder(_previewStage);
            _previewStage.tooltip = "The input sprite with the current stack applied at the scrub position, scaled " +
                "up point-filtered (nearest-neighbour) so pixels stay crisp.";

            _previewImage = new UnityEngine.UIElements.Image { scaleMode = ScaleMode.ScaleToFit };
            _previewImage.style.width = PreviewBox - 4f;
            _previewImage.style.height = PreviewBox - 4f;
            _previewStage.Add(_previewImage);

            _previewHint = new Label { pickingMode = PickingMode.Ignore };
            _previewHint.style.whiteSpace = WhiteSpace.Normal;
            _previewHint.style.unityTextAlign = TextAnchor.MiddleCenter;
            _previewHint.style.maxWidth = PreviewBox - 20f;
            _previewStage.Add(_previewHint);


            // 3) + 4) Transport row: the Life scrub slider and Play/Stop, packed together (vertical space is scarce).
            const string lifeTip = "Scrub position: raw progress 0→1 through the timeline. The stack is evaluated " +
                "at life = the Timeline's Life-remap envelope applied to this progress — exactly as it plays at " +
                "runtime (identity unless you shaped the envelope).";
            _lifeSlider = Z.MicroSlider("Life", _previewProgress, 0f, 1f, lifeTip, OnScrub, Wide, showValue: true);

            _playButton = Z.Button(_previewPlaying ? "Stop" : "Play",
                Hosted
                    ? $"Play the stack over the event's own length ({_subject.Seconds:0.###} s), on the event's " +
                      "own visual, then start over. Stop returns to the scrub position."
                    : "Play the stack over its Duration, then start over. Stop returns to the scrub position.",
                TogglePlay);
            _playButton.style.width = 60f;

            // How long the loop rests on the last frame before starting over. Preview-only and deliberately
            // NOT on the asset: it is how you want to WATCH the effect, not part of the effect. A real, stable
            // range, so a slider rather than a number — and one shared across stacks, since it is a viewing
            // habit rather than a property of any one of them.
            const string pauseTip = "How long the preview rests on the last frame before looping back to the " +
                "start — room to actually see where the effect ended. Preview-only: never saved into the stack.";
            var pauseSlider = Z.MicroSlider("Restart after", RestartPause, 0f, 2f, pauseTip,
                v => { RestartPause = v; }, Wide, showValue: true);

            // Preview-only: the same stack against a reversed clock — what an attachment set to Once Reversed
            // shows. Nothing about the asset changes; this is how you check that one stack covers both
            // directions before hanging it on a departure event.
            var reverseToggle = Z.ToggleButton("Reverse",
                "Run the preview backwards (life 1→0), exactly as the Once Reversed playback binding does — so " +
                "one authored stack can be checked as both an arrival and a departure. The hashing grain still " +
                "counts forward, so a reversed dissolve un-dissolves through a different grain.",
                _previewReversed, on => { _previewReversed = on; RenderPreview(); });

            // The transport sits BESIDE the stage, not under it. The stage is a fixed 200pt square in a pane
            // that is realistically three times that wide, so a row underneath spent height to leave a large
            // empty rectangle to its right — and height is the scarce resource in a window whose whole point
            // is that the stack below stays reachable. Wraps back to stacked if the pane ever is that narrow.
            var controls = Z.Column(_lifeSlider, Z.VSpace(2f), Z.Row(_playButton, reverseToggle),
                                    Z.VSpace(2f), pauseSlider);
            controls.style.flexShrink = 1f;
            controls.style.minWidth = 0f;

            var stageRow = Z.Row(_previewStage, Z.HSpace(), controls);
            stageRow.style.flexWrap = Wrap.Wrap;
            stageRow.style.alignItems = Align.FlexStart;
            s.Add(stageRow);

            root.Add(s);

            RenderPreview();   // paint the current scrub state now
        }

        // The loop's rest on the last frame, in seconds. A viewing preference, so it lives in EditorPrefs and
        // never touches a SpriteFx Stack asset.
        const string RestartPausePrefKey = "Laubrary.SpriteFx.Preview.RestartPause";
        static float RestartPause
        {
            get => EditorPrefs.GetFloat(RestartPausePrefKey, 0.35f);
            set => EditorPrefs.SetFloat(RestartPausePrefKey, Mathf.Clamp(value, 0f, 2f));
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

        void OnScrub(float v)
        {
            _previewProgress = v;
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
            _previewHeld = false;
            _previewPause = 0f;
            _playProgress = 0f;
            _previewFrame = 0;
            _lastTickTime = EditorApplication.timeSinceStartup;
            if (!_updateHooked) { EditorApplication.update += OnPlayTick; _updateHooked = true; }
            UpdatePlayButton();
        }

        void StopPlay()
        {
            UnhookPlayUpdate();
            UpdatePlayButton();
            if (_lifeSlider != null) _lifeSlider.value = _previewProgress;   // return to the scrub position
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

            // Rest on the last frame before looping back, so the end state is actually visible instead of
            // being wiped a frame after it arrives. Nothing moves while held — not the effect, not the frame.
            if (_previewHeld)
            {
                _previewPause -= dt;
                // Deliberately falls through to the render below rather than returning. What is held is the
                // stack's END STATE — life 1, fully filtered — and it has to be REPAINTED each tick, not just
                // left on screen from the last one: anything that rebuilds the panel mid-hold (an edit, an
                // undo, the window regaining focus) re-creates the image element and repaints it at the SCRUB
                // position, which is life 0. The pause would then show the unprocessed sprite for a third of a
                // second and read as the effect switching itself off before looping.
                if (_previewPause <= 0f)
                {
                    _previewHeld = false;
                    _playProgress = 0f;
                    _previewFrame = 0;
                }
            }
            else
            {
                // The HOST's length, not the stack's: a flash on a death event plays over the death event.
                float dur = Mathf.Max(0.001f, PreviewSeconds(spec));
                _playProgress += dt / dur;
                _previewFrame++;
                if (_playProgress >= 1f)
                {
                    if (RestartPause > 0f) { _playProgress = 1f; _previewHeld = true; _previewPause = RestartPause; }
                    else { _playProgress = Mathf.Repeat(_playProgress, 1f); _previewFrame = 0; }
                }
            }

            if (_lifeSlider != null) _lifeSlider.value = _playProgress;   // slider setter is notify:false (no loop)
            RenderPreviewAt(_playProgress, _previewFrame);
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
        /// times and a 4-fps reel reads as a 4-fps reel. It used to index by the editor's tick counter, which
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
            return f[((i % f.Length) + f.Length) % f.Length];
        }

        void UpdateSubjectLine()
        {
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

        void RenderPreview() => RenderPreviewAt(_previewProgress, 0);   // static scrub → fixed hashing frame

        void RenderPreviewAt(float progress, int frame)
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

                // A reversed pass flips the pass-local clock BEFORE the envelope and the own-clock quantisation,
                // exactly as SpriteFxFilter.Tick does — the hashing frame keeps following the forward clock, so
                // the preview reproduces the runtime's (deliberate) non-mirroring grain too.
                float raw = Mathf.Clamp01(progress);
                progress = _previewReversed ? 1f - raw : raw;

                // Own clock (Step rate): quantise exactly as SpriteFxFilter.Tick does — time snaps to the
                // 1/targetFps grid and the hashing frame becomes the step index — so scrub and Play both show
                // the stepped evaluation the runtime plays (WYSIWYG).
                if (spec.targetFps > 0f)
                {
                    // Over the HOST's length where there is one — a step grid is a rate in SECONDS, so it can
                    // only be quantised against the seconds the play-through actually takes.
                    float dur = Mathf.Max(0.001f, PreviewSeconds(spec));
                    frame = Mathf.FloorToInt(raw * dur * spec.targetFps);
                    int step = Mathf.FloorToInt(progress * dur * spec.targetFps);
                    progress = Mathf.Clamp01((step / spec.targetFps) / dur);
                }

                float life = spec.SampleEnvelope(Mathf.Clamp01(progress));
                // The SAME routine Tick uses at runtime — inline (useBurst:false) so the preview matches WYSIWYG.
                SpriteFxFilter.Apply(px, W, H, spec.modifiers, frame, life, spec.seed, useBurst: false);

                EnsurePreviewTex(W, H);
                _previewTex.SetPixels32(px);
                _previewTex.Apply(false);
                _previewImage.image = _previewTex;
                _previewImage.MarkDirtyRepaint();
                ShowImage();
            }
            catch (System.Exception e)
            {
                ShowHint("Could not read this sprite's pixels to preview.");
                Debug.LogWarning($"[SpriteFxStackWindow] Preview render failed: {e.Message}", spec);
            }
        }

        void ShowHint(string msg)
        {
            _previewHint.text = msg;
            _previewHint.Shown(true);
            _previewImage.Shown(false);
        }

        void ShowImage()
        {
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
            _lifeSlider = null;
            _playButton = null;
            _previewStage = null;
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
