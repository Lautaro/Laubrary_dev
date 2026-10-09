using System;
using Laubrary.Zui;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The "Tell me" notice of destructive editing (2026-10-09): one line saying an edit went to a copy because the
    /// original is shared or protected, with "Edit the original instead" when that is possible, and a close button. It
    /// floats over its host (positioned by the host's sheet class), so showing or closing it never moves anything.
    /// </summary>
    internal sealed class SwapNoticeTK : VisualElement {

        readonly Label text;
        readonly Button original, close;
        Action editOriginal;

        public SwapNoticeTK() {
            AddToClassList("zs-swap-notice");
            pickingMode = PickingMode.Position;
            text = new Label();
            text.AddToClassList("zs-lbl"); text.AddToClassList("zs-swap-notice__text");
            original = ZS.Button("Edit the original instead", "Go back to the original and make this change there instead. Everything that uses the original then hears it. One Undo step.", "RichButton",
                () => { var a = editOriginal; Hide(); a?.Invoke(); }, ZUICornerMask.All, 150f, 16f);
            original.AddToClassList("zs-swap-notice__button");
            close = ZS.Button("×", "Close this notice. Settings > Protected edits decides whether these are shown.", "RichButton", Hide, ZUICornerMask.All, 18f, 16f);
            close.AddToClassList("zs-swap-notice__close");
            Add(text); Add(original); Add(close);
            style.display = DisplayStyle.None;
        }

        public bool Showing => style.display == DisplayStyle.Flex;

        /// <summary>Shows <paramref name="message"/>; <paramref name="onEditOriginal"/> null hides the escape button.</summary>
        public void Show(string message, Action onEditOriginal) {
            text.text = message; text.tooltip = message;
            editOriginal = onEditOriginal;
            original.style.display = onEditOriginal != null ? DisplayStyle.Flex : DisplayStyle.None;
            style.display = DisplayStyle.Flex;
            BringToFront();
        }

        public void Hide() { style.display = DisplayStyle.None; editOriginal = null; }
    }
}
