// Render the rig's own preview camera to a PNG â€” the same technique MirageRig.CaptureNow uses, so this is
// literally the picture the preview produces. Reports lit pixels + a frame hash so two samples can be
// compared for movement.
var rigT = ZType("MirageRig");
var rig = UnityEngine.Object.FindFirstObjectByType(rigT);
if (rig == null) return "no rig";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
var cam = rigT.GetField("previewCamera", BFi).GetValue(rig) as Camera;
if (cam == null) return "no preview camera";
var liveF = rigT.GetField("_live", BFi);
var live = liveF != null ? liveF.GetValue(rig) as System.Collections.IDictionary : null;
int W = 512, H = 320;
var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
var prevT = cam.targetTexture; var prevA = RenderTexture.active;
cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
cam.targetTexture = prevT; RenderTexture.active = prevA; RenderTexture.ReleaseTemporary(rt);
var px = tex.GetPixels32();
// "lit" = not the camera's clear colour
var clear = cam.backgroundColor; var c32 = (Color32)clear;
int lit = 0; uint h = 2166136261;
foreach (var p in px)
{
    if (Mathf.Abs(p.r-c32.r) > 8 || Mathf.Abs(p.g-c32.g) > 8 || Mathf.Abs(p.b-c32.b) > 8) lit++;
    h = (h ^ p.r) * 16777619; h = (h ^ p.g) * 16777619; h = (h ^ p.b) * 16777619;
}
string outp = UnityEditor.EditorPrefs.GetString("T328.rigOut", "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0328/shots/mirage-rig.png");
System.IO.File.WriteAllBytes(outp, tex.EncodeToPNG());
UnityEngine.Object.DestroyImmediate(tex);
return "t=" + Time.realtimeSinceStartup.ToString("0.00") + " live=" + (live != null ? live.Count : -1)
  + " lit=" + lit + "/" + px.Length + " hash=" + h.ToString("X8") + " -> " + outp;

