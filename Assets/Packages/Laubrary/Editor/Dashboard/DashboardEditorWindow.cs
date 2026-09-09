using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;
using Attribute = System.Attribute;
using Debug = UnityEngine.Debug;

namespace Laubrary.Dashboards
{
    public partial class DashboardEditorWindow : EditorWindow
    {
        bool isUpdating = false;
        private Vector2 scrollPosition;
        List<TrackedField> trackedFields = new();
        private List<Type> trackedClassTypes = new();
        private List<MonoBehaviour> trackedInstances = new();
        private Dictionary<string, List<WindowLog>> windowLogEntries = new();
        EditorCoroutine updateInstancesCoroutine;
        bool isEven;
        private bool printUpdateTimer;

        [MenuItem("Laubrary/Dashboard")]
        private static void Init()
        {
            DashboardEditorWindow window = (DashboardEditorWindow)GetWindow(typeof(DashboardEditorWindow), false, "Dashboard");
            window.Show();
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        public void OnEnable()
        {
            Reset();
            Repaint();
        }

        private void Reset()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            
            trackedInstances.Clear();
            trackedFields.Clear();
            windowLogEntries.Clear();
            trackedClassTypes.Clear();
            Dashboard.logs.Clear();

            if (updateInstancesCoroutine != null)
            {
                EditorCoroutineUtility.StopCoroutine(updateInstancesCoroutine);
                updateInstancesCoroutine = null;
            }

            isUpdating = false;


            MonoBehaviour[] allMonoBehaviours = FindObjectsOfType<MonoBehaviour>();

            var allMonoBehaviourTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type.IsSubclassOf(typeof(MonoBehaviour)));

