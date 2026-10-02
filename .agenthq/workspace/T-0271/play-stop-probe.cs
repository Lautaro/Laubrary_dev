var PUB = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var sb = new System.Text.StringBuilder();
var winT = System.Type.GetType("Laubrary.Shaper.Editor.ShaperWindow, com.Lautaro-Arino.Laubrary.Shaper.Editor");
var win = UnityEditor.EditorWindow.GetWindow(winT, false, null, false);
var playingF = winT.GetField("playing", PUB);
var frameP = winT.GetProperty("previewFrame", PUB);
var curF = winT.GetField("currentFrame", PUB);
sb.Append("still playing=" + playingF.GetValue(win)
        + "  previewFrame=" + (frameP != null ? frameP.GetValue(win) : (object)"n/a")
        + "  currentFrame=" + (curF != null ? curF.GetValue(win) : (object)"n/a")
        + "  at " + System.DateTime.Now.ToString("HH:mm:ss.fff") + "\n");
playingF.SetValue(win, false);
var tick = winT.GetMethod("PlaybackTick", PUB);
var del = (UnityEditor.EditorApplication.CallbackFunction)System.Delegate.CreateDelegate(
    typeof(UnityEditor.EditorApplication.CallbackFunction), win, tick);
UnityEditor.EditorApplication.update -= del;
sb.Append("playback stopped\n");
sb.Append("scriptCompilationFailed=" + UnityEditor.EditorUtility.scriptCompilationFailed + "\n");
return sb.ToString();
