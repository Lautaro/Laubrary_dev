using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using TMPro;
using Laubrary.Zui;
using Laubrary.AssetKit.Editor;
using Laubrary.BackSplash;
using Laubrary.BackSplash.Editor;
using Laubrary.PreviewKit.Editor;

namespace Laubrary.TextSplash.Editor
{
    /// The TextSplash authoring window: edit the look + timing + transitions on the left, watch the whole
    /// in→hold→out play live on a BackSplash backdrop on the right (rendered exactly like the runtime, via a
    /// world-space TMP in a LiveScenePreview using the same ApplyLook/Evaluate the runtime uses).
    public class TextSplashWindow : ZuiAssetWindow<TextSplash>
    {
        [MenuItem("Laubrary/Text Splash")]
        public static void Open() => GetWindow<TextSplashWindow>("Text Splash");

        /// Same entry-point shape as the other tools — lets a LauAsset Edit button jump straight in.
        public static void OpenFor(TextSplash s) { var w = GetWindow<TextSplashWindow>("Text Splash"); w.SetAsset(s); }

        protected override string DefaultFolder => "Assets";
        protected override string NewAssetName => "New Splash";

        static readonly string[] DirLabels = { "None", "Left", "Right", "Top", "Bottom" };
        static readonly string[] EaseLabels = { "Linear", "Smooth", "Ease out", "Ease in" };
        static readonly string[] AxisLabels = { "X", "Y", "Z" };

        // ── preview ─────────────────────────────────────────────────────────────────
        [SerializeField] BackSplashSettings backSplash = new BackSplashSettings();
        LiveScenePreview _preview;
        GameObject _canvasGO;
        TextMeshPro _tmp;            // 3D TMP (mesh) — a UI-canvas TMP won't render in a PreviewRenderUtility scene
        float _scrub;
        bool _playing;
        double _lastTick;

        // Preview world frame + text scale (fontSize is ~world-unit-ish; scale it into a ~10.8-tall frame).
        const float PrevW = 19.2f, PrevH = 10.8f, PrevScale = 0.05f;

        protected override void OnEnable()
        {
            base.OnEnable();
            EditorApplication.update += Tick;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            EditorApplication.update -= Tick;
            DisposePreview();
        }

        void DisposePreview()
        {
            if (_canvasGO != null) { DestroyImmediate(_canvasGO); _canvasGO = null; _tmp = null; }
            _preview?.Dispose();
            _preview = null;
        }

        protected override void OnAssetChanged() => RebuildPreviewScene();

        void RebuildPreviewScene()
        {
            _preview ??= new LiveScenePreview();
            if (_canvasGO != null) { DestroyImmediate(_canvasGO); _canvasGO = null; _tmp = null; }
            if (Current == null) return;

            // A 3D TMP (mesh renderer) — NOT a UI-canvas TMP, which won't render under PreviewRenderUtility's camera.
            // We drive the same ApplyLook look + Evaluate pose; only the coordinate space differs (world units, below).
            _canvasGO = _preview.Spawn("SplashText");
            _tmp = _canvasGO.AddComponent<TextMeshPro>();
            var rt = _tmp.rectTransform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(4000f, 800f);   // generous; overflow-mode text never wraps/clips
            _canvasGO.transform.localScale = Vector3.one * PrevScale;
            RefreshLook();
            // Font fallback: a bare TextMeshPro with no default font asset renders nothing.
            if (Current.font == null && _tmp.font == null && TMP_Settings.defaultFontAsset != null)
                _tmp.font = TMP_Settings.defaultFontAsset;
            _scrub = 0f;
        }

        void RefreshLook()
        {
            if (_tmp == null || Current == null) return;
            SplashPlayer.ApplyLook(Current, _tmp, null);
            _tmp.ForceMeshUpdate();   // isolated preview scene never ticks Update(); rebuild the mesh by hand
        }

