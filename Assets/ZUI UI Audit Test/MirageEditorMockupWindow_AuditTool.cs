// MirageEditorMockupWindow_AuditTool — an independently laid-out redesign of MirageEditorMockupWindow.cs
// (read-only reference, not modified), built for a controlled experiment comparing two verification
// loops for ZUI layout work: this window was iterated against ONLY the mechanical UIAudit tool
// (Laubrary/Audit Focused Editor Window via EditorZuiAudit/EditorUIAudit), never eyeballed visually.
//
// Same sections, same fields, same mock data pattern as the Original (Toolbar/Backsplash/Previewables
// list/Entry editor: Content/Position/Scale/Zoe options/Weapon/Choreography/Clip+Loop/Sequence/Weapon
// trigger/Fire direction) — self-contained dummy local-variable data, no real project assets, nothing
// that touches the AssetDatabase. Differences from the Original are deliberate, rule-motivated layout
// choices (see the trailing report handed back to the user for the itemised list), not a blind copy.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZuiRuntime;

namespace Laubrary.UIAudit.Tests
{
    public class MirageEditorMockupWindow_AuditTool : ZUIWindow
    {
        [MenuItem("Laubrary/ZUI/Audit Case/Mirage/Audit Tool")]
        static void Open() => GetWindow<MirageEditorMockupWindow_AuditTool>("Mirage (Audit Tool)");

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

        // The Backsplash row alone (colour swatch + image thumb + position pad + zoom/tint column) needs
        // ~520px to lay out without any single control over-shrinking below its own readable minimum —
        // set a minSize so the window can't be dragged/opened narrower than its content actually needs
        // (ui-layout-rules.md's "horizontal scrollbar is a smell" — the fix is sizing the panel to its
        // content, not further shrinking already-reasonably-sized controls to fit an arbitrary width).
        protected override void OnZUIEnable() => minSize = new Vector2(520f, 480f);

