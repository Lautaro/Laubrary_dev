// PyreWindow.Preview — the right pane: the IMGUI preview viewport (the ONE deliberate IMGUI island —
// bespoke canvas painting + six gizmo interaction layers, pixel-identical to the pre-port window),
// the UI Toolkit transport, and the backdrop / test-background / preview-subject panels.
// Part of the UI Toolkit port; see PyreWindow.cs.
using Laubrary.PreviewStage;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Pyre.Editor
{
    public partial class PyreWindow
    {
        SliderInt scrubSlider;
        Label frameLabel;
        Button playButton;
        Label stageNameLabel;
        VisualElement panelsHost;   // backdrop + test background + subject panels (rebuilt together)

        VisualElement BuildRightPane()
        {
            var right = new VisualElement();
            right.style.flexGrow = 1f;
            right.style.minWidth = 0f;
            right.style.minHeight = 0f;

            previewContainer = new IMGUIContainer(DrawViewportGUI)
            {
                tooltip = "The live preview. Left-drag empty space (or middle-drag) pans the frame; the armed " +
                    "authoring tool (orbs, pins, vortices, smudge), the origin ✛ and stage sprites all edit here.",
            };
            previewContainer.style.height = previewHeight;
            previewContainer.style.flexShrink = 0f;
            right.Add(previewContainer);
            right.Add(BuildPreviewSplitter());

            var scrollLower = new ScrollView(ScrollViewMode.Vertical);
            scrollLower.style.flexGrow = 1f;
            scrollLower.style.minHeight = 0f;
            var lower = scrollLower.contentContainer;

            BuildTransport(lower);
            lower.Add(Z.VSpace());
            panelsHost = new VisualElement();
            BuildLowerPanels(panelsHost);
            lower.Add(panelsHost);
            right.Add(scrollLower);
            return right;
        }

        void RebuildPanels()
        {
            if (panelsHost == null) return;
            panelsHost.Clear();
            BuildLowerPanels(panelsHost);
            previewContainer?.MarkDirtyRepaint();
        }

        void BuildLowerPanels(VisualElement root)
        {
            BuildBackdropOptions(root);
            BuildTestBackground(root);
            BuildPreviewSubjectOptions(root);
        }

        // ── transport ───────────────────────────────────────────────────────────────────────
        void BuildTransport(VisualElement root)
        {
            playButton = Z.Button(playing ? "❚❚ Pause" : "▶ Play", "Play or pause the looping preview.", () =>
            {
                playing = !playing;
                scrub = -1;
                playButton.text = playing ? "❚❚ Pause" : "▶ Play";
            });
            root.Add(WrapRow(
                playButton,
                Z.Button("|< Restart", "Rewind to frame 0 (also restarts the live subject).", () =>
                {
                    frame = 0; acc = 0f; scrub = -1;
                    previewSubject?.Restart();
                    RefreshTransport();
                    previewContainer?.MarkDirtyRepaint();
                }),
                Z.Button("Fit", "Fit the canvas to the viewport and recentre.", () => { FitZoom(); previewPan = Vector2.zero; }),
                Z.Button("Centre", "Recentre the frame in the viewport.", () => { previewPan = Vector2.zero; previewContainer?.MarkDirtyRepaint(); }),
                Z.Toggle("Frame", "Draw a thin border around the canvas.", showFrame,
                    v => { showFrame = v; previewContainer?.MarkDirtyRepaint(); }),
                Z.Button("Bake", "Bake the sprite sheet + AnimationClip for this blast.", () => BlastBaker.Bake(spec))));

            scrubSlider = Z.SliderInt(CurrentFrame(), 0, Mathf.Max(0, FrameCount - 1),
                "Scrub to an exact frame — dragging pauses playback and holds the frame.",
                v =>
                {
                    scrub = v;
                    playing = false;
                    playButton.text = "▶ Play";
                    previewContainer?.MarkDirtyRepaint();
                    RefreshTransport();
                }, 260f);
            root.Add(Z.Field("Frame", "Scrub to an exact frame — dragging pauses playback and holds the frame.", scrubSlider));

            frameLabel = Z.Text("", ZuiText.Subtle, "Current frame / total frames.");
            root.Add(WrapRow(
                Z.Field("Zoom", "Preview magnification (canvas pixels × zoom).",
                    Z.Slider(zoom, 1f, 16f, "Preview magnification (canvas pixels × zoom).",
                        v => { zoom = Mathf.Max(1f, Mathf.Round(v)); DirtySpec(); }, 130f)),
                Z.Field("Speed", "Preview playback speed multiplier.",
                    Z.Slider(speed, 0.1f, 3f, "Preview playback speed multiplier.",
                        v => { speed = (float)System.Math.Round(v, 1); DirtySpec(); }, 130f)),
                frameLabel));
            RefreshTransport();
        }

        void RefreshTransport()
        {
            int cur = CurrentFrame();
            scrubSlider?.SetValueWithoutNotify(cur);
            if (scrubSlider != null) scrubSlider.highValue = Mathf.Max(0, FrameCount - 1);
            if (frameLabel != null) frameLabel.text = $"frame {cur + 1}/{FrameCount}";
        }

        // ── backdrop options ────────────────────────────────────────────────────────────────
        void BuildBackdropOptions(VisualElement root)
        {
            var box = Z.Box("Preview backdrop",
                "Renders live every repaint, purely as a visual aid for authoring — it's never baked into any asset and has no effect on the baked sprite sheet or the runtime blast.");

            stageNameLabel = Z.Text(
                stageBg != null ? (AssetDatabase.Contains(stageBg) ? stageBg.name : "· unsaved ·") : "· none ·",
                ZuiText.Body, "The loaded backdrop preset (mode/colour/gradient/image AND the test-background sprites below).");
            stageNameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            var recallButton = Z.Button("Recall…", "Load a saved backdrop preset (a live link — edits below change that asset).", null);
            recallButton.clicked += () =>
            {
                var wb = recallButton.worldBound;
                PreviewStageGUI.ShowRecall(new Rect(wb.x, wb.y, wb.width, wb.height), b =>
                {
                    stageBg = b; stageSel = -1;
                    if (b != null) stageSaveName = b.name;
                    RebuildPanels();
                });
            };
            box.Add(WrapRow(
                stageNameLabel,
                Z.TextInput(stageSaveName, "Name to save the backdrop preset under.", v => stageSaveName = v, 110f),
                recallButton,
                Z.Button("★", "Save the WHOLE background (mode/colour/gradient/image + sprites) under the typed name — creates the asset the first time, re-saves in place after.",
                    () => { SaveBackdrop(); RebuildPanels(); }).W(24f)));

            var modeRow = WrapRow(Z.MiniRadioVertical((int)bgMode, BgModeLabels,
                "The backdrop's fill style: a solid colour, a vertical gradient, or an image.",
                v => { bgMode = (PreviewBgMode)v; DirtySpec(); RebuildPanels(); }, 70f, 68f));

            switch (bgMode)
            {
                case PreviewBgMode.Solid:
                    modeRow.Add(Z.Field("Colour", "Solid backdrop colour.",
                        Z.Color(bgSolid, "Solid backdrop colour.", v => { bgSolid = v; DirtySpec(); }, 90f)));
                    break;
                case PreviewBgMode.Gradient:
                {
                    var gf = new UnityEditor.UIElements.GradientField { value = bgGradient, tooltip = "Vertical backdrop gradient (top → bottom)." };
                    gf.style.width = 220f;
                    gf.RegisterValueChangedCallback(e => { bgGradient = e.newValue; DirtySpec(); });
                    modeRow.Add(gf);
                    break;
                }
                case PreviewBgMode.Image:
                    modeRow.Add(Z.Object<Texture2D>(bgImage, "The backdrop image.", v => { bgImage = v; DirtySpec(); }, 120f));
                    modeRow.Add(Z.Pad(bgImagePos, new Rect(-200f, -200f, 400f, 400f),
                        "Drag to offset the backdrop image within the viewport.",
                        v => { bgImagePos = v; DirtySpec(); }, 68f));
                    var col = Z.Column(
                        Z.Field("Zoom", "How much of the backdrop image fills the viewport.",
                            Z.Slider(bgImageZoom, 0.1f, 8f, "How much of the backdrop image fills the viewport.",
                                v => { bgImageZoom = v; DirtySpec(); }, 110f)),
                        Z.Field("Tint", "Multiplies the image's own colours.",
                            Z.Color(bgImageTint, "Multiplies the image's own colours.",
                                v => { bgImageTint = v; DirtySpec(); }, 90f)));
                    modeRow.Add(col);
                    break;
            }
            box.Add(modeRow);
            root.Add(box);
        }

        void SaveBackdrop()
        {
            if (stageBg == null)
            {
                var seeded = ScriptableObject.CreateInstance<PreviewBackground>();
                seeded.mode = ToStageMode(bgMode);
                seeded.solid = bgSolid;
                var srcGrad = bgGradient;
                var clonedGrad = new Gradient();
                if (srcGrad != null) { clonedGrad.SetKeys(srcGrad.colorKeys, srcGrad.alphaKeys); clonedGrad.mode = srcGrad.mode; }
                seeded.gradient = clonedGrad;
                seeded.image = bgImage; seeded.imageTint = bgImageTint;
                seeded.imageZoom = bgImageZoom; seeded.imagePos = bgImagePos;
                stageBg = seeded; stageSel = -1;
            }
            var bg = stageBg;
            PreviewStageGUI.Save(ref bg, stageSaveName);
            stageBg = bg;
        }

        // ── test background (sprites) ───────────────────────────────────────────────────────
        void BuildTestBackground(VisualElement root)
        {
            var box = Z.Box("Test background (sprites)",
                "Part of the same preset as \"Preview backdrop\" above — Recall/★ up there loads or saves these sprites too, not just the mode/colour/gradient/image.");

            if (stageBg == null)
            {
                box.Add(Z.Text("Recall a backdrop above, or hit ★ to start one.", ZuiText.Subtle,
                    "The sprites live on the backdrop preset asset."));
                root.Add(box);
                return;
            }

            box.Add(Z.Field("Fill (α0 = overlay)", "A colour filled behind/over the sprites — alpha 0 makes it a pure overlay.",
                Z.Color(stageBg.fill, "A colour filled behind/over the sprites — alpha 0 makes it a pure overlay.",
                    v => { stageBg.fill = v; EditorUtility.SetDirty(stageBg); previewContainer?.MarkDirtyRepaint(); }, 90f)));

            for (int i = 0; i < stageBg.sprites.Count; i++)
            {
                int idx = i;
                var s = stageBg.sprites[idx];
                var sBox = Z.Box(null, null);
                sBox.Add(WrapRow(
                    Z.Button(stageSel == idx ? "●" : "○", "Select this sprite (also draggable in the preview).",
                        () => { stageSel = idx; RebuildPanels(); }).W(24f),
                    Z.Toggle("Front", "Draw this sprite IN FRONT of the blast frame instead of behind it.", s.front,
                        v => { s.front = v; EditorUtility.SetDirty(stageBg); previewContainer?.MarkDirtyRepaint(); }),
                    Z.Button("X", "Remove this sprite.", () =>
                    {
                        stageBg.sprites.RemoveAt(idx);
                        stageSel = -1;
                        EditorUtility.SetDirty(stageBg);
                        RebuildPanels();
                    }).W(22f)));
                sBox.Add(WrapRow(
                    Z.Object<Sprite>(s.sprite, "The prop sprite.", v =>
                    {
                        s.sprite = v;
                        EditorUtility.SetDirty(stageBg);
                        previewContainer?.MarkDirtyRepaint();
                    }, 120f),
                    // flipY:false — this position is ALREADY consumed with a screen-Y-down convention by
                    // PreviewStageGUI's own direct viewport dragging; matching that beats the pad's default feel.
                    Z.Pad(s.position, new Rect(-200f, -200f, 400f, 400f),
                        "Drag to position the sprite (matches dragging it directly in the viewport).",
                        v => { s.position = v; EditorUtility.SetDirty(stageBg); previewContainer?.MarkDirtyRepaint(); }, 68f, flipY: false),
                    Z.Column(
                        Z.Field("Scale", "×scale on the sprite.",
                            Z.Slider(s.scale, 0.1f, 8f, "×scale on the sprite.",
                                v => { s.scale = v; EditorUtility.SetDirty(stageBg); previewContainer?.MarkDirtyRepaint(); }, 110f)),
                        Z.Field("Tint", "Multiplies the sprite's colours.",
                            Z.Color(s.tint, "Multiplies the sprite's colours.",
                                v => { s.tint = v; EditorUtility.SetDirty(stageBg); previewContainer?.MarkDirtyRepaint(); }, 90f)))));
                box.Add(sBox);
            }

            box.Add(WrapRow(
                Z.Button("+ Add sprite", "Add a prop sprite slot.", () =>
                {
                    stageBg.sprites.Add(new StageSprite());
                    stageSel = stageBg.sprites.Count - 1;
                    EditorUtility.SetDirty(stageBg);
                    RebuildPanels();
                }),
                Z.Button("Clear", "Remove every prop sprite.", () =>
                {
                    stageBg.sprites.Clear();
                    stageSel = -1;
                    EditorUtility.SetDirty(stageBg);
                    RebuildPanels();
                })));
            root.Add(box);
        }

        // ── preview subject options ─────────────────────────────────────────────────────────
        void BuildPreviewSubjectOptions(VisualElement root)
        {
            if (spec == null) return;
            var box = Z.Box("Reel Preview",
                "Plays through the same real gameplay components the subject uses in-game (a real SpriteRenderer-driven player, rendered via LiveScenePreview) — nothing here is baked. These fields are preview-time wiring only; they aren't part of the runtime blast. Attach id targets a MetaLayer painted on the Reel's clip.");

            var row = WrapRow(
                Z.Field("Asset", "The subject asset a bridge module resolves (e.g. a Reel via Pyre.Launimator).",
                    Z.Object<Object>(spec.previewSubjectAsset, "The subject asset a bridge module resolves (e.g. a Reel via Pyre.Launimator).",
                        v => Dial("Change preview subject", () => spec.previewSubjectAsset = v), 180f)),
                Z.Field("Clip", "Which of the subject's clips to play.",
                    Z.TextInput(spec.previewSubjectClip, "Which of the subject's clips to play.",
                        v => Dial("Change preview subject", () => spec.previewSubjectClip = v), 100f)),
                Z.Field("Attach", "MetaLayer name on the clip the blast's origin aligns to.",
                    Z.TextInput(spec.previewSubjectAttachId, "MetaLayer name on the clip the blast's origin aligns to.",
                        v => Dial("Change attach id", () => spec.previewSubjectAttachId = v), 100f)));
            var pickButton = Z.Button("▾", "Pick an attach id from the clip's painted MetaLayers.", null);
            pickButton.W(20f);
            pickButton.clicked += () =>
            {
                var options = PyrePreviewSubjectProvider.GetAttachPointOptions?.Invoke(spec.previewSubjectAsset, spec.previewSubjectClip);
                if (options == null || options.Length == 0) return;
                var menu = new GenericMenu();
                foreach (var opt in options)
                {
                    string captured = opt;
                    menu.AddItem(new GUIContent(captured), captured == spec.previewSubjectAttachId,
                        () => { Dial("Change attach id", () => spec.previewSubjectAttachId = captured); RebuildPanels(); });
                }
                menu.ShowAsContext();
            };
            row.Add(pickButton);
            box.Add(row);

            if (spec.previewSubjectAsset != null && PyrePreviewSubjectProvider.Resolve == null)
                box.Add(Z.Help("No bridge module registered to resolve this asset type (e.g. Pyre.Launimator.Editor).",
                    HelpBoxMessageType.Info));
            root.Add(box);
        }

        // ── the IMGUI viewport (unchanged painting + gizmo logic from the pre-port window) ──
        void DrawViewportGUI()
        {
            if (previewContainer == null) return;
            var view = new Rect(0f, 0f, previewContainer.layout.width, previewContainer.layout.height);
            if (!(view.width > 10f) || !(view.height > 10f)) return;
            lastView = view;
            int cur = CurrentFrame();

            if (Event.current.type == EventType.Repaint)
            {
                DrawBackdrop(view);
                if (stageBg != null) PreviewStageGUI.Draw(view, stageBg, zoom, false);

                var subject = ResolvePreviewSubject();
                subjectAlignOffset = Vector2.zero;
                if (subject != null && spec != null)
                {
                    var live = LivePreview;
                    float screenPxPerWorldUnit = zoom * spec.pixelsPerUnit;
                    Vector2 subjectAnchorScreen = new Vector2(view.center.x, view.yMax - view.height * 0.15f) + previewPan;
                    float boxSize = view.height;
                    Rect subjectRect = new Rect(subjectAnchorScreen.x - boxSize * 0.5f, subjectAnchorScreen.y - boxSize * 0.5f, boxSize, boxSize);

                    subject.SpawnInto(live, Vector3.zero);
                    live.Frame(Vector3.zero, boxSize / screenPxPerWorldUnit);
                    GUI.BeginClip(view);
                    live.Draw(new Rect(subjectRect.x - view.x, subjectRect.y - view.y, subjectRect.width, subjectRect.height));
                    GUI.EndClip();

                    if (subject.TryGetAttachWorldPos(out var attachWorld))
                    {
                        Vector3 sp = live.Camera.WorldToScreenPoint(attachWorld);
                        float px2pt = subjectRect.width / Mathf.Max(1, live.Camera.pixelWidth);
                        float py2pt = subjectRect.height / Mathf.Max(1, live.Camera.pixelHeight);
                        Vector2 attachScreen = new Vector2(subjectRect.x + sp.x * px2pt, subjectRect.y + (subjectRect.height - sp.y * py2pt));
                        float ow = spec.Width * zoom, oh = spec.Height * zoom;
                        Vector2 originNoOffset = new Vector2(
                            view.x + (view.width - ow) * 0.5f + previewPan.x + Mathf.Clamp01(spec.origin.x) * ow,
                            view.y + (view.height - oh) * 0.5f + previewPan.y + oh - Mathf.Clamp01(spec.origin.y) * oh);
                        subjectAlignOffset = attachScreen - originNoOffset;
                    }
                }

                if (spec == null)
                {
                    GUI.Label(view, "No blast selected", EditorStyles.centeredGreyMiniLabel);
                }
                else
                {
                    UpdatePreviewTexture(cur);
                    if (previewTex != null)
                    {
                        float w = spec.Width * zoom, h = spec.Height * zoom;
                        GUI.BeginClip(view);
                        var local = new Rect((view.width - w) * 0.5f + previewPan.x + subjectAlignOffset.x,
                                             (view.height - h) * 0.5f + previewPan.y + subjectAlignOffset.y, w, h);
                        GUI.DrawTexture(local, previewTex, ScaleMode.StretchToFill, true);
                        if (showFrame)
                        {
                            var frameCol = new Color(1f, 1f, 1f, 0.55f);
                            EditorGUI.DrawRect(new Rect(local.x, local.y, local.width, 1f), frameCol);
                            EditorGUI.DrawRect(new Rect(local.x, local.yMax - 1f, local.width, 1f), frameCol);
                            EditorGUI.DrawRect(new Rect(local.x, local.y, 1f, local.height), frameCol);
                            EditorGUI.DrawRect(new Rect(local.xMax - 1f, local.y, 1f, local.height), frameCol);
                        }
                        GUI.EndClip();
                    }
                }
                if (stageBg != null) PreviewStageGUI.Draw(view, stageBg, zoom, true);
            }

            // Interaction priority, identical to the pre-port window: smudge → pins → vortices → orbs →
            // origin ✛ → stage sprites → pan. Each Use()s its event.
            HandleSmudgePaint(view);
            HandlePinWarp(view);
            HandleCurlVortices(view);
            HandleMetaBlob(view);
            DrawMetaOrbMarkers(view);
            DrawSmudgeStroke(view);
            DrawPinMarkers(view);
            DrawCurlVortexMarkers(view);
            if (spec != null) DrawOriginHandle(view);
            if (stageBg != null && PreviewStageGUI.Edit(view, stageBg, zoom, ref stageSel, ref draggingStage))
                EditorUtility.SetDirty(stageBg);

            var pe = Event.current;
            if (pe.type == EventType.MouseDown && (pe.button == 0 || pe.button == 2) && view.Contains(pe.mousePosition))
            { draggingPan = true; pe.Use(); }
            if (draggingPan)
            {
                if (pe.type == EventType.MouseDrag) { previewPan += pe.delta; previewContainer.MarkDirtyRepaint(); pe.Use(); }
                if (pe.type == EventType.MouseUp) { draggingPan = false; pe.Use(); }
            }
        }

        void DrawBackdrop(Rect view)
        {
            switch (bgMode)
            {
                case PreviewBgMode.Solid:
                    EditorGUI.DrawRect(view, bgSolid);
                    break;
                case PreviewBgMode.Gradient:
                {
                    var g = bgGradient ?? DefaultBgGradient();
                    const int bands = 48;
                    for (int i = 0; i < bands; i++)
                    {
                        float tt = i / (float)(bands - 1);
                        var band = new Rect(view.x, view.y + view.height * i / bands, view.width, view.height / bands + 1f);
                        EditorGUI.DrawRect(band, g.Evaluate(tt));
                    }
                    break;
                }
                case PreviewBgMode.Image:
                    if (bgImage != null)
                    {
                        EditorGUI.DrawRect(view, bgSolid);
                        var prevCol = GUI.color;
                        GUI.color = bgImageTint;
                        GUI.BeginClip(view);
                        float w = view.width * bgImageZoom, h = view.height * bgImageZoom;
                        var imgRect = new Rect((view.width - w) * 0.5f + bgImagePos.x, (view.height - h) * 0.5f - bgImagePos.y, w, h);
                        GUI.DrawTexture(imgRect, bgImage, ScaleMode.ScaleToFit, false);
                        GUI.EndClip();
                        GUI.color = prevCol;
                    }
                    else EditorGUI.DrawRect(view, bgSolid);
                    break;
            }
        }

        void FitZoom()
        {
            if (lastView.width < 2f || spec == null) return;
            float z = Mathf.Floor(Mathf.Min(lastView.width / Mathf.Max(1, spec.Width), lastView.height / Mathf.Max(1, spec.Height)));
            zoom = Mathf.Clamp(z, 1f, 16f);
            DirtySpec();
        }

        void UpdatePreviewTexture(int f)
        {
            if (previewTex != null) DestroyImmediate(previewTex);
            previewTex = BlastRenderer.RenderFrameTexture(spec, f);
        }

        // A draggable ✛ marking the blast's origin/pivot over the preview.
        void DrawOriginHandle(Rect view)
        {
            float w = spec.Width * zoom, h = spec.Height * zoom;
            Rect spr = new Rect(view.x + (view.width - w) * 0.5f + previewPan.x + subjectAlignOffset.x,
                                view.y + (view.height - h) * 0.5f + previewPan.y + subjectAlignOffset.y, w, h);
            float ox = spr.x + Mathf.Clamp01(spec.origin.x) * w;
            float oy = spr.yMax - Mathf.Clamp01(spec.origin.y) * h;

            var e = Event.current;
            Rect zone = new Rect(ox - 8f, oy - 8f, 16f, 16f);
            if (view.Contains(new Vector2(ox, oy))) EditorGUIUtility.AddCursorRect(zone, MouseCursor.MoveArrow);

            if (e.type == EventType.MouseDown && e.button == 0 && zone.Contains(e.mousePosition) && view.Contains(e.mousePosition))
            { Undo.RecordObject(spec, "Move origin"); draggingOrigin = true; e.Use(); }
            if (draggingOrigin)
            {
                if (e.type == EventType.MouseDrag)
                {
                    spec.origin = new Vector2(Mathf.Clamp01((e.mousePosition.x - spr.x) / Mathf.Max(1f, w)),
                                              Mathf.Clamp01((spr.yMax - e.mousePosition.y) / Mathf.Max(1f, h)));
                    EditorUtility.SetDirty(spec); previewContainer.MarkDirtyRepaint(); e.Use();
                }
                if (e.type == EventType.MouseUp) { draggingOrigin = false; e.Use(); }
            }

            if (e.type == EventType.Repaint && view.Contains(new Vector2(ox, oy)) && originMarkerAlpha > 0.001f)
            {
                bool white = Mathf.Repeat((float)(EditorApplication.timeSinceStartup * 6.0), 1f) < 0.5f;
                Color c = white ? Color.white : Color.black; c.a = originMarkerAlpha;
                EditorGUI.DrawRect(new Rect(ox - 7f, oy - 1f, 14f, 2f), c);
                EditorGUI.DrawRect(new Rect(ox - 1f, oy - 7f, 2f, 14f), c);
            }
        }

        Rect FrameRect(Rect view)
        {
            float w = spec.Width * zoom, h = spec.Height * zoom;
            return new Rect(view.x + (view.width - w) * 0.5f + previewPan.x, view.y + (view.height - h) * 0.5f + previewPan.y, w, h);
        }

        void HandleMetaBlob(Rect view)
        {
            if (spec == null || layerSel < 0 || layerSel >= spec.layers.Count) return;
            var l = spec.layers[layerSel];
            if (l == null || l.shape != LayerShape.MetaBlob) return;

            var e = Event.current;
            Vector2 ctr = FrameRect(view).center;

            if (placeMetaMode)
            {
                if (e.type == EventType.MouseDown && e.button == 0 && view.Contains(e.mousePosition))
                {
                    float ox = (e.mousePosition.x - ctr.x) / Mathf.Max(0.01f, zoom);
                    float oy = (ctr.y - e.mousePosition.y) / Mathf.Max(0.01f, zoom);
                    float birth = Mathf.Clamp01(l.metaOrbs.Count * l.metaSpawnInterval);
                    Undo.RecordObject(spec, "Place MetaBlob orb");
                    l.metaOrbs.Add(new MetaOrb { pos = new Vector2(ox, oy), radius = 12f, birth = birth, life = Mathf.Clamp(1f - birth, 0.25f, 1f) });
                    metaSel = l.metaOrbs.Count - 1;
                    EditorUtility.SetDirty(spec); e.Use();
                    EditorApplication.delayCall += RebuildLeft;   // the orb list in the left pane changed
                }
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 0 && view.Contains(e.mousePosition))
                for (int i = l.metaOrbs.Count - 1; i >= 0; i--)
                {
                    var o = l.metaOrbs[i];
                    Vector2 mp = new Vector2(ctr.x + o.pos.x * zoom, ctr.y - o.pos.y * zoom);
                    if ((mp - e.mousePosition).sqrMagnitude <= 100f)
                    { Undo.RecordObject(spec, "Move MetaBlob orb"); metaSel = i; draggingMetaOrb = true; e.Use(); break; }
                }
            if (draggingMetaOrb && metaSel >= 0 && metaSel < l.metaOrbs.Count)
            {
                if (e.type == EventType.MouseDrag)
                {
                    l.metaOrbs[metaSel].pos += new Vector2(e.delta.x, -e.delta.y) / Mathf.Max(0.01f, zoom);
                    EditorUtility.SetDirty(spec); previewContainer.MarkDirtyRepaint(); e.Use();
                }
                if (e.type == EventType.MouseUp) { draggingMetaOrb = false; e.Use(); EditorApplication.delayCall += RebuildLeft; }
            }
        }

        void HandlePinWarp(Rect view)
        {
            if (editPin == null || spec == null) return;
            var e = Event.current;
            Vector2 ctr = FrameRect(view).center;
            int curFrame = CurrentFrame();

            if (e.type == EventType.MouseDown && e.button == 0 && view.Contains(e.mousePosition))
            {
                for (int i = editPin.dots.Count - 1; i >= 0; i--)
                {
                    var d = editPin.dots[i];
                    if (d == null) continue;
                    Vector2 p = d.Evaluate(curFrame);
                    Vector2 mp = new Vector2(ctr.x + p.x * zoom, ctr.y - p.y * zoom);
                    if ((mp - e.mousePosition).sqrMagnitude <= 100f)
                    { Undo.RecordObject(spec, "Move pin"); pinSel = i; draggingPin = true; e.Use(); return; }
                }
                float ox = (e.mousePosition.x - ctr.x) / Mathf.Max(0.01f, zoom);
                float oy = (ctr.y - e.mousePosition.y) / Mathf.Max(0.01f, zoom);
                int nextId = 0;
                foreach (var d in editPin.dots) if (d != null) nextId = Mathf.Max(nextId, d.id + 1);
                var dot = new PinDot { id = nextId, radius = 16f };
                dot.SetKeyframe(curFrame, new Vector2(ox, oy));
                Undo.RecordObject(spec, "Add pin");
                editPin.dots.Add(dot);
                pinSel = editPin.dots.Count - 1;
                EditorUtility.SetDirty(spec); e.Use();
                EditorApplication.delayCall += RebuildLeft;   // the pin list in the left pane changed
                return;
            }

            if (draggingPin && pinSel >= 0 && pinSel < editPin.dots.Count)
            {
                if (e.type == EventType.MouseDrag)
                {
                    var d = editPin.dots[pinSel];
                    Vector2 p = d.Evaluate(curFrame) + new Vector2(e.delta.x, -e.delta.y) / Mathf.Max(0.01f, zoom);
                    d.SetKeyframe(curFrame, p);
                    EditorUtility.SetDirty(spec); previewContainer.MarkDirtyRepaint(); e.Use();
                }
                if (e.type == EventType.MouseUp) { draggingPin = false; e.Use(); EditorApplication.delayCall += RebuildLeft; }
            }
        }

        void DrawPinMarkers(Rect view)
        {
            if (Event.current.type != EventType.Repaint || editPin == null) return;
            Vector2 ctr = FrameRect(view).center;
            int curFrame = CurrentFrame();
            Handles.BeginGUI();
            var prevC = Handles.color;
            for (int i = 0; i < editPin.dots.Count; i++)
            {
                var d = editPin.dots[i];
                if (d == null) continue;
                Vector2 p = d.Evaluate(curFrame);
                Vector2 mp = new Vector2(ctr.x + p.x * zoom, ctr.y - p.y * zoom);
                if (!view.Contains(mp)) continue;
                bool sel = pinSel == i;
                bool exactKeyframe = d.keyframes != null && d.keyframes.Exists(k => k.frame == curFrame);
                Color c = sel ? new Color(1f, 0.7f, 0.2f) : new Color(0.4f, 1f, 0.6f, 0.9f);
                Handles.color = new Color(c.r, c.g, c.b, 0.35f);
                Handles.DrawWireDisc(new Vector3(mp.x, mp.y, 0f), Vector3.forward, d.radius * zoom);
                Handles.color = c;
                if (exactKeyframe) EditorGUI.DrawRect(new Rect(mp.x - 3f, mp.y - 3f, 6f, 6f), c);
                else Handles.DrawWireDisc(new Vector3(mp.x, mp.y, 0f), Vector3.forward, 3f);
                GUI.Label(new Rect(mp.x + 5f, mp.y - 9f, 26f, 14f), d.id.ToString(), EditorStyles.miniLabel);
            }
            Handles.color = prevC;
            Handles.EndGUI();
        }

        void HandleCurlVortices(Rect view)
        {
            if (editCurl == null || spec == null) return;
            var e = Event.current;
            Vector2 ctr = FrameRect(view).center;

            if (e.type == EventType.MouseDown && e.button == 0 && view.Contains(e.mousePosition))
            {
                for (int i = editCurl.Vortices.Count - 1; i >= 0; i--)
                {
                    var v = editCurl.Vortices[i];
                    if (v == null) continue;
                    Vector2 mp = new Vector2(ctr.x + v.pos.x * zoom, ctr.y - v.pos.y * zoom);
                    if ((mp - e.mousePosition).sqrMagnitude <= 100f)
                    { Undo.RecordObject(spec, "Move vortex"); vortexSel = i; draggingVortex = true; e.Use(); return; }
                }
                float ox = (e.mousePosition.x - ctr.x) / Mathf.Max(0.01f, zoom);
                float oy = (ctr.y - e.mousePosition.y) / Mathf.Max(0.01f, zoom);
                Undo.RecordObject(spec, "Add vortex");
                editCurl.Vortices.Add(new VortexPoint { pos = new Vector2(ox, oy) });
                vortexSel = editCurl.Vortices.Count - 1;
                EditorUtility.SetDirty(spec); e.Use();
                EditorApplication.delayCall += RebuildLeft;   // the vortex list in the left pane changed
                return;
            }

            if (draggingVortex && vortexSel >= 0 && vortexSel < editCurl.Vortices.Count)
            {
                if (e.type == EventType.MouseDrag)
                {
                    editCurl.Vortices[vortexSel].pos += new Vector2(e.delta.x, -e.delta.y) / Mathf.Max(0.01f, zoom);
                    EditorUtility.SetDirty(spec); previewContainer.MarkDirtyRepaint(); e.Use();
                }
                if (e.type == EventType.MouseUp) { draggingVortex = false; e.Use(); }
            }
        }

        void DrawCurlVortexMarkers(Rect view)
        {
            if (Event.current.type != EventType.Repaint || editCurl == null) return;
            Vector2 ctr = FrameRect(view).center;
            Handles.BeginGUI();
            var prevC = Handles.color;
            for (int i = 0; i < editCurl.Vortices.Count; i++)
            {
                var v = editCurl.Vortices[i];
                if (v == null) continue;
                Vector2 mp = new Vector2(ctr.x + v.pos.x * zoom, ctr.y - v.pos.y * zoom);
                if (!view.Contains(mp)) continue;
                bool sel = vortexSel == i;
                Color c = sel ? new Color(1f, 0.7f, 0.2f) : new Color(0.5f, 0.8f, 1f, 0.9f);
                Handles.color = new Color(c.r, c.g, c.b, 0.35f);
                Handles.DrawWireDisc(new Vector3(mp.x, mp.y, 0f), Vector3.forward, v.radius.staticValue * zoom);
                Handles.color = c;
                EditorGUI.DrawRect(new Rect(mp.x - 3f, mp.y - 3f, 6f, 6f), c);
                float tickAng = (v.clockwise ? -1f : 1f) * 40f * Mathf.Deg2Rad;
                Vector2 tick = new Vector2(Mathf.Cos(tickAng), Mathf.Sin(tickAng)) * 14f;
                Handles.DrawLine(new Vector3(mp.x, mp.y, 0f), new Vector3(mp.x + tick.x, mp.y - tick.y, 0f));
                GUI.Label(new Rect(mp.x + 5f, mp.y - 9f, 60f, 14f), $"{i + 1} {(v.clockwise ? "CW" : "CCW")}", EditorStyles.miniLabel);
            }
            Handles.color = prevC;
            Handles.EndGUI();
        }

        void HandleSmudgePaint(Rect view)
        {
            if (paintSmudge == null || spec == null) return;
            var e = Event.current;
            Vector2 ctr = FrameRect(view).center;
            Vector2 ToCanvas(Vector2 mp) => new Vector2((mp.x - ctr.x) / Mathf.Max(0.01f, zoom),
                                                        (ctr.y - mp.y) / Mathf.Max(0.01f, zoom));

            if (e.type == EventType.MouseDown && e.button == 0 && view.Contains(e.mousePosition))
            {
                Undo.RecordObject(spec, "Paint smudge stroke");
                var stroke = new SmudgeStroke();
                stroke.points.Add(ToCanvas(e.mousePosition));
                paintSmudge.strokes.Add(stroke);
                draggingSmudge = true; EditorUtility.SetDirty(spec); e.Use();
            }
            else if (draggingSmudge && e.type == EventType.MouseDrag && paintSmudge.strokes.Count > 0)
            {
                var pts = paintSmudge.strokes[paintSmudge.strokes.Count - 1].points;
                Vector2 p = ToCanvas(e.mousePosition);
                if (pts.Count == 0 || (p - pts[pts.Count - 1]).sqrMagnitude >= 4f)
                    pts.Add(p);
                EditorUtility.SetDirty(spec); previewContainer.MarkDirtyRepaint(); e.Use();
            }
            else if (draggingSmudge && e.type == EventType.MouseUp)
            {
                var s = paintSmudge.strokes;
                if (s.Count > 0 && s[s.Count - 1].points.Count < 2) s.RemoveAt(s.Count - 1);
                draggingSmudge = false; EditorUtility.SetDirty(spec); e.Use();
                EditorApplication.delayCall += RebuildLeft;   // the stroke count label changed
            }
        }

        void DrawSmudgeStroke(Rect view)
        {
            if (Event.current.type != EventType.Repaint || paintSmudge == null) return;
            var strokes = paintSmudge.strokes;
            if (strokes == null || strokes.Count == 0) return;
            Vector2 ctr = FrameRect(view).center;
            Vector2 ToScreen(Vector2 p) => new Vector2(ctr.x + p.x * zoom, ctr.y - p.y * zoom);

            Handles.BeginGUI();
            var prev = Handles.color;
            Handles.color = new Color(0.4f, 0.85f, 1f, 0.9f);
            foreach (var stroke in strokes)
            {
                var pts = stroke != null ? stroke.points : null;
                if (pts == null || pts.Count < 2) continue;
                for (int i = 1; i < pts.Count; i++)
                {
                    Vector2 a = ToScreen(pts[i - 1]), b = ToScreen(pts[i]);
                    if (view.Contains(a) || view.Contains(b)) Handles.DrawAAPolyLine(3f, a, b);
                }
            }
            var lastPts = strokes[strokes.Count - 1].points;
            if (lastPts != null && lastPts.Count > 0)
            {
                Vector2 head = ToScreen(lastPts[lastPts.Count - 1]);
                if (view.Contains(head))
                {
                    float r = Mathf.Max(1f, paintSmudge.size != null ? paintSmudge.size.staticValue : 12f) * zoom;
                    Handles.color = new Color(0.4f, 0.85f, 1f, 0.35f);
                    Handles.DrawWireDisc(new Vector3(head.x, head.y, 0f), Vector3.forward, r);
                }
            }
            Handles.color = prev;
            Handles.EndGUI();
        }

        void DrawMetaOrbMarkers(Rect view)
        {
            if (Event.current.type != EventType.Repaint || !showMetaMarkers) return;
            if (spec == null || layerSel < 0 || layerSel >= spec.layers.Count) return;
            var l = spec.layers[layerSel];
            if (l == null || l.shape != LayerShape.MetaBlob) return;

            Vector2 ctr = FrameRect(view).center;
            Handles.BeginGUI();
            var prevC = Handles.color;
            for (int i = 0; i < l.metaOrbs.Count; i++)
            {
                var o = l.metaOrbs[i];
                Vector2 mp = new Vector2(ctr.x + o.pos.x * zoom, ctr.y - o.pos.y * zoom);
                if (!view.Contains(mp)) continue;
                bool sel = metaSel == i;
                Color c = sel ? new Color(0.4f, 0.8f, 1f) : new Color(1f, 1f, 1f, 0.75f);
                Handles.color = new Color(c.r, c.g, c.b, 0.4f);
                Handles.DrawWireDisc(new Vector3(mp.x, mp.y, 0f), Vector3.forward, o.radius * zoom);
                EditorGUI.DrawRect(new Rect(mp.x - 2f, mp.y - 2f, 4f, 4f), c);
                GUI.Label(new Rect(mp.x + 4f, mp.y - 9f, 26f, 14f), (i + 1).ToString(), EditorStyles.miniLabel);
            }
            Handles.color = prevC;
            Handles.EndGUI();
        }
    }
}
