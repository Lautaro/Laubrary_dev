// Press the transport's own Play button, then sample the stage every 0.5s for 3s: which frames were
// reached, and did the picture actually change 1.5s apart.
var win = ZWin("ShaperWindow"); if (win==null) return "no shaper";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo stageF=null, frameF=null, playF=null, tickF=null, accF=null;
System.Reflection.MethodInfo tickM=null;
for (var t = win.GetType(); t != null; t = t.BaseType) {
  if (stageF==null) stageF=t.GetField("stage",BFi); if (frameF==null) frameF=t.GetField("currentFrame",BFi);
  if (playF==null) playF=t.GetField("playing",BFi); if (tickF==null) tickF=t.GetField("lastPlayTick",BFi);
  if (accF==null) accF=t.GetField("playAcc",BFi); if (tickM==null) tickM=t.GetMethod("PlaybackTick",BFi); }
if (playF == null || tickM == null) return "no playback API (playF=" + (playF!=null) + " tick=" + (tickM!=null) + ")";
var del = (UnityEditor.EditorApplication.CallbackFunction)System.Delegate.CreateDelegate(typeof(UnityEditor.EditorApplication.CallbackFunction), win, tickM);
playF.SetValue(win, true); if (tickF!=null) tickF.SetValue(win, UnityEditor.EditorApplication.timeSinceStartup); if (accF!=null) accF.SetValue(win, 0f);
UnityEditor.EditorApplication.update += del;
var stage = stageF.GetValue(win);
var texF = stage.GetType().GetField("_tex", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
var refresh = stage.GetType().GetMethod("Refresh");
var log = new System.Text.StringBuilder();
var frames = new System.Collections.Generic.SortedSet<int>();
double start = UnityEditor.EditorApplication.timeSinceStartup; double nextSample = 0;
UnityEditor.EditorApplication.CallbackFunction watch = null;
watch = () => {
  double el = UnityEditor.EditorApplication.timeSinceStartup - start;
  frames.Add((int)frameF.GetValue(win));
  if (el >= nextSample) {
    refresh.Invoke(stage, null);
    var tex = texF.GetValue(stage) as UnityEngine.Texture2D;
    string h = "notex"; int lit = 0;
    if (tex != null) { var px = tex.GetPixels32(); unchecked { uint hh = 2166136261u; foreach (var p in px) { if (p.a > 8) lit++; hh = (hh ^ p.r) * 16777619u; hh = (hh ^ p.g) * 16777619u; hh = (hh ^ p.b) * 16777619u; hh = (hh ^ p.a) * 16777619u; } h = hh.ToString("X8"); } }
    log.AppendLine("t=" + el.ToString("0.00") + "s frame=" + frameF.GetValue(win) + " hash=" + h + " lit=" + lit);
    nextSample += 1.5;
  }
  if (el < 3.2) return;
  UnityEditor.EditorApplication.update -= watch; UnityEditor.EditorApplication.update -= del;
  playF.SetValue(win, false);
  System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0320\out\play-walk1.txt",
    "frames reached: " + string.Join(",", frames) + "\n" + log);
};
UnityEditor.EditorApplication.update += watch;
return "playing 3s, sampling every 1.5s";
