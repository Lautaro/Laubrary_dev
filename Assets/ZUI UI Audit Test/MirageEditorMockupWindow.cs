// MirageEditorMockupWindow — a ZUI-built mockup of the Mirage editor's UI shape (toolbar, Backsplash
// section, previewables list, and the full entry editor: Content/Weapon/Choreography/Clip/Loop/
// Sequence/Weapon-trigger/Fire-direction), as it exists in the PreviewLab sandbox project as of
// 2026-07-21. Mirage (and BackSplash, and the LauAssetField/LauAssetPicker "pick or create a LauAsset"
// control) haven't been ported into THIS project yet — this reproduces their CONTROL SHAPE using only
// dummy, self-contained data instead: no Zoe/WeaponDef/Choreography/MirageView/BackSplash asset
// types, nothing that touches the AssetDatabase for real project content. Every field below is a
// plain local variable seeded with a placeholder value.
//
// Purpose: a "known good" regression fixture for the UIAudit tool, the counterpart to
// BadEditorWindowFixture.cs (which is deliberately BAD). This window was built compliant with
// ui-layout-rules.md from the start (tooltips, packed rows, explicit widths) and audited clean
// (Laubrary/Toggle Editor UI Recording + Audit Focused Editor Window: 0 issues) before being
// committed — run the same audit again after any ZUI/UIAudit-tool change to confirm it still reports
// zero issues on a realistic, multi-section, real-world-shaped editor UI, not just on trivial cases.
//
// DO NOT wire this to real Laubrary tool types even once Mirage/BackSplash/AssetKit's LauAssetField
// are ported here — keep it self-contained so it never depends on real project data, and never
// silently starts reflecting a DIFFERENT (newer/older) shape than what it was built to represent.
// If Mirage's real UI changes again later, update this mockup to match as a deliberate, separate step
// (like BadEditorWindowFixture, not a target for opportunistic cleanup mid-edit).

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZuiRuntime;

namespace Laubrary.UIAudit.Tests
{
    public class MirageEditorMockupWindow : ZUIWindow
    {
        [MenuItem("Laubrary/ZUI/Audit Case/Mirage/Original")]
        static void Open() => GetWindow<MirageEditorMockupWindow>("Mirage Mockup");

        // ── Toolbar ──────────────────────────────────────────────────────────────────────────────
        float _displayPpu = 16f;

        // ── Backsplash (mocks BackSplashGUI.DrawInline's shape — that control isn't ported here) ──
        string _backdropPresetName = "Midnight Techno";
        Color _backdropColor = new Color(0.08f, 0.08f, 0.10f);
        Sprite _backdropImage;
        Color _backdropTint = Color.white;
        float _backdropZoom = 1f;
        Vector2 _backdropImagePos;

        // ── Previewables list (one mock row, selected) ──────────────────────────────────────────
        string _entryContentName = "Mock Hero";

        // ── Entry editor ─────────────────────────────────────────────────────────────────────────
        Vector2 _entryPosition;
        float _entryScale = 1f;

        static readonly string[] MockWeaponOptions = { "(Zoe's default)", "Mock Gun A", "Mock Gun B" };
        int _weaponIndex = 1;

        string _choreographyName = "· none ·";

        static readonly string[] MockClips = { "Idle", "Shoot", "Hurt" };
        int _clipIndex = 1;
        bool _loop = true;
        readonly List<string> _clipSequence = new List<string> { "Idle", "Shoot" };

        static readonly string[] MockLayers = { "Muzzle", "Impact" };
        bool _autoFire = true;
        int _layerIndex = 0;
        Vector2 _fireDirection = Vector2.right;

        protected override void OnZUI()
        {
            DrawToolbar();
            ZUI.VerticalSpace();
            DrawBackdrop();
            ZUI.VerticalSpace();

            ZUI.Label("Previewables (1)", ZUI.ZTextStyle.SectionHeader);
            DrawPreviewableRow();
            ZUI.VerticalSpace();

            ZUI.Label("Entry", ZUI.ZTextStyle.SectionHeader);
            DrawEntry();

            ZUI.VerticalSpace();
            using (ZUI.HRow()) { GUILayout.FlexibleSpace(); ZUI.HelpIcon(
                "A MirageRig in the open scene shows previewables live and stays in sync with edits " +
                "here, in Edit mode or Play mode."); }
        }

        void DrawToolbar()
        {
            ZUI.Label("View", ZUI.ZTextStyle.Header);
            using (ZUI.HRow())
            {
                _displayPpu = ZUI.FloatField("Display PPU", _displayPpu, 70f, 1f);
                ZUI.HorizontalSpace();
                ZUI.Button(new GUIContent("Add Previewable",
                    "Pick a Zoe/Pyre/Sprite to place — opens a thumbnail browser in the real tool."));
                GUILayout.FlexibleSpace();
            }
        }

