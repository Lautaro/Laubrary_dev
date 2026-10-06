using Unity.Burst;

namespace Laubrary.ZTracker
{
    public static class ZTrackerCapability
    {
        public static bool IsSupportedPlatform => BurstCompiler.IsEnabled;
        public static string Description => "Requires Unity 6000.3 SAP and enabled Burst. Tested evidence: Windows x64; other targets require verification.";
        internal static bool CheckPlayback(out string error)
        {
            error = IsSupportedPlatform ? null : "Enable Burst before tracker playback. Managed audio rendering is not a fallback.";
            return error == null;
        }
    }
}
