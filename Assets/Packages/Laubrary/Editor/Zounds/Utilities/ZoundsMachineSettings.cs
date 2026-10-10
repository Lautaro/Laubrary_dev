using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    /// <summary>
    /// Zounds settings that belong to this machine, not to the project (they are about how one person works): kept in
    /// Unity's preferences folder, changed through Undo like the project's own settings, and saved again after an undo
    /// or redo brings an old value back.
    /// </summary>
    [FilePath("Laubrary/ZoundsMachineSettings.asset", FilePathAttribute.Location.PreferencesFolder)]
    internal sealed class ZoundsMachineSettings : ScriptableSingleton<ZoundsMachineSettings> {

        /// <summary>How the mouse works on the Klip editor's waveform (owner, 2026-10-10).</summary>
        internal enum WaveMouse {
            /// <summary>Left click places the edit marker, left drag selects, double-click plays from the pointer,
            /// right-click plays from the pointer (inside a selection: that selection's menu).</summary>
            ClickSelects = 0,
            /// <summary>The upper half selects (left) and opens a selection's menu (right); the lower half plays from the
            /// pointer (either button).</summary>
            Halves = 1,
        }

        [SerializeField] int waveMouse;
        int savedWaveMouse = -1;

        internal static WaveMouse WaveMouseScheme => (WaveMouse)Mathf.Clamp(instance.waveMouse, 0, 1);

        /// <summary>Sets the waveform's mouse scheme as one Undo step, and saves it for this machine.</summary>
        internal static void SetWaveMouse(WaveMouse v) {
            var s = instance;
            if (s.waveMouse == (int)v) return;
            Undo.RecordObject(s, "change waveform mouse");
            s.waveMouse = (int)v;
            s.SaveIfChanged();
        }

        void SaveIfChanged() {
            if (savedWaveMouse == waveMouse) return;
            savedWaveMouse = waveMouse;
            Save(true);
        }

        void OnEnable() {
            savedWaveMouse = waveMouse;
            Undo.undoRedoPerformed -= SaveIfChanged;
            Undo.undoRedoPerformed += SaveIfChanged;
        }

        void OnDisable() { Undo.undoRedoPerformed -= SaveIfChanged; }
    }
}
