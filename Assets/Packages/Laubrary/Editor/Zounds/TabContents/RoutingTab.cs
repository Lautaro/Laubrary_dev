using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Audio;

#if ADDRESSABLES_INSTALLED
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
#endif

namespace Laubrary.Zounds {

    public class RoutingTab : TabContent {

        public override string name { get; set; } = "Routing";

        internal static bool reorderableListNeedsUpdate;

        private const float BaseRuleSectionHeight = 81f;
        private const float ActiveZoundsSectionHeight = 60f;
        private const float ManualRoutingSectionHeight = 60f;

        private Vector2 scrollPos;
        private ReorderableList ruleList;
        private List<Vector2> ruleSetScrollPoses = new List<Vector2>();
        private List<Vector2> activeZoundsScrollPoses = new List<Vector2>();
        private List<Vector2> manualRoutingScrollPoses = new List<Vector2>();
        private List<bool> hasManualRoutedZounds = new List<bool>();
        private string ruleSearchText;

        private GUIContent tempContent = new GUIContent();

        private List<AudioMixerGroup> allMixerGroups = new List<AudioMixerGroup>();
        private List<AudioMixerGroup> unruledMixerGroups = new List<AudioMixerGroup>();

        public override void OnTabOpened() {
            
        }

        public override void OnGUI(SerializedObject serializedObject, Rect contentRect) {
            using (ZUI.Box())
            {

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            var zoundsWindowProperties = ZoundsWindowProperties.Instance;
            bool showActiveZounds = zoundsWindowProperties.showActiveZounds;
            bool showManuallySetRoutings = zoundsWindowProperties.showManuallySetRoutings;
            bool tempShowActiveZounds = EditorGUILayout.ToggleLeft("Show Active Zounds", showActiveZounds, GUILayout.Width(150f));
            if (tempShowActiveZounds != showActiveZounds) {
                Undo.RecordObject(zoundsWindowProperties, "toggle show active zounds.");
                zoundsWindowProperties.showActiveZounds = tempShowActiveZounds;
                reorderableListNeedsUpdate = true;
                EditorUtility.SetDirty(zoundsWindowProperties);
            }
            bool tempshowManuallySetRoutings = EditorGUILayout.ToggleLeft("Show Manually Set Routings", showManuallySetRoutings, GUILayout.Width(190f));
            if (tempshowManuallySetRoutings != showManuallySetRoutings) {
                Undo.RecordObject(zoundsWindowProperties, "toggle show manually set routings.");
                zoundsWindowProperties.showManuallySetRoutings = tempshowManuallySetRoutings;
                reorderableListNeedsUpdate = true;
                EditorUtility.SetDirty(zoundsWindowProperties);
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(5f);


            if (reorderableListNeedsUpdate || ruleList == null || ruleList.serializedProperty.serializedObject != serializedObject) {
                reorderableListNeedsUpdate = false;
                var ruleSetsProp = serializedObject.FindProperty("zoundRoutings.rules");
                ruleList = new ReorderableList(serializedObject, ruleSetsProp, true, true, true, true);
                ruleList.drawHeaderCallback = OnDrawRulesHeader;
                ruleList.elementHeightCallback = CalculateRuleElementHeight;
                ruleList.drawElementCallback = OnDrawRulesElement;
                ruleList.drawElementBackgroundCallback = OnDrawRulesBackground;
                ruleList.drawNoneElementCallback = OnDrawRulesNoneElement;
                ruleList.onAddCallback = OnAddRule;
                //ruleList.onDeleteArrayElementCallback = OnDeleteRule;
                ruleList.onRemoveCallback = OnRemoveRule;
            }

            allMixerGroups.Clear();
            GetAllAddresableMixerGroups(ref allMixerGroups);
            unruledMixerGroups.Clear();
            foreach (var targetMG in allMixerGroups) {
                bool ruled = false;
                foreach (var rule in ZoundsProject.Instance.zoundRoutings.rules) {
                    if (rule.mixerGroupRef == null) continue;
                    if (rule.mixerGroupRef.editorAsset != targetMG.audioMixer) continue;
                    if (rule.mixerGroupRef.SubObjectName != targetMG.name) continue;
                    ruled = true;
                }
                if (!ruled) {
                    unruledMixerGroups.Add(targetMG);
                }
            }

            scrollPos = GUILayout.BeginScrollView(scrollPos);

            ruleList.DoLayoutList();

            if (tempShowActiveZounds || tempshowManuallySetRoutings) {
                if (unruledMixerGroups.Count > 0) {
                    EditorGUILayout.LabelField("Unruled Manual Routings", EditorStyles.boldLabel);
                    GUILayout.Space(6f);
                    for (int i = 0; i < unruledMixerGroups.Count; i++) {
                        var mixerGroup = unruledMixerGroups[i];
                        float rectHeight = EditorGUIUtility.singleLineHeight + 10f;
                        if (zoundsWindowProperties.showActiveZounds) {
                            rectHeight += ActiveZoundsSectionHeight + 5f;
                        }

                        bool hasManualRoutedZounds = false;
                        var zoundLibrary = ZoundsProject.Instance.zoundLibrary;
                        zoundLibrary.ForEachZound(z => {
                            if (z.editor_hasManuallySetRouting) {
                                if (mixerGroup.audioMixer == z.manuallySetMixerGroupRef.editorAsset && mixerGroup.name == z.manuallySetMixerGroupRef.SubObjectName) {
                                    rectHeight += ManualRoutingSectionHeight + 5f;
                                    hasManualRoutedZounds = true;
                                    return true;
                                }
                            }
                            return false;
                        });
                        var elementRect = GUILayoutUtility.GetRect(1f, rectHeight, GUILayout.ExpandWidth(true));

                        bool even = i % 2 == 0;
                        var guiColor = GUI.color;
                        GUI.color = even ? new Color32(65, 65, 65, 255) : new Color32(75, 75, 75, 255);
                        GUI.DrawTexture(elementRect, EditorGUIUtility.whiteTexture);
                        GUI.color = guiColor;

                        elementRect.x += 5f;
                        elementRect.y += 5f;
                        elementRect.width -= 10f;
                        elementRect.height -= 10f;

                        var mixerRect = new Rect(elementRect.x, elementRect.y, elementRect.width, EditorGUIUtility.singleLineHeight);
                        var guiEnabled = GUI.enabled;
                        GUI.enabled = false;
                        EditorGUI.ObjectField(mixerRect, mixerGroup, typeof(AudioMixerGroup), false);
                        GUI.enabled = guiEnabled;

                        float yPos = mixerRect.yMax + 5f;

                        int ruleCount = ruleList == null ? 0 : ruleList.count;
                        int scrollPosIndex = ruleCount + i;

                        if (zoundsWindowProperties.showActiveZounds) {
                            var activeZoundsRect = mixerRect;
                            activeZoundsRect.y = yPos;
                            activeZoundsRect.height = ActiveZoundsSectionHeight;
                            yPos = activeZoundsRect.yMax + 5f;

                            var labelRect = new Rect(activeZoundsRect.x, activeZoundsRect.y, 85f, EditorGUIUtility.singleLineHeight);
                            EditorGUI.LabelField(labelRect, "Active Zounds:");
                            activeZoundsRect.x += labelRect.width + 5f;
                            activeZoundsRect.width -= labelRect.width + 5f;
                            GUI.Box(activeZoundsRect, GUIContent.none);

                            DrawActiveZounds(activeZoundsRect, mixerGroup.audioMixer, mixerGroup.name, scrollPosIndex);
                        }

                        if (hasManualRoutedZounds) {
                            var manualRoutingsRect = mixerRect;
                            manualRoutingsRect.y = yPos;
                            manualRoutingsRect.height = ManualRoutingSectionHeight;

                            var labelRect = new Rect(manualRoutingsRect.x, manualRoutingsRect.y, 145f, EditorGUIUtility.singleLineHeight);
                            EditorGUI.LabelField(labelRect, "Manually Routed Zounds:");
                            manualRoutingsRect.x += labelRect.width + 5f;
                            manualRoutingsRect.width -= labelRect.width + 5f;
                            GUI.Box(manualRoutingsRect, GUIContent.none);

                            DrawManuallyRoutedZounds(manualRoutingsRect, mixerGroup.audioMixer, mixerGroup.name, scrollPosIndex);
                        }

                        GUILayout.Space(5f);
                    }
                }
            }

            GUILayout.EndScrollView();
            } // end ZUI.Box
        }

        private void OnAddRule(ReorderableList list) {
            int lastIndex = list.serializedProperty.arraySize;
            list.serializedProperty.arraySize++;
            var lastElement = list.serializedProperty.GetArrayElementAtIndex(lastIndex);
            lastElement.FindPropertyRelative("conditions").arraySize = 0;
#if UNITY_6000_0_OR_NEWER
            lastElement.FindPropertyRelative("mixerGroupRef").boxedValue = new UnityEngine.AddressableAssets.AssetReference();
#endif
        }

        private void OnRemoveRule(ReorderableList list) {
            int removedIndex = list.index;
            list.serializedProperty.DeleteArrayElementAtIndex(removedIndex);
            reorderableListNeedsUpdate = true;
        }

        //private void OnDeleteRule(ReorderableList list, int index) {
        //    list.serializedProperty.DeleteArrayElementAtIndex(index);
        //    reorderableListNeedsUpdate = true;
        //}

        private void OnDrawRulesNoneElement(Rect rect) {
            EditorGUI.LabelField(rect, "Click + to add a set of rules.");
        }

        private void OnDrawRulesBackground(Rect rect, int index, bool isActive, bool isFocused) {
            bool even = index % 2 == 0;
            var guiColor = GUI.color;
            if (isActive) {
                GUI.color = even ? new Color32(70, 96, 124, 255) : new Color32(80, 106, 134, 255);
            }
            else {
                GUI.color = even ? new Color32(65, 65, 65, 255) : new Color32(75, 75, 75, 255);
            }
            GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture);
            GUI.color = guiColor;
        }

