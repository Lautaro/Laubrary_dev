using UnityEngine;
using ZuiRuntime;
using Laubrary.Choreographer;

/// Demo-only runtime UI (ZuiRuntime) to steer the demo in play mode: tune the boss/player wandering, toggle the
/// choreography debug paths, pause/resume. Collapsible so it doesn't hog the screen. Auto-finds the pieces.
public class ChoreoDemoControlUI : MonoBehaviour
{
    bool expanded = true;
    ChoreographyPlayer barrage;         // the player that has a launcher/target (the boss barrage)
    ChoreoDemoWander bossWander;
    ChoreoDemoWander playerWander;

    static readonly Color PanelBg = new(0.06f, 0.06f, 0.09f, 0.92f);

    void Start() => Refind();

    void Refind()
    {
        barrage = null;
        foreach (var p in Object.FindObjectsByType<ChoreographyPlayer>(FindObjectsSortMode.None))
            if (p.launcher != null || p.target != null) { barrage = p; break; }
        if (barrage == null) return;
        bossWander = barrage.launcher != null ? barrage.launcher.GetComponent<ChoreoDemoWander>() : null;
        playerWander = barrage.target != null ? barrage.target.GetComponent<ChoreoDemoWander>() : null;
    }

    void OnGUI()
    {
        if (!expanded)
        {
            var r = Zui.Panel(ZuiAnchor.TopLeft, 120f, 42f, PanelBg);
            var cs = new ZuiStack(r);
            if (cs.Button("▸ Controls")) expanded = true;
            return;
        }

        Rect content = Zui.Panel(ZuiAnchor.TopLeft, 250f, 320f, PanelBg);
        var s = new ZuiStack(content);
        if (s.Button("▾ Hide controls")) { expanded = false; return; }
        s.Space();

        if (barrage == null) Refind();

        s.Label("Boss (Launcher)", bold: true);
        WanderControls(ref s, bossWander);
        s.Space();
        s.Label("Player (Target)", bold: true);
        WanderControls(ref s, playerWander);
        s.Space();

        ChoreographyDebugView.GlobalEnabled = s.Toggle("Show choreo paths (debug)", ChoreographyDebugView.GlobalEnabled);
        if (barrage != null && s.Button(barrage.IsPlaying ? "Pause barrage" : "Resume barrage"))
        {
            if (barrage.IsPlaying) barrage.Pause(); else barrage.Play();
        }
    }

    static void WanderControls(ref ZuiStack s, ChoreoDemoWander w)
    {
        if (w == null) { s.Label("(none)"); return; }
        w.enabled = s.Toggle("Auto-wander", w.enabled);
        w.speed = s.Slider("Speed", w.speed, 0f, 10f);
        if (s.Button("Jump to random spot")) w.JumpNow();
    }
}
