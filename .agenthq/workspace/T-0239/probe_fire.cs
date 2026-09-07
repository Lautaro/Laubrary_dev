// Play-mode probe: aim right / left / up-left, fire, capture the game camera a couple of frames later and write a
// full frame + a 3x crop around the shooter, plus a log file. Runs on EditorApplication.update as a closure.
string Dir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0239\";
var names = new[] { "right", "left", "upleft" };
var dirs = new[] { UnityEngine.Vector2.right, UnityEngine.Vector2.left, new UnityEngine.Vector2(-1f, 1f).normalized };
int step = 0, frame = 0; var log = new System.Text.StringBuilder();
UnityEditor.EditorApplication.CallbackFunction tick = null;
System.Action<string, Laubrary.Combat2D.ProjectileWeapon> capture = (name, w) =>
{
    var cam = UnityEngine.Camera.main;
    var rt = new UnityEngine.RenderTexture(1280, 720, 24); var prev = cam.targetTexture;
    cam.targetTexture = rt; cam.Render(); cam.targetTexture = prev;
    var tex = new UnityEngine.Texture2D(1280, 720, UnityEngine.TextureFormat.RGBA32, false);
    var pa = UnityEngine.RenderTexture.active; UnityEngine.RenderTexture.active = rt;
    tex.ReadPixels(new UnityEngine.Rect(0, 0, 1280, 720), 0, 0); tex.Apply(); UnityEngine.RenderTexture.active = pa;
    System.IO.File.WriteAllBytes(Dir + "fire_" + name + ".png", tex.EncodeToPNG());
    var sp = cam.WorldToScreenPoint(w.transform.position);
    int cx = (int)(sp.x * 1280f / cam.pixelWidth), cy = (int)(sp.y * 720f / cam.pixelHeight);
    int r = 90, s = 3; var crop = new UnityEngine.Texture2D(2 * r * s, 2 * r * s, UnityEngine.TextureFormat.RGBA32, false);
    for (int y = 0; y < 2 * r * s; y++) for (int x = 0; x < 2 * r * s; x++)
    { int sx = UnityEngine.Mathf.Clamp(cx - r + x / s, 0, 1279), sy = UnityEngine.Mathf.Clamp(cy - r + y / s, 0, 719); crop.SetPixel(x, y, tex.GetPixel(sx, sy)); }
    crop.Apply();
    System.IO.File.WriteAllBytes(Dir + "fire_" + name + "_crop.png", crop.EncodeToPNG());
    var blasts = UnityEngine.Object.FindObjectsByType<Laubrary.Pyre.PyreBlastPlayer>(UnityEngine.FindObjectsSortMode.None);
    int active = 0; string info = "";
    foreach (var b in blasts) { var sr = b.GetComponent<UnityEngine.SpriteRenderer>(); if (b.gameObject.activeInHierarchy && sr.sprite != null) { active++; info += " pos=" + b.transform.position + " rotZ=" + b.transform.eulerAngles.z.ToString("F0") + " flip=" + sr.flipX; } }
    log.AppendLine(name + ": activeBlasts=" + active + info + " shooter=" + w.transform.position);
    UnityEngine.Object.Destroy(tex); UnityEngine.Object.Destroy(crop); rt.Release(); UnityEngine.Object.Destroy(rt);
    System.IO.File.WriteAllText(Dir + "probe_log.txt", log.ToString());
};
tick = () =>
{
    if (!UnityEditor.EditorApplication.isPlaying) { UnityEditor.EditorApplication.update -= tick; return; }
    var w = UnityEngine.Object.FindFirstObjectByType<Laubrary.Combat2D.ProjectileWeapon>();
    if (w == null || w.owner == null) return;
    foreach (var ad in w.owner.GetComponentsInChildren<Laubrary.ZoeCharacter.AimDriver>(true)) ad.enabled = false;   // the mouse aim would overwrite our aim every frame
    int shot = step / 3, phase = step % 3;
    if (shot >= names.Length) { UnityEditor.EditorApplication.update -= tick; log.AppendLine("DONE"); System.IO.File.WriteAllText(Dir + "probe_log.txt", log.ToString()); return; }
    frame++;
    if (phase == 0) { w.owner.aimDirection = dirs[shot]; if (frame >= 30) { step++; frame = 0; } return; }
    if (phase == 1) { w.owner.aimDirection = dirs[shot]; bool ok = w.TryFire(); log.AppendLine(names[shot] + " fired=" + ok + " aim=" + w.ResolvedAimDirection()); step++; frame = 0; return; }
    if (frame < 3) return;
    capture(names[shot], w);
    step++; frame = 0;
};
UnityEditor.EditorApplication.update += tick;
return "started";
