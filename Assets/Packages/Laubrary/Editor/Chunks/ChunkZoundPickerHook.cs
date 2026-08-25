using System;
using UnityEngine;

namespace Laubrary.Chunks.Editor
{
    /// The seam that lets the Chunks timeline offer a "pick a Zound" browser without Chunks depending on
    /// Zounds at all — the editor twin of the runtime ChunkZoundHook (which plays one).
    ///
    /// Deliberately a COPY of Zoetrope's ZoundPickerHook rather than a reference to it: a bridge is named for
    /// the two systems it joins, and Chunks reaching through Zoetrope to get to Zounds would make Chunks
    /// depend on Zoetrope for a reason that has nothing to do with Zoetrope. The bridge assembly that fills
    /// this in is Editor/ChunksZounds, mirroring Editor/ZoetropeZounds exactly.
    ///
    /// ⏳ Intended to be temporary, for the same reason ZoundPickerHook is: the stated destination is ONE
    /// LauAsset browser that can preview, browse and pick ANY LauAsset, Zounds included. When that exists
    /// both hooks should disappear rather than gain features.
    ///
    /// ⚠️ When <see cref="Available"/> is false the UI must SAY the picker is unavailable — it must NOT fall
    /// back to a text field. A typed Zound name compiles, saves and looks authored, then silently plays
    /// nothing; that failure mode is precisely what the picker exists to close (ui-layout-rules: "degrading
    /// to a text field when the option list is empty is the failure mode, not the graceful fallback").
    public static class ChunkZoundPickerHook
    {
        /// Set by the ChunksZounds editor bridge. (screenPosition, onPicked) → opens a browser.
        public static Action<Vector2, Action<string>> Show;

        /// Audition a Zound by name. Separate from Show because hearing one is a different act from choosing
        /// one — and picking a sound you cannot hear first is guessing.
        public static Action<string> Preview;

        public static bool Available => Show != null;
    }
}
