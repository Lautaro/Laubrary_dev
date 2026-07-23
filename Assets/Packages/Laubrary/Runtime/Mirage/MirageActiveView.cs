#if UNITY_EDITOR
using System;

namespace Laubrary.Mirage
{
    /// <summary>
    /// Editor-only bridge: MirageWindow calls <see cref="Set"/> whenever the browser's selected
    /// <see cref="MirageView"/> changes; any live <see cref="MirageRig"/> subscribes to <see cref="Changed"/>
    /// so switching the browser's selection hot-swaps the scene instantly — in Edit mode or Play mode —
    /// instead of only taking effect on the rig's next OnEnable.
    /// </summary>
    public static class MirageActiveView
    {
        public static event Action<MirageView> Changed;
        public static void Set(MirageView view) => Changed?.Invoke(view);
    }
}
#endif
