using Laubrary.ZTracker;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class ZTrackerDemoControls : MonoBehaviour
{
    [Tooltip("The demo song player. Space starts or stops its explicitly requested playback.")]
    public ZTrackerPlayer player;

    void Update()
    {
        if (Keyboard.current == null || !Keyboard.current.spaceKey.wasPressedThisFrame || player == null) return;
        if (player.IsPlaying) player.Stop();
        else if (!player.Play()) Debug.LogWarning(player.LastError, this);
    }
}
