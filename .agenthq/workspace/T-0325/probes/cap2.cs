// By-eye capture without P/Invoke: read the composited desktop over the window's own rect.
// EditorWindow.position is in editor points; the desktop is physical pixels, so scale by pixelsPerPoint.
string wn = UnityEditor.EditorPrefs.GetString("T320.capWin","ShaperWindow");
string outp = UnityEditor.EditorPrefs.GetString("T320.capOut","C:/Users/Lauta/AppData/Local/Temp/claude/D--UNITY-Laubrary-Dev---Shaper/b6197aa9-96a6-43f3-921b-83cf03d08706/scratchpad/shots/t320.png");
var win = ZWin(wn); if (win == null) return "NO WINDOW " + wn;
var pp = (float)typeof(UnityEditor.EditorGUIUtility).GetProperty("pixelsPerPoint", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).GetValue(null);
var r = win.position;
int x = Mathf.RoundToInt(r.x*pp), y = Mathf.RoundToInt(r.y*pp), w = Mathf.RoundToInt(r.width*pp), h = Mathf.RoundToInt(r.height*pp);
var asm = System.Reflection.Assembly.Load("System.Drawing");
var bmpT = asm.GetType("System.Drawing.Bitmap"); var gT = asm.GetType("System.Drawing.Graphics"); var szT = asm.GetType("System.Drawing.Size");
var bmp = System.Activator.CreateInstance(bmpT, new object[]{ w, h });
var g = gT.GetMethod("FromImage").Invoke(null, new object[]{ bmp });
var size = System.Activator.CreateInstance(szT, new object[]{ w, h });
gT.GetMethod("CopyFromScreen", new System.Type[]{ typeof(int), typeof(int), typeof(int), typeof(int), szT }).Invoke(g, new object[]{ x, y, 0, 0, size });
bmpT.GetMethod("Save", new System.Type[]{ typeof(string) }).Invoke(bmp, new object[]{ outp });
return "wrote " + outp + " " + w + "x" + h + " from pos " + r + " pp=" + pp;
