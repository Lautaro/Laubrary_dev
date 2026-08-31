import io
p = r"D:/UNITY/Laubrary Dev - Shaper/Assets/Packages/Laubrary/Editor/Shaper/ShaperBorderAudit.cs"
s = io.open(p, encoding="utf-8").read()

# ── BT-10: replace the "grew == reach" magic-number assertion with a real containment bound ───────────
old = '''            float grew = pb.supportHalfW - pa.supportHalfW;
            bool cullOk = Mathf.Abs(grew - Wd) < 1e-3f;
            ok &= cullOk;
            sb.AppendLine("  supportHalfW (the culling box): " + pa.supportHalfW.ToString("F4") + " -> " +
                          pb.supportHalfW.ToString("F4") + ", grew " + grew.ToString("F4") +
                          " (expected the reach, " + Wd + ")  " + Verdict(cullOk));'''
assert old in s, "BT-10 growth block not found"
new = '''            float grew = pb.supportHalfW - pa.supportHalfW;
            bool cullOk = Mathf.Abs(grew - Wd) < 1e-3f;
            ok &= cullOk;
            sb.AppendLine("  supportHalfW (the culling box) on an ISOTROPIC node: " + pa.supportHalfW.ToString("F4") +
                          " -> " + pb.supportHalfW.ToString("F4") + ", grew " + grew.ToString("F4") +
                          " (expected the reach, " + Wd + ")  " + Verdict(cullOk));

            // ...and the assertion that actually matters, because "grew by the reach" is a magic number and a
            // BOUND is a containment claim. On a disc the two agree; on an anisotropically scaled member the
            // number is right and the bound is WRONG, which is how this defect survived a green audit.
            bool containOk = BoxContainment(sb, false);
            ok &= containOk;'''
s = s.replace(old, new, 1)

# ── BT-11: add the hostless-border ordering sub-leg ───────────────────────────────────────────────────
old11 = '''            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        // ── BT-12 ─'''
assert old11 in s, "BT-11 tail not found"
new11 = '''            // (d) BD-3.6 for a border whose node bound NO fill of its own. The contract does not name this
            //     case; the implementation had it landing ABOVE a later sibling that owned a fill, so an
            //     unrelated dial (does the outlined member also carry its own colour?) decided the z-order.
            bool hostlessOk = HostlessOrdering(sb, false);
            ok &= hostlessOk;

            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        // \u2500\u2500 BT-12 \u2500'''
s = s.replace(old11, new11, 1)

# ── BT-13: three new mutations ────────────────────────────────────────────────────────────────────────
oldm = '''            sb.AppendLine("  inline controls already asserted by their own legs: BT-1 live border, BT-6 grown " +
                          "anchor, BT-8 naive union, BT-9 joined twin, BT-10 grown anchor, BT-11 removed bag " +
                          "border, BT-12 Intersect member.");'''
assert oldm in s, "BT-13 tail not found"
newm = '''            // M9 - BT-10's containment measurement against the growth rule it replaced: the canvas box grown
            //      by the BARE reach. On an anisotropically scaled member that is an under-bound, and the leg
            //      must say so. Before this leg existed the same defect measured "grew 8.0000, expected 8" and
            //      reported PASS.
            {
                var sink = new StringBuilder();
                bool passed = BoxContainment(sink, true);
                bool detected = !passed;
                ok &= detected;
                sb.AppendLine("  M9  BT-10's box bound with the BARE reach (the shipped growth): measurement " +
                              "passed = " + passed + " (must be False)  detected " + detected + "  " +
                              Verdict(detected));
            }

            // M10 - BT-6's own falsifier, asserted here as well as inline: anchoring a border's fill on the
            //       STRIP's box instead of the node's must move the colour. If it does not, BD-3.2 is satisfied
            //       by every wiring and BT-6 is decoration - which is exactly what it was while
            //       ShaperBorder.CompileStrip copied the node's local box into the strip unchanged.
            {
                var probes = new[] { new Vector2(30f, 0f), new Vector2(-30f, 6f), new Vector2(0f, 31f),
                                     new Vector2(20f, 20f) };
                int diffs = StripAnchorWidthDiffs(LinearGradient(Color.black, Color.white), probes);
                bool detected = diffs > 0;
                ok &= detected;
                sb.AppendLine("  M10 BT-6's anchor taken from the STRIP: colour diffs " + diffs + "/" +
                              probes.Length + " (must be > 0)  detected " + detected + "  " + Verdict(detected));
            }

            // M11 - BT-11's hostless-ordering leg with the correction switched off at its point of use
            //       (borderSubtreeEnd cleared on the resolved owner). It must report FAIL, or the leg would go
            //       on passing after the fix was reverted.
            {
                var sink = new StringBuilder();
                bool passed = HostlessOrdering(sink, true);
                bool detected = !passed;
                ok &= detected;
                sb.AppendLine("  M11 BT-11's hostless border with the later-sibling mask disabled: measurement " +
                              "passed = " + passed + " (must be False)  detected " + detected + "  " +
                              Verdict(detected));
            }

            sb.AppendLine("  inline controls already asserted by their own legs: BT-1 live border, BT-6 strip " +
                          "anchor + grown anchor, BT-8 naive union, BT-9 joined twin, BT-10 grown anchor + bare-" +
                          "reach bound, BT-11 removed bag border + A-owns-a-fill, BT-12 Intersect member.");'''
s = s.replace(oldm, newm, 1)

io.open(p, "w", encoding="utf-8").write(s)
print("wired")