        protected override void OnZUI()
        {
            DrawToolbar();
            ZUI.VerticalSpace();
            DrawBackdrop();
            ZUI.VerticalSpace();

            ZUI.Label("Previewables (1)", ZUI.ZTextStyle.SectionHeader);
            DrawPreviewableRow();
            ZUI.VerticalSpace();

            DrawEntryHeader();
            DrawEntry();
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

        // Improvement over the Original: reaches for ZUI.SelectableRow (the control built specifically
        // for a "clickable list/tree row with a selected look") instead of a hand-rolled full-width
        // Button standing in for the same job — ZUI-first per zui.md's Quick Index.
        void DrawPreviewableRow()
        {
            using (ZUI.HRow())
            {
                string label = $"{_entryContentName}  @ ({_entryPosition.x:0.#}, {_entryPosition.y:0.#})";
                ZUI.SelectableRow(label, selected: true, out _);
                ZUI.HorizontalSpace();
                ZUI.Button(new GUIContent("Ping", "Flash this previewable's live sprite in the Game view."), ZUI.Style.Default, GUILayout.Width(40f));
                ZUI.HorizontalSpace();
                ZUI.Button(new GUIContent("Remove", "Delete this previewable."), ZUI.Style.Default, GUILayout.Width(64f));
                GUILayout.FlexibleSpace();
            }
        }

        // Improvement over the Original: the "live preview stays in sync" explanation now shares the
        // "Entry" header's own row (HelpIcon right after the title) instead of sitting alone on a
        // trailing row at the very bottom of the window, disconnected from anything — see
        // ui-layout-rules.md's "Labeling" section, "never at the end of the area it's explaining, and
        // never on a row of its own."
        void DrawEntryHeader()
        {
            using (ZUI.HRow())
            {
                ZUI.Label("Entry", ZUI.ZTextStyle.SectionHeader);
                ZUI.HorizontalSpace();
                ZUI.HelpIcon(
                    "A MirageRig in the open scene shows previewables live and stays in sync with edits " +
                    "here, in Edit mode or Play mode.");
                GUILayout.FlexibleSpace();
            }
        }

        void DrawEntry()
        {
            ZUI.Label("Content", ZUI.ZTextStyle.Subtle);
            DrawMockAssetFieldRow(ref _entryContentName);

            // A world-space spatial position is a 2D-drag-target case, NOT a row-packing case — but the
            // Scale field beside it IS a short, unrelated-to-spatial-drag scalar with room to spare next
            // to the pad's fixed square footprint, so it shares the pad's row instead of stacking below
            // it (vertical space is the scarce resource — ui-layout-rules.md's pre-flight rule #1).
            ZUI.Label("Position", ZUI.ZTextStyle.Subtle);
            using (ZUI.HRow())
            {
                _entryPosition = ZUI.PositionPad(_entryPosition, new Rect(-10f, -10f, 20f, 20f), 80f);
                ZUI.HorizontalSpace();
                _entryScale = ZUI.FloatField("Scale", _entryScale, 70f, 0.01f);
                GUILayout.FlexibleSpace();
            }

            ZUI.VerticalSpace();
            ZUI.Label("Zoe options", ZUI.ZTextStyle.Subheader);

            // Mirage never equips a weapon the Zoe doesn't already have — this only SELECTS which of
            // the Zoe's own already-configured weapon slots to preview firing. Wrapped in NarrowLabel so
            // this compact popup's label column can't inherit a wider labelWidth left ambient by another
            // field earlier in the same OnGUI pass (ui-layout-rules.md's labelWidth-leak gotcha).
            using (ZUI.NarrowLabel("Weapon"))
                _weaponIndex = EditorGUILayout.Popup(
                    new GUIContent("Weapon", "Which of THIS ZOE'S OWN equipped weapons to make active for this preview."),
                    _weaponIndex, MockWeaponOptions, GUILayout.Width(200f));

            // Improvement over the Original: the label is the short noun alone ("Choreography"); the
            // "own motion, not weapon-driven" explanation moved into a tooltip via the documented
            // ZUI.Label-has-no-tooltip-overload fallback (ui-layout-rules.md's "Known gap" note), instead
            // of a parenthetical baked into on-screen text (authoring.md rule #12).
            GUILayout.Label(new GUIContent("Choreography",
                "This Zoe's own idle/patrol motion — distinct from any motion a weapon or Combat2D drives."),
                EditorStyles.miniLabel);
            DrawMockAssetFieldRow(ref _choreographyName);

            using (ZUI.HRow())
            {
                using (ZUI.NarrowLabel("Clip"))
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
                using (ZUI.NarrowLabel("Layer"))
                    _layerIndex = EditorGUILayout.Popup(new GUIContent("Layer", "Which painted MetaLayer on the clip triggers the shot."),
                        _layerIndex, MockLayers, GUILayout.Width(160f));
                GUILayout.FlexibleSpace();
            }

            using (new EditorGUI.DisabledScope(!_autoFire))
            {
                GUILayout.Label(new GUIContent("Fire direction", "Stands in for Combatant.aimDirection, which real " +
                    "gameplay (player input/AI) would normally drive — Mirage has neither."), EditorStyles.miniLabel);
                _fireDirection = ZUI.PositionPad(_fireDirection, new Rect(-2f, -2f, 4f, 4f), 60f);
            }
        }

        // Improvement over the Original: the "Overrides Clip/Loop above…" explanation is now the
        // "Sequence" header's own tooltip (same fallback pattern as "Choreography"/"Fire direction"
        // above) instead of a permanent on-screen prose line the user re-reads every pass regardless of
        // whether they need it — ui-layout-rules.md's "Labeling" section: "Explanations belong in a
        // tooltip, not on-screen text."
        void DrawSequence()
        {
            ZUI.VerticalSpace();
            GUILayout.Label(new GUIContent("Sequence",
                "Overrides Clip/Loop above — cycles through these forever, in order."),
                EditorStyles.boldLabel);

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
        // real widths so this fixture exercises the same crowding-prone geometry — that shared control
        // isn't ported into this project yet, so its row is hand-built here rather than depending on a
        // class that doesn't exist. Uses an explicit GUILayout.Width for the name label rather than the
        // Original's Min/MaxWidth pair — ui-layout-rules.md's "Two distinct causes" section: MaxWidth
        // alone isn't reliably enforced through a ScrollView, only an explicit Width sets a real ceiling.
        static void DrawMockAssetFieldRow(ref string currentName)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect swatchRect = GUILayoutUtility.GetRect(40f, 40f, GUILayout.Width(40f), GUILayout.Height(40f));
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(swatchRect, new Color(0.11f, 0.12f, 0.15f));

                GUILayout.Label(new GUIContent(currentName, "Mock asset"), EditorStyles.miniBoldLabel,
                    GUILayout.Width(100f));

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
