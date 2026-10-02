// Which neighbour does a raised layer break through? The Canvas card's "Depth between layers" tooltip says
// "a layer whose height rises more than this above the one below it breaks through it". Measured on a
// two-layer document, in memory, nothing written.
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var BF = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var newLayerM = typeof(Laubrary.Shaper.Editor.ShaperWindow).GetMethod("NewLayer", BF);
System.Func<string, Laubrary.Shaper.ShaperDocument, Laubrary.Shaper.ShaperLayer> NewLayer =
    (n, d) => (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[] { n, d });
System.Func<Laubrary.Shaper.ShaperDocument, int, UnityEngine.Color32[]> render = (d, f) => Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(d, f);
System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> diff = (a, b) => { int n = 0; for (int i = 0; i < a.Length; i++) if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) n++; return n; };

var d0 = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
d0.frameCount = 1;
d0.layerSpacing = 4f;
d0.layers.Add(NewLayer("Bottom", d0));
d0.layers.Add(NewLayer("Top", d0));
// two distinguishable flat colours so "which one is in front" is readable from the pixels
d0.layers[0].root.fill = new Laubrary.Shaper.ShaperFillDef { kind = Laubrary.Shaper.ShaperFillKind.Solid, solidColor = UnityEngine.Color.red, authored = true };
d0.layers[1].root.fill = new Laubrary.Shaper.ShaperFillDef { kind = Laubrary.Shaper.ShaperFillKind.Solid, solidColor = UnityEngine.Color.blue, authored = true };

System.Func<Laubrary.Shaper.ShaperDocument, string> counts = d =>
{
    var px = render(d, 0);
    int red = 0, blue = 0, other = 0;
    foreach (var p in px)
    {
        if (p.a == 0) continue;
        if (p.r > p.b + 20) red++;
        else if (p.b > p.r + 20) blue++;
        else other++;
    }
    return "red=" + red + " blue=" + blue + " mixed=" + other;
};

sb.Append("flat, spacing 4: ").Append(counts(d0)).Append("\n");

// raise the BOTTOM layer well past the spacing
d0.layers[0].height = new Laubrary.Shaper.ShaperHeightDef { technique = Laubrary.Shaper.ShaperExtrusionTechnique.Dome, depth = new ZUIValue(24f) };
sb.Append("BOTTOM raised 24 (spacing 4): ").Append(counts(d0)).Append("\n");
d0.layers[0].height = null;

// raise the TOP layer well past the spacing
d0.layers[1].height = new Laubrary.Shaper.ShaperHeightDef { technique = Laubrary.Shaper.ShaperExtrusionTechnique.Dome, depth = new ZUIValue(24f) };
sb.Append("TOP raised 24 (spacing 4):    ").Append(counts(d0)).Append("\n");
d0.layers[1].height = null;

// and the same bottom raise UNDER a spacing large enough to contain it
d0.layerSpacing = 64f;
d0.layers[0].height = new Laubrary.Shaper.ShaperHeightDef { technique = Laubrary.Shaper.ShaperExtrusionTechnique.Dome, depth = new ZUIValue(24f) };
sb.Append("BOTTOM raised 24, spacing 64: ").Append(counts(d0)).Append("\n");

UnityEngine.Object.DestroyImmediate(d0);
return sb.ToString();
