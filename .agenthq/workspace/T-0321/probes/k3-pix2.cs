var asm = System.Reflection.Assembly.Load("System.Drawing");
var bmpT = asm.GetType("System.Drawing.Bitmap"); var colT = asm.GetType("System.Drawing.Color");
var bmp = System.Activator.CreateInstance(bmpT, new object[]{ "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0321/shots/pyre.png" });
var getPix = bmpT.GetMethod("GetPixel");
var sb=new System.Text.StringBuilder();
System.Func<int,int,int> R = (x,y) => { var c=getPix.Invoke(bmp,new object[]{x,y}); return (byte)colT.GetProperty("R").GetValue(c); };
int first=-1,last=-1; int gaps=0;
for (int x=0; x<1845; x++) { int r=R(x,1215); if (r>150) { if (first<0) first=x; last=x; } }
sb.Append("r>150 on y=1215: first=").Append(first).Append(" last=").Append(last).Append("\n");
// sample every 40px
for (int x=0; x<1845; x+=40) sb.Append(x).Append(":").Append(R(x,1215)).Append(" ");
return sb.ToString();
