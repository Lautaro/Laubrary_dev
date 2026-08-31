import io
p = 'Assets/ChunksMock/Editor/ChunksMockWindow.cs'
s = io.open(p, encoding='utf-8').read()


def rep(old, new, n=1):
    global s
    assert s.count(old) == n, "MISS(%d!=%d): %r" % (s.count(old), n, old[:90])
    s = s.replace(old, new, n)


# H: BuildTiming -> AddTimingRow, gated on the recipe holding a SECOND timed capability
rep(
"""        /// The two dials that place a capability on the shared timing line. A capability that occupies no
        /// time never shows them, which is why a Layer Plan's card has no timing row at all.
        void BuildTiming(VisualElement card, MockCapability cap)
        {
            if (!cap.Timed) return;
            card.Add(Z.HGroup(
                Z.MicroSlider("Delay", cap.delay, 0f, 2f, "Seconds of dead time before this capability begins.",
                    v => Change(() => cap.delay = v), width: 130f, decimals: 2),
                Z.MicroSlider("Duration", cap.duration, 0.05f, 3f, "Seconds this capability occupies.",
                    v => Change(() => cap.duration = v), width: 130f, decimals: 2)));
        }""",
"""        /// The dials that place a capability on the recipe's shared clock, packed onto a row with whatever
        /// `leading` controls the card passes. They appear only once the recipe holds a SECOND timed
        /// capability: with one, there is nothing to be early or late relative to and no clock is drawn, so
        /// a Delay would dial something the window cannot show. A capability that occupies no time never
        /// shows them at all, which is why a Layer Plan's card has no timing row.
        void AddTimingRow(VisualElement card, MockCapability cap, params VisualElement[] leading)
        {
            var row = new List<VisualElement>(leading);
            if (cap.Timed && TimedCapabilities().Count > 1)
            {
                row.Add(Z.MicroSlider("Delay", cap.delay, 0f, 2f,
                    "Seconds from the start of the recipe until this capability begins. Capabilities may overlap.",
                    v => Change(() => cap.delay = v), width: 130f, decimals: 2));
                row.Add(Z.MicroSlider("Duration", cap.duration, 0.05f, 3f, "Seconds this capability occupies.",
                    v => Change(() => cap.duration = v), width: 130f, decimals: 2));
            }
            if (row.Count > 0) card.Add(Z.HGroup(row.ToArray()));
        }""")

# I: the workspace's timing surface
rep(
"""            // The timeline is SHARED, so it only means anything once two enabled capabilities compete for
            // time. Below the workspace, never above it: appearing must not shove the guide down the pane.
            timeline = null;
            if (EnabledTimed().Count < 2) return;

            var timing = Z.Section("Timing",
                "The order the enabled time-occupying capabilities run in. Drag the playhead to read the sequence.",
                "chunks.mock.timing", icon: "timer");
            timeline = Z.Timeline(0f, "The recipe's shared sequence. Each band is one capability's own stretch of it.");
            timing.Add(timeline);
            root.Add(timing);
            RefreshTimeline();
        }

        List<MockCapability> EnabledTimed()
        {
            EnsureRecipe();
            var list = new List<MockCapability>();
            foreach (var cap in recipe.capabilities)
                if (cap.Enabled && cap.Timed) list.Add(cap);
            return list;
        }

        /// A zero-length band is legal and paints nothing, so the delay gap never has to be omitted.
        void RefreshTimeline()
        {
            if (timeline == null) return;
            var caps = EnabledTimed();
            var segments = new List<ZuiTimelineSegment>(caps.Count * 2);
            foreach (var cap in caps)
            {
                segments.Add(new ZuiTimelineSegment(string.Empty, cap.delay, new Color(0.32f, 0.34f, 0.4f, 0.6f),
                    "Dead time before " + cap.DisplayName + " begins."));
                segments.Add(new ZuiTimelineSegment(cap.DisplayName, cap.duration, cap.BandColor,
                    cap.DisplayName + " occupies this stretch of the recipe."));
            }
            timeline.SetSegments(segments.ToArray());
        }

        /// A value-only edit repaints the guide and re-bands the timeline; it never rebuilds the window,
        /// so a slider drag cannot pull the control out from under the pointer.
        void Change(Action edit)
        {
            edit?.Invoke();
            stage?.MarkDirtyRepaint();
            RefreshTimeline();
        }""",
"""            // The clock is SHARED, so it only means anything once two capabilities compete for time.
            // Below the workspace, never above it: appearing must not shove the guide down the pane.
            tracks = null;
            if (TimedCapabilities().Count < 2) return;

            var timing = Z.Section("Timing",
                "When each time-occupying capability runs, on one shared clock. Drag the playhead to read the recipe.",
                "chunks.mock.timing", icon: "timer");
            tracks = new MockTimingTracks(recipe);
            timing.Add(tracks);
            root.Add(timing);
            tracks.Refresh();
        }

        /// Every capability that occupies time, ENABLED OR NOT. Deliberately unfiltered by Enabled: the
        /// timing surface's presence and its lane count must not change when a capability is toggled, or the
        /// spatial guide above it resizes and re-centres the picture the user is reading.
        List<MockCapability> TimedCapabilities()
        {
            EnsureRecipe();
            var list = new List<MockCapability>();
            foreach (var cap in recipe.capabilities)
                if (cap.Timed) list.Add(cap);
            return list;
        }

        /// A value-only edit repaints the guide and re-bands the clock; it never rebuilds the window,
        /// so a slider drag cannot pull the control out from under the pointer.
        void Change(Action edit)
        {
            edit?.Invoke();
            stage?.MarkDirtyRepaint();
            tracks?.Refresh();
        }""")

