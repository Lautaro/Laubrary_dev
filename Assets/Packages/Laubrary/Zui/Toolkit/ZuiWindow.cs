// ZuiWindow — base EditorWindow for the UI Toolkit half of ZUI. The retained-mode counterpart of
// the IMGUI ZUIWindow (which stays fully working for not-yet-migrated tools).
//
// Subclasses override BuildUI(root) once — no per-frame draw loop. For sections whose CONTENT
// depends on mutable data (an edited asset, an undo-able model), build them via a rebuildable
// container and call Rebuild() when the data changes; this base already triggers Rebuild() on
// undo/redo so retained controls never show stale values after Ctrl+Z.
using UnityEditor;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public abstract class ZuiWindow : EditorWindow
    {
        /// Build the window content. Called once per CreateGUI and again on every Rebuild().
        protected abstract void BuildUI(VisualElement root);
        /// Stable styling scope on both the empty browser and populated editor.
        protected virtual string PresentationTool => GetType().Name.Replace("Window", "").ToLowerInvariant();

        /// Called before a rebuild clears the root — release anything holding element references.
        protected virtual void OnBeforeRebuild() { }
        int _rebuildVersion;

        public void CreateGUI()
        {
            Z.Attach(rootVisualElement);
            Z.AttachTool(rootVisualElement, PresentationTool);
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
            Rebuild();
        }

        protected virtual void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        void OnUndoRedo() => Rebuild();

        /// Rebuild the whole window from the current data. Cheap at editor-window scale, and the
        /// one reliable way to guarantee no retained control is left showing a stale value.
        protected void Rebuild()
        {
            var scrolls = rootVisualElement.Query<ScrollView>().ToList();
            var offsets = new UnityEngine.Vector2[scrolls.Count];
            for (int i = 0; i < scrolls.Count; i++) offsets[i] = scrolls[i].scrollOffset;
            var focused = rootVisualElement.panel?.focusController?.focusedElement as VisualElement;
            string focusKey = null;
            var focusPath = new System.Collections.Generic.List<int>();
            for (var node = focused; node != null; node = node.parent)
            {
                if (node.ClassListContains("zui-generated-field") && !string.IsNullOrEmpty(node.name)) { focusKey = node.name; break; }
                if (node.parent != null) focusPath.Add(node.parent.hierarchy.IndexOf(node));
            }
            OnBeforeRebuild();
            rootVisualElement.Clear();
            BuildUI(rootVisualElement);
            int version = ++_rebuildVersion;
            rootVisualElement.schedule.Execute(() =>
            {
                if (version != _rebuildVersion) return;
                var rebuilt = rootVisualElement.Query<ScrollView>().ToList();
                for (int i = 0; i < offsets.Length && i < rebuilt.Count; i++) rebuilt[i].scrollOffset = offsets[i];
                if (focusKey != null)
                {
                    var field = rootVisualElement.Q(focusKey);
                    var input = field;
                    for (int i = focusPath.Count - 1; input != null && i >= 0; i--)
                        input = focusPath[i] >= 0 && focusPath[i] < input.hierarchy.childCount ? input.hierarchy[focusPath[i]] : null;
                    (input ?? field)?.Focus();
                }
            }).StartingIn(20);
        }
    }
}