        void Tick()
        {
            if (!_playing || Current == null) return;
            double now = EditorApplication.timeSinceStartup;
            _scrub += (float)(now - _lastTick);
            _lastTick = now;
            if (_scrub >= EffectiveDuration(Current)) _scrub = 0f;   // loop the preview
            Repaint();
        }

        int LetterCount() => _tmp != null && _tmp.textInfo != null && _tmp.textInfo.characterCount > 0
            ? _tmp.textInfo.characterCount : (Current?.text?.Length ?? 0);

        /// Play length shown/looped: per-letter stagger extends it, so the transport covers the LAST letter's out.
        float EffectiveDuration(TextSplash s) => s.perLetter ? s.SequenceDuration(LetterCount()) : s.TotalDuration;

        // ── build ───────────────────────────────────────────────────────────────────
        protected override void BuildAsset(VisualElement root, TextSplash s)
        {
            _preview ??= new LiveScenePreview();
            if (_canvasGO == null) RebuildPreviewScene();

            var split = new VisualElement();
            split.style.flexDirection = FlexDirection.Row;
            split.style.flexGrow = 1f;
            split.style.minHeight = 0f;

            var left = new ScrollView(ScrollViewMode.Vertical);
            left.style.width = 344f;
            left.style.flexShrink = 0f;
            BuildControls(left.contentContainer, s);
            split.Add(left);

            var right = new VisualElement();
            right.style.flexGrow = 1f;
            right.style.minWidth = 0f;
            right.Add(BuildTransport(s));
            var view = new IMGUIContainer(() => DrawPreview(s));
            view.style.flexGrow = 1f;
            right.Add(view);
            split.Add(right);

            root.Add(split);
        }