# J: the multi-lane clock itself, inserted before the spatial stage class
rep(
"""        /// <summary>A custom Painter2D stage is the sanctioned raw island:""",
"""        /// <summary>The recipe's clock: one LANE per time-occupying capability, every lane on the SAME
        /// absolute ruler, so two capabilities that overlap are drawn overlapping and a Delay of 0.15s starts
        /// its band at 0.15s. This is deliberately not <c>Z.Timeline</c>: that control is by contract a single
        /// strip of CONSECUTIVE bands, which can only express a chain — and a chunk's sparks and its debris
        /// fire together, so a chain is the wrong model. Hand-rolled here because the mock is disposable; the
        /// blueprint carries the multi-lane control as a ZUI requirement rather than leaving it in one
        /// window. A disabled capability keeps its lane, drawn dim: losing the lane would change this
        /// surface's height and move the guide above it.</summary>
        sealed class MockTimingTracks : VisualElement
        {
            const float LaneHeight = 16f;
            const float LaneGap = 3f;
            const float RulerHeight = 14f;
            const float TickFontSize = 9f;
            // Below this, a band cannot hold a legible name; the colour and the tooltip still identify it.
            const float MinBandForName = 52f;

            static readonly Color Track = new Color(0.14f, 0.15f, 0.18f, 1f);
            static readonly Color Playhead = new Color(1f, 0.85f, 0.3f, 0.95f);

            readonly MockRecipe recipe;
            readonly VisualElement bar;    // the painted lanes; local x IS the time axis
            readonly VisualElement ruler;  // the tick numbers and the playhead's own readout
            readonly Label playLabel;
            readonly List<Label> tickLabels = new List<Label>();
            readonly List<Label> bandLabels = new List<Label>();
            readonly List<MockCapability> lanes = new List<MockCapability>();

            float total = 1f;
            float seconds;
            bool dragging;
            float laidOutWidth = -1f;

            public MockTimingTracks(MockRecipe recipe)
            {
                this.recipe = recipe;
                style.flexDirection = FlexDirection.Column;
                style.flexGrow = 1f;
                style.minWidth = 180f;

                bar = new VisualElement();
                bar.style.flexGrow = 0f;
                bar.style.flexShrink = 0f;
                bar.style.overflow = Overflow.Hidden;
                bar.generateVisualContent += Paint;
                Add(bar);

                ruler = new VisualElement();
                ruler.style.height = RulerHeight;
                ruler.style.flexGrow = 0f;
                ruler.style.flexShrink = 0f;
                ruler.pickingMode = PickingMode.Ignore;   // the numbers are a ruler, not a second hit target
                Add(ruler);

                playLabel = new Label("0.00s") { pickingMode = PickingMode.Ignore };
                playLabel.style.position = Position.Absolute;
                playLabel.style.top = 0f;
                playLabel.style.fontSize = TickFontSize;
                playLabel.style.unityTextAlign = TextAnchor.UpperCenter;
                playLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                playLabel.style.color = new Color(1f, 1f, 1f, 0.95f);
                ruler.Add(playLabel);

                // The gestures live on the ROOT so a press anywhere in the control scrubs, including the
                // ruler — a ruler you cannot click reads as broken. x is always resolved against the BAR.
                RegisterCallback<PointerDownEvent>(OnDown);
                RegisterCallback<PointerMoveEvent>(OnMove);
                RegisterCallback<PointerUpEvent>(OnUp);
                RegisterCallback<GeometryChangedEvent>(OnGeometry);
            }

            /// Re-read the recipe: which lanes exist, how long the clock is, and where every band sits.
            public void Refresh()
            {
                lanes.Clear();
                total = 0f;
                if (recipe != null)
                {
                    foreach (var cap in recipe.capabilities)
                    {
                        if (!cap.Timed) continue;
                        lanes.Add(cap);
                        if (cap.Enabled) total = Mathf.Max(total, cap.delay + cap.duration);
                    }
                }
                if (total <= 0.01f) total = 1f;
                seconds = Mathf.Clamp(seconds, 0f, total);

                bar.style.height = Mathf.Max(LaneHeight, lanes.Count * (LaneHeight + LaneGap) - LaneGap);
                Relayout();
            }

            float BarWidth
            {
                get
                {
                    float w = bar.contentRect.width;
                    return float.IsNaN(w) ? 0f : w;
                }
            }

            float X(float t) => total > 0f ? Mathf.Clamp01(t / total) * BarWidth : 0f;

            float T(float x) => total > 0f ? Mathf.Clamp01(x / Mathf.Max(1f, BarWidth)) * total : 0f;

            void OnDown(PointerDownEvent e)
            {
                if (e.button != 0) return;
                dragging = true;
                this.CapturePointer(e.pointerId);
                Scrub(e.position);
                e.StopPropagation();
            }

            void OnMove(PointerMoveEvent e)
            {
                if (!dragging) return;
                Scrub(e.position);
                e.StopPropagation();
            }

            void OnUp(PointerUpEvent e)
            {
                if (!dragging) return;
                dragging = false;
                this.ReleasePointer(e.pointerId);
                e.StopPropagation();
            }

            /// Resolved against the BAR, not the event target: a pointer that lands on an absolutely
            /// positioned band label would otherwise report x in that label's own space.
            void Scrub(Vector2 panelPosition)
            {
                seconds = T(bar.WorldToLocal(panelPosition).x);
                PlacePlayhead();
                bar.MarkDirtyRepaint();
            }

            /// Every label position is a function of the bar's width, unknown until the panel lays out. The
            /// width guard is load-bearing: GeometryChangedEvent bubbles, so the labels this creates would
            /// otherwise re-enter it forever.
            void OnGeometry(GeometryChangedEvent _)
            {
                float w = BarWidth;
                if (Mathf.Abs(w - laidOutWidth) < 0.5f) return;
                laidOutWidth = w;
                Relayout();
            }

            void Relayout()
            {
                PlaceBandLabels();
                BuildTicks();
                PlacePlayhead();
                bar.MarkDirtyRepaint();
            }

            void PlaceBandLabels()
            {
                foreach (var l in bandLabels) l.RemoveFromHierarchy();
                bandLabels.Clear();
                if (BarWidth < 1f) return;

                for (int i = 0; i < lanes.Count; i++)
                {
                    var cap = lanes[i];
                    float left = X(cap.delay);
                    float width = X(cap.delay + cap.duration) - left;
                    if (width < MinBandForName) continue;

                    var l = new Label(cap.DisplayName) { pickingMode = PickingMode.Ignore };
                    l.style.position = Position.Absolute;
                    l.style.left = left + 4f;
                    l.style.top = i * (LaneHeight + LaneGap) + 1f;
                    l.style.width = width - 8f;
                    l.style.fontSize = 9f;
                    l.style.overflow = Overflow.Hidden;
                    l.style.color = new Color(0.06f, 0.06f, 0.08f, cap.Enabled ? 0.95f : 0.5f);
                    l.style.unityFontStyleAndWeight = FontStyle.Bold;
                    bar.Add(l);
                    bandLabels.Add(l);
                }
            }

            /// The ruler. A tick whose text would sit under the playhead's own readout is DROPPED rather
            /// than drawn behind it — a number printed on top of another number is worse than no number.
            void BuildTicks()
            {
                foreach (var l in tickLabels) l.RemoveFromHierarchy();
                tickLabels.Clear();
                float w = BarWidth;
                if (w < 40f) return;

                float step = NiceStep(total, w);
                float playLeft = X(seconds) - 22f;
                float playRight = X(seconds) + 22f;

                for (float t = 0f; t <= total + 0.0001f; t += step)
                {
                    float x = X(t);
                    if (x > playLeft && x < playRight) continue;   // occluded by the playhead readout
                    var l = new Label(t.ToString("0.00") + "s") { pickingMode = PickingMode.Ignore };
                    l.style.position = Position.Absolute;
                    l.style.top = 0f;
                    l.style.left = x - 20f;
                    l.style.width = 40f;
                    l.style.fontSize = TickFontSize;
                    l.style.unityTextAlign = TextAnchor.UpperCenter;
                    l.style.color = new Color(1f, 1f, 1f, 0.42f);
                    ruler.Add(l);
                    tickLabels.Add(l);
                }
            }

            /// A step from the 1/2/5 family that lands about five ticks on the bar, so the ruler never
            /// prints numbers closer together than they can be read.
            static float NiceStep(float span, float width)
            {
                float raw = span / Mathf.Max(2f, width / 70f);
                float[] steps = { 0.05f, 0.1f, 0.2f, 0.25f, 0.5f, 1f, 2f, 5f };
                foreach (float s in steps) if (raw <= s) return s;
                return 10f;
            }

            void PlacePlayhead()
            {
                playLabel.text = seconds.ToString("0.00") + "s";
                playLabel.style.left = X(seconds) - 22f;
                playLabel.style.width = 44f;
                BuildTicks();
            }

            void Paint(MeshGenerationContext mgc)
            {
                var view = bar.contentRect;
                if (view.width < 10f || view.height < 4f) return;
                var p = mgc.painter2D;

                for (int i = 0; i < lanes.Count; i++)
                {
                    var cap = lanes[i];
                    float top = i * (LaneHeight + LaneGap);
                    Fill(p, new Rect(0f, top, view.width, LaneHeight), Track);

                    float left = X(cap.delay);
                    float width = Mathf.Max(2f, X(cap.delay + cap.duration) - left);
                    var c = cap.BandColor;
                    if (!cap.Enabled) c = new Color(c.r, c.g, c.b, 0.22f);
                    Fill(p, new Rect(left, top, width, LaneHeight), c);
                }

                float x = X(seconds);
                Fill(p, new Rect(x - 1f, 0f, 2f, view.height), Playhead);
            }

            static void Fill(Painter2D p, Rect rect, Color color)
            {
                p.fillColor = color;
                p.BeginPath();
                p.MoveTo(new Vector2(rect.x, rect.y));
                p.LineTo(new Vector2(rect.xMax, rect.y));
                p.LineTo(new Vector2(rect.xMax, rect.yMax));
                p.LineTo(new Vector2(rect.x, rect.yMax));
                p.ClosePath();
                p.Fill();
            }
        }

        /// <summary>A custom Painter2D stage is the sanctioned raw island:""")

io.open(p, 'w', encoding='utf-8', newline='\n').write(s)
print("patch2 ok")
