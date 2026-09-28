using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// UI Toolkit twin of the Klip editor's waveform block (AudioSpectrumView.DrawLayout, T-0468): the toolbar (Trim/Clamp,
    /// Volume and Pitch with their edit-handle toggles, the length) and the 150 px waveform area with its background,
    /// waveform image (whole clip, or the detailed visible range when zoomed), trim dims and handles, playheads, and the
    /// volume and pitch envelopes drawn over it.
    ///
    /// The old view object is the model: every rule (the view window, the zoom, which texture to show, trim geometry and
    /// drags, playhead positions, the overlay set-up) is its shared code, and its callbacks are wired by the old window's
    /// own <c>WireSpectrumView</c>. This element only draws and routes input — in the old order: wheel zoom, the right-
    /// drag on either trim handle (moves both), the end handle, the start handle, then the volume envelope, then the pitch
    /// envelope, each offered the press only if the one before did not take it (IMGUI offers every control each event in
    /// draw order; in UI Toolkit only the topmost element would get it, so the envelopes do not pick and this host asks them).
    /// </summary>
    public class KlipWaveformTK : VisualElement {

        const float AreaH = 150f;
        readonly AudioSpectrumView model;
        readonly Klip klip;
        readonly ZuiToggleButton trim, clamp, vol, volEdit, pitch, pitchEdit, keepLen, time, timeEdit;
        readonly Label length;
        readonly VisualElement box, area, bg, dimStart, dimEnd, handleStart, handleEnd, heads;
        readonly VisualElement[] xmix;
        // The pitch curve's axis (T-0479): top / middle / bottom labels and the "no change" line, and the old-scale warning.
        readonly Label pitchTop, pitchMid, pitchBottom, pitchOld;
        readonly VisualElement pitchLine;
        readonly Image wave;
        readonly ZuiSkinEnvelope volEnv, pitchEnv, timeEnv;
        // The time curve's axis on the right (T-0482): x4 / x1 / x1/4 speed and the "unchanged" line.
        readonly Label timeTop, timeMid, timeBottom;
        readonly VisualElement timeLine;
        ZuiSkinEnvelope active;
        bool trimDragging;
        readonly List<VisualElement> headPool = new List<VisualElement>();

        public KlipWaveformTK(AudioSpectrumView model, Klip klip) {
            this.model = model; this.klip = klip;
            style.flexShrink = 0;
            float lh = EditorGUIUtility.singleLineHeight;

            // ── toolbar ──
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row; bar.style.height = lh; bar.style.flexShrink = 0;
            trim = ZS.Toggle("Trim", "", model.TrimEnabled, v => { model.SetTrimEnabled(v); Refresh(); }, "RichToggle", ZUICornerMask.Left, 60f, lh);
            clamp = ZS.Toggle("Clamp", "", model.ClampToTrim, v => { model.SetClampToTrim(v); Refresh(); }, "RichToggle", ZUICornerMask.Right, 60f, lh);
            vol = ZS.Toggle("Volume", "", model.VolumeEnvelope.enabled, v => { model.RequestVolumeEnabled(v); Refresh(); }, "RichToggle", ZUICornerMask.Left, 75f, lh);
            volEdit = IconToggle(model.ShowVolumeHandles, v => { model.SetShowVolumeHandles(v); Refresh(); });
            pitch = ZS.Toggle("Pitch", "", model.PitchEnvelope.enabled, v => { model.RequestPitchEnabled(v); Refresh(); }, "RichToggle", ZUICornerMask.Left, 65f, lh);
            pitchEdit = IconToggle(model.ShowPitchHandles, v => { model.SetShowPitchHandles(v); Refresh(); });
            length = new Label();
            length.AddToClassList("zs-lbl"); length.AddToClassList("zs-mini");
            length.style.width = 50f; length.style.flexShrink = 0;
            // A fixed slot beside the Pitch toggle, shown only while the pitch curve is still on its old scale.
            pitchOld = new Label("\u26A0") { tooltip = KlipChainEnvelopes.OldScaleTip };
            pitchOld.AddToClassList("zs-lbl");
            pitchOld.style.width = 16f; pitchOld.style.flexShrink = 0; pitchOld.style.unityTextAlign = TextAnchor.MiddleCenter;
            pitchOld.style.visibility = Visibility.Hidden;
            // Keep length (on the pitch curve) and the time curve, T-0482. Present only for a Klip's chain curves.
            keepLen = ZS.Toggle("Keep length", AudioSpectrumView.KeepLengthTip(model.KeepLength), model.KeepLength,
                v => { model.RequestKeepLength(v); keepLen.tooltip = AudioSpectrumView.KeepLengthTip(v); Refresh(); }, "RichToggle", ZUICornerMask.All, 90f, lh);
            time = ZS.Toggle("Time", AudioSpectrumView.TimeTip, model.TimeEnvelope.enabled, v => { model.RequestTimeEnabled(v); Refresh(); }, "RichToggle", ZUICornerMask.Left, 60f, lh);
            timeEdit = IconToggle(model.ShowTimeHandles, v => { model.SetShowTimeHandles(v); Refresh(); });
            bar.Add(trim); bar.Add(clamp); bar.Add(Gap(6f)); bar.Add(vol); bar.Add(volEdit); bar.Add(Gap(6f)); bar.Add(pitch); bar.Add(pitchEdit); bar.Add(pitchOld);
            if (model.HasKlip) { bar.Add(Gap(2f)); bar.Add(keepLen); bar.Add(Gap(6f)); bar.Add(time); bar.Add(timeEdit); }
            var flex = new VisualElement(); flex.style.flexGrow = 1; bar.Add(flex);
            bar.Add(length);
            Add(bar);
            Add(Space(5f));   // ZUI.RowSpace(0.5f)

            // ── the waveform area: GUI.Box, then the picture inset by 4 ──
            box = new VisualElement();
            box.AddToClassList("zs-waveform-box");
            box.style.height = AreaH; box.style.flexShrink = 0;
            Add(box);
            area = new VisualElement { tooltip = "Mouse wheel: zoom within the trimmed range." };
            area.AddToClassList("zs-waveform-area");
            area.style.position = Position.Absolute; area.style.left = 4; area.style.right = 4; area.style.top = 4; area.style.bottom = 4;
            // Not clipped: the old view lets an envelope's end handles spill past the picture's edge, and so does this.
            box.Add(area);
            bg = Abs(); area.Add(bg);
            wave = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            wave.style.position = Position.Absolute; wave.style.left = 0; wave.style.right = 0; wave.style.top = 0; wave.style.bottom = 0;
            area.Add(wave);
            dimStart = Abs(); dimEnd = Abs(); area.Add(dimStart); area.Add(dimEnd);
            // A Looper's crossmix spans (T-0476): the longest possible span, and the part every loop uses, at both ends.
            xmix = new VisualElement[4];
            for (int i = 0; i < 4; i++) {
                xmix[i] = Abs();
                xmix[i].AddToClassList("zs-xmix");
                if (i >= 2) xmix[i].AddToClassList("zs-xmix--min");
                area.Add(xmix[i]);
            }
            pitchLine = Abs(); area.Add(pitchLine);
            pitchTop = AxisLabel(); pitchMid = AxisLabel(); pitchBottom = AxisLabel();
            area.Add(pitchTop); area.Add(pitchMid); area.Add(pitchBottom);
            timeLine = Abs(); area.Add(timeLine);
            timeTop = AxisLabel(); timeMid = AxisLabel(); timeBottom = AxisLabel();
            foreach (var l in new[] { timeTop, timeMid, timeBottom }) { l.style.unityTextAlign = TextAnchor.UpperRight; l.style.width = 26f; area.Add(l); }
            timeTop.text = "×4"; timeMid.text = "×1"; timeBottom.text = "×¼";
            heads = Abs(); area.Add(heads);
            handleStart = Abs(); handleEnd = Abs(); area.Add(handleStart); area.Add(handleEnd);
            handleStart.AddToClassList("zs-trimhandle"); handleEnd.AddToClassList("zs-trimhandle");
            volEnv = new ZuiSkinEnvelope(null, Color.white, null, null, standalone: false) { pickingMode = PickingMode.Ignore };
            pitchEnv = new ZuiSkinEnvelope(null, Color.white, null, null, standalone: false) { pickingMode = PickingMode.Ignore };
            timeEnv = new ZuiSkinEnvelope(null, Color.white, null, null, standalone: false) { pickingMode = PickingMode.Ignore };
            foreach (var e in new[] { volEnv, pitchEnv, timeEnv }) { e.style.position = Position.Absolute; area.Add(e); }
            // Right-click a point: its random settings (T-0483). The pitch curve moves off its old scale first, inside
            // the same Undo step, as any other edit of it does.
            foreach (var e in new[] { volEnv, pitchEnv, timeEnv }) {
                var env = e;
                env.onPointContext = (i, world) => {
                    if (env.points == null || i < 0 || i >= env.points.Count || env.rt == null) return;
                    RandomPointPopup.Show(world, env.points[i], Mathf.Max(env.rt.dataXMax - env.rt.dataXMin, 1e-3f),
                        () => env.rt.yMax - env.rt.yMin,
                        () => { if (env == pitchEnv) KlipChainEnvelopes.EnsurePitchRatio(klip); },
                        () => { KlipChainEnvelopes.Touch(klip); Refresh(); });
                };
            }
            area.focusable = true;

            area.RegisterCallback<WheelEvent>(OnWheel);
            area.RegisterCallback<PointerDownEvent>(OnDown);
            area.RegisterCallback<PointerMoveEvent>(OnMove);
            area.RegisterCallback<PointerUpEvent>(OnUp);
            area.RegisterCallback<PointerLeaveEvent>(_ => { volEnv.PointerLeft(); pitchEnv.PointerLeft(); timeEnv.PointerLeft(); });
            area.RegisterCallback<KeyDownEvent>(e => { if (volEnv.KeyDown(e.keyCode) | pitchEnv.KeyDown(e.keyCode) | timeEnv.KeyDown(e.keyCode)) e.StopPropagation(); });
            area.RegisterCallback<GeometryChangedEvent>(_ => Refresh());
            schedule.Execute(Refresh).Every(33);
        }

        static VisualElement Gap(float w) { var e = new VisualElement(); e.style.width = w; e.style.flexShrink = 0; return e; }

        static Label AxisLabel() {
            var l = new Label { pickingMode = PickingMode.Ignore };
            l.AddToClassList("zs-lbl"); l.AddToClassList("zs-mini");
            l.style.position = Position.Absolute; l.style.left = 3f; l.style.width = 44f; l.style.height = 13f;
            // A dark backing so a label reads on the bright waveform as well as on the dimmed parts.
            l.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            l.style.paddingLeft = 2f; l.style.paddingRight = 2f;
            l.style.borderTopLeftRadius = l.style.borderTopRightRadius = l.style.borderBottomLeftRadius = l.style.borderBottomRightRadius = 2f;
            return l;
        }

        /// <summary>The time curve's axis on the right of the envelope area (T-0482).</summary>
        void PlaceTimeAxis(Rect envRect) {
            bool on = model.TimeEnvelope.enabled;
            foreach (var e in new VisualElement[] { timeTop, timeMid, timeBottom, timeLine }) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) return;
            var c = AudioSpectrumView.TimeCurveColor;
            foreach (var l in new[] { timeTop, timeMid, timeBottom }) { l.style.color = c; l.style.left = envRect.xMax - 29f; }
            timeTop.style.top = envRect.y + 1f;
            timeBottom.style.top = envRect.yMax - 14f;
            float my = Mathf.Round(envRect.y + envRect.height * 0.5f);
            timeMid.style.top = my - 14f;
            timeLine.style.left = envRect.x; timeLine.style.width = Mathf.Max(0f, envRect.width);
            timeLine.style.top = my; timeLine.style.height = 1f;
            timeLine.style.backgroundColor = new Color(c.r, c.g, c.b, 0.3f);
        }

        /// <summary>The pitch curve's axis on the waveform (T-0479): what its top, middle and bottom mean, and the middle
        /// line that is "no change" -- or, for a curve still on its old scale, only the warning beside the toggle.</summary>
        void PlacePitchAxis(Rect r, Rect envRect) {
            string top = null, mid = null, bottom = null; bool oldScale = false;
            bool on = model.PitchEnvelope.enabled && KlipChainEnvelopes.PitchAxis(klip, out top, out mid, out bottom, out oldScale);
            bool old = on && oldScale;
            pitchOld.style.visibility = old ? Visibility.Visible : Visibility.Hidden;
            bool axis = on && !old && top != null;
            foreach (var e in new VisualElement[] { pitchTop, pitchMid, pitchBottom }) e.style.display = axis ? DisplayStyle.Flex : DisplayStyle.None;
            pitchLine.style.display = axis && mid != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (!axis) return;
            var c = ZoundsProject.Instance.projectSettings.editorStyle.pitchEnvelopeColor;
            foreach (var l in new[] { pitchTop, pitchMid, pitchBottom }) l.style.color = new Color(c.r, c.g, c.b, 0.9f);
            pitchTop.text = top; pitchMid.text = mid ?? ""; pitchBottom.text = bottom;
            float x = envRect.x + 3f;
            pitchTop.style.left = pitchMid.style.left = pitchBottom.style.left = x;
            pitchTop.style.top = envRect.y + 1f;
            pitchBottom.style.top = envRect.yMax - 14f;
            float my = Mathf.Round(envRect.y + envRect.height * 0.5f);
            pitchMid.style.top = my - 14f;
            pitchLine.style.left = envRect.x; pitchLine.style.width = Mathf.Max(0f, envRect.width);
            pitchLine.style.top = my; pitchLine.style.height = 1f;
            pitchLine.style.backgroundColor = new Color(c.r, c.g, c.b, 0.35f);
        }
        static VisualElement Space(float h) { var e = new VisualElement(); e.style.height = h; e.style.flexShrink = 0; return e; }
        static VisualElement Abs() {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            return e;
        }

        /// <summary>ZUI.Toggle(value, "", editIcon, editIcon, RichToggle, Right, 25 wide): the edit-handles toggle, its face the pencil icon.</summary>
        ZuiToggleButton IconToggle(bool value, System.Action<bool> onChanged) {
            var t = ZS.Toggle("", "", value, onChanged, "RichToggle", ZUICornerMask.Right, 25f, EditorGUIUtility.singleLineHeight);
            t.markWhenOn = false;
            var img = new Image { image = AudioSpectrumView.editIcon, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            img.AddToClassList("zs-editicon");
            t.Add(img);
            return t;
        }

        Rect AreaRect => new Rect(0f, 0f, area.contentRect.width, area.contentRect.height);

        // ─────────────────────────── refresh ───────────────────────────

        public void Refresh() {
            if (panel == null) return;
            var clip = model.OriginalClip;
            style.display = clip == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (clip == null) return;
            model.BeginFrame(false);
            var es = ZoundsProject.Instance.projectSettings.editorStyle;

            trim.SetValueWithoutNotify(model.TrimEnabled);
            clamp.SetValueWithoutNotify(model.ClampToTrim);
            vol.SetValueWithoutNotify(model.VolumeEnvelope.enabled);
            pitch.SetValueWithoutNotify(model.PitchEnvelope.enabled);
            volEdit.SetValueWithoutNotify(model.ShowVolumeHandles);
            pitchEdit.SetValueWithoutNotify(model.ShowPitchHandles);
            length.text = model.LengthText;

            var r = AreaRect;
            if (r.width <= 1f || r.height <= 1f) return;
            bg.style.left = 0; bg.style.top = 0; bg.style.width = r.width; bg.style.height = r.height;
            bg.style.backgroundColor = es.klipWaveformBGColor;

            // The IMGUI view sizes its texture in whole points of its rect; the same numbers here give the same texture.
            var tex = model.WaveformImage(Mathf.FloorToInt(r.width), Mathf.FloorToInt(r.height), true, out var uv);
            if (wave.image != tex) wave.image = tex;
            wave.uv = uv;

            Rect trimmed = r;
            bool trimOn = model.TrimEnabled;
            dimStart.style.display = dimEnd.style.display = handleStart.style.display = handleEnd.style.display = trimOn ? DisplayStyle.Flex : DisplayStyle.None;
            if (trimOn) {
                var hs = model.TrimStartHandle(r, out var ds);
                var he = model.TrimEndHandle(r, out var de);
                Place(dimStart, ds); Place(dimEnd, de);
                dimStart.style.backgroundColor = dimEnd.style.backgroundColor = es.trimAreaColor;
                model.TrimHandlesLive(out bool startLive, out bool endLive);
                var hc = es.trimHandleColor;
                if (!enabledInHierarchy) hc.a = 0.35f;
                Place(handleStart, hs); Place(handleEnd, he);
                handleStart.style.backgroundColor = handleEnd.style.backgroundColor = hc;
                handleStart.style.display = startLive ? DisplayStyle.Flex : DisplayStyle.None;
                handleEnd.style.display = endLive ? DisplayStyle.Flex : DisplayStyle.None;
                trimmed = Rect.MinMaxRect(model.TimeToXIn(klip.trimStart, r), r.y, model.TimeToXIn(klip.trimEnd, r), r.yMax);
            }

            PlaceCrossmix(r, clip.length);

            // Playheads, drawn over the dims and under the envelopes, as the old view draws them.
            ZoundEngine.CullingGroups.TryGetValue(klip, out var playing);
            var fractions = model.PlayheadFractions(playing, out _);
            Place(heads, r);
            for (int i = 0; i < fractions.Count; i++) {
                if (i >= headPool.Count) headPool.Add(PlayHead());
                var h = headPool[i];
                if (h.parent != heads) heads.Add(h);
                h.style.display = DisplayStyle.Flex;
                h.style.left = trimmed.x + fractions[i] * trimmed.width;
                h.style.top = trimmed.y; h.style.height = trimmed.height;
            }
            for (int i = fractions.Count; i < headPool.Count; i++) headPool[i].style.display = DisplayStyle.None;

            // The envelopes, over the trimmed range when clamped, else the whole area.
            var envRect = model.ClampToTrim ? trimmed : r;
            PlacePitchAxis(r, envRect);
            PlaceTimeAxis(envRect);
            Overlay(volEnv, AudioSpectrumView.Curve.Volume, envRect);
            Overlay(pitchEnv, AudioSpectrumView.Curve.Pitch, envRect);
            Overlay(timeEnv, AudioSpectrumView.Curve.Time, envRect);
            if (model.HasKlip) {
                keepLen.SetValueWithoutNotify(model.KeepLength);
                time.SetValueWithoutNotify(model.TimeEnvelope.enabled);
                timeEdit.SetValueWithoutNotify(model.ShowTimeHandles);
            }
        }

        void Overlay(ZuiSkinEnvelope env, AudioSpectrumView.Curve which, Rect rect) {
            var def = model.PrepareOverlay(which, out var runtime, out var pts, out var colour);
            env.style.display = def != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (def == null) return;
            env.points = pts; env.def = def; env.rt = runtime; env.curveColor = colour;
            // What the plays under way hear, dotted (T-0484); nothing while the sound is not playing.
            var authored = which == AudioSpectrumView.Curve.Volume ? model.VolumeEnvelope
                         : which == AudioSpectrumView.Curve.Pitch ? model.PitchEnvelope : model.TimeEnvelope;
            LiveDrawnCurves.Fill(ref env.liveCurves, klip, authored, KlipChainEnvelopes.ModifierIndexOf(klip, authored));
            Place(env, rect);
            env.Repaint();
        }

        /// <summary>
        /// Shades the parts of a Looper's source its crossmix uses: at the start, what the incoming copy fades in over;
        /// at the end, what the outgoing copy fades out over. The faint band is the longest crossmix the range allows,
        /// the stronger one inside it the shortest, so a fixed length shows as one strong band and a random range shows
        /// how far it can reach. Bounds are the ones the engine hears (limited to half the loop).
        /// </summary>
        void PlaceCrossmix(Rect r, float clipLength) {
            bool show = klip.IsLooper;
            float from = 0f, to = 0f, lo = 0f, hi = 0f;
            if (show) {
                from = klip.trimEnabled ? klip.trimStart : 0f;
                to = klip.trimEnabled && klip.trimEnd > klip.trimStart ? Mathf.Min(klip.trimEnd, clipLength) : clipLength;
                klip.loop.Effective(to - from, out lo, out hi);
                show = hi > 0f;
            }
            for (int i = 0; i < 4; i++) xmix[i].style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            float x0 = model.TimeToXIn(from, r), x1 = model.TimeToXIn(to, r);
            Place(xmix[0], Rect.MinMaxRect(x0, r.y, model.TimeToXIn(from + hi, r), r.yMax));
            Place(xmix[1], Rect.MinMaxRect(model.TimeToXIn(to - hi, r), r.y, x1, r.yMax));
            xmix[2].style.display = xmix[3].style.display = lo > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            Place(xmix[2], Rect.MinMaxRect(x0, r.y, model.TimeToXIn(from + lo, r), r.yMax));
            Place(xmix[3], Rect.MinMaxRect(model.TimeToXIn(to - lo, r), r.y, x1, r.yMax));
        }

        static void Place(VisualElement e, Rect r) {
            e.style.left = r.x; e.style.top = r.y; e.style.width = Mathf.Max(0f, r.width); e.style.height = Mathf.Max(0f, r.height);
        }

        VisualElement PlayHead() {
            var es = ZoundsProject.Instance.projectSettings.editorStyle;
            var root = Abs();
            root.style.width = 0f;
            var cursor = new Image { image = AudioWaveformUtility.playerHeadTexture, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore, tintColor = AudioWaveformUtility.playerHeadColor };
            var sz = AudioWaveformUtility.playerHeadSize;
            cursor.style.position = Position.Absolute; cursor.style.left = -sz.x / 2f; cursor.style.top = 0; cursor.style.width = sz.x; cursor.style.height = sz.x * 1.82f;
            var line = Abs();
            float th = AudioWaveformUtility.playerHeadThickness;
            line.style.left = -th * 0.5f; line.style.width = th; line.style.top = sz.y; line.style.bottom = 0;
            line.style.backgroundColor = AudioWaveformUtility.playerHeadColor;
            root.Add(line); root.Add(cursor);
            return root;
        }

        // ─────────────────────────── input ───────────────────────────

        void OnWheel(WheelEvent e) {
            var r = AreaRect;
            if (model.WheelZoom(e.localMousePosition.x, r, e.delta.y)) { Refresh(); e.StopPropagation(); }
        }

        void OnDown(PointerDownEvent e) {
            var m = (Vector2)e.localPosition;
            var r = AreaRect;
            model.BeginFrame(true);
            area.Focus();
            if (model.TrimEnabled) {
                var hs = model.TrimStartHandle(r, out _);
                var he = model.TrimEndHandle(r, out _);
                model.TrimHandlesLive(out bool startLive, out bool endLive);
                float time = model.XToTimeIn(m.x, r);
                // Right-drag on either thin handle (with a little slop) moves both.
                if (e.button == 1) {
                    const float slop = 3f;
                    if (new Rect(hs.x - slop, hs.y, hs.width + slop * 2f, hs.height).Contains(m) ||
                        new Rect(he.x - slop, he.y, he.width + slop * 2f, he.height).Contains(m)) {
                        StartTrim(AudioSpectrumView.TrimDrag.Both, time, e); return;
                    }
                }
                if (endLive && he.Contains(m) && (e.button == 0 || e.button == 1)) { StartTrim(e.button == 0 ? AudioSpectrumView.TrimDrag.End : AudioSpectrumView.TrimDrag.Both, time, e); return; }
                if (startLive && hs.Contains(m) && (e.button == 0 || e.button == 1)) { StartTrim(e.button == 0 ? AudioSpectrumView.TrimDrag.Start : AudioSpectrumView.TrimDrag.Both, time, e); return; }
            }
            foreach (var env in new[] { timeEnv, volEnv, pitchEnv }) {
                if (env.resolvedStyle.display == DisplayStyle.None || env.rt == null) continue;
                if (env.PointerDown(area.ChangeCoordinatesTo(env, m), e.button, e.clickCount, e.shiftKey)) {
                    active = env;
                    area.CapturePointer(e.pointerId);
                    e.StopPropagation();
                    return;
                }
            }
        }

        void StartTrim(AudioSpectrumView.TrimDrag which, float time, PointerDownEvent e) {
            model.BeginTrimDrag(which, time);
            trimDragging = true;
            area.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e) {
            var m = (Vector2)e.localPosition;
            if (trimDragging) {
                model.BeginFrame(true);
                if (model.DragTrim(model.XToTimeIn(m.x, AreaRect))) Refresh();
                return;
            }
            if (active != null) { active.PointerMove(area.ChangeCoordinatesTo(active, m), e.deltaPosition, e.shiftKey, e.pressedButtons); return; }
            foreach (var env in new[] { volEnv, pitchEnv, timeEnv })
                if (env.resolvedStyle.display != DisplayStyle.None && env.rt != null)
                    env.PointerMove(area.ChangeCoordinatesTo(env, m), e.deltaPosition, e.shiftKey, e.pressedButtons);
        }

        void OnUp(PointerUpEvent e) {
            if (area.HasPointerCapture(e.pointerId)) area.ReleasePointer(e.pointerId);
            if (trimDragging) { trimDragging = false; model.EndTrimDrag(); }
            if (active != null) { active.PointerUp(); active = null; }
            onReleased?.Invoke();
            Refresh();
        }

        /// <summary>After any press ends on the waveform — the host closes its drag Undo step here, as the old window does
        /// on its mouse-up.</summary>
        public System.Action onReleased;
    }
}
