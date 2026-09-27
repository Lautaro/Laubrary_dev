using UnityEngine;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds {

    /// <summary>
    /// The panic control pinned to the top-right corner of every Zounds window: one click hard-stops
    /// everything the engine is playing. Drawn last, over the window content, so it is never hidden
    /// behind a scroll view or a fold; rows that reach the top-right corner leave
    /// <see cref="ReservedWidth"/> free for it.
    ///
    /// Adaptation from the source project: that project's engine ran every voice through a native audio
    /// plugin, so its single "kill everything" call reached into that plugin. This project's engine has
    /// no native plugin — voices are either the managed token/AudioSource pipeline or the native-DSP-free
    /// SAP voice pipeline — so this button instead calls both of this project's own "stop everything"
    /// entry points: <see cref="SapVoiceRegistry.StopAll"/> (asks every currently playing chain voice to
    /// stop) and <see cref="ZoundEngine.StopAllZounds"/> (kills tokens and flushes the AudioSource pool),
    /// so a runaway sound is stopped regardless of which pipeline is playing it.
    /// </summary>
    public static class ZoundsStopAllButton {

        public const float Width = 62f;
        public const float Height = 18f;
        public const float Margin = 2f;
        public static float ReservedWidth => Width + Margin * 2f;

        private static readonly GUIContent content = new GUIContent("Stop All",
            "Hard-stops every playing zound right now: kills all tokens, flushes the AudioSource pool, and stops every DSP voice. For runaway audio.");

        /// <summary>Draws the button in the window's top-right corner (window-local coordinates).</summary>
        public static void Draw(Rect windowRect, float y = Margin) {
            var rect = new Rect(windowRect.width - Width - Margin, y, Width, Height);
            if (ZUI.Button(rect, content, ZUI.Style.RichButton, ZUI.Tint.Danger, ZUICornerMask.All)) Click();
        }

        /// <summary>What the button does; also callable from tests.</summary>
        public static void Click() {
            SapVoiceRegistry.StopAll();
            ZoundEngine.StopAllZounds(true);
        }
    }

}
