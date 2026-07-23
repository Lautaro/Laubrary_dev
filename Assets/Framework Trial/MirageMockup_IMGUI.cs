// MirageMockup_IMGUI — IMGUI-side deliverable for the IMGUI-vs-UI-Toolkit framework trial.
// Reproduces the SHAPE of the Mirage editor mockup (see the read-only reference at
// Assets/ZUI UI Audit Test/MirageEditorMockupWindow.cs) using ONLY plain
// UnityEditor.EditorGUILayout / UnityEngine.GUILayout — no ZUI, no other Laubrary UI toolkit.
// All data below is dummy/local — no real Laubrary asset types are referenced.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.FrameworkTrial
{
    public class MirageMockup_IMGUI : EditorWindow
    {
        [MenuItem("Laubrary/Framework Trial/IMGUI (Test)")]
        static void Open()
        {
            var win = GetWindow<MirageMockup_IMGUI>("Mirage (IMGUI Trial)");
            // Packed rows (e.g. the Previewable row, the asset-field row, Weapon trigger) need ~600px
            // of width to show every explicitly-sized control without clipping — this is a hard floor,
            // not a suggestion, since there's no horizontal scroll fallback for a vertical settings panel.
            win.minSize = new Vector2(700f, 560f);
        }

        // ── Toolbar ──────────────────────────────────────────────────────────────────────────────
        float _displayPpu = 16f;

        // ── Backsplash ───────────────────────────────────────────────────────────────────────────
        bool _backsplashExpanded = true;
        Color _backdropColor = new Color(0.08f, 0.08f, 0.10f);
        Sprite _backdropImage;
        Color _backdropTint = Color.white;
        float _backdropZoom = 1f;
        Vector2 _backdropImagePos;

        // ── Previewables list (one mock row) ────────────────────────────────────────────────────
        string _entryContentName = "Mock Hero";
        Sprite _entryContentThumb;

        // ── Entry editor ─────────────────────────────────────────────────────────────────────────
        Vector2 _entryPosition;
        float _entryScale = 1f;

        static readonly string[] WeaponOptions = { "(Zoe's default)", "Mock Gun A", "Mock Gun B" };
        int _weaponIndex = 1;

        string _choreographyName = "· none ·";
        Sprite _choreographyThumb;

        static readonly string[] ClipOptions = { "Idle", "Shoot", "Hurt" };
        int _clipIndex = 1;
        bool _loop = true;
        readonly List<string> _clipSequence = new List<string> { "Idle", "Shoot" };

        static readonly string[] LayerOptions = { "Muzzle", "Impact" };
        bool _autoFire = true;
        int _layerIndex = 0;
        Vector2 _fireDirection = Vector2.right;

        Vector2 _scroll;

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawToolbar();
            EditorGUILayout.Space(8f);
            DrawBacksplash();
            EditorGUILayout.Space(8f);

            EditorGUILayout.LabelField("Previewables (1)", EditorStyles.boldLabel);
            DrawPreviewableRow();
            EditorGUILayout.Space(8f);

            EditorGUILayout.LabelField("Entry", EditorStyles.boldLabel);
            DrawEntry();

            EditorGUILayout.Space(10f);

            EditorGUILayout.EndScrollView();
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(new GUIContent("Display PPU",
                    "Pixels-per-unit used to size previews in this window; doesn't affect any real asset."),
                    GUILayout.Width(75f));
                _displayPpu = EditorGUILayout.FloatField(_displayPpu, GUILayout.Width(45f));

                GUILayout.Space(12f);

                GUILayout.Button(new GUIContent("Add Previewable",
                    "Pick a Zoe/BlastSpec/Sprite to place — opens a thumbnail browser in the real tool."),
                    GUILayout.Width(130f));

                GUILayout.FlexibleSpace();
            }
        }

        void DrawBacksplash()
        {
            _backsplashExpanded = EditorGUILayout.Foldout(_backsplashExpanded,
                new GUIContent("Backsplash", "A private backdrop copy for this preview only, not a shared asset."),
                true);
            if (!_backsplashExpanded) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Button(new GUIContent("Recall…",
                        "Copy colour/image/position/zoom/tint FROM an existing preset — a one-time copy, not a live link."),
                        GUILayout.Width(70f));
                    GUILayout.Space(6f);
                    GUILayout.Button(new GUIContent("Save…",
                        "Write this copy's current values TO a preset you pick (overwriting it) or a new one you name."),
                        GUILayout.Width(70f));
                    GUILayout.FlexibleSpace();
                }

                EditorGUILayout.Space(4f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(new GUIContent("Colour", "Solid background fill behind the image."), GUILayout.Width(45f));
                    Rect colourRect = GUILayoutUtility.GetRect(80f, 18f, GUILayout.Width(80f), GUILayout.Height(18f));
                    _backdropColor = EditorGUI.ColorField(colourRect, GUIContent.none, _backdropColor, true, true, false);

                    GUILayout.Space(14f);

                    Rect thumbRect = GUILayoutUtility.GetRect(56f, 56f, GUILayout.Width(56f), GUILayout.Height(56f));
                    _backdropImage = (Sprite)EditorGUI.ObjectField(thumbRect, _backdropImage, typeof(Sprite), false);
                    GUI.Label(thumbRect, new GUIContent("", "The backdrop image sprite."), GUIStyle.none);

                    GUILayout.Space(14f);

                    _backdropImagePos = DraggablePad(_backdropImagePos, new Rect(-40f, -40f, 80f, 80f), 56f,
                        "Drag to pan the backdrop image within the frame.");

                    GUILayout.FlexibleSpace();
                }

                EditorGUILayout.Space(6f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(new GUIContent("Zoom", "Scales the backdrop image."), GUILayout.Width(35f));
                    Rect zoomRect = GUILayoutUtility.GetRect(110f, 18f, GUILayout.Width(110f), GUILayout.Height(18f));
                    _backdropZoom = GUI.HorizontalSlider(zoomRect, _backdropZoom, 0.1f, 16f);
                    GUI.Label(zoomRect, new GUIContent("", "Scales the backdrop image. Double-click to reset to 1."), GUIStyle.none);
                    GUILayout.Label(_backdropZoom.ToString("0.00") + "x", GUILayout.Width(38f));

                    GUILayout.Space(16f);

                    GUILayout.Label(new GUIContent("Tint", "Multiplies the image's own colours."), GUILayout.Width(28f));
                    Rect tintRect = GUILayoutUtility.GetRect(70f, 18f, GUILayout.Width(70f), GUILayout.Height(18f));
                    _backdropTint = EditorGUI.ColorField(tintRect, GUIContent.none, _backdropTint, true, true, false);

                    GUILayout.FlexibleSpace();
                }
            }
        }

        void DrawPreviewableRow()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(new GUIContent(
                    $"{_entryContentName}  @ ({_entryPosition.x:0.#}, {_entryPosition.y:0.#})",
                    "Select this previewable to edit it below."),
                    GUILayout.Width(250f));

                GUILayout.Space(14f);

                GUILayout.Button(new GUIContent("Ping", "Flash this previewable's live sprite in the Game view."),
                    GUILayout.Width(45f));
                GUILayout.Space(6f);
                GUILayout.Button(new GUIContent("Remove", "Delete this previewable."), GUILayout.Width(65f));
                GUILayout.FlexibleSpace();
            }
        }

        void DrawEntry()
        {
            EditorGUILayout.LabelField("Content", EditorStyles.miniBoldLabel);
            DrawAssetFieldRow(ref _entryContentName, ref _entryContentThumb, 44f);

            EditorGUILayout.Space(6f);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(90f)))
                {
                    GUILayout.Label(new GUIContent("Position", "World-space spawn position for this previewable."), GUILayout.Width(90f));
                    _entryPosition = DraggablePad(_entryPosition, new Rect(-10f, -10f, 20f, 20f), 80f,
                        "Drag to set the world-space spawn position for this previewable.");
                }

                GUILayout.Space(14f);

                GUILayout.Label(new GUIContent("Scale", "Uniform scale applied to the previewable."), GUILayout.Width(40f));
                _entryScale = EditorGUILayout.FloatField(_entryScale, GUILayout.Width(45f));

                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Zoe options", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(new GUIContent("Weapon",
                        "Which of THIS ZOE'S OWN equipped weapons to make active for this preview."), GUILayout.Width(60f));
                    _weaponIndex = EditorGUILayout.Popup(_weaponIndex, WeaponOptions, GUILayout.Width(160f));
                    GUILayout.FlexibleSpace();
                }

                EditorGUILayout.Space(6f);

                EditorGUILayout.LabelField(new GUIContent("Choreography", "This Zoe's own motion asset (not the backdrop)."), EditorStyles.miniBoldLabel);
                DrawAssetFieldRow(ref _choreographyName, ref _choreographyThumb, 44f);

                EditorGUILayout.Space(6f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(new GUIContent("Clip", "Animation clip to play on spawn."), GUILayout.Width(35f));
                    _clipIndex = EditorGUILayout.Popup(_clipIndex, ClipOptions, GUILayout.Width(100f));

                    GUILayout.Space(12f);

                    _loop = EditorGUILayout.ToggleLeft(
                        new GUIContent("Loop", "Restart the clip from frame 0 when it finishes."), _loop, GUILayout.Width(50f));

                    GUILayout.FlexibleSpace();
                }
            }

            EditorGUILayout.Space(8f);
            DrawSequence();

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Weapon trigger", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _autoFire = EditorGUILayout.ToggleLeft(
                        new GUIContent("Auto-fire on MetaLayer",
                            "Fire the selected weapon automatically every time the playing clip reaches Layer below."),
                        _autoFire, GUILayout.Width(160f));

                    GUILayout.Space(10f);

                    using (new EditorGUI.DisabledScope(!_autoFire))
                    {
                        GUILayout.Label(new GUIContent("Layer", "Which painted MetaLayer on the clip triggers the shot."), GUILayout.Width(40f));
                        _layerIndex = EditorGUILayout.Popup(_layerIndex, LayerOptions, GUILayout.Width(120f));
                    }

                    GUILayout.FlexibleSpace();
                }

                EditorGUILayout.Space(6f);

                using (new EditorGUI.DisabledScope(!_autoFire))
                {
                    GUILayout.Label(new GUIContent("Fire direction",
                        "Stands in for aim direction, which real gameplay (player input/AI) would normally drive — this trial has neither."),
                        GUILayout.Width(120f));
                    _fireDirection = DraggablePad(_fireDirection, new Rect(-2f, -2f, 4f, 4f), 60f,
                        "Drag to set the direction the weapon fires when triggered.");
                }
            }
        }

        void DrawSequence()
        {
            EditorGUILayout.LabelField("Sequence", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (_clipSequence.Count > 0)
                {
                    EditorGUILayout.LabelField(
                        "Overrides Clip/Loop above — cycles through these forever, in order.", EditorStyles.miniLabel);
                    EditorGUILayout.Space(4f);
                }

                int removeAt = -1, swapWithPrev = -1;
                for (int i = 0; i < _clipSequence.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(new GUIContent($"{i + 1}. {_clipSequence[i]}", "One step of the looping sequence."),
                            GUILayout.Width(150f));

                        GUILayout.Space(14f);

                        using (new EditorGUI.DisabledScope(i == 0))
                        {
                            if (GUILayout.Button(new GUIContent("^", "Move this step earlier in the sequence."), GUILayout.Width(22f)))
                                swapWithPrev = i;
                        }
                        GUILayout.Space(6f);
                        if (GUILayout.Button(new GUIContent("X", "Remove this step from the sequence."), GUILayout.Width(22f)))
                            removeAt = i;
                        GUILayout.FlexibleSpace();
                    }
                }
                if (swapWithPrev > 0)
                    (_clipSequence[swapWithPrev - 1], _clipSequence[swapWithPrev]) = (_clipSequence[swapWithPrev], _clipSequence[swapWithPrev - 1]);
                if (removeAt >= 0)
                    _clipSequence.RemoveAt(removeAt);

                EditorGUILayout.Space(4f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("+ Add clip to sequence", "Append the current Clip selection to the end of the sequence."),
                        GUILayout.Width(160f)))
                        _clipSequence.Add(ClipOptions[_clipIndex]);

                    GUILayout.Space(6f);

                    using (new EditorGUI.DisabledScope(_clipSequence.Count == 0))
                    {
                        if (GUILayout.Button(new GUIContent("Clear", "Remove every step and go back to the single Clip field above."), GUILayout.Width(50f)))
                            _clipSequence.Clear();
                    }
                    GUILayout.FlexibleSpace();
                }
            }
        }

        // Asset-field row: fixed-size thumbnail + name label + Recall/New/Edit buttons, all explicitly sized.
        static void DrawAssetFieldRow(ref string currentName, ref Sprite thumbnail, float thumbSize)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect thumbRect = GUILayoutUtility.GetRect(thumbSize, thumbSize, GUILayout.Width(thumbSize), GUILayout.Height(thumbSize));
                thumbnail = (Sprite)EditorGUI.ObjectField(thumbRect, thumbnail, typeof(Sprite), false);
                GUI.Label(thumbRect, new GUIContent("", "The currently assigned asset."), GUIStyle.none);

                GUILayout.Space(8f);

                GUILayout.Label(new GUIContent(currentName, "The currently assigned asset's name."),
                    EditorStyles.miniBoldLabel, GUILayout.Width(130f));

                GUILayout.Space(6f);

                if (GUILayout.Button(new GUIContent("Recall…", "Pick an existing asset from a thumbnail browser."), GUILayout.Width(64f)))
                    currentName = "Recalled Mock";
                GUILayout.Space(4f);
                if (GUILayout.Button(new GUIContent("New", "Create a brand new asset and assign it here."), GUILayout.Width(45f)))
                    currentName = "New Mock";
                GUILayout.Space(4f);
                using (new EditorGUI.DisabledScope(true))
                    GUILayout.Button(new GUIContent("Edit", "Open this asset in its own editor."), GUILayout.Width(45f));

                GUILayout.FlexibleSpace();
            }
        }

        // Minimal draggable 2D pad — the plain-IMGUI equivalent of a "2D value" control. Maps mouse
        // position within an explicitly-sized square to a value range, with a tooltip on the same rect.
        static Vector2 DraggablePad(Vector2 value, Rect valueBounds, float pixelSize, string tooltip)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            Rect rect = GUILayoutUtility.GetRect(pixelSize, pixelSize, GUILayout.Width(pixelSize), GUILayout.Height(pixelSize));
            Event e = Event.current;

            if (e.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.16f));
            GUI.Box(rect, GUIContent.none);

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (rect.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        value = PixelToValue(e.mousePosition, rect, valueBounds);
                        GUI.changed = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        value = PixelToValue(e.mousePosition, rect, valueBounds);
                        GUI.changed = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }

            if (e.type == EventType.Repaint)
            {
                Vector2 dot = ValueToPixel(value, rect, valueBounds);
                EditorGUI.DrawRect(new Rect(dot.x - 3f, dot.y - 3f, 6f, 6f), Color.white);
                // Crosshair through the origin so the mapped range is legible at a glance.
                Vector2 origin = ValueToPixel(Vector2.zero, rect, valueBounds);
                EditorGUI.DrawRect(new Rect(rect.x, origin.y, rect.width, 1f), new Color(1f, 1f, 1f, 0.15f));
                EditorGUI.DrawRect(new Rect(origin.x, rect.y, 1f, rect.height), new Color(1f, 1f, 1f, 0.15f));
            }

            GUI.Label(rect, new GUIContent("", tooltip), GUIStyle.none);
            return value;
        }

        static Vector2 PixelToValue(Vector2 mousePos, Rect rect, Rect bounds)
        {
            float tx = Mathf.InverseLerp(rect.xMin, rect.xMax, mousePos.x);
            float ty = Mathf.InverseLerp(rect.yMin, rect.yMax, mousePos.y);
            float x = Mathf.Clamp(Mathf.Lerp(bounds.xMin, bounds.xMax, tx), bounds.xMin, bounds.xMax);
            float y = Mathf.Clamp(Mathf.Lerp(bounds.yMax, bounds.yMin, ty), bounds.yMin, bounds.yMax);
            return new Vector2(x, y);
        }

        static Vector2 ValueToPixel(Vector2 value, Rect rect, Rect bounds)
        {
            float tx = Mathf.InverseLerp(bounds.xMin, bounds.xMax, value.x);
            float ty = Mathf.InverseLerp(bounds.yMin, bounds.yMax, value.y);
            float px = Mathf.Lerp(rect.xMin, rect.xMax, tx);
            float py = Mathf.Lerp(rect.yMax, rect.yMin, ty);
            return new Vector2(px, py);
        }
    }
}
