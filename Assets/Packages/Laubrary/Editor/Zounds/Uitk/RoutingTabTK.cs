using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The UI Toolkit twin of the Zounds window's Routing tab (T-0470): the two display toggles, the reorderable list of
    /// rules (each: its conditions as removable chips, the mixer group it routes to, "+ Condition", and optionally the
    /// zounds playing through it and the zounds manually routed to it), then the mixer groups no rule covers.
    ///
    /// The list is a UI Toolkit ListView bound to the same serialized array the old ReorderableList edits, so add,
    /// remove and drag-reorder are undoable the same way. The rule's mixer group is Addressables' own AssetReference
    /// drawer, which exists only as an IMGUI drawer: UI Toolkit's PropertyField hosts it (the one place this tab relies on
    /// IMGUI, because there is no UI Toolkit version of that drawer to use).
    /// </summary>
    internal class RoutingTabTK : VisualElement {

        const float RightInset = 2f;
        const float BaseRuleSectionHeight = 81f, ActiveZoundsSectionHeight = 60f, ManualRoutingSectionHeight = 60f;
        static readonly Color EvenRow = new Color32(65, 65, 65, 255), OddRow = new Color32(75, 75, 75, 255);
        static readonly Color EvenActive = new Color32(70, 96, 124, 255), OddActive = new Color32(80, 106, 134, 255);

        readonly ZoundsWindowTK win;
        SerializedObject so;
        ListView list;
        VisualElement unruledHost;
        string sig;
        static string s_ruleSearchText;
        readonly List<System.Action> liveRefreshers = new List<System.Action>();

        public RoutingTabTK(ZoundsWindowTK win) {
            this.win = win;
            style.flexGrow = 1; style.flexShrink = 1;
            Build();
        }

        void Dirty() => ZoundsWindow.SetZoundsProjectDirty();

        string Signature() {
            var props = ZoundsWindowProperties.Instance;
            var sb = new System.Text.StringBuilder();
            sb.Append(props.showActiveZounds).Append(props.showManuallySetRoutings).Append('|');
            foreach (var r in ZoundsProject.Instance.zoundRoutings.rules) {
                sb.Append(r.mixerGroupRef != null ? r.mixerGroupRef.SubObjectName + "@" + r.mixerGroupRef.AssetGUID : "-").Append(':');
                foreach (var c in r.conditions) sb.Append((int)c.type).Append(c.name).Append(',');
                sb.Append(';');
            }
            ZoundsProject.Instance.zoundLibrary.ForEachZound(z => { if (z.editor_hasManuallySetRouting) sb.Append(z.name).Append('>').Append(z.manuallySetMixerGroupRef.SubObjectName).Append(','); });
            return sb.ToString();
        }

        void Build() {
            Clear();
            liveRefreshers.Clear();
            sig = Signature();
            so = new SerializedObject(ZoundsProject.Instance);
            var props = ZoundsWindowProperties.Instance;

            var box = new VisualElement();
            box.AddToClassList("zs-box-default");
            box.style.flexGrow = 1; box.style.flexShrink = 1;
            Add(box);

            var top = new VisualElement();
            top.style.flexDirection = FlexDirection.Row; top.style.flexShrink = 0;
            top.Add(ZequenceEditorWindowTK.Flex());
            top.Add(ToggleLeft("Show Active Zounds", props.showActiveZounds, 150f, v => {
                Undo.RecordObject(props, "toggle show active zounds.");
                props.showActiveZounds = v;
                EditorUtility.SetDirty(props);
                Build();
            }));
            top.Add(ToggleLeft("Show Manually Set Routings", props.showManuallySetRoutings, 190f, v => {
                Undo.RecordObject(props, "toggle show manually set routings.");
                props.showManuallySetRoutings = v;
                EditorUtility.SetDirty(props);
                Build();
            }));
            box.Add(top);
            box.Add(ZequenceEditorWindowTK.Space(5f));

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1; scroll.style.flexShrink = 1;
            box.Add(scroll);

            var rulesProp = so.FindProperty("zoundRoutings.rules");
            list = new ListView {
                reorderable = true, reorderMode = ListViewReorderMode.Animated, showAddRemoveFooter = true, showBorder = false,
                showBoundCollectionSize = false, virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                selectionType = SelectionType.Single,
            };
            list.AddToClassList("zs-rules");
            list.makeHeader = () => { var l = new Label("Rules"); l.AddToClassList("zs-lbl"); l.AddToClassList("zs-rules__header"); return l; };
            list.makeNoneElement = () => { var l = new Label("Click + to add a set of rules."); l.AddToClassList("zs-lbl"); l.AddToClassList("zs-rules__none"); return l; };
            list.makeItem = () => new VisualElement();
            list.bindItem = (e, i) => BindRule(e, i);
            list.onAdd = _ => {
                so.Update();
                var arr = so.FindProperty("zoundRoutings.rules");
                int last = arr.arraySize;
                arr.arraySize++;
                var el = arr.GetArrayElementAtIndex(last);
                el.FindPropertyRelative("conditions").arraySize = 0;
                el.FindPropertyRelative("mixerGroupRef").boxedValue = new UnityEngine.AddressableAssets.AssetReference();
                if (so.ApplyModifiedProperties()) Dirty();
                Build();
            };
            list.onRemove = _ => {
                int idx = list.selectedIndex;
                if (idx < 0) return;
                so.Update();
                so.FindProperty("zoundRoutings.rules").DeleteArrayElementAtIndex(idx);
                if (so.ApplyModifiedProperties()) Dirty();
                Build();
            };
            list.itemIndexChanged += (_, __) => { Dirty(); Build(); };
            list.selectionChanged += _ => list.RefreshItems();
            list.BindProperty(rulesProp);
            scroll.Add(list);

            unruledHost = new VisualElement();
            scroll.Add(unruledHost);
            BuildUnruled();
        }

        static Toggle ToggleLeft(string text, bool value, float width, System.Action<bool> changed) {
            var t = new Toggle { text = text, value = value };
            t.AddToClassList("zs-toggleleft");
            t.style.width = width;
            t.RegisterValueChangedCallback(e => changed(e.newValue));
            return t;
        }

        // ─────────────────────────── a rule ───────────────────────────

        void BindRule(VisualElement e, int index) {
            e.Clear();
            var rules = ZoundsProject.Instance.zoundRoutings.rules;
            if (index < 0 || index >= rules.Count) return;
            var rule = rules[index];
            var props = ZoundsWindowProperties.Instance;
            bool active = list.selectedIndex == index;
            bool even = index % 2 == 0;
            e.style.backgroundColor = active ? (even ? EvenActive : OddActive) : (even ? EvenRow : OddRow);
            bool hasManual = rule.mixerGroupRef != null && props.showManuallySetRoutings
                             && RoutingTab.HasManualRoutedZounds(rule.mixerGroupRef.editorAsset, rule.mixerGroupRef.SubObjectName);
            float h = BaseRuleSectionHeight + (props.showActiveZounds ? ActiveZoundsSectionHeight : 0f) + (hasManual ? ManualRoutingSectionHeight : 0f);
            e.style.height = h;

            // left half: the conditions; right half: the mixer group and "+ Condition"
            var content = new VisualElement();
            // The ReorderableList's element rect starts after its drag handle and stops short of the right edge
            // (measured: 20 pt in, 2 pt short); the old code then insets its content by 5.
            content.style.position = Position.Absolute; content.style.left = 5f; content.style.top = 5f; content.style.right = 5f + RightInset;
            content.style.height = BaseRuleSectionHeight - 10f;
            content.style.flexDirection = FlexDirection.Row;
            e.Add(content);

            var left = Conditions(index, rule);
            left.style.width = Length.Percent(50);
            content.Add(left);

            var right = new VisualElement();
            right.style.flexGrow = 1; right.style.marginLeft = 4f;
            var ruleProp = so.FindProperty("zoundRoutings.rules").GetArrayElementAtIndex(index);
            // Addressables draws an AssetReference only through its IMGUI property drawer; UI Toolkit has no version of it, so
            // this one field is hosted in an IMGUIContainer, drawn exactly as the old tab draws it (label width 74).
            string path = ruleProp.FindPropertyRelative("mixerGroupRef").propertyPath;
            var mixer = new IMGUIContainer(() => {
                so.Update();
                var prop = so.FindProperty(path);
                if (prop == null) return;
                float lw = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 74f;
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(prop);
                bool changed = EditorGUI.EndChangeCheck();
                EditorGUIUtility.labelWidth = lw;
                if (so.ApplyModifiedProperties() || changed) { Dirty(); schedule.Execute(() => { if (Signature() != sig) Build(); }); }
            });
            mixer.AddToClassList("zs-rules__mixer");
            mixer.style.height = EditorGUIUtility.singleLineHeight;
            right.Add(mixer);
            right.Add(ZequenceEditorWindowTK.Space(5f));
            Button add = null;
            add = new Button(() => GenericMenuPopup.Show(RoutingTab.ConditionMenu(index), "Select Condition", add.worldBound.position,
                                                        new List<string>(), s_ruleSearchText, t => s_ruleSearchText = t, null, 1)) { text = "+ Condition" };
            add.AddToClassList("zs-imgui-button"); add.AddToClassList("zs-rules__add");
            right.Add(add);
            content.Add(right);

            float y = 5f + BaseRuleSectionHeight;
            if (props.showActiveZounds) {
                e.Add(Section("Active Zounds:", 85f, y, ActiveZoundsSectionHeight - 10f, () => RoutingTab.ActiveZoundsFor(rule.mixerGroupRef?.editorAsset, rule.mixerGroupRef?.SubObjectName), true));
                y += 5f + ActiveZoundsSectionHeight - 10f;
            }
            if (hasManual)
                e.Add(Section("Manually Routed Zounds:", 145f, y, ManualRoutingSectionHeight - 10f, () => RoutingTab.ManuallyRoutedZoundsFor(rule.mixerGroupRef.editorAsset, rule.mixerGroupRef.SubObjectName), false));
        }

        /// <summary>DrawRules: each condition as a chip (its label, then a square X), wrapping in the half's width;
        /// "Empty" when there are none.</summary>
        VisualElement Conditions(int index, ZoundRoutings.Rule rule) {
            var scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            scroll.AddToClassList("zs-rules__conditions");
            scroll.style.height = BaseRuleSectionHeight - 10f;
            var flow = scroll.contentContainer;
            flow.style.flexDirection = FlexDirection.Row; flow.style.flexWrap = Wrap.Wrap; flow.style.paddingLeft = 2f;
            if (rule.conditions.Count == 0) {
                var empty = new Label("Empty");
                empty.AddToClassList("zs-greymini");
                empty.style.width = Length.Percent(100); empty.style.height = BaseRuleSectionHeight - 10f;
                flow.Add(empty);
                return scroll;
            }
            var lib = ZoundsProject.Instance.zoundLibrary;
            for (int i = 0; i < rule.conditions.Count; i++) {
                int ci = i;
                var c = rule.conditions[i];
                string label = RoutingTab.ConditionLabel(c.type, c.name, lib, out bool isError);
                var chip = new VisualElement();
                chip.AddToClassList("zs-chip");
                var l = new Label(label);
                l.AddToClassList("zs-lbl");
                if (isError) l.style.color = new Color(196 / 255f * 1f, 196 / 255f * 0.7f, 196 / 255f * 0.7f, 1f);
                chip.Add(l);
                var x = new Button(() => {
                    so.Update();
                    so.FindProperty("zoundRoutings.rules").GetArrayElementAtIndex(index).FindPropertyRelative("conditions").DeleteArrayElementAtIndex(ci);
                    if (so.ApplyModifiedProperties()) Dirty();
                    Build();
                }) { text = "X" };
                x.AddToClassList("zs-imgui-button"); x.AddToClassList("zs-chip__x");
                chip.Add(x);
                flow.Add(chip);
            }
            return scroll;
        }

        /// <summary>A labelled box of name chips (Active Zounds in green, Manually Routed Zounds plain).</summary>
        VisualElement Section(string label, float labelWidth, float y, float height, System.Func<List<Zound>> zounds, bool green, bool inRule = true) {
            var s = new VisualElement();
            s.style.position = Position.Absolute; s.style.left = 5f; s.style.right = 5f + (inRule ? RightInset : 0f); s.style.top = y; s.style.height = height;
            s.style.flexDirection = FlexDirection.Row;
            var l = new Label(label);
            l.AddToClassList("zs-lbl"); l.AddToClassList("zs-cliplabel");
            l.style.width = labelWidth; l.style.height = EditorGUIUtility.singleLineHeight; l.style.flexShrink = 0;
            s.Add(l);
            var box = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            box.AddToClassList("zs-guibox");
            box.style.flexGrow = 1; box.style.marginLeft = 5f;
            var flow = box.contentContainer;
            flow.style.flexDirection = FlexDirection.Row; flow.style.flexWrap = Wrap.Wrap; flow.style.paddingLeft = 2f;
            s.Add(box);
            string last = null;
            void Refresh() {
                var zs = zounds();
                var key = string.Join("|", zs.ConvertAll(z => z.name));
                if (key == last) return;
                last = key;
                flow.Clear();
                foreach (var z in zs) {
                    var chip = new Label(z.name);
                    chip.AddToClassList("zs-lbl"); chip.AddToClassList("zs-namechip");
                    if (green) chip.AddToClassList("zs-namechip--active");
                    flow.Add(chip);
                }
            }
            Refresh();
            if (green) liveRefreshers.Add(Refresh);
            return s;
        }

        // ─────────────────────────── unruled mixer groups ───────────────────────────

        void BuildUnruled() {
            unruledHost.Clear();
            var props = ZoundsWindowProperties.Instance;
            if (!props.showActiveZounds && !props.showManuallySetRoutings) return;
            var all = new List<AudioMixerGroup>();
            RoutingTab.GetAllAddresableMixerGroups(ref all);
            var unruled = new List<AudioMixerGroup>();
            RoutingTab.CollectUnruledMixerGroups(all, unruled);
            if (unruled.Count == 0) return;
            var title = new Label("Unruled Manual Routings");
            title.AddToClassList("zs-lbl"); title.AddToClassList("zs-bold"); title.style.height = 18f; title.style.marginTop = 2f; title.style.marginBottom = 2f;
            unruledHost.Add(title);
            unruledHost.Add(ZequenceEditorWindowTK.Space(6f));
            for (int i = 0; i < unruled.Count; i++) {
                var mg = unruled[i];
                bool hasManual = RoutingTab.HasManualRoutedZounds(mg.audioMixer, mg.name);
                float h = EditorGUIUtility.singleLineHeight + 10f + (props.showActiveZounds ? ActiveZoundsSectionHeight + 5f : 0f) + (hasManual ? ManualRoutingSectionHeight + 5f : 0f);
                var e = new VisualElement();
                e.style.height = h; e.style.flexShrink = 0;
                e.style.backgroundColor = i % 2 == 0 ? EvenRow : OddRow;
                var field = new ObjectField { objectType = typeof(AudioMixerGroup), allowSceneObjects = false, value = mg };
                field.AddToClassList("zs-imgui-field");
                field.SetEnabled(false);
                field.style.position = Position.Absolute; field.style.left = 5f; field.style.right = 5f; field.style.top = 5f;
                e.Add(field);
                float y = 5f + EditorGUIUtility.singleLineHeight + 5f;
                if (props.showActiveZounds) {
                    e.Add(Section("Active Zounds:", 85f, y, ActiveZoundsSectionHeight, () => RoutingTab.ActiveZoundsFor(mg.audioMixer, mg.name), true, false));
                    y += ActiveZoundsSectionHeight + 5f;
                }
                if (hasManual) e.Add(Section("Manually Routed Zounds:", 145f, y, ManualRoutingSectionHeight, () => RoutingTab.ManuallyRoutedZoundsFor(mg.audioMixer, mg.name), false, false));
                unruledHost.Add(e);
                unruledHost.Add(ZequenceEditorWindowTK.Space(5f));
            }
        }

        // ─────────────────────────── refresh ───────────────────────────

        public void Tick() {
            if (Signature() != sig) Build();
        }

        public void Live() {
            foreach (var r in liveRefreshers) r();
        }
    }
}
