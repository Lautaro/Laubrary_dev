// ChunkWindow.Preview — the workspace half: the stage, the transport that drives it, and the backdrop.
//
// The transport is built first and the stage is drawn against it, not the other way round, because the thing
// the recipe is actually authored against is TIME: a capability's delay, a pattern's stagger and a cue's
// instant are all statements about when, and none of them can be read from a still picture. So the clock runs
// at wall-clock speed, everything that shows a time reads the same field, and dialling a card while it runs
// changes the next frame rather than restarting the run.
//
// The stage draws a SCHEMATIC, not a rehearsal of the burst: where each capability throws things, how far and
// in what order, at the instant the clock is on. It is worked out from the recipe every repaint (see
// ChunkPreviewSim), which is what lets a dial moved mid-play change the very next frame instead of restarting
// the run — nothing here is a cached render that would have to be thrown away and rebuilt.
using System;
using Laubrary.BackSplash;
using Laubrary.BackSplash.Editor;
using Laubrary.Mirage;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using MirageWindow = Laubrary.Mirage.Editor.MirageWindow;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        IMGUIContainer stage;
        Button playButton;
        Slider scrubSlider;
        Label timeReadout;
        VisualElement backdropHost;
        double lastTick;

        // ── what the stage draws with ─────────────────────────────────────────────────────────────────────
        // Both frames are reused for the life of the window: a preview that runs at wall-clock speed must not
        // allocate a fresh scene graph sixty times a second.

        ChunkPreviewFrame frame;
        ChunkPreviewFrame reachScratch;

        /// Bumped by InvalidatePreview — the one signal that says "the recipe is not what it was". Only the
        /// measurements that must NOT be redone per frame (how far the recipe reaches, which sets the stage's
        /// zoom) are keyed off it; the picture itself is recomputed from live data every repaint.
        internal int previewGeneration;

        float cachedReach = 1f;
        int cachedReachGeneration = -1;
        int cachedReachSpec;

        /// Room left round the outermost thing the recipe throws, so a chunk at full reach is inside the
        /// stage rather than clipped by its edge.
        const float StageMargin = 1.18f;

        /// The clock's own strip along the bottom edge. The playhead lives HERE rather than as a bar across
        /// the picture: once the stage shows where things are in space, a vertical line sweeping across it
        /// reads as a moving object in the scene, which is exactly the wrong thing for it to say.
        const float StripHeight = 12f;

        /// The backdrop lives on the RECIPE, so the one a burst was tuned against comes back with the burst
        /// rather than with whichever window happened to be open. Allocated on first use so simply opening an
        /// older recipe never rewrites it.
        BackSplashSettings Backdrop
        {
            get
            {
                var c = Current;
                if (c == null) return null;
                c.previewBackSplash ??= new BackSplashSettings();
                return c.previewBackSplash;
            }
        }

        void BuildPreviewSection(VisualElement parent, ChunkSpec c)
        {
            previewSection = Z.Section("Preview",
                "What this recipe puts on screen, over its own clock.", "Chunks.preview", "eye");

            // A rebuild is the one moment worth re-measuring the assets the cards point at: a Pyre's own size
            // is asked for by rendering its first frame, which is far too expensive to redo per dial edit, and
            // far too stale to keep for the life of the editor.
            ChunkPreviewSim.ClearCaches();
            previewGeneration++;

            stage = new IMGUIContainer(() => DrawStage(c));
            stage.style.height = Mathf.Clamp(previewHeight, PreviewHeightMin, PreviewHeightMax);
            stage.style.flexGrow = 0f;
            stage.style.flexShrink = 0f;
            stage.style.minWidth = 200f;
            stage.AddToClassList("zui-stage");
            previewSection.Add(stage);
            previewSection.Add(BuildPreviewResizeBar());

            var chrome = new VisualElement();
            chrome.style.flexShrink = 0f;
            BuildTransport(chrome, c);
            BuildBackdropPanel(chrome);
            previewSection.Add(chrome);

            parent.Add(previewSection);
        }

        // A 6px grip on the stage's bottom edge — how tall the stage is. The stage's height is fixed by this
        // and by nothing else, so a capability appearing or disappearing can never resize the picture.
        VisualElement BuildPreviewResizeBar()
        {
            var bar = new VisualElement { tooltip = "Drag to make the preview taller or shorter." };
            bar.style.height = 6f;
            bar.style.flexShrink = 0f;
            bar.style.backgroundColor = new Color(0f, 0f, 0f, 0.25f);
            bar.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 0) { bar.CapturePointer(e.pointerId); e.StopPropagation(); }
            });
            bar.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!bar.HasPointerCapture(e.pointerId)) return;
                previewHeight = Mathf.Clamp(previewHeight + e.deltaPosition.y, PreviewHeightMin, PreviewHeightMax);
                if (stage != null) stage.style.height = previewHeight;
                e.StopPropagation();
            });
            bar.RegisterCallback<PointerUpEvent>(e =>
            {
                if (bar.HasPointerCapture(e.pointerId)) bar.ReleasePointer(e.pointerId);
            });
            return bar;
        }

        // ── transport ─────────────────────────────────────────────────────────────────────────────────────

        void BuildTransport(VisualElement root, ChunkSpec c)
        {
            playButton = Z.Button(playing ? "❚❚ Pause" : "▶ Play",
                "Run the recipe's clock at real speed, or hold it where it is.",
                () => { playing = !playing; UpdatePlayButton(); });

            var replay = Z.Button("⟲ Replay",
                "Put the clock back to the start and run it from there.",
                () => { previewTime = 0f; playing = true; UpdatePlayButton(); SyncTransport(); });

            var loopToggle = Z.Toggle("Loop",
                "Start the recipe again when it reaches the end, instead of stopping there.",
                loop, v => { loop = v; });

            root.Add(Z.HGroup(playButton, replay, loopToggle, BuildMirageButton(c)));

            float length = ChunkClock.Length(c);
            scrubSlider = Z.Slider(Mathf.Clamp(previewTime, 0f, length), 0f, length,
                "Hold the recipe at an exact moment — dragging stops playback and keeps that instant.",
                v =>
                {
                    previewTime = v;
                    playing = false;
                    UpdatePlayButton();
                    SyncTransport();
                }, 220f, showInput: false);

            timeReadout = Z.Text("", ZuiText.Subtle, "Where the clock is, and how long this recipe runs for.");
            timeReadout.style.width = 110f;

            // The composition's own aim: the direction every producer that inherits the burst direction fires
            // along when the caller passes none. It sits here rather than on a card because it belongs to the
            // whole recipe — several cards read it, and none of them owns it.
            var direction = Z.MicroSlider("Burst direction", c.directionDeg, 0f, 360f,
                "The way this whole recipe aims when nothing overrides it. 0 points right, 90 points up.",
                v => Dial("Edit Burst Direction", () => c.directionDeg = v), 170f, showValue: true, decimals: 0);

            root.Add(Z.HGroup(Z.Field("Time",
                "Hold the recipe at an exact moment — dragging stops playback and keeps that instant.",
                scrubSlider), timeReadout, direction));
        }

        VisualElement BuildMirageButton(ChunkSpec c)
        {
            // "Worth previewing" = the burst would actually put something on screen. A recipe holding only
            // coordinators, or nothing at all, would open a stage and show an empty frame — the press would
            // not do what its label says.
            bool worth = false;
            var stack = c != null ? c.capabilities : null;
            if (stack != null)
                for (int i = 0; i < stack.Count && !worth; i++)
                    worth = stack[i] != null && stack[i].enabled && stack[i].OccupiesTime;

            string tooltip = worth
                ? "Open Mirage with a throwaway preview of this burst, playing at real scale against the test " +
                  "backdrop, so you can see the composed result rather than reading dials. Bursts are one-shot, " +
                  "so the Mirage HUD carries a Replay button for this entry. Chunks only move in Play mode, so " +
                  "press Play once Mirage is up. The preview is created in memory only — it is never saved as a " +
                  "project asset, so it never appears in Mirage's own Browse list."
                : "Nothing to preview: every capability in this recipe is switched off or produces nothing, so " +
                  "a burst would put nothing on screen. Switch one on, or add a producer.";

            var button = Z.Button("Preview in Mirage", tooltip, () => PreviewInMirage(c));
            button.style.width = 150f;
            button.SetEnabled(worth);

            // A disabled UI Toolkit element does not reliably receive the pointer events a tooltip resolves
            // from — and the disabled case is exactly the one where the explanation matters — so the row
            // around it carries the same text and answers "why can't I press this?".
            var row = Z.Row(button);
            row.tooltip = tooltip;
            return row;
        }

        void UpdatePlayButton()
        {
            if (playButton != null) playButton.text = playing ? "❚❚ Pause" : "▶ Play";
        }

        // The one place the clock is read back out to everything that shows it: the scrub handle, the
        // readout, the timing playhead and the stage. None of them holds its own copy.
        internal void SyncTransport()
        {
            var c = Current;
            if (c == null) return;
            float length = ChunkClock.Length(c);
            previewTime = Mathf.Clamp(previewTime, 0f, length);

            if (scrubSlider != null)
            {
                scrubSlider.highValue = length;
                scrubSlider.SetValueWithoutNotify(previewTime);
            }
            if (timeReadout != null) timeReadout.text = $"t {previewTime:0.00} / {length:0.00} s";
            lanes?.SetTime(previewTime);
            stage?.MarkDirtyRepaint();
        }

        void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - lastTick);
            lastTick = now;

            if (!playing || this == null || Current == null) return;
            float length = ChunkClock.Length(Current);
            previewTime += Mathf.Clamp(dt, 0f, 0.25f);   // a stalled editor must not jump the whole clock
            if (previewTime >= length)
            {
                if (loop) previewTime = length > 0f ? Mathf.Repeat(previewTime, length) : 0f;
                else { previewTime = length; playing = false; UpdatePlayButton(); }
            }
            SyncTransport();
        }

        // ── the stage ─────────────────────────────────────────────────────────────────────────────────────

        // The picture is rebuilt from the recipe on every repaint rather than cached and invalidated, because
        // the thing it has to survive is an edit MADE WHILE IT RUNS — and a cache is precisely the mechanism
        // that shows a stale frame after one. Only the zoom is cached, because zooming in and out while the
        // clock runs would make everything appear to move when nothing did.
        void DrawStage(ChunkSpec c)
        {
            var view = new Rect(0f, 0f, stage.contentRect.width, stage.contentRect.height);
            if (view.width <= 1f || view.height <= 1f) return;

            BackSplashPainter.Draw(view, Backdrop, new Color(0.1f, 0.1f, 0.12f));

            // The picture keeps the whole rect except the clock strip, so the stage's own height (owned by the
            // resize bar, and by nothing else) is what decides how big the picture is.
            var field = new Rect(0f, 0f, view.width, Mathf.Max(24f, view.height - StripHeight));
            var centre = field.center;

            float reach = StageReach(c);
            float scale = Mathf.Min(field.width, field.height) / (2f * Mathf.Max(0.01f, reach) * StageMargin);

            var cross = new Color(1f, 1f, 1f, 0.35f);
            EditorGUI.DrawRect(new Rect(centre.x - 8f, centre.y - 1f, 16f, 2f), cross);
            EditorGUI.DrawRect(new Rect(centre.x - 1f, centre.y - 8f, 2f, 16f), cross);

            DrawClockStrip(c, view);

            if (Event.current.type != EventType.Repaint) return;

            frame ??= new ChunkPreviewFrame();
            ChunkPreviewSim.Build(c, previewTime, frame);
            // A cone says WHICH WAY, not how far, so it is the one thing allowed to be cut down to fit: the
            // framing is now sized to the composition rather than to its furthest particle, and a fast
            // long-lived producer's wedge would otherwise be drawn straight off the edge with its arrowhead
            // — the half that answers the question — outside the stage.
            DrawGuides(frame, centre, scale, Mathf.Min(field.width, field.height) * 0.46f);

            var label = new GUIStyle(EditorStyles.boldLabel);
            label.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
            GUI.Label(new Rect(8f, 6f, view.width - 16f, 18f), c != null ? c.name : "", label);
        }

        /// How far the recipe reaches, in world units — cached, because it samples the whole clock and would
        /// otherwise be paid for on every frame of playback. Recomputed only when the recipe changed, which
        /// is also what stops the zoom drifting while the clock runs.
        float StageReach(ChunkSpec c)
        {
            int id = c != null ? c.GetInstanceID() : 0;
            if (cachedReachGeneration == previewGeneration && cachedReachSpec == id) return cachedReach;

            reachScratch ??= new ChunkPreviewFrame();
            cachedReach = ChunkPreviewSim.Reach(c, reachScratch);
            cachedReachGeneration = previewGeneration;
            cachedReachSpec = id;
            return cachedReach;
        }

        // Painted back to front: aim cones, then flight paths, then the things themselves. Within each, the
        // capability's own order — its Layer-Plan slot first, its place in the stack second — decides depth,
        // so what draws in front on the stage is what will draw in front in the burst.
        static readonly Comparison<ChunkGuide> ByOrder = (a, b) => a.order.CompareTo(b.order);
        static readonly Comparison<ChunkGuidePath> PathsByOrder = (a, b) => a.order.CompareTo(b.order);

        void DrawGuides(ChunkPreviewFrame f, Vector2 centre, float scale, float maxConePixels)
        {
            Vector2 ToScreen(Vector2 p) => new Vector2(centre.x + p.x * scale, centre.y - p.y * scale);

            f.Guides.Sort(ByOrder);
            f.Paths.Sort(PathsByOrder);

            Handles.BeginGUI();
            var previous = Handles.color;

            for (int i = 0; i < f.Cones.Count; i++) DrawCone(f.Cones[i], ToScreen, scale, maxConePixels);

            for (int i = 0; i < f.Paths.Count; i++)
            {
                var path = f.Paths[i];
                if (path.points == null || path.points.Length < 2) continue;
                var line = new Vector3[path.points.Length];
                for (int p = 0; p < path.points.Length; p++)
                {
                    var s = ToScreen(path.points[p]);
                    line[p] = new Vector3(s.x, s.y, 0f);
                }
                Handles.color = Fade(path.color, path.alpha * 0.7f);
                Handles.DrawAAPolyLine(1.5f, line);
            }

            for (int i = 0; i < f.Guides.Count; i++) DrawGuide(f.Guides[i], ToScreen, scale);

            Handles.color = previous;
            Handles.EndGUI();

            DrawGuideLabels(f, ToScreen);
        }

        static void DrawGuide(ChunkGuide g, Func<Vector2, Vector2> toScreen, float scale)
        {
            var at = toScreen(g.pos);
            float r = Mathf.Max(1.5f, g.radius * scale);   // a guide smaller than this is a fleck nobody can see
            Handles.color = Fade(g.color, g.alpha);

            switch (g.shape)
            {
                case ChunkGuideShape.Square:
                {
                    // An oriented square rather than a dot, so a spinning chunk reads as spinning: the
                    // rotation is the only thing on a schematic that can show spin at all.
                    float rad = -g.angleDeg * Mathf.Deg2Rad;     // screen y runs down, so the turn inverts
                    float cos = Mathf.Cos(rad) * r, sin = Mathf.Sin(rad) * r;
                    var quad = new[]
                    {
                        new Vector3(at.x - cos + sin, at.y - sin - cos, 0f),
                        new Vector3(at.x + cos + sin, at.y + sin - cos, 0f),
                        new Vector3(at.x + cos - sin, at.y + sin + cos, 0f),
                        new Vector3(at.x - cos - sin, at.y - sin + cos, 0f),
                    };
                    Handles.DrawAAConvexPolygon(quad);
                    break;
                }
                case ChunkGuideShape.Dot:
                    Handles.DrawSolidDisc(new Vector3(at.x, at.y, 0f), Vector3.forward, Mathf.Max(1f, r));
                    break;
                case ChunkGuideShape.Disc:
                    // Footprint, not a solid object: a recipe's whole point is several blasts overlapping,
                    // and an opaque disc would hide every one behind the last one drawn. The rim carries the
                    // size and the fade; the fill only says "something is here".
                    Handles.color = Fade(g.color, g.alpha * 0.3f);
                    Handles.DrawSolidDisc(new Vector3(at.x, at.y, 0f), Vector3.forward, r);
                    Handles.color = Fade(g.color, Mathf.Min(1f, g.alpha * 1.1f));
                    Handles.DrawWireDisc(new Vector3(at.x, at.y, 0f), Vector3.forward, r);
                    break;
                case ChunkGuideShape.Ring:
                    Handles.DrawWireDisc(new Vector3(at.x, at.y, 0f), Vector3.forward, r);
                    break;
            }
        }

        static void DrawCone(ChunkGuideCone cone, Func<Vector2, Vector2> toScreen, float scale, float maxPixels)
        {
            var at = toScreen(cone.pos);
            float r = Mathf.Clamp(cone.radius * scale, 6f, Mathf.Max(6f, maxPixels));

            // The wedge is faint context; the arrow down its middle is the part that answers "which way".
            // A cone that covers the whole circle is drawn as an OUTLINE rather than a filled disc: a full
            // circle says "every direction", and painting it solid buries every guide inside it under the one
            // statement that constrains them least.
            if (cone.halfSpreadDeg >= 179.5f)
            {
                Handles.color = Fade(cone.color, cone.alpha * 0.45f);
                Handles.DrawWireDisc(new Vector3(at.x, at.y, 0f), Vector3.forward, r);
            }
            else
            {
                int steps = Mathf.Clamp(Mathf.CeilToInt(cone.halfSpreadDeg * 2f / 6f), 2, 64);
                var wedge = new Vector3[steps + 2];
                wedge[0] = new Vector3(at.x, at.y, 0f);
                for (int i = 0; i <= steps; i++)
                {
                    float a = (cone.centreDeg - cone.halfSpreadDeg + 2f * cone.halfSpreadDeg * i / steps) * Mathf.Deg2Rad;
                    wedge[i + 1] = new Vector3(at.x + Mathf.Cos(a) * r, at.y - Mathf.Sin(a) * r, 0f);
                }
                Handles.color = Fade(cone.color, cone.alpha * 0.18f);
                Handles.DrawAAConvexPolygon(wedge);
            }

            float rad = cone.centreDeg * Mathf.Deg2Rad;
            var tip = new Vector3(at.x + Mathf.Cos(rad) * r, at.y - Mathf.Sin(rad) * r, 0f);
            var head = 8f;
            Handles.color = Fade(cone.color, Mathf.Min(1f, cone.alpha * 1.4f));
            Handles.DrawAAPolyLine(2f, new Vector3(at.x, at.y, 0f), tip);
            Handles.DrawAAPolyLine(2f,
                new Vector3(tip.x - Mathf.Cos(rad - 0.4f) * head, tip.y + Mathf.Sin(rad - 0.4f) * head, 0f),
                tip,
                new Vector3(tip.x - Mathf.Cos(rad + 0.4f) * head, tip.y + Mathf.Sin(rad + 0.4f) * head, 0f));
        }

        // A formation's numbers say the order the points GO OFF in, so changing the stagger order renumbers
        // the picture rather than leaving the labels describing a sweep that no longer happens.
        void DrawGuideLabels(ChunkPreviewFrame f, Func<Vector2, Vector2> toScreen)
        {
            var style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = new Color(1f, 1f, 1f, 0.9f);
            for (int i = 0; i < f.Guides.Count; i++)
            {
                var g = f.Guides[i];
                if (string.IsNullOrEmpty(g.label)) continue;
                var at = toScreen(g.pos);
                GUI.Label(new Rect(at.x - 12f, at.y - 8f, 24f, 16f), g.label, style);
            }
        }

        /// The clock along the bottom edge: how far through the recipe we are, plus a tick for every cue.
        /// A cue gets no mark on the picture itself — it is a sound or a code hook, and it happens at a
        /// moment, not at a place.
        void DrawClockStrip(ChunkSpec c, Rect view)
        {
            var strip = new Rect(0f, view.height - StripHeight, view.width, StripHeight);
            EditorGUI.DrawRect(strip, new Color(0f, 0f, 0f, 0.45f));

            float length = Mathf.Max(0.0001f, ChunkClock.Length(c));

            var stack = c != null ? c.capabilities : null;
            if (stack != null)
                for (int i = 0; i < stack.Count; i++)
                {
                    if (!(stack[i] is Cues cues) || !cues.enabled || cues.cues == null) continue;
                    for (int m = 0; m < cues.cues.Count; m++)
                    {
                        var cue = cues.cues[m];
                        if (cue == null || cue.IsEmpty) continue;
                        float cx = strip.x + strip.width * Mathf.Clamp01(cue.time / length);
                        bool crossing = Mathf.Abs(previewTime - cue.time) < 0.1f;
                        EditorGUI.DrawRect(new Rect(cx - 1f, strip.y + 2f, 2f, strip.height - 4f),
                            crossing ? new Color(1f, 1f, 1f, 0.95f) : new Color(0.6f, 0.85f, 1f, 0.6f));
                    }
                }

            float x = strip.x + strip.width * Mathf.Clamp01(previewTime / length);
            EditorGUI.DrawRect(new Rect(x - 1f, strip.y, 2f, strip.height), new Color(1f, 0.75f, 0.2f, 0.95f));
        }

        static Color Fade(Color c, float alpha)
        {
            c.a = Mathf.Clamp01(alpha);
            return c;
        }

        // ── backdrop ──────────────────────────────────────────────────────────────────────────────────────

        void BuildBackdropPanel(VisualElement root)
        {
            backdropHost = new VisualElement();
            root.Add(backdropHost);
            FillBackdropPanel();
        }

        void FillBackdropPanel()
        {
            if (backdropHost == null) return;
            backdropHost.Clear();
            backdropHost.Add(BackSplashZui.Build(Backdrop, "Preview backdrop",
                "A cosmetic backdrop for the preview only — a solid colour plus one optional image. Never part " +
                "of the burst and never baked. Kept on the recipe, so it comes back when you reopen it.",
                onChanged: () => stage?.MarkDirtyRepaint(),
                onStructureChanged: () => { stage?.MarkDirtyRepaint(); FillBackdropPanel(); },
                icon: "eye", owner: Current));
        }

        // ── Mirage handoff ────────────────────────────────────────────────────────────────────────────────
        //
        // Copied from the window this one replaces, which took it near line-for-line from
        // ZoetropeWindows.PreviewInMirage(Zoe): a throwaway MirageView is created via CreateInstance and NEVER
        // passed to AssetDatabase.CreateAsset, so it is structurally invisible to every browser and picker in
        // the project (they all enumerate through AssetDatabase.FindAssets) with no "hidden" flag to maintain.

        /// Make sure the scene that actually RENDERS a Mirage preview is open. No-op when a rig is already
        /// present, and quietly does nothing when the stage scene is not in the project.
        static void EnsureMirageStageOpen()
        {
            if (UnityEngine.Object.FindFirstObjectByType<MirageRig>() != null) return;

            string path = null;
            foreach (var guid in AssetDatabase.FindAssets("MirageStage t:Scene"))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(p)) { path = p; break; }
            }
            if (string.IsNullOrEmpty(path)) return;

            // The user pressed a button that opens a preview, so a save prompt here is expected and theirs to
            // answer; a cancel means leave their scene alone and open nothing.
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                path, UnityEditor.SceneManagement.OpenSceneMode.Single);
        }

        static void PreviewInMirage(ChunkSpec spec)
        {
            if (spec == null) return;
            EnsureMirageStageOpen();

            var view = ScriptableObject.CreateInstance<MirageView>();
            view.name = $"{spec.name} (Burst Preview)";
            view.AddEntry(spec, Vector2.zero);
            MirageWindow.OpenFor(view);
        }
    }
}
