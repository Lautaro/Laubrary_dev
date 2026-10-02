// Opens Shaper cold, binds the SHIPPED demo document, clears the console, presses Play and arms a 10 s
// sampler on the editor's own update loop. Nothing is written to the asset.
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.Shaper.Editor.ShaperWindow>()) w0.Close();
UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Shaper");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
win.position = new UnityEngine.Rect(60, 20, 1600, 1150);
win.Show(); win.Repaint();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;

var demo = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>("Assets/Demos/ShaperDemo/ShaperDemoDoc.asset");
sb.Append("demo asset=").Append(demo == null ? "<null>" : demo.name)
  .Append(" frames=").Append(demo == null ? -1 : demo.frameCount)
  .Append(" rate=").Append(demo == null ? -1f : demo.frameRate)
  .Append(" layers=").Append(demo == null || demo.layers == null ? -1 : demo.layers.Count)
  .Append(" dirtyBefore=").Append(demo != null && UnityEditor.EditorUtility.IsDirty(demo)).Append("\n");
System.Reflection.MethodInfo setAsset = null;
for (var t = WT; t != null && setAsset == null; t = t.BaseType) setAsset = t.GetMethod("SetAsset", BFi | System.Reflection.BindingFlags.DeclaredOnly);
setAsset.Invoke(win, new object[] { demo });
win.Repaint();
sb.Append("bound, dirtyAfterBind=").Append(demo != null && UnityEditor.EditorUtility.IsDirty(demo)).Append("\n");

System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
Walk(win.rootVisualElement, all);
UnityEngine.UIElements.Button play = null;
foreach (var v in all) if (v is UnityEngine.UIElements.Button b && b.text != null && b.text.Contains("Play")) play = b;
UnityEngine.UIElements.VisualElement stage = null;
foreach (var v in all) if (v.GetType().Name.Contains("ShaperPreviewStage")) stage = v;
sb.Append("Play button='").Append(play == null ? "<none>" : play.text).Append("' enabled=").Append(play != null && play.enabledInHierarchy)
  .Append("  stage=").Append(stage == null ? "<none>" : stage.GetType().Name).Append("\n");

var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
lt.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);

var frameF = WT.GetField("currentFrame", BFi);
var playingF = WT.GetField("playing", BFi);
using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target = play; play.SendEvent(ev); }
sb.Append("after press: playing=").Append(playingF.GetValue(win)).Append(" frame=").Append(frameF.GetValue(win)).Append("\n");

UnityEditor.SessionState.SetString("T0307.play", "");
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
    bool bg = stage != null && (stage.resolvedStyle.backgroundImage.texture != null || stage.resolvedStyle.backgroundImage.renderTexture != null);
    var cur = UnityEditor.SessionState.GetString("T0307.play", "");
    UnityEditor.SessionState.SetString("T0307.play", cur + (now - t0).ToString("F2") + ":f" + fr + ":bg" + (bg ? 1 : 0) + ":p" + ((bool)playingF.GetValue(win) ? 1 : 0) + " ");
    taken++;
    if (taken >= 27) UnityEditor.EditorApplication.update -= sampler;
};
UnityEditor.EditorApplication.update += sampler;
sb.Append("sampler armed (27 samples @ 0.37s ~= 10s), console cleared\n");
return sb.ToString();