        void BuildControls(VisualElement host, TextSplash s)
        {
            host.Add(Z.Field("Text", "The line to splash (the default; overridable per Show call).",
                Z.TextInput(s.text, "The splashed text.", v => Edit("Edit splash text", () => s.text = v), 240f)));
            host.Add(Z.Field("Font", "TMP font (blank = default).",
                Z.Object<TMP_FontAsset>(s.font, "Font asset.", v => Edit("Edit font", () => s.font = v), 200f)));
            host.Add(Z.MicroSlider("Size", s.fontSize, 8f, 300f, "Font size.",
                v => Edit("Edit size", () => s.fontSize = v), 240f, decimals: 0, prefsKey: "splash.size"));

            host.Add(Z.Fill("Fill", s.fill, "The text FACE — a solid colour or a ZUI gradient.",
                () => { EditorUtility.SetDirty(s); RefreshLook(); Repaint(); },
                () => Undo.RecordObject(s, "Edit splash fill"),
                new ZuiFillControl.Options { grow = true }));

            host.Add(Z.MicroSlider("Alpha", s.alpha, 0f, 1f, "Overall opacity.",
                v => Edit("Edit alpha", () => s.alpha = v), 240f, prefsKey: "splash.alpha"));
            host.Add(Z.Field("Border", "Border (TMP outline) colour.",
                Z.Color(s.borderColor, "Outline colour.", v => Edit("Edit border colour", () => s.borderColor = v), 90f)));
            host.Add(Z.MicroSlider("Border width", s.borderWidth, 0f, 1f, "Outline thickness (0 = none).",
                v => Edit("Edit border width", () => s.borderWidth = v), 240f, prefsKey: "splash.bw"));

            host.Add(Z.Text("Timing", ZuiText.Section, "How long each phase lasts."));
            host.Add(Z.MicroSlider("In (s)", s.slideInDuration, 0f, 3f, "Slide/fade-in seconds.",
                v => Edit("Edit in", () => s.slideInDuration = v), 240f, decimals: 2, prefsKey: "splash.in"));
            host.Add(Z.MicroSlider("Hold (s)", s.holdDuration, 0f, 6f, "Hold-on-screen seconds (the default; overridable per Show).",
                v => Edit("Edit hold", () => s.holdDuration = v), 240f, decimals: 2, prefsKey: "splash.hold"));
            host.Add(Z.MicroSlider("Out (s)", s.slideOutDuration, 0f, 3f, "Slide/fade-out seconds.",
                v => Edit("Edit out", () => s.slideOutDuration = v), 240f, decimals: 2, prefsKey: "splash.out"));

            host.Add(Z.Text("Transition", ZuiText.Section, "Where it flies in from / out to."));
            host.Add(Z.Field("In from", "Edge it slides IN from (None = fade).",
                Z.MiniRadio((int)s.inFrom, DirLabels, "In direction.", v => Edit("Edit in-from", () => s.inFrom = (SplashDir)v), wrap: true)));
            host.Add(Z.Field("Out to", "Edge it slides OUT to (None = fade).",
                Z.MiniRadio((int)s.outTo, DirLabels, "Out direction.", v => Edit("Edit out-to", () => s.outTo = (SplashDir)v), wrap: true)));
            host.Add(Z.Field("Ease", "Easing for the slide/fade.",
                Z.MiniRadio((int)s.ease, EaseLabels, "Easing.", v => Edit("Edit ease", () => s.ease = (SplashEase)v), wrap: true)));
            host.Add(Z.MicroSlider("Anchor X", s.anchor.x, 0f, 1f, "Rest position X (viewport).",
                v => Edit("Edit anchor X", () => s.anchor = new Vector2(v, s.anchor.y)), 240f, prefsKey: "splash.ax"));
            host.Add(Z.MicroSlider("Anchor Y", s.anchor.y, 0f, 1f, "Rest position Y (viewport).",
                v => Edit("Edit anchor Y", () => s.anchor = new Vector2(s.anchor.x, v)), 240f, prefsKey: "splash.ay"));
            host.Add(Z.MicroSlider("Slide dist", s.slideDistance, 0.25f, 1.5f, "How far off-screen the slide reaches.",
                v => Edit("Edit slide distance", () => s.slideDistance = v), 240f, prefsKey: "splash.dist"));

            host.Add(Z.Text("Per-letter", ZuiText.Section, "Animate each letter independently."));
            host.Add(Z.Toggle("Animate each letter", "Stagger + spin each letter instead of moving the whole line as one block.",
                s.perLetter, v => Edit("Toggle per-letter", () => s.perLetter = v)));
            host.Add(Z.MicroSlider("Stagger (s)", s.letterStagger, 0f, 0.3f, "Seconds between consecutive letters starting.",
                v => Edit("Edit stagger", () => s.letterStagger = v), 240f, decimals: 3, prefsKey: "splash.stagger"));
            host.Add(Z.MicroSlider("Spin °", s.spinDegrees, -360f, 360f, "Degrees each letter spins through as it flies in/out (0 = none).",
                v => Edit("Edit spin", () => s.spinDegrees = v), 240f, decimals: 0, prefsKey: "splash.spin"));
            host.Add(Z.Field("Spin axis", "Axis the letter spins about (X/Y foreshorten to a flip under the flat camera; Z = in-plane).",
                Z.MiniRadio((int)s.spinAxis, AxisLabels, "Spin axis.", v => Edit("Edit spin axis", () => s.spinAxis = (SplashAxis)v))));

            host.Add(Z.Text("Colour cycle", ZuiText.Section, "Scroll the FILL colour over time (the border is untouched)."));
            host.Add(Z.Toggle("Cycle the fill colour", "Animate the fill face colour over time.",
                s.cycleFill, v => Edit("Toggle cycle", () => s.cycleFill = v)));
            host.Add(Z.MicroSlider("Speed", s.cycleSpeed, 0f, 4f, "Fill colour cycles per second.",
                v => Edit("Edit cycle speed", () => s.cycleSpeed = v), 240f, prefsKey: "splash.cyc"));
            host.Add(Z.MicroSlider("Per-letter", s.cyclePerLetter, 0f, 1f, "Cycle phase shift from one letter to the next (a travelling rainbow; per-letter mode only).",
                v => Edit("Edit cycle per-letter", () => s.cyclePerLetter = v), 240f, prefsKey: "splash.cycpl"));

            host.Add(BackSplashZui.Build(backSplash, "Preview backdrop",
                "The backdrop to audit the splash against (shared with Pyre / Mirage). Preview-only.", Repaint));
        }

