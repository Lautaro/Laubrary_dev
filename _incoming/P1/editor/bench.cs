const int W = 40, H = 50;
var grid = new Laubrary.GoreLab.GoreGrid(W, H);
for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) {
    double hx = (x + 0.5 - 20) / 7, hy = (y + 0.5 - 9) / 7.5;
    bool head = hx * hx + hy * hy <= 1, torso = x >= 10 && x <= 29 && y >= 16 && y <= 44, arm = (x >= 4 && x <= 9 || x >= 30 && x <= 35) && y >= 18 && y <= 36;
    if (head || torso || arm) grid.px[y * W + x] = Laubrary.GoreLab.GoreDefaults.Rgb(120 + (int)(Laubrary.GoreLab.GoreRng.Hash(x, y, 3) * 40), 80, 60);
}
var headTag = new Laubrary.GoreLab.MemberTag { kind = Laubrary.GoreLab.MemberKind.Ball, cx = 20, cy = 9, rx = 7, ry = 7.5, n = 2, angle = -System.Math.PI / 2, uy = -1, fz = 1 };
var torsoTag = new Laubrary.GoreLab.MemberTag { kind = Laubrary.GoreLab.MemberKind.Box, cx = 20, cy = 30.5, rx = 10, ry = 14.5, rz = 6, n = 4, angle = -System.Math.PI / 2, uy = -1, fz = 1 };
Laubrary.GoreLab.GoreTagMath.Normalize(ref headTag); Laubrary.GoreLab.GoreTagMath.Normalize(ref torsoTag);
var frame = new Laubrary.GoreLab.GoreFrameInput { grid = grid, members = new[] { new Laubrary.GoreLab.GoreMemberInput { present = true, tag = headTag }, new Laubrary.GoreLab.GoreMemberInput { present = true, tag = torsoTag } } };
var R = Laubrary.GoreLab.GoreRng.Rng(77);
var removers = new System.Collections.Generic.List<Laubrary.GoreLab.GoreRemover>();
for (int k = 0; k < 30; k++) { int m = k % 2; double yy = (R() * 2 - 1) * 0.8, z = (R() * 2 - 1) * 0.8, x1 = -0.2 + R() * 1.2; removers.Add(Laubrary.GoreLab.GoreRemovers.Capsule(-1.6, yy, z, x1, yy + (R() - 0.5) * 0.3, z, 0.12 + R() * 0.1, 0, m)); }
var cfg = new Laubrary.GoreLab.GoreCutConfig { seed = 1, jag = 1.2, jagFreq = 0.45, bone = true };
var style = Laubrary.GoreLab.GoreStyle.Fleshy(); var result = new Laubrary.GoreLab.GoreFrameResult();
for (int i = 0; i < 30; i++) Laubrary.GoreLab.GoreCut.CutFrame(frame, removers, cfg, style, result);
const int N = 200;
var sw = System.Diagnostics.Stopwatch.StartNew();
for (int i = 0; i < N; i++) Laubrary.GoreLab.GoreCut.CutFrame(frame, removers, cfg, style, result);
sw.Stop();
var headOnly = removers.FindAll(r => r.member == 0);
var sw2 = System.Diagnostics.Stopwatch.StartNew();
for (int i = 0; i < N; i++) Laubrary.GoreLab.GoreCut.CutFrame(frame, headOnly, cfg, style, result);
sw2.Stop();
return "editor: head+torso 30 pellets " + (sw.Elapsed.TotalMilliseconds / N).ToString("F3") + " ms/frame; head only 15 pellets " + (sw2.Elapsed.TotalMilliseconds / N).ToString("F3") + " ms/frame";
