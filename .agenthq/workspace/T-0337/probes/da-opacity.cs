// T-0337 — ForkBlast.opacity: prove it acts exactly where the Shape Fill's alpha dips below 1
// (PyreForkBlast.cs:386-387, aceil = baseColor.a; if (!Approximately(opaq,1)) aceil = Pow(aceil, opaq)).
var sb = new System.Text.StringBuilder();
const int W = 64, H = 64, N = 8, SEED = 1234567;
var BFi = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
var srcT = ZType("PyreFormCompositeSource");
var renderM = srcT.GetMethod("Render", BFi);
var form = System.Activator.CreateInstance(ZType("ForkBlastForm"));
var src = System.Activator.CreateInstance(srcT);
srcT.GetField("form", BFi).SetValue(src, form);
srcT.GetField("frames", BFi).SetValue(src, N);
System.Func<float, UnityEngine.Color32[]> render = ph => { var b = new UnityEngine.Color32[W*H]; renderM.Invoke(src, new object[]{W,H,ph,(uint)SEED,b}); return b; };
System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> diff = (a,b)=>{int n=0;for(int i=0;i<a.Length;i++) if(a[i].r!=b[i].r||a[i].g!=b[i].g||a[i].b!=b[i].b||a[i].a!=b[i].a) n++; return n;};
var fill = srcT.GetField("shapeFill", BFi).GetValue(src);
sb.Append("fill type=").Append(fill.GetType().Name).Append(" mode=").Append(fill.GetType().GetField("mode",BFi).GetValue(fill)).Append('\n');
// what alpha does Evaluate actually return along life?
var evalM = fill.GetType().GetMethod("Evaluate", BFi);
sb.Append("Evaluate alpha over life: ");
for (int i = 0; i < 5; i++) { var c = (UnityEngine.Color)evalM.Invoke(fill, new object[]{ i/4f, 0.5f, 0.5f }); sb.Append(c.a.ToString("F3")).Append(' '); }
sb.Append('\n');
// dip the gradientAnim's first stop alpha
var gaF = fill.GetType().GetField("gradientAnim", BFi);
var ga = gaF == null ? null : gaF.GetValue(fill);
sb.Append("gradientAnim=").Append(ga == null ? "null" : ga.GetType().Name).Append('\n');
System.Func<int> maxDiff = () => {
    float oo = (float)form.GetType().GetField("opacity",BFi).GetValue(form);
    var basis = new UnityEngine.Color32[N][]; for (int i=0;i<N;i++) basis[i]=render(i/(float)(N-1));
    form.GetType().GetField("opacity",BFi).SetValue(form, 1.5f);
    int m=0; for (int i=0;i<N;i++){ int d=diff(basis[i], render(i/(float)(N-1))); if(d>m)m=d; }
    form.GetType().GetField("opacity",BFi).SetValue(form, oo);
    return m;
};
sb.Append("opacity diff, fill untouched = ").Append(maxDiff()).Append('\n');
if (ga != null)
{
    foreach (var f2 in ga.GetType().GetFields(BFi))
    {
        var lst = f2.GetValue(ga) as System.Collections.IList;
        if (lst == null || lst.Count == 0) continue;
        sb.Append("stops field=").Append(f2.Name).Append(" count=").Append(lst.Count).Append(" elem=").Append(lst[0].GetType().Name).Append('\n');
        var st = lst[0];
        foreach (var cf in st.GetType().GetFields(BFi)) sb.Append("   stop member ").Append(cf.Name).Append(':').Append(cf.FieldType.Name).Append('\n');
        var colF = st.GetType().GetField("color", BFi);
        if (colF != null)
        {
            for (int i = 0; i < lst.Count; i++) { var s2 = lst[i]; var c = (UnityEngine.Color)colF.GetValue(s2); c.a = 0.5f; colF.SetValue(s2, c); if (s2.GetType().IsValueType) lst[i] = s2; }
            sb.Append("Evaluate alpha after dip: ");
            for (int i = 0; i < 5; i++) { var c = (UnityEngine.Color)evalM.Invoke(fill, new object[]{ i/4f, 0.5f, 0.5f }); sb.Append(c.a.ToString("F3")).Append(' '); }
            sb.Append('\n');
            sb.Append("opacity diff, every stop alpha 0.5 = ").Append(maxDiff()).Append('\n');
        }
        break;
    }
}
return sb.ToString();
