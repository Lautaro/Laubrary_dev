// MirageMockup_UIToolkit — Framework Trial: the UI Toolkit side of a controlled experiment
// reproducing the Mirage editor's shape (see the read-only reference,
// Assets/ZUI UI Audit Test/MirageEditorMockupWindow.cs) using EditorWindow.CreateGUI() +
// VisualElement/UXML-style/USS, not IMGUI. Every value below is a plain dummy local field —
// no real Zoe/WeaponDef/Choreography/BackSplash asset types, nothing that touches real
// project content.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.FrameworkTrial
{
    public class MirageMockup_UIToolkit : EditorWindow
    {
        [MenuItem("Laubrary/Framework Trial/UI Toolkit (Test)")]
        static void Open()
        {
            var win = GetWindow<MirageMockup_UIToolkit>();
            win.titleContent = new GUIContent("Mirage (UIToolkit Trial)");
            win.minSize = new Vector2(620f, 420f);
            win.Show();
        }

        // ── dummy data model (mirrors the reference mockup's fields, all local/self-contained) ──
        float _displayPpu = 16f;

        Color _backdropColor = new Color(0.08f, 0.08f, 0.10f);
        Sprite _backdropImage;
        Color _backdropTint = Color.white;
        float _backdropZoom = 1f;
        Vector2 _backdropImagePos;

        string _entryContentName = "Mock Hero";
        Vector2 _entryPosition;
        float _entryScale = 1f;

        static readonly List<string> WeaponOptions = new List<string> { "(Zoe's default)", "Mock Gun A", "Mock Gun B" };
        int _weaponIndex = 1;

        string _choreographyName = "· none ·";

        static readonly List<string> ClipOptions = new List<string> { "Idle", "Shoot", "Hurt" };
        int _clipIndex = 1;
        bool _loop = true;
        readonly List<string> _clipSequence = new List<string> { "Idle", "Shoot" };

        static readonly List<string> LayerOptions = new List<string> { "Muzzle", "Impact" };
        bool _autoFire = true;
        int _layerIndex = 0;
        Vector2 _fireDirection = Vector2.right;

        // live references needed for enable/disable + rebuild-on-change wiring
        DropdownField _layerDropdown;
        VisualElement _fireDirRow;
        VisualElement _sequenceListContainer;
        Label _previewablesHeader;
        Label _previewableRowLabel;

        public void CreateGUI()
        {
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/Framework Trial/MirageMockup_UIToolkit.uss");
            if (uss != null) rootVisualElement.styleSheets.Add(uss);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.width = new Length(100f, LengthUnit.Percent);
            rootVisualElement.Add(scroll);

            var root = scroll.contentContainer;
            root.style.width = new Length(100f, LengthUnit.Percent);
            root.AddToClassList("root-pad");

            root.Add(BuildToolbar());
            root.Add(BuildBacksplash());
            root.Add(BuildPreviewables());
            root.Add(BuildEntry());
        }

        // ── Toolbar ──────────────────────────────────────────────────────────────────────────
        VisualElement BuildToolbar()
        {
            var box = new VisualElement();
            box.AddToClassList("section-box");
            box.Add(Header("View"));

            var row = new VisualElement();
            row.AddToClassList("row");

            var ppu = new FloatField { value = _displayPpu };
            ppu.AddToClassList("w-60");
            ppu.tooltip = "Sprite pixels-per-unit used only to scale this preview on screen.";
            ppu.RegisterValueChangedCallback(e => _displayPpu = e.newValue);
            row.Add(FieldWrap("Display PPU", ppu));

            var addBtn = new Button(() => Debug.Log("[UIToolkit Trial] Add Previewable clicked")) { text = "Add Previewable" };
            addBtn.AddToClassList("w-130");
            addBtn.tooltip = "Pick a Zoe/BlastSpec/Sprite to place — opens a thumbnail browser in the real tool.";
            row.Add(addBtn);

            box.Add(row);
            return box;
        }

        // ── Backsplash (collapsible) ────────────────────────────────────────────────────────
        VisualElement BuildBacksplash()
        {
            var foldout = new Foldout { text = "Backsplash", value = true };
            foldout.tooltip = "A private backdrop copy: Recall pulls values FROM an existing preset " +
                "(a one-time copy, not a live link), Save writes this copy's values TO a preset. " +
                "Editing the fields below only changes this copy, never a shared asset elsewhere.";
            foldout.AddToClassList("section-box");
            // Foldout's actual hoverable/clickable control is its internal disclosure Toggle, which
            // does not inherit the tooltip set on the outer Foldout element — set it directly too.
            var foldoutToggle = foldout.Q<Toggle>();
            if (foldoutToggle != null) foldoutToggle.tooltip = "Expand or collapse the Backsplash section.";

            var btnRow = new VisualElement();
            btnRow.AddToClassList("row");
            var recallBtn = new Button(() => Debug.Log("[UIToolkit Trial] Backsplash Recall")) { text = "Recall…" };
            recallBtn.AddToClassList("w-70");
            recallBtn.tooltip = "Copy colour/image/position/zoom/tint FROM an existing preset — a one-time copy, not a live link.";
            var saveBtn = new Button(() => Debug.Log("[UIToolkit Trial] Backsplash Save")) { text = "Save…" };
            saveBtn.AddToClassList("w-70");
            saveBtn.tooltip = "Write this copy's current values TO a preset you pick (overwriting it) or a new one you name.";
            btnRow.Add(recallBtn);
            btnRow.Add(saveBtn);
            foldout.Add(btnRow);

            var fieldsRow = new VisualElement();
            fieldsRow.AddToClassList("row");
            fieldsRow.style.alignItems = Align.FlexStart;

            var colour = new ColorField { value = _backdropColor, showAlpha = true, hdr = false };
            colour.AddToClassList("w-90");
            colour.AddToClassList("h-20");
            colour.tooltip = "Solid background fill behind the image.";
            colour.RegisterValueChangedCallback(e => _backdropColor = e.newValue);
            fieldsRow.Add(FieldWrap("Colour", colour));

            var image = new ObjectField { objectType = typeof(Sprite), value = _backdropImage };
            image.AddToClassList("w-150");
            image.tooltip = "The backdrop image sprite.";
            image.RegisterValueChangedCallback(e => _backdropImage = e.newValue as Sprite);
            fieldsRow.Add(image);

            var imagePosPad = new Vector2PadField(_backdropImagePos, new Rect(-40f, -40f, 80f, 80f), 68f,
                "Drag to offset the backdrop image within the frame.");
            imagePosPad.OnChanged += v => _backdropImagePos = v;
            fieldsRow.Add(imagePosPad);

            var zoomTintCol = new VisualElement();
            zoomTintCol.style.flexDirection = FlexDirection.Column;
            var zoomSlider = new Slider(0.1f, 16f) { value = _backdropZoom };
            zoomSlider.AddToClassList("w-150");
            zoomSlider.tooltip = "How much of the backdrop image fills the frame.";
            zoomSlider.RegisterValueChangedCallback(e => _backdropZoom = e.newValue);
            zoomTintCol.Add(Small("Zoom"));
            zoomTintCol.Add(zoomSlider);

            var tint = new ColorField { value = _backdropTint, showAlpha = true, hdr = false };
            tint.AddToClassList("w-150");
            tint.AddToClassList("h-20");
            tint.tooltip = "Multiplies the image's own colours.";
            tint.RegisterValueChangedCallback(e => _backdropTint = e.newValue);
            var tintLabel = Small("Tint");
            tintLabel.style.marginTop = 4f;
            zoomTintCol.Add(tintLabel);
            zoomTintCol.Add(tint);

            fieldsRow.Add(zoomTintCol);
            foldout.Add(fieldsRow);

            return foldout;
        }

        // ── Previewables list ────────────────────────────────────────────────────────────────
        VisualElement BuildPreviewables()
        {
            var box = new VisualElement();
            box.AddToClassList("section-box");
            _previewablesHeader = Header("Previewables (1)");
            box.Add(_previewablesHeader);

            var row = new VisualElement();
            row.AddToClassList("row");

            _previewableRowLabel = new Label();
            _previewableRowLabel.AddToClassList("name-label");
            RefreshPreviewableRowLabel();
            row.Add(_previewableRowLabel);

            var ping = new Button(() => Debug.Log("[UIToolkit Trial] Ping previewable")) { text = "Ping" };
            ping.AddToClassList("w-40");
            ping.tooltip = "Flash this previewable's live sprite in the Game view.";
            row.Add(ping);

            var remove = new Button(() => Debug.Log("[UIToolkit Trial] Remove previewable")) { text = "Remove" };
            remove.AddToClassList("w-64");
            remove.tooltip = "Delete this previewable.";
            row.Add(remove);

            box.Add(row);
            return box;
        }

        void RefreshPreviewableRowLabel()
        {
            if (_previewableRowLabel != null)
                _previewableRowLabel.text = $"{_entryContentName}  @ ({_entryPosition.x:0.#}, {_entryPosition.y:0.#})";
        }

        // ── Entry editor ─────────────────────────────────────────────────────────────────────
        VisualElement BuildEntry()
        {
            var box = new VisualElement();
            box.AddToClassList("section-box");
            box.Add(Header("Entry"));

            box.Add(Subtle("Content"));
            box.Add(BuildAssetFieldRow(_entryContentName, "content",
                onRecall: () => { _entryContentName = "Recalled Mock"; RefreshPreviewableRowLabel(); },
                onNew: () => { _entryContentName = "New Mock"; RefreshPreviewableRowLabel(); }));

            box.Add(Subtle("Position"));
            var posRow = new VisualElement();
            posRow.AddToClassList("row");
            posRow.style.alignItems = Align.FlexStart;

            var entryPosPad = new Vector2PadField(_entryPosition, new Rect(-10f, -10f, 20f, 20f), 80f,
                "Drag to set this previewable's world-space position.");
            entryPosPad.OnChanged += v => { _entryPosition = v; RefreshPreviewableRowLabel(); };
            posRow.Add(entryPosPad);

            var scale = new FloatField { value = _entryScale };
            scale.AddToClassList("w-60");
            scale.tooltip = "Uniform scale multiplier applied to the previewable's sprite.";
            scale.RegisterValueChangedCallback(e => _entryScale = e.newValue);
            posRow.Add(FieldWrap("Scale", scale));

            box.Add(posRow);

            box.Add(BuildZoeOptions());
            box.Add(BuildSequence());
            box.Add(BuildWeaponTrigger());

            return box;
        }

        VisualElement BuildZoeOptions()
        {
            var box = new VisualElement();
            box.AddToClassList("section-box");
            box.Add(SubHeader("Zoe options"));

            var weapon = new DropdownField(WeaponOptions, _weaponIndex);
            weapon.AddToClassList("w-200");
            weapon.tooltip = "Which of THIS ZOE'S OWN equipped weapons to make active for this preview.";
            weapon.RegisterValueChangedCallback(e => _weaponIndex = WeaponOptions.IndexOf(e.newValue));
            box.Add(FieldWrap("Weapon", weapon));

            var choreoLabel = Subtle("Choreography");
            choreoLabel.tooltip = "This previewable's own motion asset, separate from any weapon it fires.";
            box.Add(choreoLabel);
            box.Add(BuildAssetFieldRow(_choreographyName, "choreography",
                onRecall: () => _choreographyName = "Recalled Mock",
                onNew: () => _choreographyName = "New Mock"));

            var clipRow = new VisualElement();
            clipRow.AddToClassList("row");

            var clip = new DropdownField(ClipOptions, _clipIndex);
            clip.AddToClassList("w-140");
            clip.tooltip = "Animation clip to play on spawn.";
            clip.RegisterValueChangedCallback(e => _clipIndex = ClipOptions.IndexOf(e.newValue));
            clipRow.Add(FieldWrap("Clip", clip));

            var loop = new Toggle("Loop") { value = _loop };
            loop.tooltip = "Restart the clip from frame 0 when it finishes.";
            loop.RegisterValueChangedCallback(e => _loop = e.newValue);
            clipRow.Add(loop);

            box.Add(clipRow);
            return box;
        }

        VisualElement BuildSequence()
        {
            var box = new VisualElement();
            box.AddToClassList("section-box");
            box.Add(SubHeader("Sequence"));
            box.Add(Hint("Overrides Clip/Loop above — cycles through these forever, in order."));

            _sequenceListContainer = new VisualElement();
            box.Add(_sequenceListContainer);
            RefreshSequenceList();

            var addRow = new VisualElement();
            addRow.AddToClassList("row");
            var addBtn = new Button(() =>
            {
                _clipSequence.Add(ClipOptions[_clipIndex]);
                RefreshSequenceList();
            })
            { text = "+ Add clip to sequence" };
            addBtn.AddToClassList("w-170");
            addBtn.tooltip = "Append another clip to the end of the sequence.";
            addRow.Add(addBtn);

            var clearBtn = new Button(() =>
            {
                _clipSequence.Clear();
                RefreshSequenceList();
            })
            { text = "Clear" };
            clearBtn.AddToClassList("w-60");
            clearBtn.tooltip = "Remove every step and go back to the single Clip field above.";
            addRow.Add(clearBtn);

            box.Add(addRow);
            return box;
        }

        void RefreshSequenceList()
        {
            _sequenceListContainer.Clear();
            for (int i = 0; i < _clipSequence.Count; i++)
            {
                int idx = i;
                var row = new VisualElement();
                row.AddToClassList("seq-row");

                var lbl = new Label($"{idx + 1}. {_clipSequence[idx]}");
                lbl.AddToClassList("name-label");
                row.Add(lbl);

                var up = new Button(() =>
                {
                    if (idx > 0)
                    {
                        (_clipSequence[idx - 1], _clipSequence[idx]) = (_clipSequence[idx], _clipSequence[idx - 1]);
                        RefreshSequenceList();
                    }
                })
                { text = "↑" };
                up.AddToClassList("w-22");
                up.tooltip = "Move this step earlier in the sequence.";
                up.SetEnabled(idx > 0);
                row.Add(up);

                var remove = new Button(() =>
                {
                    _clipSequence.RemoveAt(idx);
                    RefreshSequenceList();
                })
                { text = "X" };
                remove.AddToClassList("w-22");
                remove.tooltip = "Remove this step from the sequence.";
                row.Add(remove);

                _sequenceListContainer.Add(row);
            }
        }

        VisualElement BuildWeaponTrigger()
        {
            var box = new VisualElement();
            box.AddToClassList("section-box");
            box.Add(SubHeader("Weapon trigger"));

            var row = new VisualElement();
            row.AddToClassList("row");

            var autoFire = new Toggle("Auto-fire on MetaLayer") { value = _autoFire };
            autoFire.tooltip = "Fire the selected weapon automatically every time the playing clip reaches the Layer below.";
            row.Add(autoFire);

            _layerDropdown = new DropdownField(LayerOptions, _layerIndex);
            _layerDropdown.AddToClassList("w-140");
            _layerDropdown.tooltip = "Which painted MetaLayer on the clip triggers the shot.";
            _layerDropdown.RegisterValueChangedCallback(e => _layerIndex = LayerOptions.IndexOf(e.newValue));
            _layerDropdown.SetEnabled(_autoFire);
            row.Add(FieldWrap("Layer", _layerDropdown));

            box.Add(row);

            _fireDirRow = new VisualElement();
            _fireDirRow.AddToClassList("row");
            _fireDirRow.style.alignItems = Align.FlexStart;
            var fireDirLabel = Small("Fire direction");
            fireDirLabel.tooltip = "Stands in for Combatant.aimDirection, which real gameplay (player input/AI) " +
                "would normally drive — this trial has neither.";

            var fireDirCol = new VisualElement();
            fireDirCol.Add(fireDirLabel);
            var fireDirPad = new Vector2PadField(_fireDirection, new Rect(-2f, -2f, 4f, 4f), 60f,
                "Drag to set the aim direction used when auto-firing.");
            fireDirPad.OnChanged += v => _fireDirection = v;
            fireDirCol.Add(fireDirPad);
            _fireDirRow.Add(fireDirCol);
            _fireDirRow.SetEnabled(_autoFire);
            box.Add(_fireDirRow);

            autoFire.RegisterValueChangedCallback(e =>
            {
                _autoFire = e.newValue;
                _layerDropdown.SetEnabled(_autoFire);
                _fireDirRow.SetEnabled(_autoFire);
            });

            return box;
        }

        // ── shared row builder: swatch + name + Recall/New/Edit (mocks a LauAssetField row) ────
        VisualElement BuildAssetFieldRow(string currentName, string kind, Action onRecall, Action onNew)
        {
            var row = new VisualElement();
            row.AddToClassList("row");

            var thumb = new VisualElement();
            thumb.AddToClassList("asset-thumb");
            thumb.AddToClassList("w-40");
            thumb.AddToClassList("h-40");
            row.Add(thumb);

            var nameLabel = new Label(currentName);
            nameLabel.AddToClassList("name-label");
            row.Add(nameLabel);

            var recall = new Button(() =>
            {
                onRecall?.Invoke();
                nameLabel.text = kind == "content" ? _entryContentName : _choreographyName;
            })
            { text = "Recall…" };
            recall.AddToClassList("w-64");
            recall.tooltip = "Pick an existing asset from a thumbnail browser.";
            row.Add(recall);

            var newBtn = new Button(() =>
            {
                onNew?.Invoke();
                nameLabel.text = kind == "content" ? _entryContentName : _choreographyName;
            })
            { text = "New ▾" };
            newBtn.AddToClassList("w-64");
            newBtn.tooltip = "Create a brand new asset and assign it here.";
            row.Add(newBtn);

            var edit = new Button(() => { }) { text = "✎" };
            edit.AddToClassList("w-40");
            edit.tooltip = "Edit — open this asset in its own editor.";
            edit.SetEnabled(false);
            row.Add(edit);

            return row;
        }

        // ── small layout helpers ─────────────────────────────────────────────────────────────
        static Label Header(string text)
        {
            var l = new Label(text);
            l.AddToClassList("header-label");
            return l;
        }

        static Label SubHeader(string text)
        {
            var l = new Label(text);
            l.AddToClassList("subheader-label");
            return l;
        }

        static Label Subtle(string text)
        {
            var l = new Label(text);
            l.AddToClassList("subtle-label");
            return l;
        }

        static Label Small(string text)
        {
            var l = new Label(text);
            l.AddToClassList("subtle-label");
            return l;
        }

        static Label Hint(string text)
        {
            var l = new Label(text);
            l.AddToClassList("hint-label");
            return l;
        }

        static VisualElement FieldWrap(string label, VisualElement control)
        {
            var wrap = new VisualElement();
            wrap.AddToClassList("field-wrap");
            var lbl = new Label(label);
            lbl.AddToClassList("field-label");
            wrap.Add(lbl);
            wrap.Add(control);
            return wrap;
        }

        // ── a real 2D drag-pad control, built from scratch on VisualElement pointer events ─────
        class Vector2PadField : VisualElement
        {
            readonly Rect _range;
            readonly float _pixelSize;
            readonly VisualElement _dot;
            Vector2 _value;

            public event Action<Vector2> OnChanged;

            public Vector2PadField(Vector2 initial, Rect range, float pixelSize, string tip)
            {
                _range = range;
                _pixelSize = pixelSize;
                _value = Clamp(initial);

                AddToClassList("pad-box");
                style.width = pixelSize;
                style.height = pixelSize;
                tooltip = tip;

                _dot = new VisualElement();
                _dot.AddToClassList("pad-dot");
                Add(_dot);

                RegisterCallback<PointerDownEvent>(OnPointerDown);
                RegisterCallback<PointerMoveEvent>(OnPointerMove);
                RegisterCallback<PointerUpEvent>(OnPointerUp);
                RegisterCallback<GeometryChangedEvent>(_ => PlaceDot());

                PlaceDot();
            }

            void OnPointerDown(PointerDownEvent evt)
            {
                this.CapturePointer(evt.pointerId);
                SetFromLocal(evt.localPosition);
                evt.StopPropagation();
            }

            void OnPointerMove(PointerMoveEvent evt)
            {
                if (!this.HasPointerCapture(evt.pointerId)) return;
                SetFromLocal(evt.localPosition);
                evt.StopPropagation();
            }

            void OnPointerUp(PointerUpEvent evt)
            {
                if (this.HasPointerCapture(evt.pointerId))
                    this.ReleasePointer(evt.pointerId);
            }

            void SetFromLocal(Vector3 local)
            {
                float tx = Mathf.Clamp01((float)local.x / _pixelSize);
                float ty = Mathf.Clamp01((float)local.y / _pixelSize);
                float x = _range.xMin + tx * _range.width;
                float y = _range.yMax - ty * _range.height;
                _value = new Vector2(x, y);
                PlaceDot();
                OnChanged?.Invoke(_value);
            }

            Vector2 Clamp(Vector2 v) => new Vector2(
                Mathf.Clamp(v.x, _range.xMin, _range.xMax),
                Mathf.Clamp(v.y, _range.yMin, _range.yMax));

            void PlaceDot()
            {
                const float dot = 6f;
                float tx = _range.width > 0f ? (_value.x - _range.xMin) / _range.width : 0.5f;
                float ty = _range.height > 0f ? (_range.yMax - _value.y) / _range.height : 0.5f;
                _dot.style.left = tx * _pixelSize - dot * 0.5f;
                _dot.style.top = ty * _pixelSize - dot * 0.5f;
            }
        }
    }
}
