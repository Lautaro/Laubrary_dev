using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    public class TagsEditorWindow : BaseZoundEditorWindow<Zound, TagsEditorWindow> {

        public static TagsEditorWindow OpenWindow(Zound zound) {
            return OpenWindow<TagsEditorWindow>(zound, new Vector2(200f, 150f));
        }

        #region PROPERTIES
        [SerializeField] private string inputTagName;
        [SerializeField] private string inputTagValue;
        [SerializeField] private int selectedTagId;

        private double clickTime;
        private double doubleClickTime = 0.3;

        private Dictionary<string, ZoundLibrary.Tag> tagNameChoices = new Dictionary<string, ZoundLibrary.Tag>();
        private Dictionary<string, ZoundLibrary.Tag> tagValueChoices = new Dictionary<string, ZoundLibrary.Tag>();
        private GUIContent tempContent = new GUIContent();


        private static GUIStyle s_existingTagUnselectedStyle;
        private static GUIStyle existingTagUnselectedStyle {
            get {
                if (s_existingTagUnselectedStyle == null) {
                    s_existingTagUnselectedStyle = new GUIStyle();

                    s_existingTagUnselectedStyle.normal.textColor = Color.yellow;
                    s_existingTagUnselectedStyle.normal.background = null;
                    s_existingTagUnselectedStyle.onNormal.textColor = Color.yellow;
                    s_existingTagUnselectedStyle.onNormal.background = null;

                    s_existingTagUnselectedStyle.padding = new RectOffset(5, 5, 2, 2);
                }
                return s_existingTagUnselectedStyle;
            }
        }

        private static GUIStyle s_existingTagSelectedStyle;
        private static GUIStyle existingTagSelectedStyle {
            get {
                if (s_existingTagSelectedStyle == null) {
                    s_existingTagSelectedStyle = new GUIStyle();

                    Texture2D whiteBorderTex = CreateBorderTexture(2, Color.gray, new Color(0, 0, 0, 0));
                    s_existingTagSelectedStyle.normal.background = whiteBorderTex;
                    s_existingTagSelectedStyle.onNormal.background = whiteBorderTex;

                    s_existingTagSelectedStyle.normal.textColor = Color.yellow;
                    s_existingTagSelectedStyle.onNormal.textColor = Color.yellow;

                    s_existingTagSelectedStyle.padding = new RectOffset(5, 5, 2,2);
                }
                return s_existingTagSelectedStyle;
            }
        }

        private static GUIStyle s_quickChoiceUnselectedStyle;
        private static GUIStyle quickChoiceUnselectedStyle {
            get {
                if (s_quickChoiceUnselectedStyle == null) {
                    s_quickChoiceUnselectedStyle = new GUIStyle();

                    s_quickChoiceUnselectedStyle.normal.textColor = new Color32(163, 198, 255, 255);
                    s_quickChoiceUnselectedStyle.normal.background = null;
                    s_quickChoiceUnselectedStyle.onNormal.textColor = new Color32(163, 198, 255, 255);
                    s_quickChoiceUnselectedStyle.onNormal.background = null;

                    s_quickChoiceUnselectedStyle.padding = new RectOffset(5, 5, 2, 2);
                }
                return s_quickChoiceUnselectedStyle;
            }
        }

        private static GUIStyle s_quickChoiceSelectedStyle;
        private static GUIStyle quickChoiceSelectedStyle {
            get {
                if (s_quickChoiceSelectedStyle == null) {
                    s_quickChoiceSelectedStyle = new GUIStyle();

                    Texture2D whiteBorderTex = CreateBorderTexture(2, Color.gray, new Color(0, 0, 0, 0));
                    s_quickChoiceSelectedStyle.normal.background = whiteBorderTex;
                    s_quickChoiceSelectedStyle.onNormal.background = whiteBorderTex;

                    s_quickChoiceSelectedStyle.normal.textColor = new Color32(163, 198, 255, 255);
                    s_quickChoiceSelectedStyle.onNormal.textColor = new Color32(163, 198, 255, 255);

                    s_quickChoiceSelectedStyle.padding = new RectOffset(5, 5, 2, 2);
                }
                return s_quickChoiceSelectedStyle;
            }
        }

        private static GUIStyle s_quickValueUnselectedStyle;
        private static GUIStyle quickValueUnselectedStyle {
            get {
                if (s_quickValueUnselectedStyle == null) {
                    s_quickValueUnselectedStyle = new GUIStyle();

                    s_quickValueUnselectedStyle.normal.textColor = Color.white;
                    s_quickValueUnselectedStyle.normal.background = null;
                    s_quickValueUnselectedStyle.onNormal.textColor = Color.white;
                    s_quickValueUnselectedStyle.onNormal.background = null;

                    s_quickValueUnselectedStyle.padding = new RectOffset(5, 5, 2, 2);
                }
                return s_quickValueUnselectedStyle;
            }
        }

        private static GUIStyle s_quickValueSelectedStyle;
        private static GUIStyle quickValueSelectedStyle {
            get {
                if (s_quickValueSelectedStyle == null) {
                    s_quickValueSelectedStyle = new GUIStyle();

                    Texture2D whiteBorderTex = CreateBorderTexture(2, Color.gray, new Color(0, 0, 0, 0));
                    s_quickValueSelectedStyle.normal.background = whiteBorderTex;
                    s_quickValueSelectedStyle.onNormal.background = whiteBorderTex;

                    s_quickValueSelectedStyle.normal.textColor = Color.white;
                    s_quickValueSelectedStyle.onNormal.textColor = Color.white;

                    s_quickValueSelectedStyle.padding = new RectOffset(5, 5, 2, 2);
                }
                return s_quickValueSelectedStyle;
            }
        }
        #endregion PROPERTIES

        protected override void OnInit() {
            base.OnInit();
            titleContent.text = "Tags: " + (targetZound == null ? "(Invalid)" : targetZound.name);
            CleanupUnregisteredTags(targetZound);
        }

        protected override Zound FindZoundTarget() {
            var library = ZoundsProject.Instance.zoundLibrary;
            Zound target = library.FindZound(z => z.id == targetZoundID);
            return target;
        }

        protected override bool OnDrawGUI() {
            if (s_existingTagSelectedStyle != null && s_existingTagSelectedStyle.normal.background == null) {
                // recreate because the textures are destroyed
                s_existingTagSelectedStyle = null;
                s_quickChoiceSelectedStyle = null;
                s_quickValueSelectedStyle = null;
            }

            bool removeZound = base.OnDrawGUI();

            var contentArea = new Rect(10f, 10f, position.width - 20f, position.height - 20f);

            var zoundsProject = ZoundsProject.Instance;
            var zoundLibrary = zoundsProject.zoundLibrary;
            var projectTags = zoundLibrary.tags;
            var zoundTags = projectTags.Where(tag => targetZound.tags.Contains(tag.id));

            int tagToRemove = DrawExistingTagsSection(contentArea, zoundTags);
            DrawCreateTagSection(zoundLibrary, zoundTags);
            DrawQuickChoicesSection(contentArea, zoundLibrary, zoundTags);
            if (selectedTagId != 0) {
                DrawQuickValuesSection(contentArea, zoundLibrary, zoundTags);
            }

            if (tagToRemove != 0) {
                ZoundsWindow.ModifyZoundsProject("remove existing tag", () => {
                    targetZound.tags.Remove(tagToRemove);
                    zoundLibrary.RemoveUnusedTags();
                });

                if (tagToRemove == selectedTagId) {
                    Undo.RecordObject(this, "remove existing tag");
                    selectedTagId = 0;
                    inputTagName = "";
                    inputTagValue = "";
                    EditorUtility.SetDirty(this);
                }

                ZoundsWindowProperties.DirtyAll();
                GUI.FocusControl(null);
            }

            return removeZound;
        }

        private int DrawExistingTagsSection(Rect contentArea, IEnumerable<ZoundLibrary.Tag> zoundTags) {
            var guiColor = GUI.color;
            var buttonStyle = GUI.skin.button;
            tempContent.text = "x";
            var removeButtonSize = buttonStyle.CalcSize(tempContent);

            float spacing = 5f;
            float contentWidth = contentArea.width;
            float currentY = 0f;
            float currentWidth = 0f;

            int currentIndex = 0;
            int tagsCount = zoundTags.Count();
            foreach (var tag in zoundTags) {
                tempContent.text = tag.name;
                var size = existingTagUnselectedStyle.CalcSize(tempContent);
                currentWidth += size.x + removeButtonSize.x + spacing;
                if (currentWidth > contentWidth) {
                    currentWidth = size.x + removeButtonSize.x + spacing;
                    currentY += size.y;
                }
                currentIndex++;
            }
            currentY += EditorGUIUtility.singleLineHeight;

            GUILayout.Space(5f);

            var existingTagsArea = GUILayoutUtility.GetRect(contentWidth, currentY);
            var emptyArea = new Rect(0f, 0f, position.width, existingTagsArea.height + 15f);
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.2f);
            GUI.DrawTexture(emptyArea, EditorGUIUtility.whiteTexture);
            GUI.color = guiColor;

            currentWidth = 0f;
            currentY = 0f;

            int tagToRemove = 0;

            foreach (var tag in zoundTags) {
                tempContent.text = tag.name;
                var size = existingTagUnselectedStyle.CalcSize(tempContent);
                currentWidth += size.x + removeButtonSize.x + spacing;
                if (currentWidth > contentWidth) {
                    currentWidth = size.x + removeButtonSize.x + spacing;
                    currentY += size.y;
                }

                var removeRect = new Rect(existingTagsArea.x + currentWidth - size.x - removeButtonSize.x,
                                          existingTagsArea.y + currentY,
                                          removeButtonSize.x, removeButtonSize.y);
                if (GUI.Button(removeRect, "x")) {
                    tagToRemove = tag.id;
                }

                var tagRect = new Rect(removeRect.xMax, removeRect.y, size.x, size.y);
                if (GUI.Button(tagRect, tempContent, selectedTagId == tag.id ? existingTagSelectedStyle : existingTagUnselectedStyle)) {
                    Undo.RecordObject(this, "select existing tag");
                    selectedTagId = tag.id;
                    var splits = tag.name.Split(':');
                    if (splits.Length > 1) {
                        inputTagName = splits[0];
                        inputTagValue = "";
                        for (int i = 1; i < splits.Length; i++) {
                            if (i == splits.Length - 1) {
                                inputTagValue += splits[i];
                            }
                            else {
                                if (i == splits.Length - 2) {
                                    inputTagValue += splits[i];
                                }
                                else {
                                    inputTagValue += splits[i] + ":";
                                }
                            }
                        }
                    }
                    else {
                        inputTagName = tag.name;
                        inputTagValue = "";
                    }
                    EditorUtility.SetDirty(this);
                    GUI.FocusControl(null);
                }
            }

            var evt = Event.current;
            if (evt.type != EventType.Used && GUI.Button(emptyArea, GUIContent.none, GUI.skin.label)) {
                if (selectedTagId != 0) {
                    Undo.RecordObject(this, "clear existing tag selection");
                    selectedTagId = 0;
                    inputTagName = "";
                    inputTagValue = "";
                    EditorUtility.SetDirty(this);
                }
                GUI.FocusControl(null);
            }

            GUILayout.Space(10f);
            return tagToRemove;
        }

        private void DrawCreateTagSection(ZoundLibrary zoundLibrary, IEnumerable<ZoundLibrary.Tag> zoundTags) {
            var guiColor = GUI.color;
            GUI.color = Color.gray;
            var lineRect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(lineRect, EditorGUIUtility.whiteTexture);
            GUI.color = guiColor;

            GUILayout.Space(5f);

            var buttonStyle = GUI.skin.button;
            tempContent.text = "Create";
            var createButtonSize = buttonStyle.CalcSize(tempContent);

            var labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 65f;
            GUILayout.BeginHorizontal();
            {
                var guiEnabled = GUI.enabled;
                GUI.enabled = guiEnabled && selectedTagId == 0;
                EditorGUI.BeginChangeCheck();
                var newTagName = EditorGUILayout.TextField("Tag Name", inputTagName);
                if (EditorGUI.EndChangeCheck()) {
                    newTagName = newTagName.Replace(":", "");
                    Undo.RecordObject(this, "change input tag name");
                    inputTagName = newTagName;
                    EditorUtility.SetDirty(this);
                }
                GUILayout.Space(5f);
                if (GUILayout.Button(GUIContent.none, EditorStyles.label, GUILayout.MinWidth(createButtonSize.x), GUILayout.MaxWidth(createButtonSize.x))) {
                    // for simulating spacing only
                }
                GUI.enabled = guiEnabled;
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            {
                EditorGUI.BeginChangeCheck();
                var newTagValue = EditorGUILayout.TextField("Tag Value", inputTagValue);
                if (EditorGUI.EndChangeCheck()) {
                    newTagValue = newTagValue.Replace(":", "");
                    Undo.RecordObject(this, "change tag value");
                    inputTagValue = newTagValue;
                    EditorUtility.SetDirty(this);
                }
                GUILayout.Space(5f);
                var guiEnabled = GUI.enabled;
                GUI.enabled = guiEnabled && !string.IsNullOrWhiteSpace(inputTagName);
                if (GUILayout.Button(tempContent, GUILayout.MinWidth(createButtonSize.x), GUILayout.MaxWidth(createButtonSize.x))) {
                    CreateTag(zoundLibrary, zoundTags);
                }
                GUI.enabled = guiEnabled;
            }
            GUILayout.EndHorizontal();
            EditorGUIUtility.labelWidth = labelWidth;

            GUILayout.Space(5f);

            GUI.color = Color.gray;
            lineRect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(lineRect, EditorGUIUtility.whiteTexture);
            GUI.color = guiColor;

            GUILayout.Space(10f);
        }

        private int DrawQuickChoicesSection(Rect contentArea, ZoundLibrary zoundLibrary, IEnumerable<ZoundLibrary.Tag> zoundTags) {
            var guiColor = GUI.color;
            tagNameChoices.Clear();

            float spacing = 5f;
            float contentWidth = contentArea.width;
            float currentY = 0f;
            float currentWidth = 0f;

            int currentIndex = 0;
            foreach (var tag in zoundLibrary.tags) {
                var split = tag.name.Split(':');
                string tagName = split[0];
                if (!tagNameChoices.ContainsKey(tagName)) {

                    // uncomment this if you don't want to show tags that are already in existing tags
                    //bool proceed = true;
                    //foreach (var existingTag in zoundTags) {
                    //    var existingName = existingTag.name.Split(':');
                    //    if (existingName[0] == tagName) {
                    //        proceed = false;
                    //        break;
                    //    }
                    //}
                    //if (!proceed) continue;

                    tagNameChoices.Add(tagName, tag);
                    tempContent.text = tagName;
                    var size = existingTagUnselectedStyle.CalcSize(tempContent);
                    currentWidth += size.x + +spacing;
                    if (currentWidth > contentWidth) {
                        currentWidth = size.x + spacing;
                        currentY += size.y;
                    }
                    currentIndex++;
                }
            }
            if (currentIndex != 0) {
                currentY += EditorGUIUtility.singleLineHeight;
            }

            GUILayout.Space(5f);

            var tagChoicesArea = GUILayoutUtility.GetRect(contentWidth, currentY);
            var emptyArea = new Rect(tagChoicesArea.x - 5f, tagChoicesArea.y -5f, position.width, tagChoicesArea.height + 15f);
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.2f);
            GUI.DrawTexture(emptyArea, EditorGUIUtility.whiteTexture);
            GUI.color = guiColor;

            var evt = Event.current;

            currentWidth = 0f;
            currentY = 0f;

            int tagToRemove = 0;

            foreach (var kvp in tagNameChoices) {
                string tagName = kvp.Key;
                var tag = kvp.Value;
                tempContent.text = tagName;
                var size = existingTagUnselectedStyle.CalcSize(tempContent);
                currentWidth += size.x + spacing;
                if (currentWidth > contentWidth) {
                    currentWidth = size.x + spacing;
                    currentY += size.y;
                }

                var tagRect = new Rect(tagChoicesArea.x + currentWidth - size.x, tagChoicesArea.y + currentY, size.x, size.y);

                if (GUI.Button(tagRect, tempContent, selectedTagId == tag.id ? quickChoiceSelectedStyle : quickChoiceUnselectedStyle)) {
                    Undo.RecordObject(this, "select tag choice");
                    selectedTagId = tag.id;
                    var splits = tag.name.Split(':');
                    if (splits.Length > 1) {
                        inputTagName = splits[0];
                        inputTagValue = "";
                        for (int i = 1; i < splits.Length; i++) {
                            if (i == splits.Length - 1) {
                                inputTagValue += splits[i];
                            }
                            else {
                                if (i == splits.Length - 2) {
                                    inputTagValue += splits[i];
                                }
                                else {
                                    inputTagValue += splits[i] + ":";
                                }
                            }
                        }
                    }
                    else {
                        inputTagName = tag.name;
                        inputTagValue = "";
                    }
                    EditorUtility.SetDirty(this);
                    GUI.FocusControl(null);

                    if ((EditorApplication.timeSinceStartup - clickTime) < doubleClickTime) {
                        inputTagValue = "";
                        EditorUtility.SetDirty(this);
                        GUI.FocusControl(null);
                        CreateTag(zoundLibrary, zoundTags);
                    }
                    clickTime = EditorApplication.timeSinceStartup;
                }
            }

            if (evt.type != EventType.Used && GUI.Button(emptyArea, GUIContent.none, GUI.skin.label)) {
                if (selectedTagId != 0) {
                    Undo.RecordObject(this, "clear tag choice selection");
                    selectedTagId = 0;
                    inputTagName = "";
                    inputTagValue = "";
                    EditorUtility.SetDirty(this);
                }
                GUI.FocusControl(null);
            }

            GUILayout.Space(10f);
            return tagToRemove;
        }

        private int DrawQuickValuesSection(Rect contentArea, ZoundLibrary zoundLibrary, IEnumerable<ZoundLibrary.Tag> zoundTags) {
            var selectedTag = zoundLibrary.tags.Find(t => t.id == selectedTagId);
            if (selectedTag == null) return 0;
            var split = selectedTag.name.Split(':');
            string selectedTagName = split[0];

            tagValueChoices.Clear();

            GUILayout.Space(10f);
            var guiColor = GUI.color;

            float spacing = 5f;
            float contentWidth = contentArea.width;
            float currentY = 0f;
            float currentWidth = 0f;

            int currentIndex = 0;
            foreach (var tag in zoundLibrary.tags) {
                split = tag.name.Split(':');
                if (split.Length < 2) continue;
                string tagName = split[0];
                if (tagName == selectedTagName) {
                    string valueName = split[1];
                    tagValueChoices.Add(valueName, tag);
                    tempContent.text = valueName;
                    var size = existingTagUnselectedStyle.CalcSize(tempContent);
                    currentWidth += size.x + +spacing;
                    if (currentWidth > contentWidth) {
                        currentWidth = size.x + spacing;
                        currentY += size.y;
                    }
                    currentIndex++;
                }
            }
            if (currentIndex != 0) {
                currentY += EditorGUIUtility.singleLineHeight;
            }

            if (tagValueChoices.Count == 0) return 0;

            GUILayout.Space(5f);

            var tagValuesArea = GUILayoutUtility.GetRect(contentWidth, currentY);
            var emptyArea = new Rect(tagValuesArea.x - 5f, tagValuesArea.y - 5f, position.width, tagValuesArea.height + 15f);
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.2f);
            GUI.DrawTexture(emptyArea, EditorGUIUtility.whiteTexture);
            GUI.color = guiColor;

            currentWidth = 0f;
            currentY = 0f;

            int tagToRemove = 0;
            bool isSelectingAnyValue = false;

            foreach (var kvp in tagValueChoices) {
                string valueName = kvp.Key;
                var tag = kvp.Value;
                tempContent.text = valueName;
                var size = existingTagUnselectedStyle.CalcSize(tempContent);
                currentWidth += size.x + spacing;
                if (currentWidth > contentWidth) {
                    currentWidth = size.x + spacing;
                    currentY += size.y;
                }

                var tagRect = new Rect(tagValuesArea.x + currentWidth - size.x, tagValuesArea.y + currentY, size.x, size.y);

                bool sameValue = inputTagValue == valueName;
                if (sameValue) isSelectingAnyValue = true;

                if (GUI.Button(tagRect, tempContent, sameValue ? quickValueSelectedStyle : quickValueUnselectedStyle)) {
                    Undo.RecordObject(this, "select tag value");
                    inputTagValue = valueName;
                    EditorUtility.SetDirty(this);
                    GUI.FocusControl(null);

                    if ((EditorApplication.timeSinceStartup - clickTime) < doubleClickTime) {
                        CreateTag(zoundLibrary, zoundTags);
                    }
                    clickTime = EditorApplication.timeSinceStartup;
                }
            }

            var evt = Event.current;
            if (evt.type != EventType.Used && GUI.Button(emptyArea, GUIContent.none, GUI.skin.label)) {
                if (isSelectingAnyValue) {
                    Undo.RecordObject(this, "clear tag value selection");
                    inputTagValue = "";
                    EditorUtility.SetDirty(this);
                }
                GUI.FocusControl(null);
            }

            GUILayout.Space(10f);
            return tagToRemove;
        }

        private void CreateTag(ZoundLibrary zoundLibrary, IEnumerable<ZoundLibrary.Tag> zoundTags) {
            Undo.RecordObject(this, "create tag");

            ZoundsWindow.ModifyZoundsProject("create tag", () => {

                ZoundLibrary.Tag existingTag = null;
                foreach (var t in zoundTags) {
                    var split = t.name.Split(':');
                    if (split[0] == inputTagName) {
                        existingTag = t;
                        break;
                    }
                }

                if (existingTag != null) {
                    targetZound.tags.Remove(existingTag.id);
                }

                string tagName = inputTagName;
                if (!string.IsNullOrWhiteSpace(inputTagValue)) {
                    tagName += ":" + inputTagValue;
                }
                if (!zoundLibrary.TryGetTag(tagName, out var tagToAdd)) {
                    tagToAdd = zoundLibrary.CreateNewTag(tagName);
                }
                targetZound.tags.Add(tagToAdd.id);
                zoundLibrary.RemoveUnusedTags();
                selectedTagId = tagToAdd.id;

            });

            EditorUtility.SetDirty(this);
            ZoundsWindowProperties.DirtyAll();
            GUI.FocusControl(null);
        }



        private static Texture2D CreateBorderTexture(int borderWidth, Color borderColor, Color centerColor) {
            int width = 64, height = 32;
            Texture2D tex = new Texture2D(width, height, TextureFormat.ARGB32, false);

            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    bool isBorder = x < borderWidth || x >= width - borderWidth || y < borderWidth || y >= height - borderWidth;
                    tex.SetPixel(x, y, isBorder ? borderColor : centerColor);
                }
            }

            tex.Apply();
            return tex;
        }

        internal static void CleanupUnregisteredTags(Zound zound) {
            var zoundsProject = ZoundsProject.Instance;
            var zoundLibrary = zoundsProject.zoundLibrary;
            var projectTags = zoundLibrary.tags;
            var unusedTags = zound.tags.Where(id => projectTags.Find(tag => tag.id == id) == null).ToArray();
            bool recorded = false;
            foreach (var unusedTag in unusedTags) {
                if (!recorded) {
                    recorded = true;
                    Undo.RecordObject(zoundsProject, "cleanup unregsitered tags");
                }
                zound.tags.Remove(unusedTag);
            }
            if (recorded) {
                EditorUtility.SetDirty(zoundsProject);
                ZoundsWindow.SetZoundsProjectDirty();
            }
        }

    }

}