var asm = System.Reflection.Assembly.Load("System.Drawing");
var bmpT = asm.GetType("System.Drawing.Bitmap"); var colT = asm.GetType("System.Drawing.Color");
var bmp = System.Activator.CreateInstance(bmpT, new object[]{ "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0321/shots/pyre6.png" });
var getPix = bmpT.GetMethod("GetPixel");
int W=(int)bmpT.GetProperty("Width").GetValue(bmp), H=(int)bmpT.GetProperty("Height").GetValue(bmp);
System.Func<int,int,int> R = (x,y) => { var c=getPix.Invoke(bmp,new object[]{x,y}); return (byte)colT.GetProperty("R").GetValue(c); };
var sb=new System.Text.StringBuilder(); sb.Append(W).Append("x").Append(H).Append("\n");
for (int y=250; y<H-2; y++) { int n=0; for (int x=830;x<W-5;x+=10) if (R(x,y)>150) n++; if (n>70) sb.Append("FULLROW y=").Append(y).Append(" (pt ").Append((y/2.25f).ToString("F1")).Append(") n=").Append(n).Append("\n"); }
if (sb.Length<15) sb.Append("none\n");
return sb.ToString();