        private void OnDrawRulesHeader(Rect rect) {
            EditorGUI.LabelField(rect, "Rules");
        }

        private float CalculateRuleElementHeight(int index) {
            float elementHeight = BaseRuleSectionHeight;

            var mixerGroupRef = ZoundsProject.Instance.zoundRoutings.rules[index].mixerGroupRef;

            if (ZoundsWindowProperties.Instance.showActiveZounds) {
                elementHeight += ActiveZoundsSectionHeight;
            }

            if (index >= hasManualRoutedZounds.Count) {
                hasManualRoutedZounds.Add(false);
            }
            hasManualRoutedZounds[index] = false;
            if (mixerGroupRef != null && ZoundsWindowProperties.Instance.showManuallySetRoutings) {
                var zoundLibrary = ZoundsProject.Instance.zoundLibrary;
                zoundLibrary.ForEachZound(z => {
                    if (z.editor_hasManuallySetRouting) {
                        if (mixerGroupRef.editorAsset == z.manuallySetMixerGroupRef.editorAsset && mixerGroupRef.SubObjectName == z.manuallySetMixerGroupRef.SubObjectName) {
                            hasManualRoutedZounds[index] = true;
                            return true;
                        }
                    }
                    return false;
                });

            }
            if (hasManualRoutedZounds[index]) {
                elementHeight += ManualRoutingSectionHeight;
            }

            return elementHeight;
        }

