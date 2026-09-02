// ZuiUndoGesture — one drag, one Undo step.
//
// A ZUI drag control raises its change callback on every pointer move, and a tool's callback records an Undo
// before each mutation. Correct, and unusable: a two-second drag on a slider leaves forty Undo steps, so
// Ctrl+Z after a mis-drag walks back through the drag frame by frame instead of undoing it.
//
// The fix belongs HERE rather than in each tool's Dial wrapper, for the same reason Z.Toggle's reroute does:
// a control knows where its gesture starts and ends, and a callback does not. Every ZUI drag control opens a
// gesture on pointer-down and closes it on pointer-up; everything recorded in between collapses into the one
// step the first record named. A gesture that recorded nothing collapses nothing, so a view-only control
// pays no cost for calling this.
using UnityEditor;

namespace Laubrary.Zui
{
    internal static class ZuiUndoGesture
    {
        /// Start a gesture. The returned group is what closes it; -1 means "nothing to close".
        internal static int Begin()
        {
            // Incrementing first is what stops the drag merging with whatever the user did before it — Unity
            // merges same-group records, and the previous action is not part of this drag.
            Undo.IncrementCurrentGroup();
            return Undo.GetCurrentGroup();
        }

        /// Close a gesture opened by <see cref="Begin"/>, collapsing everything it recorded into one step.
        internal static void End(int group)
        {
            if (group < 0) return;
            Undo.CollapseUndoOperations(group);
        }
    }
}
