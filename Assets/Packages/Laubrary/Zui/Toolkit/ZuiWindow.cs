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

        /// Called before a rebuild clears the root — release anything holding element references.
        protected virtual void OnBeforeRebuild() { }

        public void CreateGUI()
        {
            Z.Attach(rootVisualElement);
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
            OnBeforeRebuild();
            rootVisualElement.Clear();
            BuildUI(rootVisualElement);
        }
    }
}
