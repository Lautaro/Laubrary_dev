// A plain host window for a surface that has no window of its own (AHQ T-0551).
//
// Cabinets is a component inspector, not a tool window. Unity's own floating property editor would not build
// its contents for a throwaway fixture object in a scripted run, so the inspector's own element tree is hosted
// here instead. That is still the real surface under test: this window asks the actual custom editor for the
// element tree it would give the Inspector, and then measures and photographs exactly that tree.
//
// The host adds no styling of its own beyond the editor-default background, so anything measured here is the
// surface's own presentation.

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.UISeparationPhase3 {

    public sealed class Phase3SurfaceWindow : EditorWindow {

        private Editor _editor;

        public static Phase3SurfaceWindow Host(Object target, float width, float height) {
            foreach (var existing in Resources.FindObjectsOfTypeAll<Phase3SurfaceWindow>()) existing.Close();
            var window = CreateInstance<Phase3SurfaceWindow>();
            window.titleContent = new GUIContent("Phase3 Surface");
            window.ShowUtility();
            window.position = new Rect(120f, 120f, width, height);
            window.Build(target);
            return window;
        }

        private void Build(Object target) {
            rootVisualElement.Clear();
            _editor = Editor.CreateEditor(target);
            VisualElement surface = _editor.CreateInspectorGUI();
            if (surface == null) {
                // The editor draws with IMGUI; host that instead so the window is still a real surface.
                surface = new IMGUIContainer(() => _editor.OnInspectorGUI());
            }
            surface.name = "phase3-surface";
            rootVisualElement.Add(surface);
        }

        private void OnDisable() {
            if (_editor != null) DestroyImmediate(_editor);
            _editor = null;
        }
    }
}
