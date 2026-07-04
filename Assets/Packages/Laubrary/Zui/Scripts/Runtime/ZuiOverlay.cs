// Base class for hotkey-toggled debug/dev overlays — the shared ancestor for the DebugMenu-style
// tools every project keeps re-writing. Subclass, implement DrawOverlay() with ZuiRuntime calls,
// pick a toggle key in the Inspector. Key handling uses the IMGUI event stream, so it works
// regardless of which input backend (new/legacy) the project uses.

using UnityEngine;

namespace ZuiRuntime
{
    public abstract class ZuiOverlay : MonoBehaviour
    {
        [Tooltip("Key that shows/hides the overlay.")]
        public KeyCode toggleKey = KeyCode.F1;

        [Tooltip("Start visible?")]
        public bool visible;

        protected abstract void DrawOverlay();

        protected virtual void OnGUI()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == toggleKey)
            {
                visible = !visible;
                e.Use();
            }
            if (visible) DrawOverlay();
        }
    }
}
