using System.Collections.Generic;
using UnityEngine;
using ZuiRuntime;
using Laubrary.Choreographer;

/// Demo-only runtime UI (ZuiRuntime): toggle the boss and each enemy wave, set missile/swarm count & size, tune
/// the player weapon and wandering, toggle debug paths, pause. All settings persist across play sessions
/// (PlayerPrefs). Collapsible. Auto-finds everything.
public class ChoreoDemoControlUI : MonoBehaviour
{
    bool expanded = true;
    bool loaded;
    DemoWave barrage;               // boss barrage (its player has a launcher)
    GameObject bossGO;
    ChoreoDemoWander bossWander, playerWander;
    DemoPlayerWeapon weapon;
    readonly List<DemoWave> enemyWaves = new();

    static readonly Color PanelBg = new(0.06f, 0.06f, 0.09f, 0.93f);
    const string P = "choreoDemo.";

    void Start() => Refind();

    void Refind()
    {
        barrage = null; bossGO = null; enemyWaves.Clear();
        foreach (var w in Object.FindObjectsByType<DemoWave>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (w.player != null && w.player.launcher != null) { barrage = w; bossGO = w.player.launcher.gameObject; }
            else enemyWaves.Add(w);
        }
        if (barrage != null)
        {
            bossWander = bossGO != null ? bossGO.GetComponent<ChoreoDemoWander>() : null;
            var playerGO = barrage.player.target;
            playerWander = playerGO != null ? playerGO.GetComponent<ChoreoDemoWander>() : null;
            weapon = playerGO != null ? playerGO.GetComponent<DemoPlayerWeapon>() : null;
        }
    }

    // ── persistence helpers ──────────────────────────────────────────────────
    static float GF(string k, float d) => PlayerPrefs.GetFloat(P + k, d);
    static void SF(string k, float v) { PlayerPrefs.SetFloat(P + k, v); }
    static int GI(string k, int d) => PlayerPrefs.GetInt(P + k, d);
    static void SI(string k, int v) { PlayerPrefs.SetInt(P + k, v); }

    void LoadSettings()
    {
        ChoreographyDebugView.GlobalEnabled = GI("debug", ChoreographyDebugView.GlobalEnabled ? 1 : 0) == 1;
        if (barrage != null)
        {
            barrage.SetCount(GI("missiles", barrage.Count));
            barrage.SetScale(GF("missileSize", barrage.scale));
            if (bossGO != null) { bool on = GI("bossActive", bossGO.activeSelf ? 1 : 0) == 1; bossGO.SetActive(on); barrage.gameObject.SetActive(on); }
        }
        if (bossWander != null) bossWander.speed = GF("bossSpeed", bossWander.speed);
        if (playerWander != null) playerWander.speed = GF("playerSpeed", playerWander.speed);
        if (weapon != null)
        {
            weapon.fireInterval = GF("fireInterval", weapon.fireInterval);
            weapon.intervalRandomness = GF("fireRandom", weapon.intervalRandomness);
            weapon.bulletSpeed = GF("bulletSpeed", weapon.bulletSpeed);
            weapon.bulletScale = GF("bulletSize", weapon.bulletScale);
        }
        foreach (var w in enemyWaves)
        {
            if (w == null) continue;
            string k = "wave." + w.gameObject.name;
            bool on = GI(k + ".active", w.gameObject.activeSelf ? 1 : 0) == 1;
            w.gameObject.SetActive(on);
            if (on) w.SetCount(GI(k + ".count", w.Count));
        }
    }

    // ── UI ───────────────────────────────────────────────────────────────────
    void OnGUI()
    {
        if (!expanded)
        {
            var rc = Zui.Panel(ZuiAnchor.TopLeft, 120f, 42f, PanelBg);
            var cs = new ZuiStack(rc);
            if (cs.Button("▸ Controls")) expanded = true;
            return;
        }

        Rect content = Zui.Panel(ZuiAnchor.TopLeft, 260f, 640f, PanelBg);
        var s = new ZuiStack(content);
        if (s.Button("▾ Hide controls")) { expanded = false; return; }
        if (barrage == null) Refind();
        if (barrage == null) { s.Label("(no demo found)"); return; }
        if (!loaded) { LoadSettings(); loaded = true; }

        s.Label("Boss", bold: true);
        if (bossGO != null)
        {
            bool on = bossGO.activeSelf;
            bool now = s.Toggle("Boss active", on);
            if (now != on) { bossGO.SetActive(now); barrage.gameObject.SetActive(now); SI("bossActive", now ? 1 : 0); }
        }
        int mc = Mathf.RoundToInt(s.Slider("Missiles", barrage.Count, 0f, 40f));
        if (mc != barrage.Count) { barrage.SetCount(mc); SI("missiles", mc); }
        float ms = s.Slider("Missile size", barrage.scale, 0.1f, 1.5f);
        if (Mathf.Abs(ms - barrage.scale) > 0.001f) { barrage.SetScale(ms); SF("missileSize", ms); }
        WanderSlider(ref s, "Boss speed", "bossSpeed", bossWander);
        s.Space();

        s.Label("Player", bold: true);
        WanderSlider(ref s, "Player speed", "playerSpeed", playerWander);
        if (weapon != null)
        {
            weapon.fireInterval = PSlider(ref s, "Fire interval", "fireInterval", weapon.fireInterval, 0.05f, 2f);
            weapon.intervalRandomness = PSlider(ref s, "Fire randomness", "fireRandom", weapon.intervalRandomness, 0f, 1f);
            weapon.bulletSpeed = PSlider(ref s, "Bullet speed", "bulletSpeed", weapon.bulletSpeed, 2f, 30f);
            weapon.bulletScale = PSlider(ref s, "Bullet size", "bulletSize", weapon.bulletScale, 0.1f, 1f);
        }
        s.Space();

        s.Label("Enemy waves", bold: true);
        foreach (var w in enemyWaves)
        {
            if (w == null) continue;
            string k = "wave." + w.gameObject.name;
            bool on = w.gameObject.activeSelf;
            bool now = s.Toggle(w.gameObject.name, on);
            if (now != on) { w.gameObject.SetActive(now); SI(k + ".active", now ? 1 : 0); }
            if (now)
            {
                int c = Mathf.RoundToInt(s.Slider("  swarm size", w.Count, 0f, 30f));
                if (c != w.Count) { w.SetCount(c); SI(k + ".count", c); }
            }
        }
        s.Space();

        bool dbg = s.Toggle("Show choreo paths (debug)", ChoreographyDebugView.GlobalEnabled);
        if (dbg != ChoreographyDebugView.GlobalEnabled) { ChoreographyDebugView.GlobalEnabled = dbg; SI("debug", dbg ? 1 : 0); }
        if (s.Button(barrage.player.IsPlaying ? "Pause barrage" : "Resume barrage"))
        {
            if (barrage.player.IsPlaying) barrage.player.Pause(); else barrage.player.Play();
        }
    }

    static float PSlider(ref ZuiStack s, string label, string key, float value, float min, float max)
    {
        float nv = s.Slider(label, value, min, max);
        if (!Mathf.Approximately(nv, value)) SF(key, nv);
        return nv;
    }

    static void WanderSlider(ref ZuiStack s, string label, string key, ChoreoDemoWander w)
    {
        if (w == null) return;
        w.speed = PSlider(ref s, label, key, w.speed, 0f, 10f);
    }
}
