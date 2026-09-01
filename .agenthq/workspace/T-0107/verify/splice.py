import io

p = r'D:\UNITY\Laubrary Dev - Shaper\Assets\Packages\Laubrary\Editor\Shaper\ShaperBorderAudit.cs'
s = io.open(p, encoding='utf-8').read()

# 1. replace M7 in BT-13
old_m7 = """            // M7 — BT-14's allocation instrument, calibrated against a KNOWN allocation. T-0106's first
            //      allocation instrument was blind to 64 KB; an uncalibrated zero proves nothing.
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                object sink = new byte[64 * 1024];
                long seen = GC.GetAllocatedBytesForCurrentThread() - before;
                bool detected = seen >= 64 * 1024 && sink != null;
                ok &= detected;
                sb.AppendLine("  M7  BT-14's instrument on a known 65536-byte allocation: saw " + seen +
                              " bytes (must be >= 65536)  detected " + detected + "  " + Verdict(detected));
            }
"""
assert s.count(old_m7) == 1, 'M7 block not found'

new_m7 = """            // M7 — BT-14's allocation instrument, run against code that PROVABLY allocates. This is the check
            //      that already earned its keep: the first version of BT-14 measured with
            //      GC.GetAllocatedBytesForCurrentThread, this leg reported 0 bytes seen for a known
            //      65,536-byte allocation, and the method turned out to be an unimplemented stub returning 0
            //      on this Mono. A green BT-14 on that instrument would have been exactly the un-failable test
            //      BT-13 exists to catch. The replacement is an exact IL decoder, and its ability to report a
            //      non-zero is measured here rather than assumed.
            {
                int fromStrip = CountAllocations(typeof(ShaperBorder).GetMethod("CompileStrip"), out string d1);
                int fromSummary = CountAllocations(typeof(ShaperFillDocument).GetMethod("Summary"), out string d2);
                bool detected = fromStrip > 0 && fromSummary > 0;
                ok &= detected;
                sb.AppendLine("  M7  BT-14's IL decoder on methods that DO allocate: ShaperBorder.CompileStrip = " +
                              fromStrip + " [" + d1 + "], ShaperFillDocument.Summary = " + fromSummary +
                              " [" + d2 + "]  both must be > 0  detected " + detected + "  " + Verdict(detected));
            }

            // M8 — the heap instrument BT-14 reports ALONGSIDE the decoder, calibrated on this editor so that
            //      its zero is read with the right amount of weight. Measured 2026-08-31:
            //      GC.GetTotalMemory(false) sees one 65,536-byte array, sees 1 MB of 1 KB arrays, and is
            //      COMPLETELY BLIND to 1 MB of 24-byte arrays — small transient garbage is recycled in the
            //      nursery without the used heap moving at all. So a zero from it bounds only the large-object
            //      case, and that is exactly how BT-14 labels it.
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long b0 = GC.GetTotalMemory(false);
                object big = new byte[64 * 1024];
                long sawBig = GC.GetTotalMemory(false) - b0;

                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long b1 = GC.GetTotalMemory(false);
                object sink = null;
                for (int i = 0; i < 43690; i++) sink = new byte[24];      // ~1 MB of small transient garbage
                long sawSmall = GC.GetTotalMemory(false) - b1;

                bool detected = sawBig > 0 && big != null && sink != null;
                ok &= detected;
                sb.AppendLine("  M8  GC.GetTotalMemory(false) calibration: one 65536-byte array -> " + sawBig +
                              " bytes seen (must be > 0);  1 MB of 24-byte arrays -> " + sawSmall +
                              " bytes seen (a 0 here is the KNOWN blind spot, reported not asserted)  " +
                              Verdict(detected));
            }
"""
s = s.replace(old_m7, new_m7)

# 2. replace the whole BT-14 body up to the contact sheet banner
start_marker = "        public static string BT14_NoAllocation()"
end_marker = "        // \u2500\u2500 the contact sheet \u2500\u2500"
start = s.index(start_marker)
end = s.index(end_marker)
new_bt14 = io.open(r'D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0107\verify\bt14_new.txt', encoding='utf-8').read()
s = s[:start] + new_bt14 + s[end:]

io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('spliced ok, length', len(s))
