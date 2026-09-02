using System;
using UnityEngine;

namespace Laubrary.Shaper.Editor
{
    /// The seam that lets Shaper's cherry-framing panel offer a "pick a Zound" browser without Shaper
    /// depending on Zounds at all -- the same optional-bridge shape ChunkZoundPickerHook / ZoundPickerHook
    /// (Zoetrope) use. Deliberately a COPY rather than a reference to either: a bridge is named for the two
    /// systems it joins, and Shaper reaching through Zoetrope or Chunks to get to Zounds would make it depend
    /// on a tool that has nothing to do with Zounds. The bridge that fills this in is Editor/ShaperZounds.
    ///
    /// ⏳ Intended to be temporary, for the same reason the other two hooks are: the stated destination is ONE
    /// LauAsset browser that can preview, browse and pick ANY LauAsset, Zounds included. When that exists
    /// every one of these hooks should disappear rather than gain features.
    ///
    /// ⚠️ When <see cref="Available"/> is false the UI must SAY the picker is unavailable -- it must NOT fall
    /// back to a text field. A typed Zound name compiles, saves and looks authored, then silently plays
    /// nothing; that failure mode is precisely what the picker exists to close (ui-layout-rules: "degrading
    /// to a text field when the option list is empty is the failure mode, not the graceful fallback").
    public static class ShaperZoundPickerHook
    {
        /// Set by the ShaperZounds editor bridge. (screenPosition, onPicked) → opens a browser.
        public static Action<Vector2, Action<string>> Show;

        /// Audition a Zound by name. Separate from Show because hearing one is a different act from choosing
        /// one -- and picking a sound you cannot hear first is guessing.
        public static Action<string> Preview;

        public static bool Available => Show != null;
    }
}
