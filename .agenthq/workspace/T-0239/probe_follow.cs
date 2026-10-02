// Play-mode probe for T-0239 rework 2: does a FOLLOWED muzzle flash track the muzzle point and the live aim for
// as long as it plays? Fires once, then — while the blast is still on screen — sweeps the aim AND walks the Zoe,
// sampling every frame: the blast's pose, the muzzle meta point it should be riding, and the live aim.
//
// The invariant that separates "follows" from "spawned once": |blast - muzzlePoint| stays CONSTANT (the Pyre's
// authored anchor offset, which rotates with the aim so its LENGTH does not change) while the muzzle point itself
// travels. A fire-and-forget flash leaves the blast frozen, so that distance grows every frame instead.
string Dir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0239\";
var log = new System.Text.StringBuilder();
int step = 0, frame = 0, sample = 0;
float aimDeg = 0f;                       // starts pointing right, sweeps up-left while the flash plays
UnityEngine.Vector2 firstMuzzle = UnityEngine.Vector2.zero, firstBlast = UnityEngine.Vector2.zero;
bool haveFirst = false;
float minOff = 9999f, maxOff = -9999f, muzzleTravel = 0f, rotSpan = 0f;
float firstRot = 0f, lastRot = 0f; int maxBlasts = 0, followerCount = -1;
UnityEngine.Vector2 prevMuzzle = UnityEngine.Vector2.zero; bool havePrev = false;

System.Func<UnityEngine.GameObject, UnityEngine.Vector2> muzzleOf = go =>
{
    // Same two-kind switch EventContext.TryResolveMetaPoint uses — "Muzzle" is a VECTOR layer on ProtoGuy, so the
    // Point accessor answers false and the naive reading of it is a flat (0,0).
    var view = go.GetComponent<Laubrary.Zoetrope.IAnimatedView>();
    if (view == null) return UnityEngine.Vector2.zero;
    UnityEngine.Vector2 p; UnityEngine.Vector2 d; float len;
    var kind = view.GetMetaLayerKind("Muzzle");
    if (kind == Laubrary.Zoetrope.MetaLayerKind.Point && view.TryGetMetaPointNearest("Muzzle", out p)) return p;
    if (kind == Laubrary.Zoetrope.MetaLayerKind.Vector && view.TryGetMetaVectorNearest("Muzzle", out p, out d, out len)) return p;
    return UnityEngine.Vector2.zero;
};

System.Action<string> shot = name =>
{
    var cam = UnityEngine.Camera.main;
    var rt = new UnityEngine.RenderTexture(1280, 720, 24); var prev = cam.targetTexture;
    cam.targetTexture = rt; cam.Render(); cam.targetTexture = prev;
    var tex = new UnityEngine.Texture2D(1280, 720, UnityEngine.TextureFormat.RGBA32, false);
    var pa = UnityEngine.RenderTexture.active; UnityEngine.RenderTexture.active = rt;
    tex.ReadPixels(new UnityEngine.Rect(0, 0, 1280, 720), 0, 0); tex.Apply(); UnityEngine.RenderTexture.active = pa;
    // Centre the crop on the MUZZLE, not the body: the whole point of the capture is the flash at the barrel, and
    // once the gun swings up a body-centred crop pushes it into the corner.
    var pl = UnityEngine.Object.FindFirstObjectByType<Laubrary.Zoetrope.ReactionFxPlayer>();
    var mzp = muzzleOf(pl.gameObject);
    var sp = cam.WorldToScreenPoint(mzp.sqrMagnitude > 1e-6f ? (UnityEngine.Vector3)mzp : pl.transform.position);
    int cx = (int)(sp.x * 1280f / cam.pixelWidth), cy = (int)(sp.y * 720f / cam.pixelHeight);
    int r = 46, s = 6; var crop = new UnityEngine.Texture2D(2 * r * s, 2 * r * s, UnityEngine.TextureFormat.RGBA32, false);
    for (int y = 0; y < 2 * r * s; y++) for (int x = 0; x < 2 * r * s; x++)
    { int sx = UnityEngine.Mathf.Clamp(cx - r + x / s, 0, 1279), sy = UnityEngine.Mathf.Clamp(cy - r + y / s, 0, 719); crop.SetPixel(x, y, tex.GetPixel(sx, sy)); }
    crop.Apply();
    System.IO.File.WriteAllBytes(Dir + "follow_" + name + ".png", crop.EncodeToPNG());
    UnityEngine.Object.Destroy(tex); UnityEngine.Object.Destroy(crop); rt.Release(); UnityEngine.Object.Destroy(rt);
};

