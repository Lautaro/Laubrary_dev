var asm = System.Reflection.Assembly.Load("System.Drawing");
var bmpT = asm.GetType("System.Drawing.Bitmap"); var colT = asm.GetType("System.Drawing.Color");
var bmp = System.Activator.CreateInstance(bmpT, new object[]{ "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0321/shots/pyre2.png" });
var getPix = bmpT.GetMethod("GetPixel");
var sb=new System.Text.StringBuilder();
System.Func<int,int,int> R = (x,y) => { var c=getPix.Invoke(bmp,new object[]{x,y}); return (byte)colT.GetProperty("R").GetValue(c); };
for (int y=1206; y<1226; y++) { int n=0; for (int x=830;x<1840;x+=10) if (R(x,y)>150) n++; sb.Append(y).Append(":").Append(n).Append(" "); }
sb.Append("\n");
// also scan every row of the right pane for full-width bright lines
for (int y=250; y<2020; y++) { int n=0; for (int x=830;x<1840;x+=10) if (R(x,y)>150) n++; if (n>70) sb.Append("FULLROW y=").Append(y).Append(" n=").Append(n).Append("\n"); }
return sb.ToString();
