using UnityEditor;
using UnityEditor.Callbacks;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        // Double-clicking a song asset in the Project window opens it here, ready to read and play.
        [OnOpenAsset]
        static bool OpenSongAsset(int instanceId, int line)
        {
            if (!(EditorUtility.InstanceIDToObject(instanceId) is ZTrackerSong opened)) return false;
            var window = GetWindow<ZTrackerModelWindow>("ZTracker");
            if (window.song != opened)
            {
                window.StopPreview(); window.song = opened; window.order = window.row = window.track = window.sub = 0;
                window.Upgrade(); window.Rebuild();
            }
            window.Focus();
            return true;
        }
    }
}
