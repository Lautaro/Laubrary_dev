using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Launimator
{
    /// <summary>
    /// One version of a reel — either the editable <c>draft</c> (<see cref="versionNumber"/> 0) or an
    /// immutable committed snapshot (1, 2, …). Holds the ordered animations and the game-ready generated
    /// assets (prefab + controller) for that version.
    ///
    /// IMPORTANT: this type MUST live in its own file (filename == class name). Unity only mints a MonoScript
    /// for the class matching the file name; when ReelVersion shared Reel.cs it had no MonoScript,
    /// so its assets serialized with <c>m_Script {fileID: 0}</c> (type resolved only via m_EditorClassIdentifier).
    /// Such script-less ScriptableObjects (a) are invisible to <c>FindAssets("t:ReelVersion")</c>, (b) become
    /// UNLOADABLE if their asset file is renamed, and (c) LOSE their serialized data (recipes!) when a dirty
    /// instance survives a domain reload. Keeping it here gives every version asset a real m_Script and avoids all
    /// three. (See ReelRepo for the historical workarounds.)
    /// </summary>
    public class ReelVersion : ScriptableObject
    {
        [Tooltip("0 = the editable draft; 1, 2, … = immutable committed snapshots.")]
        public int versionNumber;

        [Tooltip("UTC timestamp (round-trip 'o' format) this version was created/last rebuilt.")]
        public string createdUtc;

        [Tooltip("The animations that make up this reel version, in display/build order.")]
        public List<AnimationDef> animations = new List<AnimationDef>();

        [Tooltip("Generated SpriteRenderer + Animator prefab for this version.")]
        public GameObject prefab;

        [Tooltip("Generated AnimatorController (one state per animation) for this version.")]
        public RuntimeAnimatorController controller;
    }
}