        void DrawBackdrop()
        {
            using (ZUI.Box("Backsplash", tooltip:
                "A private backdrop copy: Recall pulls values FROM an existing preset (a one-time copy, not " +
                "a live link), Save writes this copy's current values TO a preset you pick or a new one you " +
                "name. Editing the fields below only ever changes this copy, never a shared asset elsewhere."))
            {
                using (ZUI.HRow())
                {
                    ZUI.Button(new GUIContent("Recall…",
                        "Copy colour/image/position/zoom/tint FROM an existing preset — a one-time copy, not a live link."));
                    ZUI.HorizontalSpace();
                    ZUI.Button(new GUIContent("Save…",
                        "Write this copy's current values TO a preset you pick (overwriting it) or a new one you name."));
                    GUILayout.FlexibleSpace();
                }

                using (ZUI.HRow())
                {
                    using (ZUI.NarrowLabel("Colour"))
                        _backdropColor = EditorGUILayout.ColorField(
                            new GUIContent("Colour", "Solid background fill behind the image."), _backdropColor, true, true, false,
                            GUILayout.Width(130f), GUILayout.Height(18f));
                    ZUI.HorizontalSpace();

                    var thumbRect = GUILayoutUtility.GetRect(68f, 68f, GUILayout.Width(68f), GUILayout.Height(68f));
                    _backdropImage = (Sprite)EditorGUI.ObjectField(thumbRect,
                        new GUIContent("", "The backdrop image sprite."), _backdropImage, typeof(Sprite), false);
                    ZUI.HorizontalSpace();

                    _backdropImagePos = ZUI.PositionPad(_backdropImagePos, new Rect(-40f, -40f, 80f, 80f), 68f);
                    ZUI.HorizontalSpace();

                    GUILayout.BeginVertical(GUILayout.Width(150f), GUILayout.Height(68f));
                    GUILayout.FlexibleSpace();
                    _backdropZoom = ZUI.MicroSlider(_backdropZoom, 0.1f, 16f, "Zoom", defaultValue: 1f,
                        options: new[] { GUILayout.Width(150f), GUILayout.Height(18f) });
                    EditorGUILayout.Space(3f);
                    GUILayout.Label(new GUIContent("Tint", "Multiplies the image's own colours."), EditorStyles.miniLabel);
                    _backdropTint = EditorGUILayout.ColorField(GUIContent.none, _backdropTint, true, true, false,
                        GUILayout.Width(150f), GUILayout.Height(18f));
                    GUILayout.FlexibleSpace();
                    GUILayout.EndVertical();
                    GUILayout.FlexibleSpace();
                }
            }
        }

        void DrawPreviewableRow()
        {
            using (ZUI.HRow())
            {
                ZUI.Button($"{_entryContentName}  @ ({_entryPosition.x:0.#}, {_entryPosition.y:0.#})");
                ZUI.HorizontalSpace();
                ZUI.Button(new GUIContent("Ping", "Flash this previewable's live sprite in the Game view."), ZUI.Style.Default, GUILayout.Width(40f));
                ZUI.HorizontalSpace();
                ZUI.Button(new GUIContent("Remove", "Delete this previewable."), ZUI.Style.Default, GUILayout.Width(64f));
                GUILayout.FlexibleSpace();
            }
        }

        void DrawEntry()
        {
            ZUI.Label("Content", ZUI.ZTextStyle.Subtle);
            DrawMockAssetFieldRow(ref _entryContentName);

            // A world-space spatial position is a 2D-drag-target case, NOT a row-packing case.
            ZUI.Label("Position", ZUI.ZTextStyle.Subtle);
            _entryPosition = ZUI.PositionPad(_entryPosition, new Rect(-10f, -10f, 20f, 20f), 80f);
            _entryScale = ZUI.FloatField("Scale", _entryScale, 70f, 0.01f);

            ZUI.VerticalSpace();
            ZUI.Label("Zoe options", ZUI.ZTextStyle.Subheader);

            // Mirage never equips a weapon the Zoe doesn't already have — this only SELECTS which of
            // the Zoe's own already-configured weapon slots to preview firing.
            _weaponIndex = EditorGUILayout.Popup(
                new GUIContent("Weapon", "Which of THIS ZOE'S OWN equipped weapons to make active for this preview."),
                _weaponIndex, MockWeaponOptions, GUILayout.Width(200f));

            ZUI.Label("Choreography (own motion)", ZUI.ZTextStyle.Subtle);
            DrawMockAssetFieldRow(ref _choreographyName);

            using (ZUI.HRow())
            {
                _clipIndex = EditorGUILayout.Popup(new GUIContent("Clip", "Animation clip to play on spawn."),
                    _clipIndex, MockClips, GUILayout.Width(200f));
                ZUI.HorizontalSpace();
                _loop = ZUI.Toggle(_loop, new GUIContent("Loop", "Restart the clip from frame 0 when it finishes."));
                GUILayout.FlexibleSpace();
            }

            DrawSequence();

            ZUI.VerticalSpace();
            ZUI.Label("Weapon trigger", ZUI.ZTextStyle.Subheader);
            using (ZUI.HRow())
            {
                _autoFire = ZUI.Toggle(_autoFire, new GUIContent("Auto-fire on MetaLayer",
                    "Fire the selected weapon automatically every time the playing clip reaches Layer below."));
                ZUI.HorizontalSpace();
                using (new EditorGUI.DisabledScope(!_autoFire))
                {
                    _layerIndex = EditorGUILayout.Popup(new GUIContent("Layer", "Which painted MetaLayer on the clip triggers the shot."),
                        _layerIndex, MockLayers, GUILayout.Width(160f));
                }
                GUILayout.FlexibleSpace();
            }

            using (new EditorGUI.DisabledScope(!_autoFire))
            {
                GUILayout.Label(new GUIContent("Fire direction", "Stands in for Combatant.aimDirection, which real " +
                    "gameplay (player input/AI) would normally drive — Mirage has neither."), EditorStyles.miniLabel);
                _fireDirection = ZUI.PositionPad(_fireDirection, new Rect(-2f, -2f, 4f, 4f), 60f);
            }
        }

