using UnityEngine;
using ZuiRuntime;
using Laubrary.Choreographer;

/// Demo-only runtime UI (built with ZuiRuntime) to steer the barrage demo in play mode: toggle and tune the
/// boss (Launcher) and player (Target) wandering, and the choreography's freeze/pause. Auto-finds the pieces,
/// so it needs no wiring — drop it on any GameObject in the scene.
public class ChoreoDemoControlUI : MonoBehaviour
{
    ChoreographyPlayer player;
    ChoreoDemoWander bossWander;    // on the Launcher
    ChoreoDemoWander playerWander;  // on the Target

    void Start() => Refind();

    void Refind()
    {
        player = Object.FindFirstObjectByType<ChoreographyPlayer>();
        if (player == null) return;
        bossWander = player.launcher != null ? player.launcher.GetComponent<ChoreoDemoWander>() : null;
        playerWander = player.target != null ? player.target.GetComponent<ChoreoDemoWander>() : null;
    }

    void OnGUI()
    {
        if (player == null) { Refind(); if (player == null) return; }

        Rect content = Zui.Panel(ZuiAnchor.TopLeft, 250f, 300f, new Color(0.06f, 0.06f, 0.09f, 0.92f));
        var s = new ZuiStack(content);
        s.Header("Barrage Controls");

        s.Label("Boss (Launcher)", bold: true);
        WanderControls(ref s, bossWander);
        s.Space();

        s.Label("Player (Target)", bold: true);
        WanderControls(ref s, playerWander);
        s.Space();

        player.freezeAnchors = s.Toggle("Freeze anchors", player.freezeAnchors);
        if (s.Button(player.IsPlaying ? "Pause choreography" : "Resume choreography"))
        {
            if (player.IsPlaying) player.Pause(); else player.Play();
        }
    }

    static void WanderControls(ref ZuiStack s, ChoreoDemoWander w)
    {
        if (w == null) { s.Label("(no wander found)"); return; }
        w.enabled = s.Toggle("Auto-wander", w.enabled);
        w.speed = s.Slider("Speed", w.speed, 0f, 10f);
        if (s.Button("Jump to random spot")) w.JumpNow();
    }
}