            foreach (var type in allMonoBehaviourTypes)
            {
                // Check each field and property in the type for the custom attribute
                var members = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(member => member.MemberType == MemberTypes.Field || member.MemberType == MemberTypes.Property);

                foreach (var member in members)
                {
                    if (Attribute.IsDefined(member, typeof(DashboardAttribute)))
                    {
                        trackedClassTypes.Add(type);
                        break;  // No need to check further members if one is found
                    }
                }
            }
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode || change == PlayModeStateChange.EnteredEditMode ||
                change == PlayModeStateChange.ExitingPlayMode || change == PlayModeStateChange.ExitingEditMode)
            {
                Reset();
            }
        }

        private void OnGUI()
        {
            isEven = false;
            var dashboardLogs = Dashboard.logs;
            windowLogEntries.Clear();
            List<MonoBehaviour> allOwners = dashboardLogs.Where(log => log.owner != null).Select(log => log.owner)
                                                   .Concat(trackedFields.Select(tf => tf.owner))
                                                   .Distinct()
                                                   .ToList();
            // PAINT OWNED DASHBOARD LOGS
            foreach (var owner in allOwners)
            {
                var windowLogList = new List<WindowLog>();

                var matchingTrackedFields = trackedFields.Where(tf => tf.owner == owner);
                foreach (var field in matchingTrackedFields)
                {
                    var title = field.logTitle.ToUpper();

                    if (string.IsNullOrEmpty(title))
                        title = field.fieldInfo.Name;

                    var text = $"[{title}] {field?.currentValue.ToString()}";
                    windowLogList.Add(new WindowLog() { text = text, textColor = field.textColor, textSize = field.textSize });
                }

                var matchingDashboardLogs = Dashboard.logs.Where(dl => dl.owner == owner).ToList();

                foreach (var dashboardLog in matchingDashboardLogs)
                {
                    var title = $"[{dashboardLog.id}]";
                    if (string.IsNullOrEmpty(dashboardLog.id))
                        title = "";

                    var fullText = $"{title} {dashboardLog.text}";
                    windowLogList.Add(new WindowLog() { text = fullText, textColor = dashboardLog.textColor, textSize = dashboardLog.textSize });
                }

                windowLogEntries.Add(owner.name, windowLogList);
            }

            // PAINT OWNERLESS DASHBOARD LOGS
            var ownerLessDashboardlogs = dashboardLogs.Where(log => log.owner == null);

            var ownerlessLogList = new List<WindowLog>();
            foreach (var ownerlessLog in ownerLessDashboardlogs)
            {
                ownerlessLogList.Add(new WindowLog() { text = ownerlessLog.text, textColor = ownerlessLog.textColor, textSize = ownerlessLog.textSize });
            }
            if (ownerLessDashboardlogs.Count() > 0 )
                windowLogEntries.Add("[DASHBOARD]", ownerlessLogList);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            if (windowLogEntries.Count == 0)
                GUILayout.Label("Nothing is being tracked. Mark a MonoBehaviour field with [Dashboard] " +
                                "or call Dashboard.Log, and it appears here.", EditorStyles.wordWrappedMiniLabel);
            foreach (var windowLog in windowLogEntries)
            {
                var owner = windowLog.Key;
                var windowLogList = windowLog.Value;
                var ownerName = $"{owner}";

                EditorGUILayout.BeginVertical(GroupBoxStyle);
                GUILayout.Label(ownerName, style: OwnerStyle);
                DrawHorizontalLine(Color.gray, 1f);


                foreach (var entry in windowLogList)
                {
                    var style = ValueStyle;
                    style.fontSize = entry.textSize;
                    style.normal.textColor = entry.textColor;
                    GUILayout.Label(entry.text, style: style);
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(15);

                isEven = !isEven;
            }

            EditorGUILayout.EndScrollView();
            if (isUpdating == false)
            {
                isUpdating = true;
                updateInstancesCoroutine = EditorCoroutineUtility.StartCoroutineOwnerless(UpdateInstances());
            }

            Repaint();
        }

        private IEnumerator UpdateInstances()
        {
            Stopwatch watch = new Stopwatch();
            watch.Start();
            var tempTrackedInstance = new List<MonoBehaviour>();
            var tempTrackedFields = new List<TrackedField>();
            foreach (var type in trackedClassTypes)
            {
                if (type.IsSubclassOf(typeof(MonoBehaviour)))
                {
                    // Yield return null will wait for the next frame before continuing
                    // This can help keep the editor responsive if you have a lot of types to process
                    watch.Stop();

                    if (printUpdateTimer)
                        Debug.Log(watch.ElapsedMilliseconds);

                    yield return null;

                    MonoBehaviour[] instances = (MonoBehaviour[])FindObjectsOfType(type);
                    foreach (var instance in instances)
                    {
                        FieldInfo[] fieldInfos = instance.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                        foreach (FieldInfo field in fieldInfos)
                        {
                            DashboardAttribute attribute = field.GetCustomAttribute<DashboardAttribute>();
                            if (attribute != null)
                            {
                                tempTrackedFields.Add(new TrackedField(instance, field, attribute.dashboardName, attribute.textSize, attribute.textColor));
                                tempTrackedInstance.Add(instance);
                            }
                        }
                    }
                }
            }

            trackedInstances = tempTrackedInstance;
            trackedFields = tempTrackedFields;
            isUpdating = false;
        }

        void DrawHorizontalLine(Color color, float height = 1.0f)
        {
            lineStyle ??= new GUIStyle
            {
                normal = { background = EditorGUIUtility.whiteTexture },
                margin = new RectOffset(0, 0, 4, 4)
            };
            lineStyle.fixedHeight = height;

            var c = GUI.color;
            GUI.color = color;
            GUILayout.Box(GUIContent.none, lineStyle);
            GUI.color = c;
        }

        // OnGUI repaints unconditionally (it ends with Repaint()), so anything built per pass is built
        // dozens of times a second for as long as the window is open. The group background used to be a
        // fresh 2x2 Texture2D on every group on every pass, and a Texture2D is an engine object nothing
        // ever released: 3046 of them accumulated over 60 repaints of a single log line. Styles and the
        // texture are therefore built once and reused; the texture is HideAndDontSave so it survives
        // repaints without being saved into anything.
        GUIStyle ownerStyle, boxStyle, lineStyle, entryStyle;
        Texture2D groupBg;

        GUIStyle ValueStyle => entryStyle ??= new GUIStyle(GUI.skin.label);

        GUIStyle OwnerStyle => ownerStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            fontSize = 14
        };

        GUIStyle GroupBoxStyle
        {
            get
            {
                if (groupBg == null)
                {
                    groupBg = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                    var pix = new Color[4];
                    for (int i = 0; i < pix.Length; ++i) pix[i] = Color.gray;
                    groupBg.SetPixels(pix);
                    groupBg.Apply();
                    boxStyle = null;
                }
                return boxStyle ??= new GUIStyle(GUI.skin.box) { normal = { background = groupBg } };
            }
        }

        void OnDestroy()
        {
            if (groupBg != null) DestroyImmediate(groupBg);
            groupBg = null;
        }
    }
}