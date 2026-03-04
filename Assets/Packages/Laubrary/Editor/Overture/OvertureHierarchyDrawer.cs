using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using Laubrary.Overture;

namespace Laubrary.Overture.Editor
{

[InitializeOnLoad]
public static class OvertureHierarchyDrawer
{
    
    private static readonly Color managerColor = new Color(0.3f, 0.6f, 0.9f, 0.3f);
    private static readonly Color[] stateColors = new Color[]
    {
        new Color(0.8f, 0.4f, 0.4f, 0.3f),
        new Color(0.4f, 0.8f, 0.4f, 0.3f),
        new Color(0.9f, 0.7f, 0.3f, 0.3f),
        new Color(0.7f, 0.4f, 0.8f, 0.3f),
        new Color(0.4f, 0.8f, 0.8f, 0.3f),
        new Color(0.8f, 0.6f, 0.4f, 0.3f),
    };
    
    private static Dictionary<string, int> stateColorIndices = new Dictionary<string, int>();
    private static int nextColorIndex = 0;

    private static GUIStyle buttonStyle;
    private static GUIStyle labelStyle;
    
    static OvertureHierarchyDrawer()
    {
        EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
    }

    private static void OnHierarchyGUI(int instanceID, Rect selectionRect)
    {
        return; // Disabled for now
#pragma warning disable CS0618
        GameObject obj = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
        #pragma warning restore CS0618
        if (obj == null)
            return;

        OvertureManager manager = obj.GetComponent<OvertureManager>();
        OvertureState state = obj.GetComponent<OvertureState>();

        if (manager != null)
        {
            DrawManagerItem(obj, selectionRect);
        }
        else if (state != null)
        {
            DrawStateItem(obj, state, selectionRect);
        }
    }

    private static void DrawManagerItem(GameObject obj, Rect selectionRect)
    {
        InitializeStyles();

        Rect backgroundRect = new Rect(selectionRect.x, selectionRect.y, selectionRect.width + selectionRect.x, selectionRect.height);
        EditorGUI.DrawRect(backgroundRect, managerColor);

        Rect labelRect = new Rect(selectionRect.x + 18, selectionRect.y, selectionRect.width - 18, selectionRect.height);
        EditorGUI.LabelField(labelRect, obj.name, labelStyle);
    }

    private static void DrawStateItem(GameObject obj, OvertureState state, Rect selectionRect)
    {
        InitializeStyles();

        string statePath = GetGameObjectPath(obj);
        if (!stateColorIndices.ContainsKey(statePath))
        {
            stateColorIndices[statePath] = nextColorIndex;
            nextColorIndex = (nextColorIndex + 1) % stateColors.Length;
        }

        Color stateColor = stateColors[stateColorIndices[statePath]];
        
        Rect backgroundRect = new Rect(selectionRect.x, selectionRect.y, selectionRect.width + selectionRect.x, selectionRect.height);
        EditorGUI.DrawRect(backgroundRect, stateColor);

        Rect labelRect = new Rect(selectionRect.x + 18, selectionRect.y, selectionRect.width - 140, selectionRect.height);
        EditorGUI.LabelField(labelRect, obj.name, labelStyle);

        float buttonWidth = 65f;
        float buttonHeight = 16f;
        float spacing = 2f;
        float rightOffset = 5f;

        Rect simulateButtonRect = new Rect(
            selectionRect.xMax - (buttonWidth * 2 + spacing + rightOffset),
            selectionRect.y + (selectionRect.height - buttonHeight) * 0.5f,
            buttonWidth,
            buttonHeight
        );

        Rect transitionButtonRect = new Rect(
            selectionRect.xMax - (buttonWidth + rightOffset),
            selectionRect.y + (selectionRect.height - buttonHeight) * 0.5f,
            buttonWidth,
            buttonHeight
        );

        bool isPlayMode = Application.isPlaying;
        
        GUI.enabled = true;
        if (GUI.Button(simulateButtonRect, "Simulate", buttonStyle))
        {
            SimulateStateActivation(obj, state);
        }

        GUI.enabled = isPlayMode;
        if (GUI.Button(transitionButtonRect, "Transition", buttonStyle))
        {
            if (isPlayMode)
            {
                state.TransitionHere();
            }
        }
        GUI.enabled = true;
    }

