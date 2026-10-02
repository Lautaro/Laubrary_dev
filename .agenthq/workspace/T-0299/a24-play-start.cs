var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;

System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
Walk(win.rootVisualElement, all);

// the real transport button a user presses
UnityEngine.UIElements.Button play = null;
foreach (var v in all) if (v is UnityEngine.UIElements.Button b && b.text != null && b.text.Contains("Play")) play = b;
sb.Append("Play button text='").Append(play == null ? "<none>" : play.text).Append("' tooltip=").Append(play == null ? "" : play.tooltip).Append("\n");

// the preview stage element (for the never-blank check)
UnityEngine.UIElements.VisualElement stage = null;
foreach (var v in all) if (v.GetType().Name.Contains("ShaperPreviewStage")) stage = v;
sb.Append("stage=").Append(stage == null ? "<none>" : stage.GetType().Name).Append("\n");

var frameF = WT.GetField("currentFrame", BFi);
var playingF = WT.GetField("playing", BFi);
sb.Append("before press: playing=").Append(playingF.GetValue(win)).Append(" frame=").Append(frameF.GetValue(win)).Append("\n");
using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target = play; play.SendEvent(ev); }
sb.Append("after press:  playing=").Append(playingF.GetValue(win)).Append(" frame=").Append(frameF.GetValue(win)).Append("\n");

// sample every ~0.37s of REAL time, 12 samples, on the editor's own update loop
UnityEditor.SessionState.SetString("A24.samples", "");
double t0 = UnityEditor.EditorApplication.timeSinceStartup;
double next = t0 + 0.37;
int taken = 0;
UnityEditor.EditorApplication.CallbackFunction sampler = null;
sampler = () =>
{
    double now = UnityEditor.EditorApplication.timeSinceStartup;
    if (now < next) return;
    next = now + 0.37;
    int fr = (int)frameF.GetValue(win);
    bool bg = false;
    if (stage != null) bg = stage.resolvedStyle.backgroundImage.texture != null || stage.resolvedStyle.backgroundImage.renderTexture != null;
    var cur = UnityEditor.SessionState.GetString("A24.samples", "");
    UnityEditor.SessionState.SetString("A24.samples", cur + (now - t0).ToString("F2") + ":f" + fr + ":bg" + (bg ? 1 : 0) + ":p" + ((bool)playingF.GetValue(win) ? 1 : 0) + " ");
    taken++;
    if (taken >= 12) UnityEditor.EditorApplication.update -= sampler;
};
UnityEditor.EditorApplication.update += sampler;
sb.Append("sampler armed (12 samples @ 0.37s)\n");
return sb.ToString();