        VisualElement BuildTransport(TextSplash s)
        {
            var row = Z.Row(
                Z.ToggleButton(_playing ? "⏸ Pause" : "▶ Play", "Play the whole in → hold → out (loops).",
                    _playing, v => { _playing = v; _scrub = 0f; _lastTick = EditorApplication.timeSinceStartup; Repaint(); }),
                Z.MicroSlider("t", _scrub, 0f, Mathf.Max(0.01f, EffectiveDuration(s)), "Scrub through the sequence.",
                    v => { _scrub = v; _playing = false; Repaint(); }, 220f, decimals: 2),
                Z.Text($"total {EffectiveDuration(s):0.00}s", ZuiText.Subtle, "Total play length."));
            row.style.flexShrink = 0f;
            return row;
        }

        void DrawPreview(TextSplash s)
        {
            var rect = GUILayoutUtility.GetRect(10f, 10f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (Event.current.type != EventType.Repaint) return;

            // Backdrop (BackSplash) — colour fill + optional image, exactly like Pyre's viewport.
            EditorGUI.DrawRect(rect, backSplash != null ? backSplash.cameraColor : new Color(0.08f, 0.08f, 0.10f));
            if (backSplash != null && backSplash.image != null && backSplash.image.texture != null)
            {
                var sp = backSplash.image;
                var tr = sp.textureRect;
                var tc = new Rect(tr.x / sp.texture.width, tr.y / sp.texture.height, tr.width / sp.texture.width, tr.height / sp.texture.height);
                var prev = GUI.color; GUI.color = backSplash.imageTint;
                GUI.DrawTextureWithTexCoords(rect, sp.texture, tc, true);
                GUI.color = prev;
            }

            // The splash: pose the world-space TMP at the scrub time, frame, and composite over the backdrop.
            if (_preview != null && _tmp != null && Current != null)
            {
                float rx = (Current.anchor.x - 0.5f) * PrevW;
                float ry = (Current.anchor.y - 0.5f) * PrevH;
                if (Current.perLetter)
                {
                    // The line rests at its anchor; each letter carries its own motion, in LOCAL vertex units
                    // (world ÷ the text's localScale) so the per-vertex offsets land at the right world distance.
                    _tmp.transform.position = new Vector3(rx, ry, 0f);
                    _tmp.alpha = 1f;
                    SplashPlayer.ApplyPerLetter(Current, _tmp, _scrub, Current.holdDuration, PrevW / PrevScale, PrevH / PrevScale);
                }
                else
                {
                    // Evaluate in the same frame units (PrevW×PrevH world): rest = anchor, offset = slide.
                    SplashPlayer.Evaluate(Current, _scrub, Current.holdDuration, PrevW, PrevH, out var off, out var a, out _);
                    _tmp.transform.position = new Vector3(rx + off.x, ry + off.y, 0f);
                    _tmp.alpha = a;
                    if (Current.cycleFill) SplashPlayer.ApplyCycle(Current, _tmp, _scrub);
                    _tmp.ForceMeshUpdate();   // push animated alpha (and any look/cycle change) into the mesh this frame
                }
                _preview.Frame(Vector3.zero, PrevH);
                _preview.Draw(rect);
            }
        }

        void Edit(string label, System.Action apply)
        {
            if (Current == null) return;
            Undo.RecordObject(Current, label);
            apply();
            EditorUtility.SetDirty(Current);
            RefreshLook();
            Repaint();
        }
    }
}
