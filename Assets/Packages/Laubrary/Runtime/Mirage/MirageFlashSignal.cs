#if UNITY_EDITOR
using System;

namespace Laubrary.Mirage
{
    /// <summary>
    /// Editor-only bridge: MirageWindow's Ping button calls <see cref="Flash"/> for an entry id; MirageHud
    /// subscribes and draws a pulsing outline around that entry's LIVE rendered sprite in the actual Game
    /// View — the thing Mirage cares about, unlike Unity's native Ping (which only flashes the Project
    /// window / Hierarchy row, neither of which is where you're actually looking while composing a view).
    /// </summary>
    public static class MirageFlashSignal
    {
        public static event Action<string> Requested;
        public static void Flash(string entryId) => Requested?.Invoke(entryId);
    }
}
#endif