        private void OnDrawRulesElement(Rect rect, int index, bool isActive, bool isFocused) {
            var zoundLibrary = ZoundsProject.Instance.zoundLibrary;
            var element = ruleList.serializedProperty.GetArrayElementAtIndex(index);
            var conditionsProp = element.FindPropertyRelative("conditions");
            var mixerGroupRefProp = element.FindPropertyRelative("mixerGroupRef");

            var contentRect = new Rect(rect.x + 5f, rect.y + 5f, rect.width - 10f, BaseRuleSectionHeight - 10f);
            var leftSection = contentRect;
            leftSection.width /= 2f;

            var rightSection = leftSection;
            rightSection.x = leftSection.xMax + 4f;
            rightSection.width -= 4f;
            var mixerGroupRect = rightSection;
            mixerGroupRect.height = EditorGUIUtility.singleLineHeight;
            var addRuleRect = mixerGroupRect;
            addRuleRect.y += mixerGroupRect.height + 5f;

            DrawRules(leftSection, index, zoundLibrary, conditionsProp);

            var lblWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 74f;
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(mixerGroupRect, mixerGroupRefProp);
            if (EditorGUI.EndChangeCheck()) {
                reorderableListNeedsUpdate = true;
            }
            EditorGUIUtility.labelWidth = lblWidth;
            if (GUI.Button(addRuleRect, "+ Condition")) {
                var addMenu = new GenericMenu();
#if ZOUNDS_CONSIDER_FOLDERS
                var folders = ZoundsFilter.GetFolders();
                foreach (var folder in folders) {
                    string displayName = folder.Substring(1, folder.Length-1).Replace('/', '\\');
                    if (string.IsNullOrEmpty(displayName)) displayName = "[Root]";
                    string f = folder;
                    addMenu.AddItem(new GUIContent("Folder/" + displayName), false, () => {
                        AddRule(index, ZoundRoutings.Rule.RuleType.Folder, f);
                    });
                }
#endif
                var tags = zoundLibrary.tags;
                var addedKeyTags = new HashSet<string>();
                foreach (var tag in tags) {
                    var t = tag;
                    var nameSplit = tag.name.Split(':');
                    if (nameSplit.Length > 1) {
                        string keyTag = nameSplit[0];
                        if (!addedKeyTags.Contains(keyTag)) {
                            addedKeyTags.Add(keyTag);
                            addMenu.AddItem(new GUIContent("Tag/" + keyTag), false, () => {
                                AddRule(index, ZoundRoutings.Condition.ConditionType.Tag, keyTag);
                            });
                        }
                    }
                    addMenu.AddItem(new GUIContent("Tag/" + tag.name), false, () => {
                        AddRule(index, ZoundRoutings.Condition.ConditionType.Tag, t.name);
                    });
                }

                GenericMenuPopup.Show(
                    addMenu,
                    "Select Condition",
                    Event.current.mousePosition,
                    new List<string>(),
                    ruleSearchText,
                    searchText => ruleSearchText = searchText,
                    null, 1
                    );
            }

            float currentY = rect.y + 5f + BaseRuleSectionHeight;

            var mixerGroupRef = ZoundsProject.Instance.zoundRoutings.rules[index].mixerGroupRef;

            if (ZoundsWindowProperties.Instance.showActiveZounds) {
                var labelRect = new Rect(rect.x + 5f, currentY, 85f, EditorGUIUtility.singleLineHeight);
                EditorGUI.LabelField(labelRect, "Active Zounds:");
                var activeZoundsRect = new Rect(labelRect.xMax + 5f, labelRect.y, rect.width - 10f - labelRect.width - 5f, ActiveZoundsSectionHeight - 10f);
                GUI.Box(activeZoundsRect, GUIContent.none);
                DrawActiveZounds(activeZoundsRect, mixerGroupRef.editorAsset, mixerGroupRef.SubObjectName, index);

                currentY += 5f + activeZoundsRect.height;
            }

            if (hasManualRoutedZounds[index]) {
                var labelRect = new Rect(rect.x + 5f, currentY, 145f, EditorGUIUtility.singleLineHeight);
                EditorGUI.LabelField(labelRect, "Manually Routed Zounds:");
                var manualRoutingRect = new Rect(labelRect.xMax + 5f, labelRect.y, rect.width - 10f - labelRect.width - 5f, ManualRoutingSectionHeight - 10f);
                GUI.Box(manualRoutingRect, GUIContent.none);
                DrawManuallyRoutedZounds(manualRoutingRect, mixerGroupRef.editorAsset, mixerGroupRef.SubObjectName, index);
            }
        }

