// Capture the first PopupWindow's own screen rect.
UnityEditor.EditorWindow pop = null;
foreach (var w in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w.GetType().Name == "PopupWindow") { pop = w; break; }
if (pop == null) return "NO POPUP";
var pp = (float)typeof(UnityEditor.EditorGUIUtility).GetProperty("pixelsPerPoint", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).GetValue(null);
var r = pop.position;
int x = Mathf.RoundToInt(r.x*pp), y = Mathf.RoundToInt(r.y*pp), w2 = Mathf.RoundToInt(r.width*pp), h2 = Mathf.RoundToInt(r.height*pp);
string outp = "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0326/shots/popup.png";
var asm = System.Reflection.Assembly.Load("System.Drawing");
var bmpT = asm.GetType("System.Drawing.Bitmap"); var gT = asm.GetType("System.Drawing.Graphics"); var szT = asm.GetType("System.Drawing.Size");
var bmp = System.Activator.CreateInstance(bmpT, new object[]{ w2, h2 });
var g = gT.GetMethod("FromImage").Invoke(null, new object[]{ bmp });
var size = System.Activator.CreateInstance(szT, new object[]{ w2, h2 });
gT.GetMethod("CopyFromScreen", new System.Type[]{ typeof(int), typeof(int), typeof(int), typeof(int), szT }).Invoke(g, new object[]{ x, y, 0, 0, size });
bmpT.GetMethod("Save", new System.Type[]{ typeof(string) }).Invoke(bmp, new object[]{ outp });
return "wrote " + outp + " " + w2 + "x" + h2 + " from " + r;

