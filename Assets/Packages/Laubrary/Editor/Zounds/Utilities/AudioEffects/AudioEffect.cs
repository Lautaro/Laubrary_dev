using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    public abstract class AudioEffect {

        public abstract string Name { get; }
        public abstract string ToggleLabel { get; }

        public abstract bool IsActive(Klip klip);
        public abstract bool IsEnabled(Klip klip);
        public abstract void SetEnabled(Klip klip, bool enabled);

        /// <summary>
        /// Process audio samples in-place or return a new array.
        /// The pipeline passes the same float[] through each active effect in order.
        /// </summary>
        public abstract float[] Process(float[] samples, int channels, int sampleRate, Klip klip);

        /// <summary>
        /// Draw the effect's editor UI inside a foldout body.
        /// Called by KlipEditorWindow when the foldout is visible.
        /// Return true if any value changed (caller handles undo/dirty).
        /// </summary>
        public virtual bool DrawUI(Klip klip, ref bool isDragging, AudioClip sourceClip) { return false; }
    }

}