    private static void SimulateStateActivation(GameObject stateObject, OvertureState state)
    {
        OvertureManager manager = stateObject.GetComponentInParent<OvertureManager>();
        if (manager == null)
        {
            manager = stateObject.GetComponent<OvertureManager>();
        }

        if (manager == null)
        {
            Debug.LogWarning($"Cannot simulate state '{stateObject.name}': No OvertureManager found.");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(manager.gameObject, "Simulate State Activation");

        OvertureState[] allStates = manager.GetComponentsInChildren<OvertureState>(true);
        
        foreach (OvertureState s in allStates)
        {
            if (s.transform.parent != null)
            {
                s.gameObject.SetActive(false);
                for (int i = 0; i < s.transform.childCount; i++)
                {
                    s.transform.GetChild(i).gameObject.SetActive(false);
                }
            }
        }

        List<OvertureState> pathToState = GetStatePathToRoot(state, manager.transform);
        pathToState.Reverse();

        foreach (OvertureState s in pathToState)
        {
            s.gameObject.SetActive(true);
            
            EnableStateGameObjects(s);
            
            IOvertureTransition[] transitions = s.GetComponents<IOvertureTransition>();
            foreach (IOvertureTransition transition in transitions)
            {
                if (transition.enabled && 
                    (transition.transitionType == TransitionType.Enter || transition.transitionType == TransitionType.Both))
                {
                    EnableTransitionTargets(transition);
                }
            }
        }

        EditorUtility.SetDirty(manager.gameObject);
        Debug.Log($"Simulated activation of state: {stateObject.name}");
    }

    private static void EnableStateGameObjects(OvertureState state)
    {
        SerializedObject serializedState = new SerializedObject(state);
        SerializedProperty enableOnEnterProp = serializedState.FindProperty("enableOnEnter");
        
        if (enableOnEnterProp != null)
        {
            for (int i = 0; i < enableOnEnterProp.arraySize; i++)
            {
                SerializedProperty element = enableOnEnterProp.GetArrayElementAtIndex(i);
                GameObject obj = element.objectReferenceValue as GameObject;
                if (obj != null)
                {
                    obj.SetActive(true);
                }
            }
        }
    }

    private static void EnableTransitionTargets(IOvertureTransition transition)
    {
        MonoBehaviour transitionMono = transition as MonoBehaviour;
        if (transitionMono == null)
            return;

        SerializedObject serializedTransition = new SerializedObject(transitionMono);
        SerializedProperty targetObjectsProp = serializedTransition.FindProperty("targetObjects");
        
        if (targetObjectsProp != null)
        {
            for (int i = 0; i < targetObjectsProp.arraySize; i++)
            {
                SerializedProperty element = targetObjectsProp.GetArrayElementAtIndex(i);
                GameObject obj = element.objectReferenceValue as GameObject;
                if (obj != null)
                {
                    obj.SetActive(true);
                }
            }
        }
    }

    private static List<OvertureState> GetStatePathToRoot(OvertureState state, Transform managerTransform)
    {
        List<OvertureState> path = new List<OvertureState>();
        OvertureState current = state;

        while (current != null && current.transform != managerTransform)
        {
            path.Add(current);
            Transform parent = current.transform.parent;
            current = (parent != null && parent != managerTransform) ? parent.GetComponent<OvertureState>() : null;
        }

        return path;
    }

    private static string GetGameObjectPath(GameObject obj)
    {
        string path = obj.name;
        Transform parent = obj.transform.parent;
        
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        
        return path;
    }

    private static void InitializeStyles()
    {
        if (buttonStyle == null)
        {
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 10,
                padding = new RectOffset(2, 2, 2, 2),
                margin = new RectOffset(0, 0, 0, 0)
            };
        }

        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Bold
            };
        }
    }
}

} // namespace Laubrary.Overture.Editor
