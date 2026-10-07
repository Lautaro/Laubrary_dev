using System.Collections.Generic;
using Laubrary.Launimator;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Launimator.Editor
{
    /// <summary>
    /// Optional, self-contained "auto-slice from marquee" feature for the Laumination Builder. Marquee a part of
    /// the sheet (Grid mode's persistent box) and right-click it to auto-detect every sprite inside the box via
    /// projection profiles (<see cref="AutoScavenger.DetectCellsInBox"/>) — using the SAME alpha threshold and
    /// background-colour key already set on the window, so transparency matches the rest of the tool. The
    /// detection only ADDS cells to the existing region/sequence stores; the human then adjusts them normally.
    /// Kept in a partial file so the core window is untouched apart from the right-click hook.
    /// </summary>
    public partial class LauminationBuilderWindow
    {
        private const string AutoLabel = "auto";
        private bool _autoDragging;

        /// <summary>
        /// Right-button marquee gesture for auto-detect, usable in ANY tool mode (Grid/Box/Pick). A right-DRAG
        /// defines a fresh box; a plain right-CLICK inside an existing box reuses it. On release (with area) it
        /// opens the auto-add menu. Returns true when it consumed the event. Reuses the window's _box/_hasBox.
        /// </summary>
        private bool HandleAutoMarquee(Rect imageRect, Vector2 mouse, Event e)
        {
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button != 1 || !imageRect.Contains(mouse)) break;
                    // Right-click on an existing usable box → act on it without starting a new drag.
                    if (_hasBox && _box.width >= 1 && _box.height >= 1 && TexRectToContent(_box).Contains(mouse))
                    {
                        ShowMarqueeAutoMenu(); e.Use(); return true;
                    }
                    // Otherwise begin a fresh right-drag marquee.
                    _autoDragging = true;
                    _dragStartTex = SnapTex(ContentToTex(mouse));
                    _box = new Rect(_dragStartTex.x, _dragStartTex.y, 0, 0);
                    _hasBox = true; e.Use(); return true;

                case EventType.MouseDrag:
                    if (!_autoDragging) break;
                    Vector2 cur = SnapTex(ContentToTex(mouse));
                    float x0 = Mathf.Min(_dragStartTex.x, cur.x), y0 = Mathf.Min(_dragStartTex.y, cur.y);
                    float x1 = Mathf.Max(_dragStartTex.x, cur.x), y1 = Mathf.Max(_dragStartTex.y, cur.y);
                    _box = ClampBox(new Rect(x0, y0, x1 - x0, y1 - y0));
                    e.Use(); RefreshBoxDependentLabels(); Repaint(); return true;

                case EventType.MouseUp:
                    if (e.button != 1 || !_autoDragging) break;
                    _autoDragging = false;
                    if (_box.width >= 1 && _box.height >= 1) ShowMarqueeAutoMenu();
                    else { _hasBox = false; _box = default; } // a plain right-click, no region — silently drop
                    e.Use(); DeferRefresh(); return true;
            }
            return false;
        }

        /// <summary>What an auto-add does to the existing sprite palette / sequence.</summary>
        private enum AutoAddMode
        {
            Append,             // keep the current sprites; add the detected ones
            Replace,            // clear the palette; keep only the detected ones
            ReplaceAndSequence, // clear palette + sequence; add, baseline all, and sequence one of each
        }

        /// <summary>Context menu shown when the user right-clicks / releases a right-drag marquee on the canvas.</summary>
        private void ShowMarqueeAutoMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Add Sprites"), false, () => AutoAddSpritesFromBox(AutoAddMode.Append, false, null));
            menu.AddItem(new GUIContent("Replace Sprites"), false, () => AutoAddSpritesFromBox(AutoAddMode.Replace, false, null));
            menu.AddItem(new GUIContent("Replace Sprites and Sequence"), false, () => AutoAddSpritesFromBox(AutoAddMode.ReplaceAndSequence, false, null));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("New Animation…"), false, () =>
                NamePromptWindow.Show("New Animation", "Name for the new animation:", SuggestNewAnimName(),
                    name => AutoAddSpritesFromBox(AutoAddMode.ReplaceAndSequence, true, name)));
            menu.ShowAsContext();
        }

        /// <summary>
        /// Detect every sprite inside the current marquee and add them per <paramref name="mode"/>. When
        /// <paramref name="asNewAnimation"/>, resolve the current document before starting a fresh unsaved
        /// animation. Detection itself has no persistent side effects.
        /// </summary>
        private void AutoAddSpritesFromBox(AutoAddMode mode, bool asNewAnimation, string newAnimName)
        {
            var px = GetPixels();
            if (px == null) { SetStatus("Auto-add failed: texture not readable."); return; }

            // Detect against a candidate key; a failed detection or cancelled transition changes no data.
            var detectionKey = CurrentColorKey();
            Color32 detectedColor = default;
            bool autoBg = !detectionKey.enabled && RegionSlicer.TryDetectBackgroundColor(px, _texW, _texH, out detectedColor);
            if (autoBg)
            {
                detectionKey.enabled = true;
                detectionKey.color = detectedColor;
            }

            var boxInt = new RectInt(
                Mathf.RoundToInt(_box.x), Mathf.RoundToInt(_box.y),
                Mathf.RoundToInt(_box.width), Mathf.RoundToInt(_box.height));

            var s = AutoScavenger.Settings.Default;
            s.alphaThreshold = _alphaThreshold;
            s.key = detectionKey;

            List<Rect> cells = AutoScavenger.DetectCellsInBox(px, _texW, _texH, boxInt, s);
            if (cells == null || cells.Count == 0)
            {
                SetStatus("No sprites detected in the marquee — adjust the box, the alpha threshold, or the background key.");
                return;
            }
            if (asNewAnimation)
            {
                if (string.IsNullOrWhiteSpace(newAnimName) || !ConfirmDocumentTransition()) return;
                StartNewAnimation(newAnimName);
                mode = AutoAddMode.ReplaceAndSequence;
            }

            // The common mapping is the single history entry for replacement. No payload can survive
            // attached to a different image just because a new frame happens to occupy the same index.
            if (mode != AutoAddMode.Append)
            {
                RemapSequence(System.Array.Empty<int>(), "Replace detected sprites");
                _regions.Clear(); ClearThumbCache();
                ClearSelection();
                _seqSelected = -1; _seqMultiSel.Clear(); _seqAnchor = -1; _animFrame = 0;
                _frameZero = new CellRef(-1, -1); _frameZeroOn = false; _frameZeroSel = false;
            }
            else RecordUndo("Add detected sprites");
            if (autoBg)
            {
                _bgKey = detectionKey.color; _bgKeyEnabled = true; RebuildDisplaySheet();
            }

            // All auto-detected cells live in one dedicated region (created lazily, like Pick/Box).
            // Scoped to the loaded sheet like every other region kind: a frame's pixels are read from its
            // region's own texture, and a region without one cannot be previewed or saved.
            int idx = _regions.FindIndex(r => r.label == AutoLabel && r.sourceTextureGuid == CurrentSheetGuid());
            if (idx < 0) { _regions.Add(new Region { label = AutoLabel, bounds = new Rect(0, 0, _texW, _texH), sourceTextureGuid = CurrentSheetGuid() }); idx = _regions.Count - 1; }
            var reg = _regions[idx];
            int firstCell = reg.cells.Count;
            reg.cells.AddRange(cells);
            reg.SyncPivots(GlobalPivot());

            if (mode == AutoAddMode.ReplaceAndSequence)
            {
                // Baseline (align feet) every new cell, then put one of each into the sequence in reading order.
                var key = CurrentColorKey();
                for (int c = firstCell; c < reg.cells.Count; c++)
                    if (RegionSlicer.ContentBaselinePivot(px, _texW, _texH, reg.cells[c], _alphaThreshold, out Vector2 piv, key))
                        reg.pivots[c] = new Vector2(Mathf.Clamp01(piv.x), Mathf.Clamp01(piv.y));

                _sequence.Clear();
                for (int c = firstCell; c < reg.cells.Count; c++) _sequence.Add(new CellRef(idx, c));
                _seqSelected = _sequence.Count > 0 ? 0 : -1;
                _seqMultiSel.Clear(); if (_seqSelected >= 0) _seqMultiSel.Add(_seqSelected); _seqAnchor = _seqSelected;
                _animFrame = 0;
                SelectSingle(idx, firstCell);
            }
            else
            {
                SelectSingle(idx, reg.cells.Count - 1);
            }

            SyncMetaFrames();
            _previewHash = -1;

            string verb = mode == AutoAddMode.Append ? "Added"
                : mode == AutoAddMode.Replace ? "Replaced palette with"
                : "Replaced sprites + sequence with";
            _status = asNewAnimation
                ? $"New animation '{_animName}': {cells.Count} sprites added. Save to keep it."
                : $"{verb} {cells.Count} sprite(s).";
            if (autoBg) _status += $" Auto-detected background RGB({_bgKey.r},{_bgKey.g},{_bgKey.b}).";

            // The right-drag marquee is a transient gesture in Pick mode — clear it so the canvas stays clean.
            // (Grid keeps its box, matching the manual "Add Region" workflow.)
            if (_toolMode == ToolMode.Pick) { _hasBox = false; _box = default; }
            Refresh();
        }

        /// <summary>A sensible default name for a brand-new animation (avoids clobbering an existing one).</summary>
        private string SuggestNewAnimName()
        {
            var taken = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            if (_boundLauminary != null)
            {
                string folder = LauminaryRepo.DraftFolder(_boundLauminary);
                if (AssetDatabase.IsValidFolder(folder))
                    foreach (var guid in AssetDatabase.FindAssets("t:LauminaryVersion", new[] { folder }))
                    {
                        var draft = AssetDatabase.LoadAssetAtPath<LauminaryVersion>(AssetDatabase.GUIDToAssetPath(guid));
                        if (draft?.animations == null) continue;
                        foreach (var animation in draft.animations) if (animation != null) taken.Add(animation.name);
                    }
            }
            string baseName = "anim";
            if (!taken.Contains(baseName)) return baseName;
            for (int i = 2; i < 999; i++) if (!taken.Contains($"{baseName}_{i}")) return $"{baseName}_{i}";
            return baseName;
        }

        /// <summary>
        /// Reset the working sequence so the next Save CREATES a new animation (rather than renaming/overwriting
        /// the one currently being edited): clear the sequence + selection + per-frame events/meta-layers, set
        /// the name, and drop the bound-animation / orphan handle so the save path treats it as a fresh add.
        /// </summary>
        private void StartNewAnimation(string name)
        {
            PausePreview();
            DestroyPreviewBake();
            _sequence.Clear();
            _seqSelected = -1; _seqMultiSel.Clear(); _seqAnchor = -1; _animFrame = 0;
            _events = new List<FrameEvent>();
            _zonesEnabled = false; _zones.Clear();
            _metaEnabled = false; _metaLayers = new List<MetaLayer>(); _activeLayer = -1; ClearMaskCache();
            _frameZero = new CellRef(-1, -1); _frameZeroOn = false; _frameZeroSel = false;
            _showingFrameZero = false; _inDivider = false;
            _detectedCells.Clear(); _detectedBox = default;
            _animationAsepriteSourcePath = "";
            _editAsePath = null; _editSheetPath = null; _editRects.Clear(); _editSourceGuids.Clear();
            if (!string.IsNullOrWhiteSpace(name)) _animName = name.Trim();
            _boundAnimName = null;                       // bound lauminary kept; save adds a new entry
            if (_boundLauminary == null) _orphanAsset = null; // orphan mode: save creates a new orphan
            BeginCleanDocument();
        }
    }

    /// <summary>Non-modal ZUI name prompt. Calls <c>onAccept(text)</c> with the
    /// entered value when the user confirms; does nothing on cancel. Enter accepts, Escape cancels.</summary>
    internal class NamePromptWindow : EditorWindow
    {
        private string _value = "";
        private string _label = "Name:";
        private System.Action<string> _onAccept;

        public static void Show(string title, string label, string initial, System.Action<string> onAccept)
        {
            var w = CreateInstance<NamePromptWindow>();
            w.titleContent = new GUIContent(title);
            w._label = label;
            w._value = initial ?? "";
            w._onAccept = onAccept;
            w.position = new Rect(Screen.width / 2f - 160f, Screen.height / 2f - 50f, 320f, 96f);
            w.minSize = w.maxSize = new Vector2(340f, 92f);
            w.ShowUtility();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            Z.Attach(root);
            root.style.paddingLeft = root.style.paddingRight = 8;
            root.style.paddingTop = root.style.paddingBottom = 8;
            Button accept = null;
            var field = Z.TextInput(_value, _label, v =>
            {
                _value = v; accept?.SetEnabled(!string.IsNullOrWhiteSpace(v));
            }, 240f);
            root.Add(Z.Field("Name", "The name under which this animation will be saved.", field));
            var row = Z.Row();
            var cancel = Z.Button("Cancel", "Close without creating an animation.", Close);
            cancel.style.width = 70;
            accept = Z.Button("Create", "Create an unsaved animation with this name.", Accept);
            accept.style.width = 70;
            accept.SetEnabled(!string.IsNullOrWhiteSpace(_value));
            row.Add(cancel); row.Add(accept); root.Add(row);
            root.RegisterCallback<KeyDownEvent>(e =>
            {
                if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && !string.IsNullOrWhiteSpace(_value))
                { Accept(); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.Escape) { Close(); e.StopPropagation(); }
            });
            field.schedule.Execute(() => { field.Focus(); field.SelectAll(); });
        }

        private void Accept()
        {
            string v = _value;
            var cb = _onAccept;
            _onAccept = null;
            Close();
            cb?.Invoke(v);
        }
    }
}
