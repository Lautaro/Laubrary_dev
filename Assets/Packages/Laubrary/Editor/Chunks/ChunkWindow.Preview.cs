// ChunkWindow.Preview — the workspace half: the stage, the transport that drives it, and the backdrop.
//
// The transport is built first and the stage is drawn against it, not the other way round, because the thing
// the recipe is actually authored against is TIME: a capability's delay, a pattern's stagger and a cue's
// instant are all statements about when, and none of them can be read from a still picture. So the clock runs
// at wall-clock speed, everything that shows a time reads the same field, and dialling a card while it runs
// changes the next frame rather than restarting the run.
//
// The stage's own drawing is a placeholder until the schematic lands: it paints the backdrop, the origin and
// the playhead, which is exactly enough to see that the clock is moving and that the backdrop panel below it
// is connected to something.
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

        // A placeholder, deliberately honest about being one: it paints the backdrop the panel below edits,
        // the origin every recipe has, and a bar that moves with the clock. The schematic that draws each
        // capability's own guide replaces the middle of this method and nothing else.
        void DrawStage(ChunkSpec c)
        {
            var view = new Rect(0f, 0f, stage.contentRect.width, stage.contentRect.height);
            if (view.width <= 1f || view.height <= 1f) return;

            BackSplashPainter.Draw(view, Backdrop, new Color(0.1f, 0.1f, 0.12f));

            var centre = new Vector2(view.width * 0.5f, view.height * 0.5f);
            var cross = new Color(1f, 1f, 1f, 0.35f);
            EditorGUI.DrawRect(new Rect(centre.x - 8f, centre.y - 1f, 16f, 2f), cross);
            EditorGUI.DrawRect(new Rect(centre.x - 1f, centre.y - 8f, 2f, 16f), cross);

            float length = Mathf.Max(0.0001f, ChunkClock.Length(c));
            float x = view.width * Mathf.Clamp01(previewTime / length);
            EditorGUI.DrawRect(new Rect(x - 1f, 0f, 2f, view.height), new Color(1f, 0.75f, 0.2f, 0.8f));

            var label = new GUIStyle(EditorStyles.boldLabel);
            label.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
            GUI.Label(new Rect(8f, 6f, view.width - 16f, 18f), c != null ? c.name : "", label);
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
