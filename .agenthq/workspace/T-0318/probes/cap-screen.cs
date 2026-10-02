// T-0318 — by-eye capture through Unity's own screen reader (PrintWindow returns a blank client area for
// these UI-Toolkit windows in this session; ReadScreenPixel reads the composited desktop instead, so the
// window must be foreground and unobstructed — Focus() is called first and the shot is a real screen read).
string wn = UnityEditor.EditorPrefs.GetString("T318.capWin","ChunkWindow");
string name = UnityEditor.EditorPrefs.GetString("T318.capName","shot");
var win = ZWin(wn); if (win == null) return "NO WINDOW " + wn;
win.Focus(); win.Repaint();
var r = win.position;
var t = System.Type.GetType("UnityEditorInternal.InternalEditorUtility,UnityEditor");
if (t == null) return "no InternalEditorUtility";
var m = t.GetMethod("ReadScreenPixel", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
if (m == null) return "no ReadScreenPixel";
int w = (int)r.width, h = (int)r.height;
var px = (UnityEngine.Color[])m.Invoke(null, new object[]{ new UnityEngine.Vector2(r.x, r.y), w, h });
if (px == null) return "null pixels";
var tex = new UnityEngine.Texture2D(w, h, UnityEngine.TextureFormat.RGBA32, false);
// ReadScreenPixel returns bottom-up
var flip = new UnityEngine.Color[px.Length];
for (int y = 0; y < h; y++) System.Array.Copy(px, y*w, flip, (h-1-y)*w, w);
tex.SetPixels(flip); tex.Apply();
string path = "C:/Users/Lauta/AppData/Local/Temp/claude/D--UNITY-Laubrary-Dev---Shaper/b6197aa9-96a6-43f3-921b-83cf03d08706/scratchpad/shots/t0318-" + name + ".png";
System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
UnityEngine.Object.DestroyImmediate(tex);
return "wrote " + path + " " + w + "x" + h + " at " + r;
