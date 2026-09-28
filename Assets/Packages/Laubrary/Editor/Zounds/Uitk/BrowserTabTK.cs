using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Laubrary.Zui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static Laubrary.Zounds.ZoundsWindowProperties.ZoundTabProperties;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The UI Toolkit twin of the Zounds window's Browser tab (T-0470): the title box with the settings panel, the presets
    /// bar, search and master volume, the quick-controls toolbar, and the list (or grid) of sounds.
    ///
    /// The layout follows BrowserTab.OnGUI line for line; row geometry, filtering, grouping, presets and every edit are
    /// the old tab's own shared code. The top part is rebuilt only when what it shows changes (so typing in the search
    /// box keeps its focus); the list is rebuilt when the filtered set changes and otherwise refreshed in place.
    /// </summary>
    internal class BrowserTabTK : VisualElement, IZoundRowHost {

        const float ToolbarHeight = 30f;
        /// <summary>A Flat toolbar button's width past its text, and the gap between toolbar items, as measured on the
        /// old window (the IMGUI style's own size, not the sheet's padding): 10.5 pt each.</summary>
        const float FlatExtra = 10.5f, ToolbarGap = 10.5f;

        readonly ZoundsWindowTK win;
        readonly ZoundBrowserFilterEngine engine = new ZoundBrowserFilterEngine();
        static bool s_recent;                 // All / Recent (the old tab keeps it per tab instance, for the session)
        static bool s_showSettings;           // the gear's panel (likewise not saved)
        static Zound s_selected;              // grid mode's inspected sound
        static string s_tagsSearchText = "";
        static ZoundsEditorPresets.ViewPreset s_presetToRename;
        static string s_lastPresetName;

        readonly VisualElement top, listHost;
        string topSig, listSig;
        List<Zound> filtered = new List<Zound>();
        List<KeyValuePair<string, List<Zound>>> groups;
        BrowserTab.ZoundListRowLayout rowLayout;
        readonly List<ZoundListRowTK> rows = new List<ZoundListRowTK>();
        readonly List<GridCellTK> cells = new List<GridCellTK>();
        GridInspectorTK inspector;
        TextField search;
        ZuiSkinThumbSlider volume;
        bool toolbarWrapped;
        VisualElement toolbarHost;

        static ZoundsWindowProperties.ZoundTabProperties Props => ZoundsWindowProperties.Instance.zoundTabProperties[0];

        public BrowserTabTK(ZoundsWindowTK win) {
            this.win = win;
            style.flexGrow = 1; style.flexShrink = 1;
            top = new VisualElement(); top.style.flexShrink = 0;
            listHost = new VisualElement(); listHost.style.flexGrow = 1; listHost.style.flexShrink = 1;
            Add(top); Add(listHost);
            RefreshFiltered();
            BuildTop();
            BuildList();
            RegisterCallback<GeometryChangedEvent>(_ => CheckWidthDependent());
        }

        // ─────────────────────────── state → signatures ───────────────────────────

        void RefreshFiltered() {
            bool dirty = Props.dirty;
            if (Props.selectedTypes.HasFlag(ZoundType.Everything)) Props.selectedTypes = ZoundType.None;
            engine.Invalidate();
            var list = engine.GetFilteredZounds(BrowserTab.ZoundsToDisplay(s_recent, 0), Props);
            list = engine.EvaluateGroup(list, Props);
            // The engine clears the shared "dirty" mark; the old window still needs it to refresh its own cache.
            Props.dirty = dirty;
            filtered = list;
            groups = engine.groupCache;
        }

        float WindowWidth => win.position.width;

        string TopSignature() {
            var bs = ZoundsProject.Instance.browserSettings;
            var sb = new StringBuilder();
            sb.Append(ZoundsWindow.CurrentProjectJSON != null ? ZoundsWindow.CurrentProjectJSON.name : "-").Append('|')
              .Append(bs.fancyTitle).Append(s_showSettings).Append(s_recent).Append(bs.showSearch).Append(bs.showMasterVolume)
              .Append(bs.showAddZound).Append(bs.showStopAll).Append(bs.showMSClean).Append(bs.showTypeKlip).Append(bs.showTypeZeq)
              .Append(bs.showTypeFiles).Append(bs.showTypeMissing).Append(bs.showTagsFilter).Append(bs.showGroupBy).Append(bs.showColumnMode)
              .Append(bs.multicolumn).Append(bs.tagsOnOwnRow).Append((int)Props.selectedTypes).Append(Props.selectedTags.Count > 0)
              .Append(Props.groupBy).Append(toolbarWrapped).Append(WindowWidth >= 420f);
            if (s_showSettings) {
                sb.Append(bs.showVolume).Append(bs.showPitch).Append(bs.showChance).Append(bs.showNameField).Append(bs.showTags).Append(bs.showMute)
                  .Append(bs.showSolo).Append(bs.showOpenEditor).Append(bs.showConvertToZequence).Append(bs.showRouting).Append(bs.showDuplicate)
                  .Append(bs.showRemove).Append(bs.vpcShowSliderType).Append(bs.vpcShowInputBoxes).Append((int)bs.buttonSizeMode).Append(bs.killOnPlay)
                  .Append(bs.msOnly).Append(bs.vpcPercentage).Append(bs.vpcCompactLabel).Append(bs.highQualityWaveform).Append(bs.showPresetsAlways);
            }
            sb.Append(BrowserTab.PresetsBarHeight(WindowWidth, out _));
            foreach (var p in ZoundsEditorPresets.Instance.viewPresets) sb.Append(p.name).Append(',');
            return sb.ToString();
        }

        string ListSignature() {
            var bs = ZoundsProject.Instance.browserSettings;
            var sb = new StringBuilder();
            sb.Append(bs.multicolumn).Append((int)bs.buttonSizeMode).Append(bs.itemWidth).Append(s_recent).Append(Props.groupBy)
              .Append(bs.showVolume).Append(bs.showPitch).Append(bs.showChance).Append(bs.showNameField).Append(bs.showTags).Append(bs.tagsOnOwnRow)
              .Append(bs.showMute).Append(bs.showSolo).Append(bs.showOpenEditor).Append(bs.showConvertToZequence).Append(bs.showRouting)
              .Append(bs.showDuplicate).Append(bs.showRemove).Append(bs.vpcShowSliderType).Append(bs.vpcShowInputBoxes).Append(Application.isPlaying).Append('|');
            if (bs.multicolumn) sb.Append(s_selected != null ? s_selected.GetHashCode() : 0).Append(Mathf.FloorToInt(WindowWidth)).Append('|');
            foreach (var z in filtered) sb.Append(z.GetHashCode()).Append(':').Append(z.name).Append(';');
            if (groups != null) foreach (var g in groups) sb.Append(g.Key).Append(g.Value.Count).Append('/');
            return sb.ToString();
        }

        /// <summary>5 Hz: rebuild what changed shape, refresh the rest in place.</summary>
        public void Tick() {
            RefreshFiltered();
            if (TopSignature() != topSig) BuildTop();
            if (ListSignature() != listSig) BuildList();
            else { foreach (var r in rows) r.Sync(); foreach (var c in cells) c.Sync(); inspector?.Sync(); }
            SyncTop?.Invoke();
        }

        /// <summary>30 Hz: play pulses and tints.</summary>
        public void Live() {
            foreach (var r in rows) r.Live();
            foreach (var c in cells) c.Live();
        }

        void CheckWidthDependent() {
            bool wrap = NeedsToolbarWrap();
            if (wrap != toolbarWrapped || TopSignature() != topSig) { toolbarWrapped = wrap; BuildTop(); }
            if (ZoundsProject.Instance.browserSettings.multicolumn && ListSignature() != listSig) BuildList();
        }

        // ─────────────────────────── IZoundRowHost ───────────────────────────

        public void OpenZoundEditor(Zound zound) => OpenEditor(zound, this);

        /// <summary>BrowserTab.OpenZoundEditor, opening the UI Toolkit editors (the Zequence twin does the same).</summary>
        internal static void OpenEditor(Zound zound, IZoundRowHost host) {
            if (zound == null) return;
            if (zound is ClipZound clip) {
                if (EditorUtility.DisplayDialog("Convert to Klip: " + zound.name, "In order for this audio clip to be editable, it must be converted into a Klip. Convert this into a Klip?\n" + zound.name, "Convert", "Cancel")) {
                    BrowserTab.ConvertClipToKlipShared(clip);
                    host.ListChanged();
                }
            }
            else if (zound is Klip klip) KlipEditorWindowTK.Open(klip, false);
            else if (zound is Zequence zeq) ZequenceEditorWindowTK.Open(zeq, false);
        }

        public void ListChanged(Zound select = null) {
            if (select != null) s_selected = select;
            RefreshFiltered();
            BuildList();
        }

        // ─────────────────────────── top: title box, settings, presets, search, toolbar ───────────────────────────

        event Action SyncTop;

        void BuildTop() {
            top.Clear();
            SyncTop = null;
            toolbarWrapped = NeedsToolbarWrap();
            topSig = TopSignature();

            top.Add(TitleBox());
            top.Add(ZequenceEditorWindowTK.Space(ZS.RowSpace));
            top.Add(PresetsBar());
            top.Add(ZequenceEditorWindowTK.Space(ZS.RowSpace));
            var sv = SearchAndVolume();
            if (sv != null) top.Add(sv);
            top.Add(ZequenceEditorWindowTK.Space(ZS.RowSpace));
            toolbarHost = Toolbar();
            top.Add(toolbarHost);
            top.Add(ZequenceEditorWindowTK.Space(ZS.RowSpace * 2f));
        }

        VisualElement TitleBox() {
            var box = new VisualElement();
            box.AddToClassList("zs-box-default");
            box.style.flexShrink = 0;
            var bs = ZoundsProject.Instance.browserSettings;
            string fileName = ZoundsWindow.CurrentProjectJSON != null ? ZoundsWindow.CurrentProjectJSON.name : "No Project Loaded";
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.FlexStart;
            if (bs.fancyTitle) row.Add(ZequenceEditorWindowTK.Flex());
            var title = new Label(fileName);
            if (bs.fancyTitle) { title.AddToClassList("zs-title"); title.AddToClassList("zs-text-title"); }
            else { title.AddToClassList("zs-lbl"); title.AddToClassList("zs-bold"); title.style.height = 18f; }
            row.Add(title);
            row.Add(ZequenceEditorWindowTK.Flex());
            var gear = new Button(() => { s_showSettings = !s_showSettings; BuildTop(); }) { tooltip = "Toggle browser settings." };
            gear.AddToClassList("zs-gear");
            gear.Add(new Image { image = EditorGUIUtility.IconContent("SettingsIcon").image, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore });
            row.Add(gear);
            box.Add(row);
            box.Add(ZequenceEditorWindowTK.Space(10f));
            if (s_showSettings) box.Add(SettingsPanel());
            return box;
        }

        // ── the settings panel (BrowserTab.DrawSettingsPanel) ──

        SerializedObject so;
        SerializedProperty Prop(string name) => so.FindProperty("browserSettings." + name);

        void Apply() {
            if (so.ApplyModifiedProperties()) ZoundsWindow.SetZoundsProjectDirty();
        }

        VisualElement SettingsPanel() {
            so = new SerializedObject(ZoundsProject.Instance);
            var panel = new VisualElement();
            panel.AddToClassList("zs-settings");
            panel.Add(ZoundsWindowTK.ProjectFileRow(() => { ListChanged(); BuildTop(); }));
            panel.Add(ZequenceEditorWindowTK.Space(ZS.RowSpace));

            SectionHeader(panel, "Display Options");
            panel.Add(ToggleRow(("showVolume", "Vol"), ("showPitch", "Pit"), ("showChance", "Cha"), ("showNameField", "Name"), ("showTags", "Tags"),
                                ("showMute", "Mute"), ("showSolo", "Solo"), ("showOpenEditor", "Edit"), ("showConvertToZequence", "Conv"),
                                ("showRouting", "Route"), ("showDuplicate", "Dup"), ("showRemove", "Del")));
            panel.Add(ZequenceEditorWindowTK.Space(ZS.RowSpace));

            // VPC Sliders: label (80), the two-way radio, Input Boxes.
            {
                var r = Row();
                var l = new Label("VPC Sliders"); l.AddToClassList("zs-lbl"); l.style.width = 80f; l.style.height = 18f;
                r.Add(l);
                bool sliderType = ZoundsProject.Instance.browserSettings.vpcShowSliderType;
                var a = ZS.Toggle("Type+Values", "", sliderType, _ => { Prop("vpcShowSliderType").boolValue = true; Apply(); }, "RichToggle", ZUICornerMask.Left, -1f, 18f);
                var b = ZS.Toggle("Only Values", "", !sliderType, _ => { Prop("vpcShowSliderType").boolValue = false; Apply(); }, "RichToggle", ZUICornerMask.Right, -1f, 18f);
                a.style.width = b.style.width = 100f;   // the radio's cells, measured 100 each
                r.Add(a); r.Add(b);
                r.Add(ZequenceEditorWindowTK.Gap(ZS.MediumSpacing));
                r.Add(SettingToggle("vpcShowInputBoxes", "Input Boxes", ZUICornerMask.None));
                r.Add(ZequenceEditorWindowTK.Flex());
                panel.Add(r);
                SyncTop += () => {
                    bool st = ZoundsProject.Instance.browserSettings.vpcShowSliderType;
                    a.SetValueWithoutNotify(st); b.SetValueWithoutNotify(!st);
                };
            }
            panel.Add(ZequenceEditorWindowTK.Space(ZS.RowSpace));

            SectionHeader(panel, "Global Settings");
            {
                var r = Row();
                var bsm = new PropertyField(Prop("buttonSizeMode"), "Button Size Mode");
                var mode = new EnumField("Button Size Mode", ZoundsProject.Instance.browserSettings.buttonSizeMode) { tooltip = "Choose how zound buttons calculate their width." };
                mode.AddToClassList("zs-imgui-field"); mode.AddToClassList("zs-label-110");
                mode.style.maxWidth = 200f; mode.style.flexGrow = 1;
                mode.RegisterValueChangedCallback(e => { Prop("buttonSizeMode").enumValueIndex = (int)(ZoundsProject.BrowserSettings.ButtonSizeMode)e.newValue; Apply(); BuildTop(); });
                r.Add(mode);
                r.Add(ZequenceEditorWindowTK.Gap(10f));
                if (ZoundsProject.Instance.browserSettings.buttonSizeMode != ZoundsProject.BrowserSettings.ButtonSizeMode.Auto) {
                    var width = new Slider("Width", 38f, 800f) { value = ZoundsProject.Instance.browserSettings.itemWidth, showInputField = true, tooltip = "Width of each element." };
                    width.AddToClassList("zs-imgui-slider"); width.AddToClassList("zs-imgui-field"); width.AddToClassList("zs-label-45");
                    width.style.maxWidth = 200f; width.style.flexGrow = 1;
                    width.RegisterValueChangedCallback(e => { Prop("itemWidth").floatValue = e.newValue; Apply(); });
                    r.Add(width);
                    r.Add(ZequenceEditorWindowTK.Gap(10f));
                    SyncTop += () => { if (width.focusController?.focusedElement == null || !width.Contains(width.focusController.focusedElement as VisualElement)) width.SetValueWithoutNotify(ZoundsProject.Instance.browserSettings.itemWidth); };
                }
                r.Add(SettingToggle("killOnPlay", "Kill On Play", ZUICornerMask.None));
                r.Add(ZequenceEditorWindowTK.Gap(10f));
                var ms = ModifyToggle("M/S Only", "Only show either muted or solo zounds.", () => ZoundsProject.Instance.browserSettings.msOnly,
                                      v => ZoundsWindow.ModifyZoundsProject("toggle MS only", () => { ZoundsProject.Instance.browserSettings.msOnly = v; }), 65f);
                r.Add(ms);
                r.Add(ZequenceEditorWindowTK.Flex());
                panel.Add(r);
            }
            panel.Add(ZequenceEditorWindowTK.Space(ZS.RowSpace * 0.5f));
            {
                var r = Row();
                var bs = ZoundsProject.Instance.browserSettings;
                r.Add(ModifyToggle("VPC %", "", () => bs.vpcPercentage, v => ZoundsWindow.ModifyZoundsProject("toggle VPC percentage", () => { bs.vpcPercentage = v; })));
                r.Add(ZequenceEditorWindowTK.Gap(10f));
                r.Add(ModifyToggle("VPC Label", "", () => bs.vpcCompactLabel, v => ZoundsWindow.ModifyZoundsProject("toggle VPC compact label", () => { bs.vpcCompactLabel = v; })));
                r.Add(ZequenceEditorWindowTK.Gap(10f));
                r.Add(ModifyToggle("Fancy Title", "", () => bs.fancyTitle, v => ZoundsWindow.ModifyZoundsProject("toggle fancy title", () => { bs.fancyTitle = v; })));
                panel.Add(r);
            }
            panel.Add(ZequenceEditorWindowTK.Space(ZS.RowSpace * 0.5f));
            {
                // The skin chooser acts on the Zounds sheet (the old window's). This window's look is a copy and does not
                // follow a skin change.
                var r = Row();
                var sheet = AssetDatabase.LoadAssetAtPath<ZUIStyleSheetAsset>("Assets/Packages/Laubrary/Editor/Zounds/ZUI Assets/ZOUNDS ZUI Style Sheet.asset");
                var names = sheet != null ? sheet.GetSkinNames() : new string[0];
                var options = new List<string> { "Default" }; options.AddRange(names);
                string current = sheet?.ActiveSkin?.name;
                int idx = current == null ? 0 : Mathf.Max(0, Array.IndexOf(names, current) + 1);
                r.AddToClassList("zs-skinrow");
                var skin = new PopupField<string>("Skin", options, idx);
                skin.AddToClassList("zs-imgui-field"); skin.AddToClassList("zs-label-34");
                skin.style.maxWidth = 200f; skin.style.flexGrow = 1;
                skin.RegisterValueChangedCallback(e => {
                    if (sheet == null) return;
                    sheet.SetActiveSkin(e.newValue == "Default" ? null : e.newValue);
                    if (ZUI.ActiveSheet == sheet) ZUI.InvalidateAllStyles();
                    EditorUtility.SetDirty(sheet);
                });
                r.Add(skin);
                panel.Add(r);
            }
            panel.Add(ZequenceEditorWindowTK.Space(ZS.RowSpace));

            SectionHeader(panel, "Quick Controls Customization");
            panel.Add(ToggleRow(("showAddZound", "Add"), ("showStopAll", "Kill"), ("showMSClean", "Mute/Solo"), ("showMasterVolume", "Master Vol"), ("showSearch", "Search")));
            panel.Add(ZequenceEditorWindowTK.Space(ZS.RowSpace * 0.5f));
            panel.Add(ToggleRow(("showTypeKlip", "Klip"), ("showTypeZeq", "Zeq"), ("showTypeFiles", "Files"), ("showTypeMissing", "Missing"),
                                ("showTagsFilter", "Tags"), ("showGroupBy", "Grouping"), ("showColumnMode", "Layout")));
            panel.Add(ZequenceEditorWindowTK.Space(ZS.RowSpace));

            SectionHeader(panel, "Waveform");
            {
                var r = Row();
                r.Add(SettingToggle("highQualityWaveform", "HQ Wave", ZUICornerMask.None, AudioWaveformUtility.ClearCache));
                panel.Add(r);
            }
            panel.Add(ZequenceEditorWindowTK.Space(ZS.RowSpace));
            return panel;
        }

        static VisualElement Row() {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row; r.style.flexShrink = 0; r.style.alignItems = Align.FlexStart;
            return r;
        }

        static void SectionHeader(VisualElement panel, string text) {
            panel.Add(ZequenceEditorWindowTK.Space(5f));
            var l = new Label(text);
            l.AddToClassList("zs-subheader"); l.AddToClassList("zs-text-subheader");
            panel.Add(l);
            panel.Add(ZequenceEditorWindowTK.Space(2f));
        }

        /// <summary>DrawSettingToggle: a RichToggle bound to a browser setting (18 high, 28 to 90 wide).</summary>
        ZuiToggleButton SettingToggle(string prop, string label, ZUICornerMask corners, Action afterChange = null) {
            var sp = Prop(prop);
            var t = ZS.Toggle(label, "", sp.boolValue, v => { Prop(prop).boolValue = v; Apply(); afterChange?.Invoke(); }, "RichToggle", corners, -1f, 18f);
            Stretch(t, 90f);
            SyncTop += () => { so.Update(); t.SetValueWithoutNotify(Prop(prop).boolValue); };
            return t;
        }

        ZuiToggleButton ModifyToggle(string label, string tooltip, Func<bool> get, Action<bool> set, float maxWidth = -1f) {
            var t = ZS.Toggle(label, tooltip, get(), v => set(v), "RichToggle", ZUICornerMask.None, -1f, 18f);
            Stretch(t, maxWidth);
            SyncTop += () => t.SetValueWithoutNotify(get());
            return t;
        }

        /// <summary>An IMGUI toggle that stretches (the RichToggle style): the row's spare width is shared equally, each
        /// between 28 and <paramref name="maxWidth"/> (no cap when it is not positive).</summary>
        static void Stretch(VisualElement t, float maxWidth) {
            t.style.flexGrow = 1; t.style.flexShrink = 1; t.style.flexBasis = 0;
            t.style.minWidth = 28f;
            t.style.maxWidth = maxWidth > 0f ? maxWidth : StyleKeyword.None;
        }

        VisualElement ToggleRow(params (string prop, string label)[] items) {
            var r = Row();
            for (int i = 0; i < items.Length; i++) {
                if (i > 0) r.Add(ZequenceEditorWindowTK.Gap(ZS.MediumSpacing));
                var mask = i == 0 ? ZUICornerMask.Left : i == items.Length - 1 ? ZUICornerMask.Right : ZUICornerMask.None;
                r.Add(SettingToggle(items[i].prop, items[i].label, mask));
            }
            return r;
        }

        // ── presets bar (PresetsBarDrawer.DrawPresets) ──

        VisualElement PresetsBar() {
            float h = BrowserTab.PresetsBarHeight(WindowWidth, out _);
            var bar = new VisualElement();
            bar.AddToClassList("zs-presets");
            bar.style.height = h; bar.style.flexShrink = 0;
            var label = new Label("Presets:");
            label.AddToClassList("zs-lbl");
            label.style.position = Position.Absolute; label.style.left = 0; label.style.top = 0;
            label.style.width = PresetsBarDrawer.presetsLabelWidth; label.style.height = EditorGUIUtility.singleLineHeight;
            bar.Add(label);
            var viewport = new VisualElement();
            viewport.style.position = Position.Absolute; viewport.style.left = PresetsBarDrawer.presetsLabelWidth; viewport.style.top = 0;
            viewport.style.right = 0; viewport.style.bottom = 0;
            viewport.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            bar.Add(viewport);
            var scroll = new ScrollView(ScrollViewMode.Horizontal);
            scroll.AddToClassList("zs-presets__scroll");
            scroll.style.position = Position.Absolute; scroll.style.left = 0; scroll.style.top = 0; scroll.style.bottom = 0;
            scroll.style.right = PresetsBarDrawer.savePresetButtonWidth + 4f;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.horizontalScrollerVisibility = h > 20f ? ScrollerVisibility.Auto : ScrollerVisibility.Hidden;
            scroll.contentContainer.style.flexDirection = FlexDirection.Row;
            viewport.Add(scroll);
            scroll.Add(PresetButton(null));
            foreach (var p in ZoundsEditorPresets.Instance.viewPresets) scroll.Add(PresetButton(p.name));
            var save = new Button { text = "Save Preset", tooltip = "Save as a new preset, or use an existing preset name to override it." };
            save.clicked += () => SavePresetPopup.Show(save.worldBound, s_lastPresetName, SavePreset);
            save.AddToClassList("zs-imgui-button");
            save.style.position = Position.Absolute; save.style.right = 2f; save.style.top = 2f;
            save.style.width = PresetsBarDrawer.savePresetButtonWidth; save.style.height = h - 4f;
            bar.Add(save);
            return bar;
        }

        static void SavePreset(string name) {
            s_lastPresetName = BrowserTab.SavePresetShared(name, ref s_presetToRename);
        }

        VisualElement PresetButton(string presetName) {
            string text = presetName ?? "Default";
            var b = ZS.Button(text, "", "Default", null, ZUICornerMask.None, -1f, 22f);
            b.AddToClassList("zs-presetbutton");
            b.clickable = null;
            b.RegisterCallback<PointerUpEvent>(e => {
                if (!b.ContainsPoint(e.localPosition)) return;
                BrowserTab.PresetClickShared(text, e.button,
                    applied => { if (applied != null) s_lastPresetName = applied; ListChanged(); BuildTop(); },
                    preset => { s_presetToRename = preset; SavePresetPopup.Show(b.worldBound, text, SavePreset); },
                    SavePreset);
            });
            ZoundListRowTK.TintButton(b, PresetsBarDrawer.GenerateRandomColor(text), new Color32(255, 228, 198, 255));
            // GUI.color tints the flat colour too.
            var tint = PresetsBarDrawer.GenerateRandomColor(text);
            b.style.backgroundColor = new Color(57 / 255f * tint.r, 76 / 255f * tint.g, 36 / 255f * tint.b, 1f);
            b.style.borderTopColor = b.style.borderBottomColor = b.style.borderLeftColor = b.style.borderRightColor = new Color(32 / 255f * tint.r, 79 / 255f * tint.g, 124 / 255f * tint.b, 1f);
            return b;
        }

        // ── search + master volume ──

        VisualElement SearchAndVolume() {
            var bs = ZoundsProject.Instance.browserSettings;
            bool showBoth = bs.showSearch && bs.showMasterVolume;
            bool sideBy = WindowWidth >= 420f || !showBoth;
            if (!bs.showSearch && !bs.showMasterVolume) return null;
            var host = new VisualElement();
            host.style.flexShrink = 0;
            host.style.flexDirection = sideBy ? FlexDirection.Row : FlexDirection.Column;
            host.AddToClassList("zs-searchvolume");
            // IMGUI shares the spare width equally between the two halves after each one's own minimum, and the search half's
            // minimum is the larger (measured: search box to 507, the volume label from 550, at a 1000 pt window): an 82 pt head start reproduces that.
            if (bs.showSearch) { var s = SearchRow(); if (sideBy) { s.style.flexGrow = 1; s.style.flexBasis = showBoth ? 82f : 0f; } host.Add(s); }
            if (showBoth) host.Add(sideBy ? ZequenceEditorWindowTK.Gap(8f) : ZequenceEditorWindowTK.Space(6f));
            if (bs.showMasterVolume) { var v = VolumeRow(); if (sideBy) { v.style.flexGrow = 1; v.style.flexBasis = 0; } host.Add(v); }
            return host;
        }

        VisualElement SearchRow() {
            var r = Row();
            r.AddToClassList("zs-searchrow");
            search = new TextField { value = Props.searchText };
            search.AddToClassList("zs-search");
            search.style.flexGrow = 1; search.style.flexShrink = 1;
            var ghost = new Label("Search...") { pickingMode = PickingMode.Ignore };
            ghost.AddToClassList("zs-search__ghost");
            search.Add(ghost);
            void Ghost() => ghost.style.display = string.IsNullOrEmpty(search.value) && search.focusController?.focusedElement != search ? DisplayStyle.Flex : DisplayStyle.None;
            search.RegisterValueChangedCallback(e => {
                Undo.RecordObject(ZoundsWindowProperties.Instance, "change search text");
                Props.searchText = e.newValue;
                Props.dirty = true;
                EditorUtility.SetDirty(ZoundsWindowProperties.Instance);
                Ghost();
                RefreshFiltered(); BuildList();
            });
            search.RegisterCallback<FocusInEvent>(_ => Ghost());
            search.RegisterCallback<FocusOutEvent>(_ => Ghost());
            Ghost();
            r.Add(search);
            var x = new Button { text = "X" };
            x.AddToClassList("zs-imgui-button"); x.AddToClassList("zs-searchclear");
            x.clicked += () => {
                Undo.RecordObject(ZoundsWindowProperties.Instance, "change search text");
                Props.ClearFilters();
                Props.dirty = true;
                EditorUtility.SetDirty(ZoundsWindowProperties.Instance);
                search.SetValueWithoutNotify(Props.searchText);
                search.Blur();
                Ghost();
                RefreshFiltered(); BuildList(); BuildTop();
            };
            r.Add(x);
            SyncTop += () => { if (search.focusController?.focusedElement != search && search.value != Props.searchText) { search.SetValueWithoutNotify(Props.searchText); Ghost(); } };
            return r;
        }

        static float MasterVolume {
            get { var ps = ZoundsProject.Instance.projectSettings; return Application.isPlaying ? ps.playerVolume : ps.editorVolume; }
        }

        static string VolumeLabel(float v) => string.Format("Vol {0,3}%", Mathf.RoundToInt(v * 100f));

        VisualElement VolumeRow() {
            volume = new ZuiSkinThumbSlider(VolumeLabel(MasterVolume), MasterVolume * 100f, 0f, 100f, BigSliderGeometry, pct => {
                float v = pct / 100f;
                Undo.RecordObject(ZoundsProject.Instance, "change master volume");
                var ps = ZoundsProject.Instance.projectSettings;
                if (Application.isPlaying) ps.playerVolume = v; else ps.editorVolume = v;
                EditorUtility.SetDirty(ZoundsProject.Instance);
                volume.text = VolumeLabel(v);
            });
            volume.AddToClassList("zs-slider-bigslider");
            volume.style.height = 26f;
            SyncTop += () => { volume.SetValueWithoutNotify(MasterVolume * 100f); volume.text = VolumeLabel(MasterVolume); };
            return volume;
        }

        /// <summary>The sheet's BigSlider, copied once: 9.5 × 20.3 thumb, 7.4 track, label column at least 61.9, no value box.</summary>
        static readonly ZuiSkinRangeSlider.Geometry BigSliderGeometry = new ZuiSkinRangeSlider.Geometry {
            thumbWidth = 9.5f, thumbHeight = 20.3f, trackHeight = 7.4f, labelWidth = 61.9f, showValueField = false, valueWidth = 0f,
        };

        // ── the quick-controls toolbar ──

        /// <summary>The old toolbar's wrap test: the sum of the items' widths (without the gaps) against the view width - 16.</summary>
        bool NeedsToolbarWrap() {
            var bs = ZoundsProject.Instance.browserSettings;
            float needed = 5f;
            if (bs.showAddZound) needed += ToolbarHeight;
            if (bs.showStopAll) needed += FlatWidth("Kill");
            if (bs.showMSClean) needed += FlatWidth("X") + FlatWidth("M") + FlatWidth("S");
            if (bs.showTypeKlip) needed += FlatWidth("Klip");
            if (bs.showTypeZeq) needed += FlatWidth("Zeq");
            if (bs.showTypeFiles) needed += FlatWidth("Files");
            if (bs.showTypeMissing) needed += FlatWidth("Missing");
            if (bs.showTagsFilter) needed += FlatWidth("Tags");
            if (bs.showGroupBy) needed += FlatWidth("Grouping");
            if (bs.showColumnMode) needed += 60f;
            return needed > WindowWidth - 16f;
        }

        /// <summary>A Flat button's width: its text (13 px, the sheet's Flat font) plus the style's 20 + 20 padding.</summary>
        float FlatWidth(string text) {
            if (s_measure == null) { s_measure = new Label(); s_measure.style.fontSize = 13f; }
            if (s_measure.panel == null) { s_measure.style.position = Position.Absolute; s_measure.style.visibility = Visibility.Hidden; Add(s_measure); }
            var sz = s_measure.MeasureTextSize(text, 0, MeasureMode.Undefined, 0, MeasureMode.Undefined);
            return Mathf.Ceil(sz.x) + FlatExtra;
        }
        static Label s_measure;

        VisualElement Toolbar() {
            var bs = ZoundsProject.Instance.browserSettings;
            var host = new VisualElement(); host.style.flexShrink = 0;
            VisualElement r = null;
            bool rowHasItem = false;
            void NewRow() { r = Row(); r.AddToClassList("zs-toolbar"); r.style.height = ToolbarHeight; r.Add(ZequenceEditorWindowTK.Gap(5f)); host.Add(r); rowHasItem = false; }
            void Gap() { if (rowHasItem) r.Add(ZequenceEditorWindowTK.Gap(ToolbarGap)); rowHasItem = true; }
            NewRow();

            if (bs.showAddZound) {
                Gap();
                Button add = null;
                add = ZequenceEditorWindowTK.IconButton("add-new", "Add new item.", "Flat", ZUICornerMask.All, ToolbarHeight, ToolbarHeight, () => {
                    AddZoundPopup.Show(add.worldBound);
                });
                add.Q<Image>().AddToClassList("zs-toolbaricon");
                add.AddToClassList("zs-iconbutton");
                r.Add(add);
            }
            if (bs.showStopAll) {
                Gap();
                r.Add(Flat("Kill", "", ZUICornerMask.All, () => ZoundEngine.StopAllZounds()));
            }
            if (bs.showMSClean) {
                Gap();
                r.Add(Flat("X", "", ZUICornerMask.Left, () => ZoundsWindow.ModifyZoundsProject("clean mute/solo", () => {
                    ZoundsProject.Instance.zoundLibrary.ForEachZound(z => { z.mute = false; z.solo = false; });
                    ZoundsProject.Instance.zoundLibrary.soloStatusNeedsUpdate = true;
                })));
                var m = Flat("M", "", ZUICornerMask.Square, () => ZoundsWindow.ModifyZoundsProject("mute all visible", () => {
                    foreach (var z in filtered) if (z is Klip || z is Zequence) z.mute = true;
                }));
                m.style.backgroundColor = OverFlat(new Color(0.8f, 0.2f, 0.15f, 0.7f));
                r.Add(m);
                var s = Flat("S", "", ZUICornerMask.Right, () => ZoundsWindow.ModifyZoundsProject("solo all visible", () => {
                    foreach (var z in filtered) if (z is Klip || z is Zequence) z.solo = true;
                    ZoundsProject.Instance.zoundLibrary.soloStatusNeedsUpdate = true;
                }));
                s.style.backgroundColor = OverFlat(new Color(0.15f, 0.65f, 0.25f, 0.7f));
                r.Add(s);
            }

            bool anyType = bs.showTypeKlip || bs.showTypeZeq || bs.showTypeFiles || bs.showTypeMissing;
            if (toolbarWrapped && (anyType || bs.showTagsFilter || bs.showGroupBy || bs.showColumnMode)) {
                host.Add(ZequenceEditorWindowTK.Space(ZS.RowSpace * 0.5f));
                NewRow();
            }

            bool typeDrawn = false;
            void Type(bool show, ZoundType type, string label) {
                if (!show) return;
                if (!typeDrawn) Gap(); else r.Add(ZequenceEditorWindowTK.Gap(ToolbarGap));
                bool on = Props.selectedTypes.HasFlag(type);
                var t = ZS.Toggle(label, "", on, v => {
                    Undo.RecordObject(ZoundsWindowProperties.Instance, "toggle type filter");
                    if (!v) Props.selectedTypes &= ~type; else Props.selectedTypes |= type;
                    EditorUtility.SetDirty(ZoundsWindowProperties.Instance);
                    Props.dirty = true;
                    RefreshFiltered(); BuildList();
                }, "Flat", ZUICornerMask.None, -1f, ToolbarHeight);
                t.AddToClassList("zs-toolbarbutton");
                r.Add(t);
                typeDrawn = true;
            }
            Type(bs.showTypeKlip, ZoundType.Klip, "Klip");
            Type(bs.showTypeZeq, ZoundType.Zequence, "Zeq");
            Type(bs.showTypeFiles, ZoundType.AudioClip, "Files");
            Type(bs.showTypeMissing, ZoundType.Missing, "Missing");

            if (bs.showTagsFilter) {
                Gap();
                Button tags = null;
                tags = Flat("Tags", "", ZUICornerMask.All, () => OpenTagsMenu(tags));
                if (Props.selectedTags.Count > 0) tags.AddToClassList("zs-Active");
                r.Add(tags);
            }
            if (bs.showGroupBy) {
                Gap();
                var group = Flat("Grouping", "", ZUICornerMask.All, () => {
                    var menu = new GenericMenu();
                    foreach (GroupBy g in Enum.GetValues(typeof(GroupBy))) {
                        string label = g == GroupBy.None ? "No Grouping" : g.ToString();
                        menu.AddItem(new GUIContent(label), Props.groupBy == g, sel => {
                            Undo.RecordObject(ZoundsWindowProperties.Instance, "change group by");
                            Props.groupBy = (GroupBy)sel;
                            EditorUtility.SetDirty(ZoundsWindowProperties.Instance);
                            Props.dirty = true;
                            RefreshFiltered(); BuildList(); BuildTop();
                        }, g);
                    }
                    menu.ShowAsContext();
                });
                if (Props.groupBy != GroupBy.None) group.AddToClassList("zs-Active");
                r.Add(group);
            }
            if (bs.showColumnMode) {
                Gap();
                bool grid = bs.multicolumn;
                r.Add(Segments(new[] { ("multicolumn", "Grid mode"), ("singlecolumn", "List mode") }, grid ? 0 : 1, 58f, i => {
                    ZoundsWindow.ModifyZoundsProject("toggle column view", () => { ZoundsProject.Instance.browserSettings.multicolumn = i == 0; });
                    RefreshFiltered(); BuildList(); BuildTop();
                }, true));
                if (!grid) {
                    var t = new Button { text = "T", tooltip = "Tags on own row(s). When on, all tags wrap below the row. When off, tags share row 1 in a fixed area (clipped if too long)." };
                    t.AddToClassList("zs-minibutton"); t.AddToClassList("zs-tagsrow");
                    t.EnableInClassList("zs-on", bs.tagsOnOwnRow);
                    t.clicked += () => {
                        bool v = !ZoundsProject.Instance.browserSettings.tagsOnOwnRow;
                        ZoundsWindow.ModifyZoundsProject("toggle tags-on-own-row", () => { ZoundsProject.Instance.browserSettings.tagsOnOwnRow = v; });
                        BuildList(); BuildTop();
                    };
                    r.Add(t);
                }
            }

            Gap();
            r.Add(Segments(new[] { ("All", "Show every Zound in the library."), ("Recent", "Show only Zounds triggered this editor session, newest first.") },
                           s_recent ? 1 : 0, 96.5f, i => { s_recent = i == 1; RefreshFiltered(); BuildList(); BuildTop(); }, false));
            if (s_recent) {
                Gap();
                r.Add(Flat("Clear recent", "Clear the recently-triggered Zounds list.", ZUICornerMask.All, () => { ZoundsRecentHistory.ClearRecent(); ListChanged(); }));
            }
            return host;
        }

        /// <summary>ZUI.OverrideButtonBgColor on a Flat button: the colour is laid over the style's border colour
        /// (measured: M = 153,59,64 = 0.7 × override + 0.3 × (32,79,124)).</summary>
        static Color OverFlat(Color c) {
            var under = new Color(32 / 255f, 79 / 255f, 124 / 255f, 1f);
            return new Color(Mathf.Lerp(under.r, c.r, c.a), Mathf.Lerp(under.g, c.g, c.a), Mathf.Lerp(under.b, c.b, c.a), 1f);
        }

        static Button Flat(string text, string tooltip, ZUICornerMask corners, Action onClick) {
            var b = ZS.Button(text, tooltip, "Flat", onClick, corners, -1f, ToolbarHeight);
            b.AddToClassList("zs-toolbarbutton");
            return b;
        }

        /// <summary>GUILayout.Toolbar: joined segments of equal width, the selected one pressed.</summary>
        static VisualElement Segments((string content, string tooltip)[] items, int selected, float width, Action<int> onSelect, bool icons) {
            var g = new VisualElement();
            g.AddToClassList("zs-segments");
            g.AddToClassList(icons ? "zs-segments--layout" : "zs-segments--mode");
            g.style.flexDirection = FlexDirection.Row; g.style.width = width; g.style.height = ToolbarHeight; g.style.flexShrink = 0;
            for (int i = 0; i < items.Length; i++) {
                int index = i;
                var b = new Button(() => { if (index != selected) onSelect(index); }) { tooltip = items[i].tooltip };
                b.AddToClassList("zs-segment");
                b.AddToClassList(i == 0 ? "zs-segment--left" : i == items.Length - 1 ? "zs-segment--right" : "zs-segment--mid");
                b.EnableInClassList("zs-on", i == selected);
                b.style.flexGrow = 1; b.style.flexBasis = 0;
                if (icons) {
                    var tex = ZUI.FindIcon(items[i].content) ?? Resources.Load<Texture>("ZoundsWindowIcons/" + items[i].content);
                    var img = new Image { image = tex, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                    img.AddToClassList("zs-buttonicon");
                    b.Add(img);
                }
                else b.text = items[i].content;
                g.Add(b);
            }
            return g;
        }

        void OpenTagsMenu(VisualElement from) {
            var selectedTags = Props.selectedTags;
            var menu = new GenericMenu();
            var addedKeyTags = new HashSet<string>();
            void Changed() { EditorUtility.SetDirty(ZoundsWindowProperties.Instance); Props.dirty = true; }
            foreach (var tag in ZoundsProject.Instance.zoundLibrary.tags) {
                string tagName = tag.name;
                bool on = selectedTags.Contains(tagName);
                var split = tagName.Split(':');
                if (split.Length > 1) {
                    string key = split[0];
                    if (addedKeyTags.Add(key)) {
                        bool on2 = selectedTags.Contains(key);
                        menu.AddItem(new GUIContent(key), on2, sel => {
                            Undo.RecordObject(ZoundsWindowProperties.Instance, "change selected tags");
                            if ((bool)sel) { if (!selectedTags.Contains(key)) selectedTags.Add(key); } else selectedTags.RemoveAll(t => t == key);
                            Changed();
                        }, on2);
                    }
                }
                menu.AddItem(new GUIContent(tagName), on, sel => {
                    Undo.RecordObject(ZoundsWindowProperties.Instance, "change selected tags");
                    if ((bool)sel) { if (!selectedTags.Contains(tagName)) selectedTags.Add(tagName); } else selectedTags.RemoveAll(t => t == tagName);
                    Changed();
                }, on);
            }
            TagMenuPopup.ShowTagMenu(menu, "Select Tags", from.worldBound.position, new List<string>(), s_tagsSearchText, s => s_tagsSearchText = s,
                                     null, 3, true, ZoundsEditorPresets.Instance.tagsPresets);
        }

        // ─────────────────────────── the list / grid ───────────────────────────

        void BuildList() {
            listHost.Clear();
            rows.Clear(); cells.Clear(); inspector = null;
            listSig = ListSignature();
            var bs = ZoundsProject.Instance.browserSettings;
            bool fill = !(s_recent && filtered.Count == 0);
            style.flexGrow = fill ? 1 : 0;
            listHost.style.flexGrow = fill ? 1 : 0;
            win.SetFill(fill);
            if (!fill) {
                listHost.Add(ZequenceEditorWindowTK.Space(5f));
                var help = new HelpBox("No Zounds triggered since the list was cleared.", HelpBoxMessageType.Info);
                help.AddToClassList("zs-infobox");
                listHost.Add(help);
                listHost.Add(ZequenceEditorWindowTK.Space(5f));
                return;
            }
            var frame = Row();
            frame.style.flexGrow = 1; frame.style.flexShrink = 1; frame.style.alignItems = Align.Stretch;
            frame.style.marginLeft = 5f; frame.style.marginRight = 5f; frame.style.marginBottom = 5f;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("zs-browserlist");
            scroll.style.flexGrow = 1; scroll.style.flexShrink = 1;
            frame.Add(scroll);
            listHost.Add(frame);
            if (bs.multicolumn) BuildGrid(scroll);
            else BuildRows(scroll);
        }

        void BuildRows(ScrollView scroll) {
            rowLayout = BrowserTab.PrepareListRowLayoutShared(filtered, ZS.ItemSpacing, ZS.MediumSpacing, MeasureZoundBtn);
            scroll.Add(ZequenceEditorWindowTK.Space(1f));
            void AddRow(int i) {
                var row = new ZoundListRowTK(filtered[i], this, () => rowLayout);
                rows.Add(row);
                scroll.Add(row);
                if (i < filtered.Count - 1) scroll.Add(ZequenceEditorWindowTK.Space(ZS.RowSpace * 0.5f));
            }
            if (groups != null && groups.Count > 0) {
                int i = 0;
                foreach (var g in groups) {
                    scroll.Add(GroupLabel(g.Key));
                    foreach (var _ in g.Value) { if (i >= filtered.Count) break; AddRow(i); i++; }
                }
            }
            else for (int i = 0; i < filtered.Count; i++) AddRow(i);
        }

        static Label GroupLabel(string text) {
            var l = new Label(text);
            l.AddToClassList("zs-lbl"); l.AddToClassList("zs-bold"); l.AddToClassList("zs-grouplabel");
            return l;
        }

        /// <summary>The ZoundBtn style's natural width for a name: its 16 px bold text plus 10 + 10 padding.</summary>
        float MeasureZoundBtn(string name) {
            if (s_btnMeasure == null) { s_btnMeasure = new Label(); s_btnMeasure.style.fontSize = 16f; s_btnMeasure.style.unityFontStyleAndWeight = FontStyle.Bold; }
            if (s_btnMeasure.panel == null) { s_btnMeasure.style.position = Position.Absolute; s_btnMeasure.style.visibility = Visibility.Hidden; Add(s_btnMeasure); }
            return Mathf.Ceil(s_btnMeasure.MeasureTextSize(name, 0, MeasureMode.Undefined, 0, MeasureMode.Undefined).x) + 20f;
        }
        static Label s_btnMeasure;

        // ── grid mode (BrowserTab.DrawZoundsMulticolumn + ZoundGridItemView) ──

        void BuildGrid(ScrollView scroll) {
            var bs = ZoundsProject.Instance.browserSettings;
            float contentW = WindowWidth;
            int selectedIndex = s_selected != null ? filtered.IndexOf(s_selected) : -1;
            if (bs.buttonSizeMode == ZoundsProject.BrowserSettings.ButtonSizeMode.Fixed) {
                float itemWidth = bs.itemWidth;
                if (itemWidth > contentW - 8f) itemWidth = contentW - 8f;
                int columns = Mathf.Max(1, Mathf.FloorToInt(contentW / itemWidth));
                int index = 0;
                void FixedRow(bool first) {
                    if (!first) scroll.Add(ZequenceEditorWindowTK.Space(BrowserTab.MULTICOLUMN_V_GAP));
                    var r = Row();
                    r.style.justifyContent = Justify.Center;
                    int start = index;
                    for (int c = 0; c < columns; c++) {
                        if (c > 0) r.Add(ZequenceEditorWindowTK.Gap(BrowserTab.MULTICOLUMN_H_GAP));
                        if (index >= filtered.Count) { var ph = new VisualElement(); ph.style.width = itemWidth; ph.style.height = 24f; ph.style.flexShrink = 0; r.Add(ph); }
                        else { var cell = new GridCellTK(filtered[index], this, itemWidth, index == selectedIndex); cells.Add(cell); r.Add(cell); }
                        index++;
                    }
                    scroll.Add(r);
                    if (selectedIndex >= start && selectedIndex < start + columns && selectedIndex < filtered.Count) AddInspector(scroll, filtered[selectedIndex]);
                }
                if (groups != null && groups.Count > 0) {
                    foreach (var g in groups) {
                        scroll.Add(GroupLabel(g.Key));
                        int members = g.Value.Count;
                        bool first = true;
                        while (members > 0) { FixedRow(first); first = false; members -= columns; }
                    }
                }
                else {
                    int rowCount = Mathf.CeilToInt(filtered.Count / (float)columns);
                    for (int i = 0; i < rowCount; i++) FixedRow(i == 0);
                }
            }
            else {
                int total = 0;
                if (groups != null && groups.Count > 0)
                    foreach (var g in groups) { scroll.Add(GroupLabel(g.Key)); FlowRows(scroll, g.Value, ref total, selectedIndex, contentW); }
                else FlowRows(scroll, filtered, ref total, selectedIndex, contentW);
            }
        }

        void FlowRows(ScrollView scroll, List<Zound> zounds, ref int total, int selectedIndex, float maxWidth) {
            var bs = ZoundsProject.Instance.browserSettings;
            VisualElement r = null;
            float x = 0f;
            Zound pending = null;
            for (int i = 0; i < zounds.Count; i++) {
                float w = MeasureZoundBtn(zounds[i].name);
                if (bs.buttonSizeMode == ZoundsProject.BrowserSettings.ButtonSizeMode.Min) w = Mathf.Max(w, bs.itemWidth);
                if (r == null) { r = Row(); scroll.Add(r); x = 0f; }
                if (x + w > maxWidth - 40f && x > 0f) {
                    if (pending != null) { AddInspector(scroll, pending); pending = null; }
                    scroll.Add(ZequenceEditorWindowTK.Space(BrowserTab.MULTICOLUMN_V_GAP));
                    r = Row(); scroll.Add(r); x = 0f;
                }
                if (x > 0f) r.Add(ZequenceEditorWindowTK.Gap(BrowserTab.MULTICOLUMN_H_GAP));
                bool sel = total == selectedIndex;
                var cell = new GridCellTK(zounds[i], this, w, sel); cells.Add(cell); r.Add(cell);
                if (sel) pending = zounds[i];
                x += w + BrowserTab.MULTICOLUMN_H_GAP;
                total++;
            }
            if (pending != null) AddInspector(scroll, pending);
        }

        void AddInspector(ScrollView scroll, Zound zound) {
            inspector = new GridInspectorTK(zound, this);
            scroll.Add(inspector);
        }

        internal void ToggleSelect(Zound zound) {
            s_selected = s_selected == zound ? null : zound;
            BuildList();
        }

        // ─────────────────────────── a grid cell (ZoundGridItemView.DrawButton) ───────────────────────────

        internal class GridCellTK : VisualElement {
            readonly Zound zound;
            readonly BrowserTabTK tab;
            readonly bool selected, isClip, isMissing, klipIssue;
            readonly VisualElement pulseFill, pulseBorder, highlight, wash, stripe, zeqStripe;
            readonly Button button;
            Color tint = new Color(-1, 0, 0);

            public GridCellTK(Zound zound, BrowserTabTK tab, float width, bool selected) {
                this.zound = zound; this.tab = tab; this.selected = selected;
                isClip = zound.IsClipOrLocalZound();
                isMissing = !isClip && zound.id == 0;
                klipIssue = zound is Klip k && string.IsNullOrEmpty(k.externalSourcePath)
                            && (k.audioClipRef == null || !k.audioClipRef.RuntimeKeyIsValid() || k.audioClipRef.editorAsset == null);
                style.width = width; style.height = 24f; style.flexShrink = 0;
                pulseFill = Part(); pulseBorder = Part(); highlight = Part(); wash = Part();
                pulseBorder.style.borderTopWidth = pulseBorder.style.borderBottomWidth = pulseBorder.style.borderLeftWidth = pulseBorder.style.borderRightWidth = 3f;
                highlight.style.left = -1f; highlight.style.top = -1f; highlight.style.right = -1.5f; highlight.style.bottom = -1f;
                Add(pulseFill); Add(pulseBorder); Add(highlight); Add(wash);
                if (isMissing) {
                    var l = new Label(zound.name);
                    l.AddToClassList("zs-lbl");
                    l.style.unityTextAlign = TextAnchor.MiddleCenter;
                    l.style.color = new Color(0.8f, 0.4f, 0.4f, 1f);
                    l.style.position = Position.Absolute; l.style.left = 0; l.style.top = 0; l.style.right = 0; l.style.bottom = 0;
                    l.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0 || e.button == 1) tab.ToggleSelect(zound); });
                    Add(l);
                }
                else {
                    button = ZS.Button(zound.name, zound.name + ": Left click to play. Right click to open configuration panel. Middle click or Alt left click to copy the name to clipboard.",
                                       "ZoundBtn", null, ZUICornerMask.None, -1f, -1f);
                    button.clickable = null;
                    button.style.position = Position.Absolute; button.style.left = 0; button.style.top = 0; button.style.right = 0; button.style.bottom = 0;
                    button.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) e.StopPropagation(); });
                    button.RegisterCallback<PointerUpEvent>(OnClick);
                    Add(button);
                }
                stripe = Part(); zeqStripe = Part();
                stripe.style.left = 1f; stripe.style.right = 1f; stripe.style.top = 0; stripe.style.bottom = StyleKeyword.Auto; stripe.style.height = 2f;
                zeqStripe.style.left = 1f; zeqStripe.style.right = 1f; zeqStripe.style.top = StyleKeyword.Auto; zeqStripe.style.bottom = 0; zeqStripe.style.height = 1.5f;
                Add(stripe); Add(zeqStripe);
                Sync(); Live();
            }

            static VisualElement Part() {
                var e = new VisualElement { pickingMode = PickingMode.Ignore };
                e.style.position = Position.Absolute; e.style.left = 0; e.style.top = 0; e.style.right = 0; e.style.bottom = 0;
                return e;
            }

            void OnClick(PointerUpEvent e) {
                if (!button.ContainsPoint(e.localPosition)) return;
                if (e.button == 0) {
                    if (e.altKey) ZoundBrowserPlaybackVisuals.CopyToClipboard(zound.name);
                    else if (e.ctrlKey) InfoViewWindow.OpenWindow(zound);
                    else ZoundBrowserPlaybackVisuals.PlayOrStopFromBrowser(zound);
                }
                else if (e.button == 1) tab.ToggleSelect(zound);
                else if (e.button == 2) ZoundBrowserPlaybackVisuals.CopyToClipboard(zound.name);
            }

            public void Sync() {
                if (button != null && button.text != zound.name) button.text = zound.name;
                bool ms = zound.mute || zound.solo;
                Color c = zound.mute ? ZoundListRowTK.MuteWash : ZoundListRowTK.SoloWash;
                wash.style.display = ms ? DisplayStyle.Flex : DisplayStyle.None;
                wash.style.backgroundColor = c;
                stripe.style.visibility = ms ? Visibility.Visible : Visibility.Hidden;
                stripe.style.backgroundColor = c;
                bool zeqMark = zound is Zequence zeq && zeq.HasLocalMuteOrSoloEntry();
                zeqStripe.style.visibility = zeqMark ? Visibility.Visible : Visibility.Hidden;
                zeqStripe.style.backgroundColor = Color.yellow;
            }

            public void Live() {
                Color t = Color.white, hl = Color.clear, fill = Color.clear, border = Color.clear;
                if (!isMissing) {
                    bool playing = ZoundBrowserPlaybackVisuals.TryGetAnyInstanceToken(zound, out var token);
                    if (klipIssue) t = new Color(1f, 0.4f, 0f, 1f);
                    else {
                        ZoundBrowserPlaybackVisuals.UpdateZoundButtonPulse(zound, isClip, playing, token);
                        if (playing) {
                            if (token.state == ZoundToken.State.Paused) t = new Color(0.9f, 0.5f, 0.9f, 1f);
                            else if (token.audioSource.volume < Mathf.Epsilon) t = new Color(0.9f, 0.5f, 0.1f, 1f);
                            else if (selected) t = isClip ? ZoundsEditorColors.clipFlashColorStartSelected : ZoundsEditorColors.flashColorStartSelected;
                            else if (isClip) t = ZoundsEditorColors.clipFlashColorStart;
                        }
                        else if (selected) t = isClip ? Color.cyan : ZoundsEditorColors.flashColorStartSelected;
                        else if (isClip) t = Color.cyan;
                    }
                    if (token != null) hl = token.isChildZound ? (token.isDelayFinished ? Color.clear : Color.yellow * t) : t;
                    float intensity = ZUI.GetPulseIntensity(ZoundBrowserPlaybackVisuals.ZoundPulseKey(zound));
                    if (intensity > 0f && token != null) {
                        var bc = isClip ? ZoundsEditorColors.clipFlashColorEnd : token.audioSource.mute ? ZoundsEditorColors.flashColorEndMuted : ZoundsEditorColors.flashColorEnd;
                        fill = new Color(bc.r, bc.g, bc.b, 0.25f * intensity);
                        border = new Color(bc.r, bc.g, bc.b, intensity);
                        t = Color.Lerp(t, Color.black, intensity * 0.6f);
                    }
                }
                pulseFill.style.backgroundColor = fill;
                pulseBorder.style.borderTopColor = pulseBorder.style.borderBottomColor = pulseBorder.style.borderLeftColor = pulseBorder.style.borderRightColor = border;
                highlight.style.backgroundColor = hl;
                if (button != null && t != tint) { tint = t; ZoundListRowTK.TintButton(button, t, ZoundListRowTK.ZoundBtnText); }
            }
        }

        // ─────────────────────────── the grid inspector (ZoundBrowserEditor.DrawMulticolumn) ───────────────────────────

        internal class GridInspectorTK : VisualElement, IZoundRowHost {
            readonly Zound zound;
            readonly BrowserTabTK tab;
            readonly VisualElement area;
            readonly ZoundListRowTKParts parts;

            public GridInspectorTK(Zound zound, BrowserTabTK tab) {
                this.zound = zound; this.tab = tab;
                AddToClassList("zs-helpbox");
                style.height = BrowserTab.inspectorHeight; style.flexShrink = 0;
                area = new VisualElement();
                area.style.position = Position.Absolute;
                Add(area);
                parts = new ZoundListRowTKParts(zound, this, area, vertical: true);
                RegisterCallback<GeometryChangedEvent>(_ => Layout());
            }

            public void OpenZoundEditor(Zound z) => tab.OpenZoundEditor(z);
            public void ListChanged(Zound select = null) => tab.ListChanged(select);
            public void Sync() => parts.Sync();

            void Layout() {
                var pad = resolvedStyle;
                float w = pad.width - pad.paddingLeft - pad.paddingRight - pad.borderLeftWidth - pad.borderRightWidth;
                if (float.IsNaN(w) || w <= 0f) return;
                area.style.left = pad.paddingLeft; area.style.top = pad.paddingTop;
                area.style.width = w;
                var bs = ZoundsProject.Instance.browserSettings;
                int fieldCount = (bs.showNameField ? 1 : 0) + (bs.showVolume ? 1 : 0) + (bs.showPitch ? 1 : 0) + (bs.showChance ? 1 : 0);
                float fieldMul, tagsMul;
                if (bs.showTags) {
                    if (fieldCount > 2) { fieldMul = 0.4f; tagsMul = 0.2f; }
                    else if (fieldCount > 0) { fieldMul = 0.5f; tagsMul = 0.5f; }
                    else { fieldMul = 0f; tagsMul = 1f; }
                }
                else { fieldMul = fieldCount > 2 ? 0.5f : fieldCount > 0 ? 1f : 0f; tagsMul = 0f; }
                const float buttonWidth = 30f;
                float leftW = (bs.showOpenEditor ? buttonWidth : 0f) + ((bs.showMute || bs.showSolo) ? 24f : 0f);
                float removeW = (bs.showRouting ? buttonWidth : 0f) + (bs.showConvertToZequence ? buttonWidth : 0f) + (bs.showDuplicate ? buttonWidth : 0f) + (bs.showRemove ? buttonWidth : 0f);
                float leftGap = leftW > 0 ? BrowserTab.LEFT_BUTTONS_TO_NAME_GAP : 0f;
                float rightGap = removeW > 0 ? ZS.ItemSpacing : 0f;
                // The tags decide the panel's height (UpdateInspectorHeight): at least the base height, else the text's.
                float height = BrowserTab.inspectorHeight;
                float x = 0f;
                var edit = new Rect(x, 0f, bs.showOpenEditor ? buttonWidth : 0f, 0f); x += edit.width;
                var ms = new Rect(x, 0f, (bs.showMute || bs.showSolo) ? 24f : 0f, 0f); x += ms.width + leftGap;
                float remaining = w - x - removeW - rightGap;
                var fields = new Rect(x, 0f, remaining * (1f - tagsMul), 0f);
                var tagsR = new Rect(fields.xMax, 0f, remaining * tagsMul, 0f);
                var remove = new Rect(tagsR.xMax + rightGap, 0f, removeW, 0f);
                float tagsH = parts.TagsHeight(tagsR.width);
                height = Mathf.Max(height, tagsH);
                if (!Mathf.Approximately(style.height.value.value, height)) style.height = height;
                float baseH = Mathf.Min(height, BrowserTab.inspectorHeight);
                edit.height = ms.height = fields.height = remove.height = baseH;
                tagsR.height = height;
                area.style.height = height;
                parts.LayoutGrid(edit, ms, fields, tagsR, remove);
            }
        }
    }
}
