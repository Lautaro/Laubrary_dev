// Do the Zoe window's OWN derivation helpers answer on a COMPOSITE character? Round 15 taught
// GetClipNameOptions to union the parts; everything downstream still resolves through
// FindAnimationByName, which reads view.version — and a composite has none.
var sb = new System.Text.StringBuilder();
var BFs = System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
var zoeWinT = ZType("ZoeWindow");
System.Type defWinT = null;
for (var b = zoeWinT; b != null; b = b.BaseType) if (b.Name.StartsWith("ZoetropeDefWindow")) { defWinT = b; break; }
var mClips  = defWinT != null ? defWinT.GetMethod("GetClipNameOptions", BFs) : null;
var mEvents = zoeWinT.GetMethod("AllFrameEventNames", BFs);
var mLayers = zoeWinT.GetMethod("AllPointLayerIds", BFs);
var mFrames = zoeWinT.GetMethod("GetFrameCount", BFs);
sb.Append("methods: clips=").Append(mClips != null).Append(" events=").Append(mEvents != null)
  .Append(" layers=").Append(mLayers != null).Append(" frames=").Append(mFrames != null).Append("\n");
if (mClips == null || mEvents == null) return sb.ToString();
System.Func<string,string> run = path => {
    var zoe = UnityEditor.AssetDatabase.LoadAssetAtPath(path, ZType("Zoe"));
    if (zoe == null) return path + ": missing\n";
    System.Reflection.FieldInfo vf = null;
    for (var ty = zoe.GetType(); ty != null && vf == null; ty = ty.BaseType)
        vf = ty.GetField("view", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.DeclaredOnly);
    if (vf == null) return path + ": no view field\n";
    var view = vf.GetValue(zoe);
    var clips = mClips.Invoke(null, new object[]{ view }) as string[];
    var evs = mEvents.Invoke(null, new object[]{ view }) as string[];
    var lays = mLayers.Invoke(null, new object[]{ view }) as string[];
    int frames = (clips != null && clips.Length > 0) ? (int)mFrames.Invoke(null, new object[]{ view, clips[0] }) : -1;
    return path + "\n   viewType=" + (view == null ? "<null>" : view.GetType().Name)
        + "\n   clips=" + (clips == null ? "<null>" : clips.Length + " [" + string.Join(",", clips) + "]")
        + "\n   frameEventNames=" + (evs == null ? "<null>" : evs.Length + " [" + string.Join(",", evs) + "]")
        + "\n   pointLayerIds=" + (lays == null ? "<null>" : lays.Length + " [" + string.Join(",", lays) + "]")
        + "\n   frameCount(first clip)=" + frames + "\n";
};
sb.Append(run("Assets/Demos/ProtoGuyDemo/ProtoGuy.asset"));
sb.Append(run("Assets/Demos/PreviewDemo/PreviewShooterZoe.asset"));
sb.Append(run("Assets/Zoetrope/Hero.asset"));
return sb.ToString();
