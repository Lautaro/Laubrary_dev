// ChunkWindow.Timeline — the Chunks 2.0 timeline panel (AgentHQ T-0038).
//
// The design doc calls the timeline "the structural backbone — a bottom-docked track view, in the spirit of
// Pyre's own envelope timeline, showing when each attached module fires and where its events sit", and
// ChunkWindow.BuildAsset already calls BuildTimeline LAST for exactly that reason.
//
// Shape of the panel, top to bottom:
//   1. the TRACK — one lane per module that is actually switched on in this spec (including one per enabled
//      entry of ChunkSpec.blastGroups, so each extra blast can be delayed on its own), plus a permanent Events
//      lane. Drag a module's block along its lane, or press anywhere in the lane to place it there and keep
//      dragging; drag an event chip to move it in time. This is the ONE sanctioned raw-painting island (a
//      bespoke 2D track canvas); everything around it is a Z.* control.
//   2. one fixed, non-wrapping row: the view Window slider + "+ Add event".
//   3. one card per event marker — kind, its name/picker, its time, and remove.
//
// Why the marker cards sit BELOW the track and not inside it: ui-layout-rules' "Stable workspace" rule. The
// track is the thing being aimed at with a pointer, so nothing may appear ABOVE it or reflow it. Adding a
// marker grows the card list underneath, and the track does not move a pixel — the markers themselves are
// absolutely-positioned children of the canvas, and an absolutely-positioned child cannot resize its parent.
// For the same reason a DRAG never rebuilds the panel: the numbers a drag changes are pushed into the cards'
// existing fields (RefreshTimelineNumbers), so the element under the pointer is never replaced mid-gesture.
//
// ZUI GAP raised by this file (per the ZUI-first rule): there is no ZUI control for a MULTI-LANE track editor.
// Z.Timeline (ZuiTimeline) is the closest thing and is deliberately a different control — ONE bar of
// consecutive bands with a playhead, not N independently-draggable lanes — and Z.Envelope edits a curve, not
// a schedule. This canvas is written in the ZuiTimeline idiom on purpose (Painter2D background whose local x
// IS the time axis, absolutely-positioned Labels because Painter2D cannot draw text, pointer-captured drags)
// so that generalising it into a ZUI control later is a move, not a rewrite. See the task report.
using System;
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        // Stable body cleared/refilled on every structural change (add / remove / kind swap), so those repaint
        // just this panel — the same _modifiersBody + RebuildModifiers split ChunkWindow.Modifiers uses.
        VisualElement _timelineBody;
        ChunkTrackView _trackView;

        // The marker cards' time fields, held so a drag on the canvas can push its new number into them
        // WITHOUT rebuilding the panel underneath the pointer.
        readonly Dictionary<ChunkTimelineMarker, FloatField> _markerTimeFields =
            new Dictionary<ChunkTimelineMarker, FloatField>();

        void BuildTimeline(VisualElement root, ChunkSpec c)
        {
            // A spec serialized before the timeline existed deserializes this as null; ChunkSpec.OnValidate
            // re-creates it on load, and this is the same repair for the window's own path into it.
            c.timeline ??= new ChunkTimeline();
            var t = c.timeline;

            var s = Z.Section("Timeline",
                "When each module of this burst fires relative to burst-start, and the code/sound events fired " +
                "along the way. A module with no block on its lane fires on the spot, exactly as it did before " +
                "timelines existed — leaving this off costs a burst nothing.",
                "chunks.timeline");
            s.SetHeaderToggle(t.enabled,
                "Schedule when each module fires, and fire code/sound events along the burst.",
                v => DialAndRebuild("Timeline", () => t.enabled = v));
            root.Add(s);

            // Folded off: nothing else to build, matching every other module section's "off = short window" rule.
            if (!t.enabled) { _timelineBody = null; _trackView = null; _markerTimeFields.Clear(); return; }

            _timelineBody = new VisualElement();
            s.Add(_timelineBody);
            RebuildTimelineBody();
        }

        void RebuildTimelineBody()
        {
            var c = Spec;
            if (c == null || _timelineBody == null) { _timelineBody?.Clear(); return; }
            var t = c.timeline;
            if (t == null) { _timelineBody.Clear(); return; }
            t.markers ??= new List<ChunkTimelineMarker>();
            t.tracks ??= new List<ChunkTimelineTrack>();

            _timelineBody.Clear();
            _markerTimeFields.Clear();

            // ── 1. the track canvas ────────────────────────────────────────────────────────────────────
            _trackView = new ChunkTrackView(t, TimelineLanes(c),
                "Drag a module's block along its lane — or press anywhere in the lane to place it there — to " +
                "set when it fires. Drag an event chip to move it in time. Right-click a block for more.",
                apply => Dial("Timeline", apply),
                RefreshTimelineNumbers);
            _timelineBody.Add(_trackView);

            // ── 2. the controls row (fixed, non-wrapping — nothing here appears or disappears) ─────────
            var addBtn = Z.Button("+ Add event",
                "Add a Code Event or a Zound Event to the timeline.", null).W(110f);
            addBtn.clicked += () => ShowAddEventMenu(addBtn, t);

            var row = Z.Row(
                Z.MicroSlider("Window (s)", t.windowSeconds, ChunkTimeline.MinWindow, ChunkTimeline.MaxWindow,
                    "How much time the track above shows. A VIEW range only — it never delays, cuts or " +
                    "shortens anything you authored.",
                    v =>
                    {
                        Dial("Timeline window", () => t.windowSeconds =
                            Mathf.Clamp(v, ChunkTimeline.MinWindow, ChunkTimeline.MaxWindow));
                        // Retune the canvas in place rather than rebuilding the panel: a slider fires on
                        // every pixel of its drag, and rebuilding under the pointer would drop the gesture.
                        _trackView?.SetWindow(t.windowSeconds);
                    },
                    150f, showValue: true, decimals: 2),
                Z.HSpace(),
                addBtn);
            row.style.flexWrap = Wrap.NoWrap;
            _timelineBody.Add(row);

            // ── 3. one card per marker ─────────────────────────────────────────────────────────────────
            for (int i = 0; i < t.markers.Count; i++)
            {
                if (t.markers[i] == null)
                {
                    int at = i;
                    Dial("Remove event", () => t.markers.RemoveAt(at));
                    RebuildTimelineBody();
                    return;
                }
                _timelineBody.Add(BuildMarkerCard(t, t.markers[i]));
            }
        }

        /// The lanes the track shows: one per module that is actually SWITCHED ON in this spec. A lane for a
        /// module that is off is noise — and, worse, a promise that scheduling it would do something.
        ///
        /// The Pyre Spawn lane is hidden while a Spawn Formation is on, because ChunkModules.Run makes the
        /// formation SUPERSEDE the plain spawner (a formation IS a set of spawns). A lane whose block could
        /// never fire would be a lie told in the one place the user goes to find out when things fire.
        ///
        /// ⚠️ That suppression is SPECIFIC to those two and must never be copied onto a blast group. A group's
        /// own `useFormation` supersedes nothing — the group fires either way, as one spawn or as a formation
        /// of them — so an enabled group ALWAYS gets its lane. Hiding it would hide a block that really fires,
        /// which is the same lie in the opposite direction.
        static List<ChunkTrackView.Lane> TimelineLanes(ChunkSpec c)
        {
            var lanes = new List<ChunkTrackView.Lane>(6);
            if (c.particleSplash != null && c.particleSplash.Enabled)
                lanes.Add(new ChunkTrackView.Lane(ChunkModules.Splash, "Splash",
                    "When the Particle Splash sprays, in seconds after burst-start.",
                    new Color(0.42f, 0.70f, 0.95f)));
            if (c.fragmentSlicer != null && c.fragmentSlicer.Enabled)
                lanes.Add(new ChunkTrackView.Lane(ChunkModules.Fragments, "Fragments",
                    "When the sliced fragments launch, in seconds after burst-start.",
                    new Color(0.95f, 0.72f, 0.32f)));
            bool formation = c.spawnFormation != null && c.spawnFormation.Enabled;
            if (formation)
                lanes.Add(new ChunkTrackView.Lane(ChunkModules.Formation, "Formation",
                    "When the Spawn Formation starts placing its spawns, in seconds after burst-start — its own " +
                    "per-point stagger then runs on top of this. A formation replaces the plain Pyre Spawn, " +
                    "which is why that lane is not shown while this one is.",
                    new Color(0.85f, 0.45f, 0.85f)));
            else if (c.pyreSpawn != null && c.pyreSpawn.Enabled)
                lanes.Add(new ChunkTrackView.Lane(ChunkModules.PyreSpawn, "Pyre Spawn",
                    "When the Pyre blast spawns, in seconds after burst-start.",
                    new Color(0.95f, 0.45f, 0.32f)));

            // One lane per FURTHER blast group. This is what makes "the background blast first, then the
            // pieces, then the front blasts" authorable at all — without a lane of its own a group can only
            // fire at the same instant as every other one, and depth alone would have to carry the whole
            // effect. Disabled groups get no lane, exactly like every other module that is switched off.
            //
            // The lane KEY comes from ChunkModules.BlastGroupTrack and is never re-derived here: it bakes the
            // group's position in on purpose, so two groups holding the same blast at different depths get two
            // independent delays instead of one shared one.
            if (c.blastGroups != null)
            {
                for (int i = 0; i < c.blastGroups.Count; i++)
                {
                    var g = c.blastGroups[i];
                    if (g == null || !g.Enabled) continue;
                    string name = g.DisplayName;
                    lanes.Add(new ChunkTrackView.Lane(
                        // KEY — bakes the group's POSITION in on purpose (ChunkModules.BlastGroupTrack's own
                        // comment explains why). Never re-derive it from the name, and never let the DISPLAY
                        // text below leak into it: a rename or a blast-asset swap must not orphan the delay
                        // already stored in timeline.tracks (T-0081 D-03).
                        ChunkModules.BlastGroupTrack(g, i),
                        // DISPLAY ONLY — the group's own identity (its authored label, or its blast asset's
                        // name), exactly what its section header and the Layer Stack's "Modules" box now call
                        // it. No ordinal: there is no "Blast 1" anywhere in the product (the first blast is
                        // titled "Pyre Spawn"), and a position number goes stale the moment a group is
                        // reordered. Hand-truncated, not left to clip: the gutter is a fixed 96px column, and a
                        // name cut off mid-word by overflow reads as a rendering fault rather than "there is more".
                        LsEllipsize(name, 12),
                        $"When \"{name}\" spawns, in seconds after burst-start."
                        + (g.useFormation
                            ? " Its own formation then staggers each of its spawns on top of this delay."
                            : string.Empty)
                        + " It fires whether or not the Spawn Formation module is on — an extra blast group is "
                        + "never superseded. Which slot it DRAWS in is the Layer Stack's job, not this lane's.",
                        BlastGroupColor(i)));
                }
            }
            return lanes;
        }

        /// A distinct hue per further blast group, walked far enough around the wheel that two neighbouring
        /// groups never read as one band. Starts warm, next to the Pyre Spawn lane's own colour, because
        /// group 2 is conceptually the second blast rather than a different kind of thing.
        static Color BlastGroupColor(int index)
            => Color.HSVToRGB(Mathf.Repeat(0.09f + 0.17f * index, 1f), 0.55f, 0.92f);

        // ── marker cards ────────────────────────────────────────────────────────────────────────────────
        // One row per marker, and one row is all it needs: ui-layout-rules' card rule says a card whose
        // fields are short puts them all on the header line rather than claiming rows of its own. The row's
        // SHAPE is identical for both kinds (same widths in the same order), so flipping Code↔Zound swaps
        // which control occupies the name slot without moving anything else on screen.
        //
        // No drag-reorder grip: a marker's position in the list means nothing, its TIME is its order, and a
        // grip would advertise a rearrangement that has no effect.
        VisualElement BuildMarkerCard(ChunkTimeline t, ChunkTimelineMarker m)
        {
            var box = Z.Box(null, null);
            var header = new VisualElement();
            header.AddToClassList("zui-row");
            header.style.flexWrap = Wrap.NoWrap;

            header.Add(Z.Segmented((int)m.kind, new[] { "Code", "Zound" },
                "Code raises a named hook your game subscribes to (ChunkTimelineEvents.CodeEvent). " +
                "Zound plays a sound from the Zounds library.",
                v =>
                {
                    Dial("Event kind", () => m.kind = (ChunkEventKind)v);
                    RebuildTimelineBody();   // the name slot swaps control type
                }).W(110f));

            header.Add(Z.HSpace(6f));
            header.Add(m.kind == ChunkEventKind.Zound ? BuildZoundSlot(m) : BuildCodeSlot(m));
            header.Add(Z.HSpace(6f));

            // Time is a numeric INPUT, not a slider, on purpose: dragging the chip on the track is the real
            // gesture, and there is no honest stable ceiling on "seconds after burst-start" to make a slider
            // range out of (ui-layout-rules: "slider if the range is real and stable; input if you'd only be
            // guessing the cap"). It is still scrub-draggable via ZuiScrub, never keyboard-only.
            const string timeTip = "Seconds after burst-start at which this event fires.";
            var timeField = Z.Float(m.time, timeTip, v =>
            {
                Dial("Event time", () => m.time = Mathf.Max(0f, v));
                _trackView?.Refresh();
            }, 56f);
            _markerTimeFields[m] = timeField;
            header.Add(Z.Field("At (s)", timeTip, timeField));

            header.Add(Z.Flexible());
            header.Add(Z.Button("X", "Remove this event (undoable).", () =>
            {
                Dial("Remove event", () => t.markers.Remove(m));
                RebuildTimelineBody();
            }).W(22f));

            box.Add(header);
            return box;
        }

        /// A Code Event's hook name. A text field is RIGHT here and only here: this is where the name is
        /// DECLARED. Every reference to a name elsewhere in Laubrary is a picker.
        VisualElement BuildCodeSlot(ChunkTimelineMarker m)
        {
            const string tip = "The hook name raised on ChunkTimelineEvents.CodeEvent. Your game subscribes to " +
                "that event and switches on this name. Declared here — so it is typed here, and picked " +
                "everywhere it is referenced.";
            var field = Z.TextInput(m.codeName, tip,
                v =>
                {
                    Dial("Event name", () => m.codeName = v);
                    _trackView?.Refresh();
                }, 190f);
            // Delayed (commits on Enter / focus loss) rather than per-keystroke: ChunkWindow.Dial re-slices
            // the debris preview and records an Undo step on every call, so a live field would spend both on
            // every letter of a name and leave a dozen undo steps behind one word.
            field.isDelayed = true;
            return field;
        }

        /// A Zound Event's reference. PICKED, never typed — the stored value is the same name string Zounds
        /// itself uses (ZoundEngine.PlayZound(name)), exactly as PlayZoundEffect stores it.
        ///
        /// With no Zounds bridge registered the control SAYS the picker is unavailable and is disabled. It
        /// deliberately does NOT degrade to a text field: a typed name compiles, saves and looks authored,
        /// then silently plays nothing — which is the exact failure the picker exists to prevent.
        VisualElement BuildZoundSlot(ChunkTimelineMarker m)
        {
            if (!ChunkZoundPickerHook.Available)
            {
                var unavailable = Z.Button("Zound picker unavailable",
                    "No audio tool is registered in this project, so there is nothing to pick from. With Zounds " +
                    "present the ChunksZounds bridge registers the picker automatically and this becomes a browser.",
                    null).W(190f);
                unavailable.SetEnabled(false);
                return unavailable;
            }

            string Current() => string.IsNullOrEmpty(m.zoundName) ? "(none)" : m.zoundName;
            var button = Z.Button(Current(),
                "Click to pick a Zound. Right-click to hear the current one.", null).W(190f);

            button.clicked += () =>
            {
                // Screen position derived from the window's own rect rather than GUIUtility.GUIToScreenPoint:
                // this runs from a UI Toolkit callback, where there is no active IMGUI context for that
                // conversion to be measured against.
                var wb = button.worldBound;
                var screen = new Vector2(position.x + wb.x, position.y + wb.yMax);
                ChunkZoundPickerHook.Show(screen, picked =>
                {
                    Dial("Pick Zound", () => m.zoundName = picked);
                    button.text = Current();
                    _trackView?.Refresh();
                    ChunkZoundPickerHook.Preview?.Invoke(picked);   // hear what you just chose, immediately
                });
            };

            // Right-click auditions it. A sound field you cannot hear from is a name you have to trust, and
            // the whole reason these are picked rather than typed is that trusting a name does not work.
            button.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 1 || string.IsNullOrEmpty(m.zoundName)) return;
                ChunkZoundPickerHook.Preview?.Invoke(m.zoundName);
                e.StopPropagation();
            });

            return button;
        }

        /// The Envelope-style popover card the design doc mandates for every "add a thing" menu — the same
        /// Z.Menu the "+ Add modifier" catalog uses. Each item ACTS on click (it adds that marker there and
        /// then), rather than opening a second place to choose, per ui-layout-rules' "Label = action".
        void ShowAddEventMenu(VisualElement anchor, ChunkTimeline t)
        {
            Z.Menu(anchor)
                .Section("Add event")
                .Item("Code Event",
                    "A named hook raised on ChunkTimelineEvents.CodeEvent, for your game to react to.",
                    () => AddMarker(t, ChunkEventKind.Code))
                .Item("Zound Event",
                    ChunkZoundPickerHook.Available
                        ? "A sound played from the Zounds library, picked from a browser."
                        : "A sound from the Zounds library. No audio tool is registered in this project yet, so " +
                          "there will be nothing to pick until Zounds is present.",
                    () => AddMarker(t, ChunkEventKind.Zound))
                .Show();
        }

        void AddMarker(ChunkTimeline t, ChunkEventKind kind)
        {
            float at = FirstFreeTime(t);
            Dial("Add event", () =>
            {
                t.markers ??= new List<ChunkTimelineMarker>();
                t.markers.Add(new ChunkTimelineMarker { kind = kind, time = at });
            });
            RebuildTimelineBody();
        }

        /// Where a newly added event goes: burst-start, nudged along in small steps while something is
        /// already sitting there. Two chips at the same instant draw exactly on top of each other, and a new
        /// event the user cannot see is indistinguishable from one that was never added.
        static float FirstFreeTime(ChunkTimeline t)
        {
            const float Step = 0.05f;
            float at = 0f;
            if (t.markers == null) return at;
            for (int guard = 0; guard < 64; guard++)
            {
                bool taken = false;
                for (int i = 0; i < t.markers.Count; i++)
                {
                    var m = t.markers[i];
                    if (m != null && Mathf.Abs(m.time - at) < Step * 0.5f) { taken = true; break; }
                }
                if (!taken) return at;
                at += Step;
            }
            return at;
        }

        /// Push the numbers a canvas drag changed into the marker cards' existing fields. Deliberately NOT a
        /// rebuild: the pointer is on the canvas, and replacing the panel under it would drop the gesture and
        /// (worse) re-enter this from inside an event handler on an element that no longer exists.
        void RefreshTimelineNumbers()
        {
            foreach (var kv in _markerTimeFields)
            {
                if (kv.Key == null || kv.Value == null) continue;
                kv.Value.SetValueWithoutNotify(kv.Key.time);
            }
        }
    }

    /// The bottom-docked track canvas: one lane per enabled module plus a permanent Events lane, drawn to
    /// scale against a shared time axis.
    ///
    /// Written in ZuiTimeline's idiom deliberately (see this file's header): a Painter2D-painted background
    /// whose element-local x IS the time axis, real Labels for text because Painter2D cannot draw any, and
    /// pointer-captured drags. Every child is ABSOLUTELY positioned, which is what guarantees that adding a
    /// marker can never reflow the surface the user is dragging on.
    sealed class ChunkTrackView : VisualElement
    {
        /// One module row: which module it schedules (always a ChunkModules const), what it is called on
        /// screen, what its tooltip says, and the colour its block is drawn in.
        public readonly struct Lane
        {
            public readonly string ModuleName;
            public readonly string Label;
            public readonly string Tooltip;
            public readonly Color Color;

            public Lane(string moduleName, string label, string tooltip, Color color)
            {
                ModuleName = moduleName;
                Label = label;
                Tooltip = tooltip;
                Color = color;
            }
        }

        const float LaneH = 24f;      // one row's height
        const float GutterW = 96f;    // the left name column; the time axis starts at its right edge
        const float RulerH = 14f;     // permanently reserved: a ruler that came and went would move the track
        const float BlockW = 46f;     // a module block, wide enough to hold its own delay readout
        const float ChipW = 92f;      // an event chip, wide enough for a short name
        // Right margin sized to the WIDEST draggable so an item at the very end of the window is still fully
        // on screen. Sizing it smaller would either clip the item or force a clamp that decouples an item's
        // drawn position from the time it actually holds.
        const float RightPad = ChipW + 6f;
        const float TickFont = 9f;
        const float CharWidth = 5.4f;

        static readonly Color EventColor = new Color(0.55f, 0.85f, 0.55f);
        static readonly Color EmptyEventColor = new Color(0.45f, 0.45f, 0.45f);

        readonly ChunkTimeline _timeline;
        readonly List<Lane> _lanes;
        readonly Action<Action> _mutate;     // wraps a mutation in the window's Dial/Undo contract
        readonly Action _changed;            // "a value moved" — the host pushes it into its cards

        readonly VisualElement _grid;
        readonly VisualElement _ruler;
        readonly Dictionary<string, VisualElement> _blockEls = new Dictionary<string, VisualElement>();

        float _window;
        float _laidOutWidth = -1f;

        // Live drag state. One at a time by construction — whichever element began the drag holds the
        // pointer capture, and every move/up bubbles from it to the grid, where the handlers live.
        //
        // A drag moves the ELEMENT and retitles it; it does not touch the asset until the pointer comes up.
        // That is deliberate and not just an optimisation: ChunkWindow.Dial re-slices the debris PREVIEW on
        // every call, so mutating per pointer-move would re-read and re-cut the subject sprite every frame of
        // a drag that has nothing to do with slicing. One gesture = one Dial = one undo step, and the live
        // readout comes from the pending value instead.
        VisualElement _dragEl;
        Label _dragLabel;
        string _dragModule;
        ChunkTimelineMarker _dragMarker;
        float _grabOffset;
        float _pendingTime;
        bool _dragging;

        public ChunkTrackView(ChunkTimeline timeline, List<Lane> lanes, string tooltip,
                              Action<Action> mutate, Action changed)
        {
            _timeline = timeline;
            _lanes = lanes ?? new List<Lane>();
            _mutate = mutate;
            _changed = changed;
            _window = Mathf.Clamp(timeline != null ? timeline.windowSeconds : 2f,
                                  ChunkTimeline.MinWindow, ChunkTimeline.MaxWindow);

            style.flexDirection = FlexDirection.Column;
            // A workspace canvas legitimately takes the width it is given (ui-layout-rules exempts preview
            // viewports from the no-stretch rule) — but capped, so it never runs off across a very wide window.
            style.flexGrow = 1f;
            style.flexShrink = 1f;
            style.minWidth = 340f;
            style.maxWidth = 900f;
            style.marginBottom = 4f;

            _grid = new VisualElement { tooltip = tooltip };
            _grid.style.height = (_lanes.Count + 1) * LaneH;   // +1 for the permanent Events lane
            _grid.style.flexGrow = 0f;
            _grid.style.flexShrink = 0f;
            _grid.style.overflow = Overflow.Hidden;
            _grid.generateVisualContent += Paint;
            Add(_grid);

            _ruler = new VisualElement { pickingMode = PickingMode.Ignore };
            _ruler.style.height = RulerH;
            _ruler.style.flexGrow = 0f;
            _ruler.style.flexShrink = 0f;
            Add(_ruler);

            // A press on empty lane space places that lane's module there and keeps dragging. That is the
            // discoverable half of the gesture: a user who never guesses that the block is draggable still
            // finds that the lane responds. Blocks and chips stop propagation, so pressing one drags it.
            _grid.RegisterCallback<PointerDownEvent>(OnGridDown);
            // Move/up live on the GRID, not on each block: a captured pointer sends its events to the
            // capturing element, and they bubble here — so one pair of handlers serves every draggable.
            _grid.RegisterCallback<PointerMoveEvent>(OnDragMove);
            _grid.RegisterCallback<PointerUpEvent>(OnDragUp);
            RegisterCallback<GeometryChangedEvent>(OnGeometry);
        }

        /// Retune the visible time range without rebuilding — what the Window slider calls on every tick of
        /// its drag.
        public void SetWindow(float seconds)
        {
            float w = Mathf.Clamp(seconds, ChunkTimeline.MinWindow, ChunkTimeline.MaxWindow);
            if (Mathf.Approximately(w, _window)) return;
            _window = w;
            Refresh();
        }

        /// Re-place every block/chip/label and repaint. Cheap enough to call on any external edit.
        public void Refresh()
        {
            PlaceChildren();
            BuildRuler();
            _grid.MarkDirtyRepaint();
        }

        // ── geometry ────────────────────────────────────────────────────────────────────────────────────
        float GridWidth
        {
            get
            {
                float w = _grid.contentRect.width;
                return float.IsNaN(w) ? 0f : w;
            }
        }

        float TrackWidth => Mathf.Max(1f, GridWidth - GutterW - RightPad);

        /// seconds → x, in the grid's own local space. Guarded against a zero window, which the clamp on
        /// windowSeconds already prevents but which a deserialized 0 could still deliver.
        float X(float t) => GutterW + Mathf.Clamp01(_window > 0f ? t / _window : 0f) * TrackWidth;

        /// x → seconds, rounded to a hundredth so a drag never stores float noise like 0.30000001.
        float T(float x)
        {
            float t = Mathf.Clamp01((x - GutterW) / TrackWidth) * _window;
            return Mathf.Round(t * 100f) / 100f;
        }

        /// The width guard is load-bearing, not an optimisation: GeometryChangedEvent bubbles, so the child
        /// elements this handler creates would re-enter it forever without it.
        void OnGeometry(GeometryChangedEvent _)
        {
            float w = GridWidth;
            if (Mathf.Abs(w - _laidOutWidth) < 0.5f) return;
            _laidOutWidth = w;
            Refresh();
        }

        // ── children (Painter2D cannot draw text, so every label is a real element) ──────────────────────
        void PlaceChildren()
        {
            _grid.Clear();
            _blockEls.Clear();
            if (GridWidth <= 1f) return;

            for (int i = 0; i < _lanes.Count; i++)
            {
                var lane = _lanes[i];
                _grid.Add(GutterLabel(lane.Label, lane.Tooltip, i));
                var block = ModuleBlock(lane, i);
                _blockEls[lane.ModuleName] = block;
                _grid.Add(block);
            }

            int eventsRow = _lanes.Count;
            _grid.Add(GutterLabel("Events",
                "Code and Zound events fired along the burst. Add one with \"+ Add event\" below, then drag " +
                "its chip to place it in time.", eventsRow));

            var markers = _timeline != null ? _timeline.markers : null;
            if (markers == null) return;
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (m == null) continue;
                _grid.Add(MarkerChip(m, eventsRow));
            }
        }

        Label GutterLabel(string text, string tooltip, int row)
        {
            var l = new Label(text) { tooltip = tooltip };
            l.style.position = Position.Absolute;
            l.style.left = 4f;
            l.style.width = GutterW - 8f;
            l.style.top = row * LaneH + (LaneH - 14f) * 0.5f;
            l.style.height = 14f;
            l.style.fontSize = 10f;
            l.style.overflow = Overflow.Hidden;
            l.style.whiteSpace = WhiteSpace.NoWrap;
            l.style.unityTextAlign = TextAnchor.MiddleLeft;
            l.style.color = new Color(1f, 1f, 1f, 0.72f);
            return l;
        }

        VisualElement ModuleBlock(Lane lane, int row)
        {
            float delay = RawDelay(lane.ModuleName);
            var block = new VisualElement
            {
                tooltip = $"{lane.Label} — fires {delay:0.##} s after burst-start. Drag to reschedule, " +
                          $"right-click for more.\n{lane.Tooltip}",
            };
            Style(block, row, BlockW, lane.Color, 2f);
            block.style.left = X(delay);

            var text = new Label(delay <= 0f ? "0" : $"{delay:0.##}") { pickingMode = PickingMode.Ignore };
            text.style.fontSize = TickFont;
            text.style.color = ReadableOn(lane.Color);
            text.style.unityTextAlign = TextAnchor.MiddleCenter;
            block.Add(text);

            string moduleName = lane.ModuleName;
            block.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 1)
                {
                    // The one action a drag cannot express, offered where the block is: send it back to 0 s.
                    Z.Menu(block)
                        .Item("Fire on the spot (0 s)",
                            "Clear this module's scheduled delay so it fires the instant the burst does.",
                            () => Commit(moduleName, null, 0f))
                        .Show();
                    e.StopPropagation();
                    return;
                }
                if (e.button != 0) return;
                BeginDrag(block, text, moduleName, null, block.layout.x, delay, e);
            });
            return block;
        }

        VisualElement MarkerChip(ChunkTimelineMarker m, int row)
        {
            var color = m.IsEmpty ? EmptyEventColor : EventColor;
            var chip = new VisualElement
            {
                tooltip = $"{(m.kind == ChunkEventKind.Zound ? "Zound" : "Code")} event \"{m.DisplayName}\" " +
                          $"at {Mathf.Max(0f, m.time):0.##} s. Drag to move it in time; edit it in its card below.",
            };
            Style(chip, row, ChipW, color, 7f);
            chip.style.left = X(Mathf.Max(0f, m.time));
            // The rounded LEFT edge is the instant; the squared right edge is just the label's box. Marking
            // the two ends differently is what stops a chip reading as a duration it does not have.
            chip.style.borderTopRightRadius = 2f;
            chip.style.borderBottomRightRadius = 2f;

            var text = new Label(m.DisplayName) { pickingMode = PickingMode.Ignore };
            text.style.fontSize = TickFont;
            text.style.color = ReadableOn(color);
            text.style.unityTextAlign = TextAnchor.MiddleCenter;
            text.style.whiteSpace = WhiteSpace.NoWrap;
            chip.Add(text);

            chip.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                BeginDrag(chip, null, null, m, chip.layout.x, Mathf.Max(0f, m.time), e);
            });
            return chip;
        }

        static void Style(VisualElement el, int row, float width, Color color, float radius)
        {
            el.style.position = Position.Absolute;
            el.style.top = row * LaneH + 4f;
            el.style.height = LaneH - 9f;
            el.style.width = width;
            el.style.backgroundColor = color;
            el.style.borderTopLeftRadius = radius;
            el.style.borderBottomLeftRadius = radius;
            el.style.borderTopRightRadius = radius;
            el.style.borderBottomRightRadius = radius;
            el.style.justifyContent = Justify.Center;
            el.style.alignItems = Align.Center;
            el.style.overflow = Overflow.Hidden;
        }

        // ── dragging ────────────────────────────────────────────────────────────────────────────────────
        /// `readout` is the element's own in-block Label (a module block shows its delay there), or null
        /// for a chip, whose label is its NAME and must not be overwritten with a number.
        void BeginDrag(VisualElement el, Label readout, string moduleName, ChunkTimelineMarker marker,
                       float elLeft, float startTime, PointerDownEvent e)
        {
            _dragging = true;
            _dragEl = el;
            _dragLabel = readout;
            _dragModule = moduleName;
            _dragMarker = marker;
            _pendingTime = startTime;
            _grabOffset = _grid.WorldToLocal(e.position).x - elLeft;
            el.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnDragMove(PointerMoveEvent e)
        {
            if (!_dragging || _dragEl == null) return;
            MoveTo(T(_grid.WorldToLocal(e.position).x - _grabOffset));
            e.StopPropagation();
        }

        /// The visual half of a drag: the element follows the pointer and says where it now is. Nothing here
        /// touches the asset — Commit does that, once, when the pointer comes up.
        void MoveTo(float t)
        {
            _pendingTime = t;
            _dragEl.style.left = X(t);
            if (_dragLabel != null) _dragLabel.text = t <= 0f ? "0" : $"{t:0.##}";
        }

        void OnDragUp(PointerUpEvent e)
        {
            if (!_dragging) return;
            var el = _dragEl;
            float t = _pendingTime;
            string moduleName = _dragModule;
            var marker = _dragMarker;

            _dragging = false;
            _dragEl = null;
            _dragLabel = null;
            _dragModule = null;
            _dragMarker = null;
            if (el != null) el.ReleasePointer(e.pointerId);
            e.StopPropagation();

            Commit(moduleName, marker, t);
        }

        /// The ONE data edit a whole drag makes. Routed through the host's Dial, so it carries the
        /// Undo.RecordObject contract and lands as a single undo step.
        void Commit(string moduleName, ChunkTimelineMarker marker, float t)
        {
            if (moduleName != null) _mutate(() => _timeline.SetDelayFor(moduleName, t));
            else if (marker != null) _mutate(() => marker.time = Mathf.Max(0f, t));
            else return;

            // Rebuild the children so every readout and tooltip states the new number, then let the host push
            // it into the marker cards. Last, and only here: this replaces the element the pointer event was
            // dispatched on, so nothing may touch that event afterwards.
            Refresh();
            _changed?.Invoke();
        }

        /// A press on empty lane space places that lane's module at the pressed instant AND continues as a
        /// drag, so one gesture both finds the affordance and uses it. Never creates an event: a stray click
        /// that authored a nameless marker would be worse than one that did nothing.
        void OnGridDown(PointerDownEvent e)
        {
            if (e.button != 0 || _dragging) return;
            var local = _grid.WorldToLocal(e.position);
            if (local.x < GutterW) return;
            int row = Mathf.FloorToInt(local.y / LaneH);
            if (row < 0 || row >= _lanes.Count) return;

            string name = _lanes[row].ModuleName;
            if (!_blockEls.TryGetValue(name, out var block) || block == null) return;

            // Hand the gesture straight to that lane's block: it jumps under the pointer and the press flows
            // on as a normal drag, so one motion both discovers the affordance and uses it. The data edit
            // still happens once, on release, exactly as it does for a drag begun on the block itself.
            _dragging = true;
            _dragEl = block;
            _dragLabel = block.childCount > 0 ? block.ElementAt(0) as Label : null;
            _dragModule = name;
            _dragMarker = null;
            _grabOffset = BlockW * 0.5f;
            block.CapturePointer(e.pointerId);
            MoveTo(T(local.x - BlockW * 0.5f));
            e.StopPropagation();
        }

        /// The stored delay, bypassing DelayFor's "0 when disabled" contract — the panel only exists while
        /// the timeline is enabled, and reading through the disabled short-circuit would peg every block to
        /// the left edge during the frame an undo flips the flag.
        float RawDelay(string moduleName)
        {
            var tracks = _timeline != null ? _timeline.tracks : null;
            if (tracks == null) return 0f;
            for (int i = 0; i < tracks.Count; i++)
                if (tracks[i] != null && tracks[i].moduleName == moduleName) return Mathf.Max(0f, tracks[i].delay);
            return 0f;
        }

        // ── ruler ───────────────────────────────────────────────────────────────────────────────────────
        void BuildRuler()
        {
            _ruler.Clear();
            float w = GridWidth;
            if (w <= 1f) return;

            float step = GridStep(_window);
            float lastRight = float.NegativeInfinity;
            for (float t = 0f; t <= _window + 1e-4f; t += step)
            {
                string text = Format(t);
                float tw = text.Length * CharWidth + 4f;
                float left = Mathf.Clamp(X(t) - tw * 0.5f, 0f, Mathf.Max(0f, w - tw));
                if (left < lastRight) continue;   // would overlap the previous number — drop it, don't overdraw
                lastRight = left + tw;

                var l = new Label(text) { pickingMode = PickingMode.Ignore };
                l.style.position = Position.Absolute;
                l.style.left = left;
                l.style.top = 0f;
                l.style.width = tw;
                l.style.fontSize = TickFont;
                l.style.unityTextAlign = TextAnchor.UpperCenter;
                l.style.color = new Color(1f, 1f, 1f, 0.42f);
                _ruler.Add(l);
            }
        }

        /// A step that keeps the ruler under about a dozen numbers however wide the window gets.
        static float GridStep(float window)
        {
            float[] candidates = { 0.05f, 0.1f, 0.25f, 0.5f, 1f, 2f, 5f };
            for (int i = 0; i < candidates.Length; i++)
                if (window / candidates[i] <= 12f) return candidates[i];
            return 10f;
        }

        static string Format(float seconds) => seconds >= 10f ? $"{seconds:0.#}" : $"{seconds:0.##}";

        /// Text drawn ON a coloured block has to survive whatever colour that block is.
        static Color ReadableOn(Color c)
        {
            float lum = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            return lum > 0.55f ? new Color(0f, 0f, 0f, 0.8f) : new Color(1f, 1f, 1f, 0.85f);
        }

        // ── painting ────────────────────────────────────────────────────────────────────────────────────
        void Paint(MeshGenerationContext mgc)
        {
            var r = _grid.contentRect;
            if (r.width <= 1f || r.height <= 1f) return;
            var p = mgc.painter2D;

            Fill(p, 0f, 0f, r.width, r.height, new Color(0f, 0f, 0f, 0.30f));
            // The name column, marked off so the time axis visibly starts where it starts.
            Fill(p, 0f, 0f, GutterW, r.height, new Color(0f, 0f, 0f, 0.25f));

            int rows = _lanes.Count + 1;
            for (int i = 0; i < rows; i++)
            {
                float y = i * LaneH;
                if ((i & 1) == 1) Fill(p, GutterW, y, r.width - GutterW, LaneH, new Color(1f, 1f, 1f, 0.03f));
                if (i > 0) Fill(p, 0f, y, r.width, 1f, new Color(0f, 0f, 0f, 0.45f));
            }

            // The Events lane reads as a different KIND of row, not just one more module.
            float ey = _lanes.Count * LaneH;
            Fill(p, GutterW, ey, r.width - GutterW, LaneH, new Color(0.55f, 0.85f, 0.55f, 0.05f));

            float step = GridStep(_window);
            for (float t = step; t <= _window + 1e-4f; t += step)
                Fill(p, X(t), 0f, 1f, r.height, new Color(1f, 1f, 1f, 0.09f));

            // Burst-start. Everything on this canvas is measured from it, so it is the one bright line.
            Fill(p, GutterW, 0f, 1f, r.height, new Color(1f, 1f, 1f, 0.55f));
        }

        static void Fill(Painter2D p, float x, float y, float w, float h, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x + w, y));
            p.LineTo(new Vector2(x + w, y + h)); p.LineTo(new Vector2(x, y + h));
            p.ClosePath(); p.Fill();
        }
    }
}
