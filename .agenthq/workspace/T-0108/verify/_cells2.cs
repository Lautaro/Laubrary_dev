var png = System.IO.File.ReadAllBytes(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\light-contact-sheet.png");
var tex = new UnityEngine.Texture2D(2,2);
UnityEngine.ImageConversion.LoadImage(tex, png);
int Cell=96, Cols=6, Pad=6, rows=4;
var all = tex.GetPixels32();
int texW = tex.width;
System.Func<int,int,int,UnityEngine.Color32> px = (c,x,y) => {
  int col=c%Cols, row=rows-1-(c/Cols);
  int gx = Pad + col*(Cell+Pad) + x, gy = Pad + row*(Cell+Pad) + y;
  return all[gy*texW+gx];
};
System.Func<int,int,int> diff = (a,b) => {
  int n=0;
  for(int y=1;y<Cell-1;y++)
    for(int x=1;x<Cell-1;x++){
      if (x<44 && y<22) continue;
      var p=px(a,x,y); var q=px(b,x,y);
      if(p.r!=q.r||p.g!=q.g||p.b!=q.b) n++;
    }
  return n;
};
var sb=new System.Text.StringBuilder();
sb.AppendLine("size " + tex.width + "x" + tex.height + "  (index-label plate x<44,y<22 excluded)");
sb.AppendLine("cell 01 vs 13 (flat rim 0 vs flat rim 1.5) differing px: " + diff(0,12) + " (expected 0)");
sb.AppendLine("shadow combos 19v20 " + diff(18,19) + ", 19v21 " + diff(18,20) + ", 19v22 " + diff(18,21) + " (all expected 0)");
sb.AppendLine("cell 16 receive ON vs 17 receive OFF differing px: " + diff(15,16) + " (expected > 0)");
sb.AppendLine("cell 17 receive OFF vs 18 intensityScale 0 differing px: " + diff(16,17) + " (expected > 0)");
var c14 = px(13, Cell/2, Cell/2); var c15 = px(14, Cell/2, Cell/2);
sb.AppendLine("centre of 14 Over = (" + c14.r + "," + c14.g + "," + c14.b + ")  15 Add = (" + c15.r + "," + c15.g + "," + c15.b + ")  Add brighter: " + (c15.r > c14.r));
sb.AppendLine("cell 09 rim 0.0 vs 12 rim 1.8 differing px: " + diff(8,11) + " (expected > 0)");
sb.AppendLine("cell 01 Silhouette vs 02 Solids Orb differing px: " + diff(0,1) + " (expected > 0)");
return sb.ToString();
