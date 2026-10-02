var asm = System.Reflection.Assembly.Load("System.Drawing");
var bmpT = asm.GetType("System.Drawing.Bitmap"); var colT = asm.GetType("System.Drawing.Color");
var bmp = System.Activator.CreateInstance(bmpT, new object[]{ "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0321/shots/pyre3.png" });
var getPix = bmpT.GetMethod("GetPixel");
System.Func<int,int,int> R = (x,y) => { var c=getPix.Invoke(bmp,new object[]{x,y}); return (byte)colT.GetProperty("R").GetValue(c); };
var sb=new System.Text.StringBuilder();
for (int y=250; y<2020; y++) { int n=0; for (int x=830;x<1840;x+=10) if (R(x,y)>150) n++; if (n>70) sb.Append("FULLROW y=").Append(y).Append(" n=").Append(n).Append("\n"); }
if (sb.Length==0) sb.Append("no full-width bright row\n");
return sb.ToString();
