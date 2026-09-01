import io

# ── 1. BT-12: add the BD-3.4 ancestor-clip measurement ────────────────────────────────────────────────
pa = r"D:/UNITY/Laubrary Dev - Shaper/Assets/Packages/Laubrary/Editor/Shaper/ShaperBorderAudit.cs"
s = io.open(pa, encoding="utf-8").read()

old = '''                sb.AppendLine("  an Intersect member MAY own a border: border owners " + owners +
                              " (expected 1), subtract diagnostic " + doc.hasSubtractBorder +
                              " (expected False)  " + Verdict(intersectOk));
            }

            sb.Append("  RESULT: " + Verdict(ok));'''
assert old in s, "BT-12 tail not found"
new = '''                sb.AppendLine("  an Intersect member MAY own a border: border owners " + owners +
                              " (expected 1), subtract diagnostic " + doc.hasSubtractBorder +
                              " (expected False)  " + Verdict(intersectOk));
            }

            // BD-3.4, which had no measurement of its own: a member's rim MUST NOT survive in a region a LATER
            // Subtract member carved out of the bag. The clip that stops it is `min(coverage_strip,
            // coverage_ancestor)` against the nearest binding ancestor OF THE NODE - never against the node
            // itself, which for an Outward strip would clip the whole border away. Without it a rim would be
            // left floating in a hole, which is FC-3.3's problem in a new costume.
            {
                var a = Disc("A", 34f);
                a.fill = Solid(new Color(0.1f, 0.3f, 0.1f));
                a.border = Border(ShaperShellAlignment.Inward, 6f, false, Solid(Color.magenta));
                var hole = Disc("Hole", 24f, 34f, 0f, ShaperCombineMode.Subtract);
                var bag = ShaperNode.Bag("Carved", ShaperCombineMode.Add, a, hole);
                bag.fill = Solid(Color.white);
                var rig = Build(bag); Paint(rig);

                // A's inward rim runs near x = +31 on the right, which the Subtract disc covers, and near
                // x = -31 on the left, which it does not.
                Color inHole = LinearAt(rig, 31f, 0f);
                Color intact = LinearAt(rig, -31f, 0f);
                bool clipOk = inHole.a < 0.01f && intact.a > 0.99f &&
                              Mathf.Abs(intact.r - LinearOf(Color.magenta).r) < 0.02f;
                ok &= clipOk;
                sb.AppendLine("  BD-3.4 ancestor clip: the member's rim inside a LATER Subtract member's hole has" +
                              " alpha " + inHole.a.ToString("F4") + " (expected 0); on the intact side alpha " +
                              intact.a.ToString("F4") + " and it is the rim colour (expected 1)  " + Verdict(clipOk));
            }

            sb.Append("  RESULT: " + Verdict(ok));'''
s = s.replace(old, new, 1)

# ── 2. BT-14's XML doc still names an instrument the body does not use ────────────────────────────────
old14 = '''        /// <b>Measured with <see cref="GC.GetAllocatedBytesForCurrentThread"/> and not with
        /// <c>GC.GetTotalMemory</c>.</b> T-0106 measured the latter, calibrated it, and found it BLIND TO 64 KB
        /// of real allocation on this editor \u2014 a zero from it is consistent with the claim and does not establish
        /// it. <c>GetAllocatedBytesForCurrentThread</c> is a running total of every allocation this thread has
        /// made, so it sees a single 24-byte box, and it is scoped to this thread rather than to the process, so
        /// the editor's background churn does not land in the delta. Its own sensitivity is proven in BT-13's M7.'''
assert old14 in s, "BT-14 doc block not found"
new14 = '''        /// <b>The claim is carried by the IL DECODE, not by a heap number.</b> This doc block used to say the
        /// leg measured <c>GC.GetAllocatedBytesForCurrentThread</c>; it does not and never did - that API
        /// returns 0 on this Mono, which is why the body fell back to <c>GC.GetTotalMemory</c>. The stale
        /// sentence is corrected rather than deleted because it named the right instrument for the wrong
        /// runtime, and the next reader would otherwise reach for it again. <c>GetTotalMemory</c> is reported
        /// and NEVER asserted: BT-13's M8 shows it blind to 1 MB of small transient garbage, and it has been
        /// observed reporting 0 on one run and 12,288 on the next with the code unchanged. What establishes the
        /// claim is a transitive decode of the call graph, whose sensitivity is proven by M7.'''
s = s.replace(old14, new14, 1)
io.open(pa, "w", encoding="utf-8").write(s)
print("audit doc + BT-12 updated")

# ── 3. ShaperBorder's class doc repeats BD-1.6's over-claim ───────────────────────────────────────────
pb = r"D:/UNITY/Laubrary Dev - Shaper/Assets/Packages/Laubrary/Runtime/Shaper/ShaperBorder.cs"
t = io.open(pb, encoding="utf-8").read()
oldb = '''    /// silhouette it traces (BT-4); near the boundary an OUTWARD strip's field is <c>\u2212d</c>, and
    /// <c>smoothstep</c> is odd about its midpoint, so <c>Coverage(\u2212d, h) = 1 \u2212 Coverage(d, h)</c> identically
    /// and the pair sums to exactly 1 at every sample (BT-5).'''
assert oldb in t, "ShaperBorder doc block not found"
newb = '''    /// silhouette it traces (BT-4); near the boundary an OUTWARD strip's field is <c>\u2212d</c>, and
    /// <c>smoothstep</c> is odd about its midpoint, so <c>Coverage(\u2212d, h) = 1 \u2212 Coverage(d, h)</c> in real
    /// arithmetic and the pair sums to 1 to within ONE ULP in float (BT-5). BD-1.6 says "exactly 1 at every
    /// sample" and that is an over-claim: <c>fl(0.5 \u2212 d)</c> is not <c>1 \u2212 fl(d + 0.5)</c>, and the cubic is then
    /// evaluated at two slightly different points. Swept over 400,001 values of <c>d</c> across the band the
    /// float sum differs from 1 at 10,018 of them, always by exactly one ULP; on a TRIANGLE every one of the
    /// 136 ring samples differs, while on a DISC none of the 208 do - which is why the first version of BT-5,
    /// measured on a disc alone, reported the over-claim as verified.'''
t = t.replace(oldb, newb, 1)
io.open(pb, "w", encoding="utf-8").write(t)
print("ShaperBorder doc corrected")