        private void DrawRules(Rect rulesRect, int index, ZoundLibrary zoundLibrary, SerializedProperty conditionsProp) {
            float ruleElementHeight = 20f;
            float ruleElementSpacing = 2f;

            float currentY = rulesRect.y;
            float currentWidth = 2f;
            GUIStyle labelStyle = EditorStyles.label;
            float scrollViewWidth = 0f;
            float scrollViewHeight = 0f;

            if (conditionsProp.arraySize == 0) {
                tempContent.text = "Empty";
                var labelSize = EditorStyles.centeredGreyMiniLabel.CalcSize(tempContent);
                scrollViewWidth = labelSize.x;
                scrollViewHeight = labelSize.y;
            }

            for (int i = 0; i < conditionsProp.arraySize; i++) {
                var conditionProp = conditionsProp.GetArrayElementAtIndex(i);
                var conditionType = (ZoundRoutings.Condition.ConditionType)conditionProp.FindPropertyRelative("type").enumValueIndex;
                string elementName = conditionProp.FindPropertyRelative("name").stringValue;
                string label;
#if ZOUNDS_CONSIDER_FOLDERS
                if (conditionType == ZoundRoutings.Condition.ConditionType.Folder) {
                    string folderName = elementName.Substring(1, elementName.Length - 1);
                    if (string.IsNullOrEmpty(folderName)) folderName = "[Root]";
                    label = "Folder:" + folderName;
                }
                else
#endif
                if (conditionType == ZoundRoutings.Condition.ConditionType.Tag) {
                    if (!zoundLibrary.TryGetTag(elementName, out var tagByName) && zoundLibrary.tags.Find(t => t.name.StartsWith(elementName + ":")) == null) {
                        label = "(Missing) " + elementName;
                    }
                    else {
                        label = elementName;
                    }
                }
                else {
                    label = "(Undefined)";
                }
                tempContent.text = label;
                float labelWidth = labelStyle.CalcSize(tempContent).x + 4f + ruleElementHeight; // since the remove button is square, height = width

                if (labelWidth > scrollViewWidth) {
                    scrollViewWidth = labelWidth;
                }

                float restWidth = rulesRect.width - currentWidth;
                if (currentWidth > ruleElementSpacing && labelWidth > restWidth) { // change row if labelWidth exceeds the remaining row width
                    if (currentWidth > scrollViewWidth) {
                        scrollViewWidth = currentWidth;
                    }
                    currentY += ruleElementHeight;
                    scrollViewHeight += ruleElementHeight;
                    currentWidth = ruleElementSpacing;
                }
                var ruleRect = new Rect(rulesRect.x + currentWidth, currentY, labelWidth, ruleElementHeight);
                currentWidth += labelWidth + ruleElementSpacing;
            }
            scrollViewHeight += ruleElementHeight;

            for (int i = ruleSetScrollPoses.Count; i <= index; i++) {
                ruleSetScrollPoses.Add(Vector2.zero);
            }
            var scrollViewRect = rulesRect;
            scrollViewRect.width = scrollViewWidth;
            scrollViewRect.height = scrollViewHeight;
            ruleSetScrollPoses[index] = GUI.BeginScrollView(rulesRect, ruleSetScrollPoses[index], scrollViewRect);

            currentY = rulesRect.y;
            currentWidth = 2f;

            if (conditionsProp.arraySize == 0) {
                EditorGUI.LabelField(rulesRect, tempContent, EditorStyles.centeredGreyMiniLabel);
            }

            for (int i = 0; i < conditionsProp.arraySize; i++) {
                var ruleProp = conditionsProp.GetArrayElementAtIndex(i);
                var ruleType = (ZoundRoutings.Condition.ConditionType)ruleProp.FindPropertyRelative("type").enumValueIndex;
                string elementName = ruleProp.FindPropertyRelative("name").stringValue;
                string label;
                bool isError = false;
#if ZOUNDS_CONSIDER_FOLDERS
                if (ruleType == ZoundRoutings.Rule.RuleType.Folder) {
                    label = "Folder:" + elementName.Substring(1, elementName.Length-1);
                }
                else 
#endif
                if (ruleType == ZoundRoutings.Condition.ConditionType.Tag) {
                    if (!zoundLibrary.TryGetTag(elementName, out var tagByName) && zoundLibrary.tags.Find(t => t.name.StartsWith(elementName + ":")) == null) {
                        isError = true;
                        label = "(Missing) " + elementName;
                    }
                    else {
                        label = elementName;
                    }
                }
                else {
                    isError = true;
                    label = "(Undefined)";
                }
                tempContent.text = label;
                float labelWidth = labelStyle.CalcSize(tempContent).x + 4f + ruleElementHeight; // since the remove button is square, height = width

                float restWidth = rulesRect.width - currentWidth;
                if (currentWidth > ruleElementSpacing && labelWidth > restWidth) { // change row if labelWidth exceeds the remaining row width
                    currentY += ruleElementHeight;
                    currentWidth = ruleElementSpacing;
                }
                var ruleRect = new Rect(rulesRect.x + currentWidth, currentY, labelWidth, ruleElementHeight);
                currentWidth += labelWidth + ruleElementSpacing;

                EditorGUI.HelpBox(ruleRect, "", MessageType.None);
                var ruleContentRect = new Rect(ruleRect.x + 2f, ruleRect.y + 2f, ruleRect.width - 4f, ruleRect.height - 4f);
                var ruleLabelRect = ruleContentRect;
                ruleLabelRect.width -= ruleElementHeight;
                var removeButtonRect = ruleContentRect;
                removeButtonRect.x = removeButtonRect.xMax - ruleElementHeight;
                removeButtonRect.width = ruleElementHeight;

                var guiColor = GUI.color;
                if (isError) GUI.color = new Color(1f, 0.7f, 0.7f, 1f);
                EditorGUI.LabelField(ruleLabelRect, label);
                GUI.color = guiColor;

                if (GUI.Button(removeButtonRect, "X")) {
                    conditionsProp.DeleteArrayElementAtIndex(i);
                    i--;
                }
            }

            GUI.EndScrollView();
        }

