var asm = System.Reflection.Assembly.Load("System.Drawing");
var bmpT = asm.GetType("System.Drawing.Bitmap");
var bmp = System.Activator.CreateInstance(bmpT, new object[]{ "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0321/shots/pyre.png" });
var getPix = bmpT.GetMethod("GetPixel");
var colT = asm.GetType("System.Drawing.Color");
var sb=new System.Text.StringBuilder();
for (int y=1210; y<=1220; y++) {
  sb.Append("y=").Append(y).Append(" ");
  int runStart=-1; var prev="";
  for (int x=830; x<1845; x+=5) {
    var c = getPix.Invoke(bmp, new object[]{ x, y });
    int r=(byte)colT.GetProperty("R").GetValue(c), g=(byte)colT.GetProperty("G").GetValue(c), b=(byte)colT.GetProperty("B").GetValue(c);
    if (r>90 && x>1400) { sb.Append("bright@").Append(x).Append("(").Append(r).Append(") "); break; }
  }
  sb.Append("\n");
}
// scan full row y=1215 for the light line extent
int first=-1,last=-1;
for (int x=825; x<1845; x++) { var c=getPix.Invoke(bmp,new object[]{x,1215}); int r=(byte)colT.GetProperty("R").GetValue(c);
  if (r>=85 && r<=140) { if (first<0) first=x; last=x; } }
sb.Append("lightline y=1215 first=").Append(first).Append(" last=").Append(last).Append("\n");
for (int x=1500; x<1520; x++) { var c=getPix.Invoke(bmp,new object[]{x,1215}); sb.Append((byte)colT.GetProperty("R").GetValue(c)).Append(" "); }
sb.Append("\n@1600 rows: ");
for (int y=1208; y<1224; y++) { var c=getPix.Invoke(bmp,new object[]{1600,y}); sb.Append(y).Append(":").Append((byte)colT.GetProperty("R").GetValue(c)).Append(" "); }
return sb.ToString();
