// Console harness for the GoreLab engine outside Unity: runs every golden case, then times a typical frame cut.
// Build + run: _incoming\P1\harness\run.sh (uses the C# compiler and .NET runtime bundled with the Unity editor).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Laubrary.GoreLab;
using Laubrary.GoreLab.Tests;

static class Program
{
    static int Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : Path.Combine("..", "..", "..", GoreGoldenChecker.FileName);
        var results = GoreGoldenChecker.RunAll(File.ReadAllText(path));
        int pass = 0, fail = 0, skip = 0;
        foreach (var r in results)
        {
            if (r.status == "PASS") pass++; else if (r.status == "FAIL") fail++; else skip++;
            Console.WriteLine(r);
        }
        Console.WriteLine($"TOTAL cases={results.Count} pass={pass} fail={fail} skip={skip}");
        Bench();
        return fail == 0 ? 0 : 1;
    }

    // A 40x50 sprite with a head (ball) and a torso (box), 30 pellets (15 per member) as capsules, one group and then two groups.
    static void Bench()
    {
        const int W = 40, H = 50;
        var grid = new GoreGrid(W, H);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                double hx = (x + 0.5 - 20) / 7, hy = (y + 0.5 - 9) / 7.5;
                bool head = hx * hx + hy * hy <= 1, torso = x >= 10 && x <= 29 && y >= 16 && y <= 44, arm = (x >= 4 && x <= 9 || x >= 30 && x <= 35) && y >= 18 && y <= 36;
                if (head || torso || arm) grid.px[y * W + x] = GoreDefaults.Rgb(120 + (int)(GoreRng.Hash(x, y, 3) * 40), 80, 60);
            }
        var headTag = new MemberTag { kind = MemberKind.Ball, cx = 20, cy = 9, rx = 7, ry = 7.5, n = 2, angle = -Math.PI / 2, uy = -1, fz = 1 };
        var torsoTag = new MemberTag { kind = MemberKind.Box, cx = 20, cy = 30.5, rx = 10, ry = 14.5, rz = 6, n = 4, angle = -Math.PI / 2, uy = -1, fz = 1 };
        GoreTagMath.Normalize(ref headTag); GoreTagMath.Normalize(ref torsoTag);
        var frame = new GoreFrameInput { grid = grid, members = new[] { new GoreMemberInput { present = true, tag = headTag }, new GoreMemberInput { present = true, tag = torsoTag } } };

        var R = GoreRng.Rng(77);
        var removers = new List<GoreRemover>();
        for (int k = 0; k < 30; k++)
        {
            int m = k % 2;
            double y = (R() * 2 - 1) * 0.8, z = (R() * 2 - 1) * 0.8, x0 = -1.6, x1 = -0.2 + R() * 1.2;
            removers.Add(GoreRemovers.Capsule(x0, y, z, x1, y + (R() - 0.5) * 0.3, z, 0.12 + R() * 0.1, 0, m));
        }
        var cfg = new GoreCutConfig { seed = 1, jag = 1.2, jagFreq = 0.45, bone = true };
        var style = GoreStyle.Fleshy();
        var result = new GoreFrameResult();

        Time("30 pellets, one group", frame, removers, cfg, style, result);
        var two = new List<GoreRemover>(removers);
        for (int k = 15; k < 30; k++) { var r = two[k]; r.group = 1; two[k] = r; }
        Time("30 pellets, two groups", frame, two, cfg, style, result);
        var slice = new List<GoreRemover>(removers) { GoreRemovers.Plane(0, 1, 0, 0.2, 1, 1) };
        Time("30 pellets + a torso slice (newest)", frame, slice, cfg, style, result);
        Time("2 pellets (1 per member): baseline cost of the solids", frame, removers.GetRange(0, 2), cfg, style, result);
        var headOnly = new List<GoreRemover>(); foreach (var r in removers) if (r.member == 0) headOnly.Add(r);
        Time("15 pellets on the head only", frame, headOnly, cfg, style, result);
    }

    static void Time(string label, GoreFrameInput frame, List<GoreRemover> removers, GoreCutConfig cfg, GoreStyle style, GoreFrameResult result)
    {
        for (int i = 0; i < 50; i++) GoreCut.CutFrame(frame, removers, cfg, style, result);   // warm-up (JIT, scratch buffers)
        const int N = 400;
        long alloc0 = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < N; i++) GoreCut.CutFrame(frame, removers, cfg, style, result);
        sw.Stop();
        long alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
        Console.WriteLine($"BENCH {label}: {sw.Elapsed.TotalMilliseconds / N:F3} ms per frame, changed={result.changed}, chunk={result.chunkCount}, pieces={result.pieces.Count}, gibs={result.gibs.Count}, bleed={result.bleed.Count}, alloc/cut={alloc / N} B");
    }
}
