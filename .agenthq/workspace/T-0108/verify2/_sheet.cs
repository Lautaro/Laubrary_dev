string o = "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0108/verify2/sheet.txt";
var sb = new System.Text.StringBuilder();
byte[] bytes = System.IO.File.ReadAllBytes("D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0108/light-contact-sheet.png");
var tex = new UnityEngine.Texture2D(2,2, UnityEngine.TextureFormat.RGBA32, false);
UnityEngine.ImageConversion.LoadImage(tex, bytes);
int W=tex.width, H=tex.height;
sb.AppendLine("sheet " + W + "x" + H);
var px = tex.GetPixels32();
// find the most blue-dominant pixels in the whole sheet, grouped by cell
// cells: 6 cols x 4 rows. derive cell size
int cols=6, rows=4;
// find blue-dominant pixels overall
var counts = new int[cols*rows];
var worst = new float[cols*rows];
var worstPx = new string[cols*rows];
for (int y=0;y<H;y++) for(int x=0;x<W;x++){
  var c = px[(H-1-y)*W + x];   // GetPixels32 is bottom-up; map to top-down y
  int cx = x*cols/W, cy = y*rows/H;
  if (cx>=cols) cx=cols-1; if (cy>=rows) cy=rows-1;
  int ci = cy*cols+cx;
  float d = (float)c.b - (float)c.r;
  if (d > 12f) { counts[ci]++; if (d > worst[ci]) { worst[ci]=d; worstPx[ci]="rgba("+c.r+","+c.g+","+c.b+","+c.a+") @("+x+","+y+")"; } }
}
for (int i=0;i<cols*rows;i++)
  sb.AppendLine("cell " + (i+1).ToString("00") + "  blue-dominant(B-R>12) px = " + counts[i] + "   worst B-R = " + worst[i] + "  " + (worstPx[i]??""));
System.IO.File.WriteAllText(o, sb.ToString());
UnityEngine.Object.DestroyImmediate(tex);
return "ok";
