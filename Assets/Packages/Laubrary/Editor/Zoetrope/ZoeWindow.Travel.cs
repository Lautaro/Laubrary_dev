using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zoetrope.Editor
{
    /// The Travel section of a custom event card: how far the move carries the character, drawn as an envelope
    /// under a timeline of the move, with a preview of the move travelling along a floor line.
    ///
    /// Everything here is opt-in per card. Closed, the section is one short row (its toggle and, when set, the
    /// distance it travels); nothing else is built until it is opened. The timeline and the envelope share one
    /// time axis: the timeline is inset to the envelope's own plot area, so a point on the curve sits exactly
    /// under the instant of the move above it.
    public partial class ZoeWindow
    {
        /// View state of one card's Travel section, keyed by the travel INSTANCE so it survives the rebuilds
        /// every edit triggers and never moves between cards on reorder. Never saved; never on the Undo stack.
        sealed class TravelView
        {
            public bool open, timing, playing;
            public float seconds;
        }
        static readonly ConditionalWeakTable<ReactionTravel, TravelView> s_travelViews = new();

        const float TravelStageHeight = 120f, TravelEnvelopeHeight = 96f;
        static readonly Color BandA = new Color(0.25f, 0.36f, 0.5f), BandB = new Color(0.2f, 0.29f, 0.41f);
        static readonly Color BandOwn = new Color(0.62f, 0.38f, 0.14f);

        void BuildTravelSection(VisualElement root, SerializedProperty reactionProp, Zoe zoe, int index)
        {
            var reaction = zoe.events != null && index < zoe.events.Count ? zoe.events[index]?.reaction : null;
            if (reaction == null) return;
            if (reaction.travel == null) reaction.travel = new ReactionTravel();
            var travel = reaction.travel;
            var view = s_travelViews.GetOrCreateValue(travel);

            var maxProp = reactionProp.FindPropertyRelative("travel.maxDistancePx");
            var bipolarProp = reactionProp.FindPropertyRelative("travel.bipolar");
            if (maxProp == null || bipolarProp == null) return;
            string maxPath = maxProp.propertyPath, bipolarPath = bipolarProp.propertyPath;

            var content = new VisualElement();
            content.style.display = view.open ? DisplayStyle.Flex : DisplayStyle.None;

            // ── the always-present row: the section toggle and, once set, what the move travels ──
            string summary = travel.IsAuthored ? $"→ {travel.EndPx:0.#} px" : "";
            var summaryText = Z.Text(summary, ZuiText.Subtle,
                "Where this move ends up, relative to where it started, in the art's pixels (forward = the way " +
                "the character faces).");
            var openToggle = Z.Toggle("Travel",
                "How far this move carries the character over its length. Opens a preview, a timeline and the " +
                "envelope that shapes the travel.", view.open, v =>
                {
                    view.open = v;
                    if (v && travel.envelope.Count == 0) SeedTravelEnvelope(zoe, travel);
                    content.style.display = v ? DisplayStyle.Flex : DisplayStyle.None;
                    if (v && content.childCount == 0) BuildTravelContent(content, travel, view, zoe, reaction, maxPath, bipolarPath, summaryText);
                });
            root.Add(Z.Row(openToggle, Z.HSpace(), summaryText, Z.Flexible()));
            root.Add(content);
            if (view.open) BuildTravelContent(content, travel, view, zoe, reaction, maxPath, bipolarPath, summaryText);
        }

        // A fresh travel starts as a straight line from the start to the full distance, so the first edit to
        // Max distance already does something visible.
        void SeedTravelEnvelope(Zoe zoe, ReactionTravel travel)
        {
            Undo.RecordObject(zoe, "Travel");
            travel.envelope.Add(new ZUIEnvelopePoint(0f, 0f));
            travel.envelope.Add(new ZUIEnvelopePoint(1f, 1f));
            EditorUtility.SetDirty(zoe);
        }

        void BuildTravelContent(VisualElement content, ReactionTravel travel, TravelView view, Zoe zoe,
                                ReactionFx reaction, string maxPath, string bipolarPath, Label summaryText)
        {
            content.Clear();
            var visual = ZoeEventVisual.Of(zoe, reaction);
            float total = visual.EventSeconds;
            float[] frameSecs = FrameSecondsOf(visual);

            // ── options: distance, polarity, frame timing, play ──
            ZuiEnvelope envelope = null;
            ZuiTimeline timeline = null;
            IMGUIContainer stage = null;
            Button playButton = null;

            void RefreshSummary() => summaryText.text = travel.IsAuthored ? $"→ {travel.EndPx:0.#} px" : "";
            void Show(float seconds)
            {
                view.seconds = total > 0f ? Mathf.Clamp(seconds, 0f, total) : 0f;
                timeline?.SetSecondsWithoutNotify(view.seconds);
                envelope?.SetPlayhead(total > 0f ? view.seconds / total : float.NaN);
                stage?.MarkDirtyRepaint();
            }

            const string maxTip = "The distance, in the art's pixels, that the top of the envelope stands for. " +
                "Scales the whole move without redrawing the curve. Negative = backwards.";
            var maxField = Z.Float(travel.maxDistancePx, maxTip, v =>
            {
                Commit(maxPath, p => p.floatValue = v);
                RefreshSummary();
                stage?.MarkDirtyRepaint();
            }, 56f);

            var polarity = Z.Segmented(travel.bipolar ? 1 : 0, new[] { "Uni", "Bi" },
                "Uni: the envelope runs from 0 to Max distance. Bi: from minus to plus Max distance, for a move " +
                "that goes one way and then the other.", i =>
                {
                    Commit(bipolarPath, p => p.boolValue = i == 1);
                    BuildTravelContent(content, travel, view, zoe, reaction, maxPath, bipolarPath, summaryText);
                    RefreshSummary();
                });

            var timingToggle = Z.Toggle("Frame timing",
                "Show the animation's frames on the timeline and as lines through the envelope, with each " +
                "frame's time from Launimator (where timing is edited). Orange bands are frames with their own " +
                "duration.", view.timing, v =>
                {
                    view.timing = v;
                    BuildTravelContent(content, travel, view, zoe, reaction, maxPath, bipolarPath, summaryText);
                });

            playButton = Z.Button(view.playing ? "❚❚" : "▶", "Play or pause the move with its travel.", () =>
            {
                view.playing = !view.playing;
                if (view.playing && view.seconds >= total) view.seconds = 0f;
                playButton.text = view.playing ? "❚❚" : "▶";
            }).W(30f);

            content.Add(Z.Row(playButton, Z.HSpace(), Z.Field("Max distance", maxTip, maxField),
                Z.Text("px", ZuiText.Subtle, maxTip), Z.HSpace(), polarity, Z.HSpace(), timingToggle, Z.Flexible()));

            if (total <= 0f || visual.Frames == null || visual.Frames.Length == 0)
            {
                content.Add(Z.Text("Pick a clip for this event to give the move a length.", ZuiText.Subtle,
                    "Travel is shaped over the move's length, which comes from its clip (or Fixed seconds)."));
                return;
            }

            // ── preview stage ──
            stage = new IMGUIContainer(() => DrawTravelStage(stage, visual, frameSecs, total, travel, view.seconds));
            stage.style.height = TravelStageHeight;
            stage.tooltip = "The move playing with its travel. The faint figure marks where it started; the " +
                "tick marks where it ends up.";
            content.Add(stage);

            // ── timeline, inset to the envelope's plot so both share one time axis ──
            timeline = Z.Timeline(view.seconds, "Scrub through the move. The line through the envelope below " +
                "follows the same instant.", s => { view.playing = false; playButton.text = "▶"; Show(s); });
            timeline.SetSegments(TravelSegments(visual, frameSecs, total, view.timing));
            content.Add(timeline);

            var options = new ZuiEnvelopeOptions
            {
                xMin = 0f, xMax = 1f,
                yMin = travel.bipolar ? -1f : 0f, yMax = 1f,
                minPoints = 2,
                gridRows = travel.bipolar ? 4 : 2,
                curveColor = new Color(1f, 0.72f, 0.3f),
                frameStarts01 = view.timing ? FrameStarts01(frameSecs, total) : null,
                showFrameLines = view.timing,
            };
            envelope = Z.Envelope(travel.envelope, options,
                "Where the character is over the move: across = the move's length, up = how much of Max " +
                "distance it has travelled at that moment. Click to add a point, drag to move, right-click to " +
                "remove, drag a segment vertically to bend it.",
                () =>
                {
                    EditorUtility.SetDirty(zoe);
                    RefreshSummary();
                    stage.MarkDirtyRepaint();
                },
                () => Undo.RecordObject(zoe, "Travel envelope"),
                float.NaN, TravelEnvelopeHeight);
            // Full width, like the timeline above it: the standard envelope style fixes a narrow width, and the
            // two only share a time axis if they span the same space.
            envelope.style.width = StyleKeyword.Auto;
            envelope.style.maxWidth = StyleKeyword.None;
            envelope.style.alignSelf = Align.Stretch;
            content.Add(envelope);

            void AlignToPlot()
            {
                if (!envelope.HasLayout) return;
                var plot = envelope.PlotRect;
                timeline.style.marginLeft = plot.x;
                timeline.style.marginRight = Mathf.Max(0f, envelope.contentRect.width - plot.xMax);
            }
            envelope.RegisterCallback<GeometryChangedEvent>(_ => AlignToPlot());

            // ── playback clock: lives on the section, so it stops when the card is rebuilt or closed ──
            double last = EditorApplication.timeSinceStartup;
            // One clock per section: a rebuild of this content (polarity, frame timing) replaces the old one.
            (content.userData as IVisualElementScheduledItem)?.Pause();
            content.userData = content.schedule.Execute(() =>
            {
                double now = EditorApplication.timeSinceStartup;
                float dt = (float)(now - last);
                last = now;
                if (!view.playing || !view.open) return;
                float next = view.seconds + dt;
                Show(next > total ? next - total : next);
            }).Every(16);
            Show(view.seconds);
        }

        // Each frame's time in seconds: the animation's own durations when it has them, else one tick at its fps.
        static float[] FrameSecondsOf(ZoeEventVisual visual)
        {
            int n = visual.Frames != null ? visual.Frames.Length : 0;
            if (visual.FrameSeconds != null && visual.FrameSeconds.Length == n) return visual.FrameSeconds;
            var s = new float[n];
            float each = visual.Fps > 0f ? 1f / visual.Fps : 0f;
            for (int i = 0; i < n; i++) s[i] = each;
            return s;
        }

        // Frame `i` of the clip, repeated to fill the move (loops, or a Fixed-seconds length), from start to end.
        static List<(int frame, float start, float length)> FrameSpans(float[] frameSecs, float total)
        {
            var spans = new List<(int, float, float)>();
            float clip = 0f;
            foreach (float s in frameSecs) clip += s;
            if (clip <= 0f || total <= 0f) return spans;
            float t = 0f;
            for (int guard = 0; t < total - 1e-5f && guard < 4096; guard++)
            {
                int i = guard % frameSecs.Length;
                float len = Mathf.Min(frameSecs[i], total - t);
                if (len <= 0f) break;
                spans.Add((i, t, len));
                t += len;
            }
            return spans;
        }

        static ZuiTimelineSegment[] TravelSegments(ZoeEventVisual visual, float[] frameSecs, float total, bool timing)
        {
            if (!timing) return new[] { new ZuiTimelineSegment("Move", total, BandA, $"The whole move: {total:0.###} s.") };
            var spans = FrameSpans(frameSecs, total);
            bool own = visual.FrameSeconds != null;
            var segs = new ZuiTimelineSegment[spans.Count];
            for (int k = 0; k < spans.Count; k++)
            {
                var (frame, _, len) = spans[k];
                bool timed = own && visual.Fps > 0f && Mathf.Abs(frameSecs[frame] - 1f / visual.Fps) > 1e-4f;
                string tip = timed
                    ? $"Frame {frame + 1}: {frameSecs[frame] * 1000f:0} ms (its own timing, set in Launimator)."
                    : $"Frame {frame + 1}: {frameSecs[frame] * 1000f:0} ms ({visual.Fps:0.#} fps).";
                segs[k] = new ZuiTimelineSegment((frame + 1).ToString(), len, timed ? BandOwn : (k % 2 == 0 ? BandA : BandB), tip);
            }
            return segs;
        }

        static float[] FrameStarts01(float[] frameSecs, float total)
        {
            var spans = FrameSpans(frameSecs, total);
            var starts = new float[spans.Count];
            for (int k = 0; k < spans.Count; k++) starts[k] = spans[k].start / total;
            return starts;
        }

        /// The preview: the move's frame at `seconds`, standing on a floor line, shifted by the travel. Drawn
        /// facing right; a faint copy marks where it started and a tick marks where it ends up.
        static void DrawTravelStage(IMGUIContainer stage, ZoeEventVisual visual, float[] frameSecs, float total,
                                    ReactionTravel travel, float seconds)
        {
            if (stage == null) return;
            Rect r = new Rect(0f, 0f, stage.layout.width, stage.layout.height);
            if (!(r.width > 20f) || visual.Frames == null || visual.Frames.Length == 0) return;
            EditorGUI.DrawRect(r, new Color(0.11f, 0.12f, 0.14f));

            // How much room the move needs: the curve's extent plus the widest frame.
            float minPx = 0f, maxPx = 0f;
            for (int i = 0; i <= 48; i++) { float o = travel.OffsetPx(i / 48f); minPx = Mathf.Min(minPx, o); maxPx = Mathf.Max(maxPx, o); }
            float spriteW = 1f, spriteH = 1f;
            foreach (var s in visual.Frames)
                if (s != null) { spriteW = Mathf.Max(spriteW, s.rect.width); spriteH = Mathf.Max(spriteH, s.rect.height); }
            float floorY = r.yMax - 14f;
            float scale = Mathf.Min((floorY - 6f) / spriteH, (r.width - 24f) / (maxPx - minPx + spriteW));
            scale = Mathf.Max(0.25f, scale);
            float originX = r.center.x - (minPx + maxPx) * 0.5f * scale;

            EditorGUI.DrawRect(new Rect(r.x + 6f, floorY, r.width - 12f, 1f), new Color(1f, 1f, 1f, 0.35f));

            int frame = FrameAtSeconds(frameSecs, seconds);
            float offset = total > 0f ? travel.OffsetPx(seconds / total) : 0f;
            DrawStageSprite(visual.Frames[0], originX, floorY, scale, new Color(1f, 1f, 1f, 0.22f));
            DrawStageSprite(visual.Frames[Mathf.Clamp(frame, 0, visual.Frames.Length - 1)], originX + offset * scale, floorY, scale, Color.white);

            float endX = originX + travel.EndPx * scale;
            EditorGUI.DrawRect(new Rect(endX - 0.5f, floorY - 5f, 1f, 10f), new Color(1f, 0.72f, 0.3f, 0.9f));
            GUI.Label(new Rect(r.x + 6f, floorY + 1f, r.width - 12f, 13f),
                $"{offset:0.#} px   ·   {seconds:0.00} / {total:0.00} s", EditorStyles.miniLabel);
        }

        static int FrameAtSeconds(float[] frameSecs, float seconds)
        {
            float clip = 0f;
            foreach (float s in frameSecs) clip += s;
            if (clip <= 0f || frameSecs.Length == 0) return 0;
            float t = Mathf.Repeat(seconds, clip);
            for (int i = 0; i < frameSecs.Length; i++) { t -= frameSecs[i]; if (t < 0f) return i; }
            return frameSecs.Length - 1;
        }

        // A sprite with its pivot standing on (x, floorY), point-sampled at `scale`.
        static void DrawStageSprite(Sprite s, float x, float floorY, float scale, Color tint)
        {
            if (s == null || s.texture == null) return;
            var tr = s.textureRect;
            var dest = new Rect(x - s.pivot.x * scale, floorY - (tr.height - s.pivot.y) * scale, tr.width * scale, tr.height * scale);
            var uv = new Rect(tr.x / s.texture.width, tr.y / s.texture.height, tr.width / s.texture.width, tr.height / s.texture.height);
            var prev = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(dest, s.texture, uv, true);
            GUI.color = prev;
        }
    }
}