UnityEditor.EditorApplication.CallbackFunction tick = null;
tick = () =>
{
    if (!UnityEditor.EditorApplication.isPlaying) { UnityEditor.EditorApplication.update -= tick; return; }
    var w = UnityEngine.Object.FindFirstObjectByType<Laubrary.Combat2D.ProjectileWeapon>();
    var player = UnityEngine.Object.FindFirstObjectByType<Laubrary.Zoetrope.ReactionFxPlayer>();
    if (w == null || w.owner == null || player == null) return;
    foreach (var ad in w.owner.GetComponentsInChildren<Laubrary.ZoeCharacter.AimDriver>(true)) ad.enabled = false;
    var zoeGo = player.gameObject;
    frame++;

    // 0: settle, aim right.
    if (step == 0) { w.owner.aimDirection = UnityEngine.Vector2.right; if (frame >= 30) { step = 1; frame = 0; } return; }

    // 1: fire one shot.
    if (step == 1)
    {
        w.owner.aimDirection = UnityEngine.Vector2.right;
        bool ok = w.TryFire();
        log.AppendLine("FIRED=" + ok + " aim=" + w.ResolvedAimDirection());
        step = 2; frame = 0; aimDeg = 0f; return;
    }

    // 2: while the flash plays, sweep the aim AND walk the Zoe. Sample the blast against the live muzzle point.
    if (step == 2)
    {
        aimDeg += 9f;                                                   // turn the gun mid-flash
        w.owner.aimDirection = new UnityEngine.Vector2(UnityEngine.Mathf.Cos(aimDeg * UnityEngine.Mathf.Deg2Rad), UnityEngine.Mathf.Sin(aimDeg * UnityEngine.Mathf.Deg2Rad));
        zoeGo.transform.position += new UnityEngine.Vector3(0.05f, 0f, 0f);   // walk it sideways at the same time

        Laubrary.Pyre.PyreBlastPlayer live = null; int active = 0; string who = "";
        foreach (var b in UnityEngine.Object.FindObjectsByType<Laubrary.Pyre.PyreBlastPlayer>(UnityEngine.FindObjectsSortMode.None))
        {
            var sr = b.GetComponent<UnityEngine.SpriteRenderer>();
            if (b.gameObject.activeInHierarchy && sr != null && sr.sprite != null)
            {
                active++;
                who += " [" + b.gameObject.name + " id" + b.GetInstanceID() + " @" + b.transform.position.x.ToString("F2") + "," + b.transform.position.y.ToString("F2") + "]";
                // The MUZZLE flash is the one riding this Zoe; anything else (a disc hit, a stray) must not be
                // mistaken for it, so pick by proximity to the muzzle rather than by iteration order.
                if (live == null || UnityEngine.Vector2.Distance(b.transform.position, muzzleOf(zoeGo)) < UnityEngine.Vector2.Distance(live.transform.position, muzzleOf(zoeGo))) live = b;
            }
        }
        if (active > 1) log.AppendLine("    NOTE multiple blasts:" + who);
        if (active > maxBlasts) maxBlasts = active;

        if (live != null)
        {
            var mz = muzzleOf(zoeGo);
            var bp = (UnityEngine.Vector2)live.transform.position;
            float off = UnityEngine.Vector2.Distance(bp, mz);
            float rot = live.transform.eulerAngles.z;
            bool flip = live.GetComponent<UnityEngine.SpriteRenderer>().flipX;
            if (!haveFirst) { firstMuzzle = mz; firstBlast = bp; firstRot = rot; haveFirst = true; }
            if (havePrev) muzzleTravel += UnityEngine.Vector2.Distance(mz, prevMuzzle);
            prevMuzzle = mz; havePrev = true;
            if (off < minOff) minOff = off; if (off > maxOff) maxOff = off;
            lastRot = rot;
            // how many followers are riding this blast (the pooling-leak check)
            followerCount = live.GetComponents<Laubrary.Zoetrope.FxFollowTarget>().Length;
            sample++;
            log.AppendLine("f" + sample.ToString("00")
                + " aim=" + aimDeg.ToString("F0")
                + " muzzle=(" + mz.x.ToString("F3") + "," + mz.y.ToString("F3") + ")"
                + " blast=(" + bp.x.ToString("F3") + "," + bp.y.ToString("F3") + ")"
                + " |blast-muzzle|=" + off.ToString("F4")
                + " rotZ=" + rot.ToString("F0") + " flip=" + flip
                + " blasts=" + active + " followers=" + followerCount);
            if (sample == 1) shot("1_early");
            if (sample == 6) shot("2_turning");
            if (sample == 12) shot("3_turned");
            if (sample == 15) shot("4_mirrored");
        }
        else if (haveFirst) { step = 3; frame = 0; return; }   // flash finished

        if (frame > 240) { step = 3; frame = 0; }
        return;
    }

    // 3: verdict.
    UnityEditor.EditorApplication.update -= tick;
    float muzzleMoved = UnityEngine.Vector2.Distance(prevMuzzle, firstMuzzle);
    float blastFrozenIfNotFollowing = muzzleMoved;
    log.AppendLine("---");
    log.AppendLine("samples=" + sample + " maxConcurrentBlasts=" + maxBlasts + " followersOnBlast=" + followerCount);
    log.AppendLine("muzzle point travelled (net)=" + muzzleMoved.ToString("F3") + "  (path length=" + muzzleTravel.ToString("F3") + ")");
    log.AppendLine("|blast-muzzle| min=" + minOff.ToString("F4") + " max=" + maxOff.ToString("F4") + " spread=" + (maxOff - minOff).ToString("F4"));
    log.AppendLine("rotZ first=" + firstRot.ToString("F0") + " last=" + lastRot.ToString("F0") + " turned=" + UnityEngine.Mathf.DeltaAngle(firstRot, lastRot).ToString("F0") + " deg");
    // The blast never has to SIT on the muzzle point — a Pyre lands its authored ANCHOR there, so a small offset
    // is correct, and it grows a little as the flash's sprite scales up over its life. What separates "follows"
    // from "spawned once" is that the offset stays small while the muzzle point travels far: fire-and-forget
    // would leave the blast behind by the full travel distance.
    bool followsPos = maxOff < 0.25f && muzzleTravel > 1.0f && maxOff < 0.2f * muzzleTravel;
    bool followsDir = UnityEngine.Mathf.Abs(UnityEngine.Mathf.DeltaAngle(firstRot, lastRot)) > 20f;
    bool oneBlast = maxBlasts == 1;
    bool oneFollower = followerCount == 1;
    log.AppendLine("VERDICT followsPosition=" + followsPos + " followsDirection=" + followsDir
        + " exactlyOneBlast=" + oneBlast + " exactlyOneFollower=" + oneFollower);
    System.IO.File.WriteAllText(Dir + "follow_log.txt", log.ToString());
};
UnityEditor.EditorApplication.update += tick;
return "started";