        private void DrawActiveZounds(Rect activeZoundsRect, UnityEngine.Object audioMixerAsset, string mixerGroupName, int scrollElementIndex) {
            float ruleElementHeight = 18f;
            float ruleElementSpacing = 2f;

            float currentY = activeZoundsRect.y;
            float currentWidth = 2f;
            GUIStyle labelStyle = EditorStyles.label;
            float scrollViewWidth = 0f;
            float scrollViewHeight = 0f;

            var zoundRoutings = ZoundsProject.Instance.zoundRoutings;
            foreach (var kvp in ZoundEngine.CullingGroups) {
                if (kvp.Value.Count == 0) continue;
                var z = kvp.Key;
                var runtimeMixerGroup = zoundRoutings.GetRouting(z);
                if (runtimeMixerGroup == null || 
                    runtimeMixerGroup.audioMixer != audioMixerAsset ||
                    runtimeMixerGroup.name != mixerGroupName) {

                    continue;
                }

                tempContent.text = z.name;
                float labelWidth = labelStyle.CalcSize(tempContent).x + 4f;

                if (labelWidth > scrollViewWidth) {
                    scrollViewWidth = labelWidth;
                }

                float restWidth = activeZoundsRect.width - currentWidth;
                if (currentWidth > ruleElementSpacing && labelWidth > restWidth) { // change row if labelWidth exceeds the remaining row width
                    if (currentWidth > scrollViewWidth) {
                        scrollViewWidth = currentWidth;
                    }
                    currentY += ruleElementHeight;
                    scrollViewHeight += ruleElementHeight;
                    currentWidth = ruleElementSpacing;
                }
                var zoundRect = new Rect(activeZoundsRect.x + currentWidth, currentY, labelWidth, ruleElementHeight);
                currentWidth += labelWidth + ruleElementSpacing;
            }
            scrollViewHeight += ruleElementHeight;

            for (int i = manualRoutingScrollPoses.Count; i <= scrollElementIndex; i++) {
                manualRoutingScrollPoses.Add(Vector2.zero);
            }
            var scrollViewRect = activeZoundsRect;
            scrollViewRect.width = scrollViewWidth;
            scrollViewRect.height = scrollViewHeight;
            manualRoutingScrollPoses[scrollElementIndex] = GUI.BeginScrollView(activeZoundsRect, manualRoutingScrollPoses[scrollElementIndex], scrollViewRect);

            currentY = activeZoundsRect.y;
            currentWidth = 2f;

            foreach (var kvp in ZoundEngine.CullingGroups) {
                if (kvp.Value.Count == 0) continue;
                var z = kvp.Key;
                var runtimeMixerGroup = zoundRoutings.GetRouting(z);
                if (runtimeMixerGroup == null ||
                    runtimeMixerGroup.audioMixer != audioMixerAsset ||
                    runtimeMixerGroup.name != mixerGroupName) {

                    continue;
                }

                tempContent.text = z.name;
                float labelWidth = labelStyle.CalcSize(tempContent).x + 4f;

                float restWidth = activeZoundsRect.width - currentWidth;
                if (currentWidth > ruleElementSpacing && labelWidth > restWidth) { // change row if labelWidth exceeds the remaining row width
                    currentY += ruleElementHeight;
                    currentWidth = ruleElementSpacing;
                }
                var zoundBoxRect = new Rect(activeZoundsRect.x + currentWidth, currentY, labelWidth, ruleElementHeight);
                currentWidth += labelWidth + ruleElementSpacing;

                var guiColor = GUI.color;
                GUI.color = Color.green;
                EditorGUI.HelpBox(zoundBoxRect, "", MessageType.None);
                GUI.color = guiColor;
                var zoundNameRect = new Rect(zoundBoxRect.x + 2f, zoundBoxRect.y + 2f, zoundBoxRect.width - 4f, zoundBoxRect.height - 4f);

                EditorGUI.LabelField(zoundNameRect, z.name);
            }

            GUI.EndScrollView();
        }

