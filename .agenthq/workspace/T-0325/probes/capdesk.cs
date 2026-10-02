// Capture the whole primary desktop, downscaled, so I can see what is covering what.
string outp = UnityEditor.EditorPrefs.GetString("T325.deskOut", "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0325/shots/desktop.png");
int W = 3840, H = 2160; float sc = 0.35f;
int dw = Mathf.RoundToInt(W * sc), dh = Mathf.RoundToInt(H * sc);
var asm = System.Reflection.Assembly.Load("System.Drawing");
var bmpT = asm.GetType("System.Drawing.Bitmap"); var gT = asm.GetType("System.Drawing.Graphics"); var szT = asm.GetType("System.Drawing.Size");
var big = System.Activator.CreateInstance(bmpT, new object[]{ W, H });
var g = gT.GetMethod("FromImage").Invoke(null, new object[]{ big });
var size = System.Activator.CreateInstance(szT, new object[]{ W, H });
gT.GetMethod("CopyFromScreen", new System.Type[]{ typeof(int), typeof(int), typeof(int), typeof(int), szT }).Invoke(g, new object[]{ 0, 0, 0, 0, size });
var small = System.Activator.CreateInstance(bmpT, new object[]{ dw, dh });
var g2 = gT.GetMethod("FromImage").Invoke(null, new object[]{ small });
var imgT = asm.GetType("System.Drawing.Image"); var rectT = asm.GetType("System.Drawing.Rectangle"); var gu = asm.GetType("System.Drawing.GraphicsUnit");
var dr = System.Activator.CreateInstance(rectT, new object[]{ 0,0,dw,dh });
var sr = System.Activator.CreateInstance(rectT, new object[]{ 0,0,W,H });
gT.GetMethod("DrawImage", new System.Type[]{ imgT, rectT, rectT, gu }).Invoke(g2, new object[]{ big, dr, sr, System.Enum.Parse(gu,"Pixel") });
bmpT.GetMethod("Save", new System.Type[]{ typeof(string) }).Invoke(small, new object[]{ outp });
return "wrote " + outp + " " + dw + "x" + dh;
