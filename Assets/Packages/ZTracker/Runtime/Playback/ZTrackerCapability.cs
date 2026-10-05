using System;
using UnityEngine;

namespace Laubrary.ZTracker
{
    public static class ZTrackerCapability
    {
        public static bool IsSupportedPlatform => IntPtr.Size == 8 &&
            (Application.platform == RuntimePlatform.WindowsEditor ||
             Application.platform == RuntimePlatform.WindowsPlayer);

        // A passive capability read never attempts to load the native library.
        public static string Description => IsSupportedPlatform
            ? "Windows x64; native availability is checked when Play is requested."
            : "Playback requires Windows x64. Song data remains available.";

        internal static bool CheckNative(out string error)
        {
            error = null;
            if (!IsSupportedPlatform) { error = Description; return false; }
            try
            {
                if (ZTrackerNative.ZT_GetABIVersion() == 1) return true;
                error = "The tracker native library has an incompatible version.";
            }
            catch (DllNotFoundException) { error = "The Windows x64 tracker native library is missing."; }
            catch (EntryPointNotFoundException) { error = "The tracker native library needs to be rebuilt for this add-on."; }
            catch (BadImageFormatException) { error = "The tracker native library is not Windows x64."; }
            return false;
        }
    }
}
