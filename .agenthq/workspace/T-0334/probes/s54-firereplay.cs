// Is the Fire composite source phase-independent, or does it need frames replayed in order?
const int W=64,H=64,N=8;
var srcT=ZType("FireCompositeSource"); var BFi=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance;
System.Func<UnityEngine.Color32[],string> cov = px => { int c=0; foreach(var p in px) if (p.a>0) c++; return c.ToString(); };
System.Func<UnityEngine.Color32[],string> hash = px => { unchecked { uint h=2166136261u; foreach(var p in px){h=(h^p.r)*16777619u;h=(h^p.g)*16777619u;h=(h^p.b)*16777619u;h=(h^p.a)*16777619u;} return h.ToString("X8"); } };
var sb=new System.Text.StringBuilder();
// cold: a brand new source, rendered straight at frame 4's phase
var s1=System.Activator.CreateInstance(srcT); var b1=new UnityEngine.Color32[W*H];
srcT.GetMethod("Render",BFi).Invoke(s1,new object[]{W,H,4f/(N-1),(uint)1234567,b1});
sb.Append("cold  cov=").Append(cov(b1)).Append(" hash=").Append(hash(b1)).Append("\n");
// replayed: the same source stepped through frames 0..4 in order
var s2=System.Activator.CreateInstance(srcT); var b2=new UnityEngine.Color32[W*H];
for (int f=0; f<=4; f++) srcT.GetMethod("Render",BFi).Invoke(s2,new object[]{W,H,(float)f/(N-1),(uint)1234567,b2});
sb.Append("replay cov=").Append(cov(b2)).Append(" hash=").Append(hash(b2)).Append("\n");
sb.Append("sameAsCold=").Append(hash(b1)==hash(b2)).Append("\n");
return sb.ToString();
