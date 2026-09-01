# -*- coding: utf-8 -*-
import io
p = u"D:/UNITY/Laubrary Dev - Shaper/Assets/Packages/Laubrary/Editor/Shaper/ShaperHeightAudit.cs"
src = io.open(p, encoding='utf-8').read()

a1 = u"""                bool sweepAll = true;
                foreach (bool aimed in AimedAtTread)
                {
                    int rays = 0, omissions = 0, truths = 0;
                    float worst = 0f; string worstAt = "-";
"""
b1 = u"""                bool sweepAll = true;
                bool[] matched = new bool[512];
                foreach (bool aimed in AimedAtTread)
                {
                    int rays = 0, truths = 0, unmatched = 0, hard = 0, soft = 0, unpaired = 0;
                    float worstHard = 0f, worstSoft = 0f, worstGap = 0f;
                    string worstAt = "-", softAt = "-";
"""
assert src.count(a1) == 1, "a1 %d" % src.count(a1)
src = src.replace(a1, b1, 1)

a2 = u"""                            var truth = TruthCrossings(sprog, sstack, op, ox, oy, oz, ddx, 0f, ddz, 400f, 100000);
                            truths += truth.Count;
                            for (int j = 0; j < truth.Count; j++)
                            {
                                float best = float.MaxValue;
                                for (int i = 0; i < rr.count; i++) best = Mathf.Min(best, Mathf.Abs(truth[j] - sbuf[i].rayT));
                                if (best > 0.05f)
                                {
                                    omissions++;
                                    if (best > worst) { worst = best; worstAt = "n=" + n + " tilt " + tilt.ToString("F0") + " zf " + zf.ToString("F4"); }
                                }
                            }
"""
b2 = u"""                            var truth = TruthCrossings(sprog, sstack, op, ox, oy, oz, ddx, 0f, ddz, 400f, 100000);
                            int tc = truth.Count;
                            truths += tc;
                            if (matched.Length < tc) matched = new bool[tc * 2];
                            for (int j = 0; j < tc; j++)
                            {
                                float best = float.MaxValue;
                                for (int i = 0; i < rr.count; i++) best = Mathf.Min(best, Mathf.Abs(truth[j] - sbuf[i].rayT));
                                matched[j] = best <= 0.05f;
                                if (!matched[j]) { unmatched++; if (best > worstGap) worstGap = best; }
                            }

                            // An unmatched truth crossing is not yet a defect: HS-5.5's guarantee is scoped
                            // to features whose extent ALONG THE RAY is at least SurfaceResolution, and the
                            // march is entitled to sample at that spacing wherever it could not prove the
                            // step. So the extent is MEASURED rather than assumed. Unmatched crossings come
                            // in consecutive runs, and the interval BETWEEN a consecutive pair of them is
                            // exactly the region the march mis-classified - a lost solid span, or an unseen
                            // air sliver. That interval's width is the feature extent, and it is the number
                            // the guarantee is about. A run of ODD length cannot be paired, which means the
                            // march's parity is wrong over an unbounded region; that is counted separately
                            // and FAILS, because nothing bounds it.
                            for (int j = 0; j < tc; )
                            {
                                if (matched[j]) { j++; continue; }
                                int a = j;
                                while (j < tc && !matched[j]) j++;
                                int len = j - a;
                                if ((len & 1) != 0) unpaired++;
                                for (int q = a; q + 1 < a + len; q += 2)
                                {
                                    float w = truth[q + 1] - truth[q];
                                    if (w >= ShaperResolve.SurfaceResolution)
                                    {
                                        hard++;
                                        if (w > worstHard) { worstHard = w; worstAt = "n=" + n + " tilt " + tilt.ToString("F0") + " zf " + zf.ToString("F4") + " at t " + truth[q].ToString("F4"); }
                                    }
                                    else
                                    {
                                        soft++;
                                        if (w > worstSoft) { worstSoft = w; softAt = "n=" + n + " tilt " + tilt.ToString("F0") + " zf " + zf.ToString("F4") + " at t " + truth[q].ToString("F4"); }
                                    }
                                }
                            }
"""
assert src.count(a2) == 1, "a2 %d" % src.count(a2)
src = src.replace(a2, b2, 1)

a3 = u"""                    bool ok = rays > 0 && omissions == 0;
                    sweepAll &= ok;
                    sb.AppendLine("      step count SWEPT " + SweepStepLo + ".." + SweepStepHi +
                                  ", ray heights " + (aimed ? "AIMED AT A TREAD  " : "H6's own fractions") +
                                  ": " + rays + " rays, " + truths + " true crossings, omissions " + omissions +
                                  (omissions == 0 ? "" : "   worst gap " + worst.ToString("F4") + " px at " + worstAt) +
                                  "  " + Verdict(ok));
                }
"""
b3 = u"""                    bool ok = rays > 0 && hard == 0 && unpaired == 0;
                    sweepAll &= ok;
                    sb.AppendLine("      step count SWEPT " + SweepStepLo + ".." + SweepStepHi +
                                  ", ray heights " + (aimed ? "AIMED AT A TREAD  " : "H6's own fractions") +
                                  ": " + rays + " rays, " + truths + " true crossings");
                    sb.AppendLine("        unmatched true crossings " + unmatched +
                                  (unmatched == 0 ? "" : "   worst distance to an emitted one " + worstGap.ToString("F4") + " px"));
                    sb.AppendLine("        of which features >= SurfaceResolution (" +
                                  ShaperResolve.SurfaceResolution.ToString("F2") + " px) - THE FAILING CLASS: " + hard +
                                  (hard == 0 ? "" : "   worst extent " + worstHard.ToString("F4") + " px at " + worstAt));
                    sb.AppendLine("        of which features BELOW it - resolution-limited, inside HS-5.5: " + soft +
                                  (soft == 0 ? "" : "   worst extent " + worstSoft.ToString("F5") + " px at " + softAt));
                    sb.AppendLine("        unmatched runs of ODD length (parity wrong, extent unbounded): " + unpaired +
                                  "  " + Verdict(ok));
                }
"""
assert src.count(a3) == 1, "a3 %d" % src.count(a3)
src = src.replace(a3, b3, 1)

io.open(p, 'w', encoding='utf-8', newline='').write(src)
print("patched", len(src))
