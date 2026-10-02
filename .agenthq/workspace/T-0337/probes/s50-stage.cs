// T-0337 §3 — round 20's unexplained s42/s43 PRE/POST stage-hash disagreement, taken apart.
//
// The hypothesis this tests is not a guess: ShaperPreviewStage.Refresh reads the frame through the frame
// CACHE (`_frameCache.ComputeFrame(frame, doc)`) and, when that hands back nothing, HOLDS the picture already
// on screen rather than blanking it — ShaperPreviewStage.cs:562-570, T-0188, with the comment saying exactly
// that. A prebaker fills the cache in the background (`StartPrebakeIfNeeded`). So a probe that edits the
// document, calls Refresh and reads `_tex` in the SAME round trip can legitimately read the picture from
// BEFORE its own edit. That is a property of the probe's timing, not of the document.
//
// Measured here, on the bound document, with the stage never played:
//   A  the settled hash of each of four frames (Refresh, then read on a LATER round trip — see s51)
//   B  the hash read IMMEDIATELY after InvalidateFrameCache + Refresh, in the same round trip
//   C  whether ComputeFrame itself returns null on the first call after an invalidate
var win = ZWin("ShaperWindow"); if (win == null) return "no shaper";
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo stageF = null, frameF = null, assetF = null;
for (var t = win.GetType(); t != null; t = t.BaseType)
{ if (stageF == null) stageF = t.GetField("stage", BFi); if (frameF == null) frameF = t.GetField("currentFrame", BFi); if (assetF == null) assetF = t.GetField("asset", BFi); }
var stage = stageF.GetValue(win);
var st = stage.GetType();
var texF = st.GetField("_tex", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
var refreshM = st.GetMethod("Refresh");
var invM = st.GetMethod("InvalidateFrameCache");
var cacheF = st.GetField("_frameCache", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
var docF = st.GetField("_doc", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
var sb = new System.Text.StringBuilder();

System.Func<string> hash = () => {
    var tex = texF.GetValue(stage) as UnityEngine.Texture2D;
    if (tex == null) return "notex";
    var px = tex.GetPixels32(); unchecked { uint h = 2166136261u; int lit = 0;
        foreach (var p in px) { if (p.a > 8) lit++; h = (h ^ p.r) * 16777619u; h = (h ^ p.g) * 16777619u; h = (h ^ p.b) * 16777619u; h = (h ^ p.a) * 16777619u; }
        return h.ToString("X8") + "/" + lit; } };

int[] frames = { 0, 4, 8, 12 };

// (1) hashes read the way s42 reads them — set the frame, Refresh, read, all in one round trip
sb.Append("SAME-ROUND-TRIP (the way s42/s43 read):\n  ");
foreach (int f in frames) { frameF.SetValue(win, f); refreshM.Invoke(stage, null); sb.Append('f').Append(f).Append('=').Append(hash()).Append(' '); }
sb.Append('\n');

// (2) the same four, but with the cache dropped first — the state a document EDIT leaves behind
invM.Invoke(stage, null);
sb.Append("IMMEDIATELY AFTER InvalidateFrameCache, same round trip:\n  ");
foreach (int f in frames) { frameF.SetValue(win, f); refreshM.Invoke(stage, null); sb.Append('f').Append(f).Append('=').Append(hash()).Append(' '); }
sb.Append('\n');

// (3) the decisive one: does the cache hand Refresh anything at all on the first call after an invalidate?
var cache = cacheF == null ? null : cacheF.GetValue(stage);
var docGet = docF == null ? null : docF.GetValue(stage) as System.Delegate;
object doc = docGet == null ? null : docGet.DynamicInvoke();
sb.Append("cache=").Append(cache == null ? "<not found>" : cache.GetType().Name)
  .Append(" doc=").Append(doc == null ? "<null>" : doc.GetType().Name).Append('\n');
if (cache != null && doc != null)
{
    var cf = cache.GetType().GetMethod("ComputeFrame");
    invM.Invoke(stage, null);
    sb.Append("ComputeFrame right after an invalidate: ");
    foreach (int f in frames)
    {
        object px = null; try { px = cf.Invoke(cache, new object[] { f, doc }); } catch (System.Exception e) { sb.Append("f").Append(f).Append("=EX(").Append(e.GetBaseException().GetType().Name).Append(") "); continue; }
        sb.Append('f').Append(f).Append('=').Append(px == null ? "NULL — Refresh holds the old picture" : "pixels").Append(' ');
        invM.Invoke(stage, null);   // drop it again so each frame is asked cold
    }
    sb.Append('\n');
}

// leave the stage on frame 0 with the cache dropped, so s51 (a LATER round trip, after
// EditorApplication.update has run) reads exactly the state s43 would have read
invM.Invoke(stage, null);
frameF.SetValue(win, 12);
refreshM.Invoke(stage, null);
UnityEditor.EditorPrefs.SetString("T337.stageHash", hash());
sb.Append("PARKED at frame 12, cache dropped, hash read in this same round trip = ").Append(hash()).Append('\n');
return sb.ToString();