        void DrawSequence()
        {
            ZUI.VerticalSpace();
            ZUI.Label("Sequence", ZUI.ZTextStyle.Subheader);
            if (_clipSequence.Count > 0)
                ZUI.Label("Overrides Clip/Loop above — cycles through these forever, in order.", ZUI.ZTextStyle.Subtle);

            int removeAt = -1, swapWithPrev = -1;
            for (int i = 0; i < _clipSequence.Count; i++)
            {
                using (ZUI.HRow())
                {
                    ZUI.Label($"{i + 1}. {_clipSequence[i]}");
                    ZUI.HorizontalSpace();
                    using (new EditorGUI.DisabledScope(i == 0))
                        if (ZUI.Button(new GUIContent("↑", "Move this step earlier in the sequence."), ZUI.Style.Default, GUILayout.Width(22f)))
                            swapWithPrev = i;
                    ZUI.HorizontalSpace();
                    if (ZUI.Button(new GUIContent("X", "Remove this step from the sequence."), ZUI.Style.Default, GUILayout.Width(22f)))
                        removeAt = i;
                    GUILayout.FlexibleSpace();
                }
                if (i < _clipSequence.Count - 1) ZUI.VerticalSpace(0.5f);
            }
            if (swapWithPrev > 0)
                (_clipSequence[swapWithPrev - 1], _clipSequence[swapWithPrev]) = (_clipSequence[swapWithPrev], _clipSequence[swapWithPrev - 1]);
            if (removeAt >= 0)
                _clipSequence.RemoveAt(removeAt);

            ZUI.VerticalSpace();
            using (ZUI.HRow())
            {
                if (ZUI.Button(new GUIContent("+ Add clip to sequence", "Append another clip to the end of the sequence.")))
                    _clipSequence.Add(MockClips[_clipIndex]);
                ZUI.HorizontalSpace();
                if (_clipSequence.Count > 0 &&
                    ZUI.Button(new GUIContent("Clear", "Remove every step and go back to the single Clip field above."), ZUI.Style.Default, GUILayout.Width(50f)))
                    _clipSequence.Clear();
                GUILayout.FlexibleSpace();
            }
        }

        // Mocks LauAssetField.Draw's exact row shape (swatch + name + Recall/New/Edit), matching its
        // real widths (40/50-160/64/48/24) so this fixture exercises the same crowding-prone geometry
        // — that shared control isn't ported into this project yet, so its row is hand-built here
        // rather than depending on a class that doesn't exist.
        static void DrawMockAssetFieldRow(ref string currentName)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect swatchRect = GUILayoutUtility.GetRect(40f, 40f, GUILayout.Width(40f), GUILayout.Height(40f));
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(swatchRect, new Color(0.11f, 0.12f, 0.15f));

                GUILayout.Label(new GUIContent(currentName, "Mock asset"), EditorStyles.miniBoldLabel,
                    GUILayout.MinWidth(50), GUILayout.MaxWidth(160));

                if (GUILayout.Button(new GUIContent("Recall…", "Pick an existing asset from a thumbnail browser."), GUILayout.Width(64)))
                    currentName = "Recalled Mock";
                if (GUILayout.Button(new GUIContent("New ▾", "Create a brand new asset and assign it here."), GUILayout.Width(48)))
                    currentName = "New Mock";
                using (new EditorGUI.DisabledScope(true))
                    GUILayout.Button(new GUIContent("✎", "Edit — open this asset in its own editor."), GUILayout.Width(24));
            }
        }
    }
}
