using System;
using UnityEngine;

namespace Laubrary.Zoetrope.Editor
{
    /// The seam that lets a Zoetrope editor offer a "pick a Zound" browser without Zoetrope depending on
    /// Zounds at all.
    ///
    /// Zounds is a separate tool on a separate UI stack (still IMGUI), and deliberately stays that way for
    /// now — so this is the same optional-bridge shape PyreEditorLink / ChunkSpecEditorLink use: the consumer
    /// declares a hook, the bridge module fills it in via [InitializeOnLoad], and a project without Zounds
    /// simply never registers one and the field degrades to plain text.
    ///
    /// ⏳ Intended to be temporary. The stated destination is ONE LauAsset browser that can preview, browse
    /// and pick ANY LauAsset — with a Zound treated as a LauAsset like everything else. When that exists this
    /// hook should disappear rather than gain features: it is a bridge across a gap that is meant to close.
    public static class ZoundPickerHook
    {
        /// Set by the ZoetropeZounds editor bridge. (screenPosition, onPicked) → opens a browser.
        public static Action<Vector2, Action<string>> Show;

        /// Audition a Zound by name. Separate from Show because hearing one is a different act from choosing
        /// one — and picking a sound you cannot hear first is guessing.
        public static Action<string> Preview;

        public static bool Available => Show != null;
    }
}