        private void DrawManuallyRoutedZounds(Rect manualRoutingsRect, UnityEngine.Object audioMixerAsset, string mixerGroupName, int scrollElementIndex) {
            float ruleElementHeight = 18f;
            float ruleElementSpacing = 2f;

            float currentY = manualRoutingsRect.y;
            float currentWidth = 2f;
            GUIStyle labelStyle = EditorStyles.label;
            float scrollViewWidth = 0f;
            float scrollViewHeight = 0f;

            var zoundLibrary = ZoundsProject.Instance.zoundLibrary;
            zoundLibrary.ForEachZound(z => {
                if (!z.editor_hasManuallySetRouting ||
                    audioMixerAsset != z.manuallySetMixerGroupRef.editorAsset ||
                    mixerGroupName != z.manuallySetMixerGroupRef.SubObjectName) {

                    return;
                }

                tempContent.text = z.name;
                float labelWidth = labelStyle.CalcSize(tempContent).x + 4f;

                if (labelWidth > scrollViewWidth) {
                    scrollViewWidth = labelWidth;
                }

                float restWidth = manualRoutingsRect.width - currentWidth;
                if (currentWidth > ruleElementSpacing && labelWidth > restWidth) { // change row if labelWidth exceeds the remaining row width
                    if (currentWidth > scrollViewWidth) {
                        scrollViewWidth = currentWidth;
                    }
                    currentY += ruleElementHeight;
                    scrollViewHeight += ruleElementHeight;
                    currentWidth = ruleElementSpacing;
                }
                var zoundRect = new Rect(manualRoutingsRect.x + currentWidth, currentY, labelWidth, ruleElementHeight);
                currentWidth += labelWidth + ruleElementSpacing;
            });
            scrollViewHeight += ruleElementHeight;

            for (int i = manualRoutingScrollPoses.Count; i <= scrollElementIndex; i++) {
                manualRoutingScrollPoses.Add(Vector2.zero);
            }
            var scrollViewRect = manualRoutingsRect;
            scrollViewRect.width = scrollViewWidth;
            scrollViewRect.height = scrollViewHeight;
            manualRoutingScrollPoses[scrollElementIndex] = GUI.BeginScrollView(manualRoutingsRect, manualRoutingScrollPoses[scrollElementIndex], scrollViewRect);

            currentY = manualRoutingsRect.y;
            currentWidth = 2f;

            zoundLibrary.ForEachZound(z => {
                if (!z.editor_hasManuallySetRouting ||
                    audioMixerAsset != z.manuallySetMixerGroupRef.editorAsset ||
                    mixerGroupName != z.manuallySetMixerGroupRef.SubObjectName) {

                    return;
                }

                tempContent.text = z.name;
                float labelWidth = labelStyle.CalcSize(tempContent).x + 4f;

                float restWidth = manualRoutingsRect.width - currentWidth;
                if (currentWidth > ruleElementSpacing && labelWidth > restWidth) { // change row if labelWidth exceeds the remaining row width
                    currentY += ruleElementHeight;
                    currentWidth = ruleElementSpacing;
                }
                var zoundBoxRect = new Rect(manualRoutingsRect.x + currentWidth, currentY, labelWidth, ruleElementHeight);
                currentWidth += labelWidth + ruleElementSpacing;

                EditorGUI.HelpBox(zoundBoxRect, "", MessageType.None);
                var zoundNameRect = new Rect(zoundBoxRect.x + 2f, zoundBoxRect.y + 2f, zoundBoxRect.width - 4f, zoundBoxRect.height - 4f);

                EditorGUI.LabelField(zoundNameRect, z.name);
            });

            GUI.EndScrollView();
        }

