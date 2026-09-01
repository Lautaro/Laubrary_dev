import io

p = r'D:\UNITY\Laubrary Dev - Shaper\Assets\Packages\Laubrary\Editor\Shaper\ShaperBorderAudit.cs'
s = io.open(p, encoding='utf-8').read()

old_a = """                bool heapOk = delta <= 0 && collections == 0;
                ok &= heapOk;
                sb.AppendLine("  heap over " + reps + " tiles x " + (W * H) + " samples = " + (reps * W * H) +
                              " samples: GetTotalMemory delta " + delta + " bytes (expected <= 0)" +
                              "  gen-0 collections " + collections + " (expected 0)  " + Verdict(heapOk));"""
new_a = """                // REPORTED, NOT ASSERTED, and the reason is measured rather than assumed. GC.GetTotalMemory is
                // process-wide, not scoped to this loop, so the editor's own background churn lands in the
                // delta: this leg was observed reporting 0 bytes on one run and 12,288 on the next, over a loop
                // the decode above proves contains no allocation opcode at all — and 12,288 is one of the two
                // figures FT-9 already recorded for the same instrument. Combined with M8's calibration, which
                // shows it blind to 1 MB of small transient garbage, it is unreliable in BOTH directions and
                // cannot carry a verdict. Reporting it anyway is not decoration: a delta in the megabytes here
                // would be worth chasing even though a small one means nothing.
                sb.AppendLine("  heap over " + reps + " tiles x " + (W * H) + " samples = " + (reps * W * H) +
                              " samples: GetTotalMemory delta " + delta +
                              " bytes, gen-0 collections " + collections +
                              " (process-wide, informational only - see BT-13 M8)");"""
assert s.count(old_a) == 1, 'heap block A not found'
s = s.replace(old_a, new_a)

old_b = """                bool callOk = delta2 <= 0 && collections2 == 0;
                ok &= callOk;
                sb.AppendLine("  heap over " + Calls + " calls on an 8x8 tile: GetTotalMemory delta " + delta2 +
                              " bytes (expected <= 0)  gen-0 collections " + collections2 +
                              " (expected 0)  " + Verdict(callOk));"""
new_b = """                sb.AppendLine("  heap over " + Calls + " calls on an 8x8 tile: GetTotalMemory delta " + delta2 +
                              " bytes, gen-0 collections " + collections2 +
                              " (process-wide, informational only - see BT-13 M8)");"""
assert s.count(old_b) == 1, 'heap block B not found'
s = s.replace(old_b, new_b)

old_c = """            // ── instrument 2: the heap, over a large loop, with its blind spot stated ──────────────────────
            //
            // Reported and asserted only for what it can actually see. Its calibration is BT-13's M8: on this
            // editor GC.GetTotalMemory(false) sees a single 64 KB array and is BLIND to 1 MB of 24-byte
            // transient objects. So a zero here bounds the large-object case and nothing finer, and the IL
            // decode above is what carries the claim. Saying so is the point — T-0106's allocation leg reported
            // a zero from an instrument it had already measured to be blind, and this file will not repeat it."""
new_c = """            // ── instrument 2: the heap, over a large loop — REPORTED, NEVER ASSERTED ──────────────────────
            //
            // It carries no verdict, and that is a finding rather than a caution. Its calibration is BT-13's
            // M8: on this editor GC.GetTotalMemory(false) is BLIND to 1 MB of 24-byte transient objects, which
            // is precisely the shape a per-sample allocation takes. And it is NOISY in the other direction:
            // this same loop reported 0 bytes on one run and 12,288 on the next with the code unchanged,
            // because the instrument is process-wide and the editor allocates in the background. An instrument
            // that cannot see the thing it is looking for AND moves when nothing happened cannot decide
            // anything. The IL decode above is what carries the claim; these two numbers are context."""
assert s.count(old_c) == 1, 'comment block C not found'
s = s.replace(old_c, new_c)

io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
