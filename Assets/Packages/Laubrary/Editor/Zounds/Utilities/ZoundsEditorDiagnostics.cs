using UnityEngine;

namespace Laubrary.Zounds {

    /// <summary>
    /// Deliberate no-op placeholder, kept only so the chain editor UI (ported from another project's
    /// Zounds tooling) compiles and behaves exactly like the original without rewriting its call sites.
    /// In the source project this fed a separate layout-debugging overlay that let a developer see
    /// exactly which on-screen rectangle every drawn row/control occupied, which does not exist here.
    /// Stripping the calls instead would mean touching every one of the many small rows the chain
    /// editor draws just to satisfy the compiler, for no behavioural benefit.
    ///
    /// If a real layout-debugging view is ever wanted in this project, give <see cref="Record"/> and
    /// <see cref="BeginChainEditor"/> a real implementation (e.g. stash the rects in a list a debug
    /// window can draw over the editor) — the chain editor code does not need to change again.
    /// </summary>
    internal static class ZoundsEditorDiagnostics {

        /// <summary>Bumped once per repaint of the chain editor. Not read anywhere yet; free to wire up later.</summary>
        public static int chainEditorRepaints;

        /// <summary>Marks the start of one chain editor layout pass. No-op placeholder — see class summary.</summary>
        public static void BeginChainEditor(Rect windowRect) { }

        /// <summary>Records the rect a labelled piece of the chain editor was drawn at. No-op placeholder — see class summary.</summary>
        public static void Record(string label, Rect rect) { }
    }

}