        private static void AddRule(int setIndex, ZoundRoutings.Condition.ConditionType type, string name) {
            var zoundsProject = ZoundsProject.Instance;
            var rules = zoundsProject.zoundRoutings.rules[setIndex].conditions;

            var existingRule = rules.Find(r => r.type == type && r.name == name);
            if (existingRule != null) {
                return;
            }

            ZoundsWindow.ModifyZoundsProject("add rule", () => {
                var newRule = new ZoundRoutings.Condition();
                newRule.type = type;
                newRule.name = name;
                rules.Add(newRule);
            });
        }

        public static void GetAllAddresableMixerGroups(ref List<AudioMixerGroup> allMixerGroups) {
            if (allMixerGroups == null) allMixerGroups = new List<AudioMixerGroup>();
#if ADDRESSABLES_INSTALLED
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) {
                Debug.LogWarning("[Zounds] Please create Addressable Asset Settings first.");
                return;
            }

            foreach (var group in settings.groups) {
                foreach (var entry in group.entries) {
                    string path = AssetDatabase.GUIDToAssetPath(entry.guid);
                    UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);

                    if (obj is AudioMixer mixer) {
                        AudioMixerGroup[] groups = mixer.FindMatchingGroups(string.Empty);
                        allMixerGroups.AddRange(groups);
                    }
                    else if (obj is AudioMixerGroup mixerGroup) {
                        allMixerGroups.Add(mixerGroup);
                    }
                }
            }
#endif
        }

    }

}
