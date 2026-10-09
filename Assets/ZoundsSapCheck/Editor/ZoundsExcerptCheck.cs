// A kept check for playing part of a sound (T-0565 / T-0563): a Zequence track's own excerpt, and a play started part-way.
//
//   1. A track with its own excerpt reads exactly that part of the source: its read head starts at the excerpt's start, and
//      its play lasts the excerpt's length.
//   2. An excerpt of a sound whose curves are still anchored to "a fraction of its trim" hears each curve over the audio it
//      is drawn over: the samples of the excerpt equal the same stretch of an ordinary play of the whole trim. The saved
//      sound's curves are left exactly as they were.
//   3. Starting a Zequence part-way (Play from here): a track already sounding starts that far into its audio; a later
//      track keeps its place (its wait shortened by the same amount); a track that would already be over does not play.
// In-memory sounds only (copies of a real Klip, never added to the library, never saved), and a generated tone for 2.
using System;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds;
using Laubrary.Zounds.Dsp;

public static class ZoundsExcerptCheck {

    [MenuItem("Laubrary/Zounds/Checks/31 - Playing part of a sound (track excerpt, play from here)")]
    public static void RunFromMenu() { Debug.Log(Execute()); }

    const int SR = 48000;

    static float FirstHead(ZoundToken t) {
        var buf = new double[8]; var w = new float[8];
        if (t == null || !(t.audioSource != null && t.audioSource.generator is ZoundSapVoiceGenerator g)) return float.NaN;
        int n = g.ReadSourcePositions(buf, w);
        return n > 0 ? (float)buf[0] : float.NaN;
    }

    static bool Child(ZoundToken t, CompositeZound.ZoundEntry e, out ZoundToken child) {
        var mi = typeof(ZoundToken).GetMethod("TryGetEntryToken", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var a = new object[] { e, null };
        bool ok = (bool)mi.Invoke(t, a);
        child = a[1] as ZoundToken;
        return ok && child != null;
    }

    public static string Execute() {
        var sb = new StringBuilder();
        int fail = 0;
        void Check(bool ok, string what) { sb.Append(ok ? "  ok   " : "  FAIL ").Append(what).Append('\n'); if (!ok) fail++; }

        Klip src = null;
        foreach (var z in ZoundsProject.Instance.zoundLibrary.GetAllZounds()) if (z is Klip k && ZoundSapPlayback.LoadSourceClip(k, out bool pre) != null && !pre) { src = k; break; }
        if (src == null) return "SKIPPED - no playable Klip in this project\n";
        float S = ZoundSapPlayback.LoadSourceClip(src).length;

        Klip Copy(int id, string name) {
            var c = JsonUtility.FromJson<Klip>(JsonUtility.ToJson(src));
            typeof(Zound).GetField("id").SetValue(c, id);
            c.name = name;
            c.effectChain = new ZoundEffectChain();   // plain: the lengths below are then exact
            c.ownCurves = null;   // plain too: the copied sound's own Volume/Pitch/Time curves would change every length below
            c.chainPresetId = 0;
            c.minPitch = c.maxPitch = 1f; c.minVolume = c.maxVolume = 1f;
            c.trimEnabled = false;
            if (c.timeStretch != null) c.timeStretch.liveEnabled = false;
            return c;
        }
        var args = new ZoundArgs { startImmediately = true, volumeOverride = 0f, pitchOverride = 1f, chanceOverride = 1f, ignoreCooldown = true, bypassGlobalSolo = true };

        // ── 1: a track's own excerpt ──
        float a = Mathf.Min(0.40f, S * 0.2f), b = Mathf.Min(1.10f, S * 0.6f);
        {
            var zeq = new Zequence(-9600) { name = "excerpt check (in memory)", mode = CompositeZound.Mode.Parallel };
            var c = Copy(-9601, "piece");
            zeq.localKlips.Add(c);
            var e = new CompositeZound.ZoundEntry { zoundId = c.id, local = true, ownTrim = true, trimStart = a, trimEnd = b, overridePitch = true, overrideVolume = true };
            zeq.zoundEntries.Add(e);
            var t = ZoundEngine.PlayToken(zeq, args);
            Child(t, e, out var child);
            float head = FirstHead(child);
            float dur = child != null ? child.duration : -1f;
            ZoundSapPlayback.TryGetPlayLength(c, ZoundSapPlayback.Excerpt.Of(a, b), out float planned);
            t.Kill();
            Check(Mathf.Abs(head - a) < 0.03f, "1. the track's read head starts at its excerpt's start (" + head.ToString("0.000") + " s, excerpt " + a.ToString("0.000") + "-" + b.ToString("0.000") + " s)");
            Check(Mathf.Abs(dur - (b - a)) < 0.01f && Mathf.Abs(planned - (b - a)) < 0.01f,
                  "1. its play lasts the excerpt (" + dur.ToString("0.000") + " s played, " + planned.ToString("0.000") + " s worked out, " + (b - a).ToString("0.000") + " s expected)");
        }

        // ── 2: an excerpt of a sound with trim-anchored curves ──
        {
            int n = 2 * SR; var tone = new float[n];
            for (int i = 0; i < n; i++) tone[i] = 0.5f * Mathf.Sin(2f * Mathf.PI * 330f * i / SR);
            var chain = new ZoundEffectChain();
            var gain = new ZoundEffectNode(ZoundEffectType.Gain); gain.p[0] = 1f; chain.nodes.Add(gain);
            var vol = new ZoundModifier(ZoundModifierType.Envelope) { name = "Volume" }; vol.p[0] = 0f; vol.p[1] = 0f;
            var vp = vol.curve.GetPointsList(); vp.Clear();
            vp.Add(new ZUIEnvelopePoint(0f, 1f, 1f)); vp.Add(new ZUIEnvelopePoint(0.5f, 0.15f, 2f)); vp.Add(new ZUIEnvelopePoint(1f, 0.8f, 1f));
            chain.modifiers.Add(vol);
            chain.bindings.Add(new ZoundModifierBinding { modifierIndex = 0, nodeIndex = -1, paramIndex = SourceStageParam.Volume,
                combine = ModulationCombine.Scale, depth = 1f, schema = ChainModulationCompat.CURRENT_SCHEMA });
            var k = Copy(-9602, "legacy curves");
            k.effectChain = chain; k.trimEnabled = true; k.trimStart = 0.5f; k.trimEnd = 1.5f;
            var own = new CurveAnchor.Axis { trimStart = 0.5f, trimEnd = 1.5f, sourceLength = 2f };
            var plan = ZoundSapPlayback.Plan(k, 0.8 * SR, 1.2 * SR, SR, n, own);
            var whole = ZoundDspOffline.Render(tone, 1, SR, SR, chain, 1f, 1f, 1.0f, 0.5f, 1.5f);
            var part = ZoundDspOffline.Render(tone, 1, SR, SR, plan.chain, 1f, 1f, 0.4f, 0.8f, 1.2f);
            float diff = 0f;
            // From 50 ms in: every play starts with a short fade-in against clicks, which the middle of a longer play has not.
            int off = (int)(0.3f * SR), skip = SR / 20, len = (int)(0.4f * SR) - 64;
            for (int i = skip; i < len; i++) diff = Mathf.Max(diff, Mathf.Abs(whole.left[off + i] - part.left[i]));
            Check(diff < 1e-3f, "2. an excerpt (0.8-1.2 s) of a 0.5-1.5 s trim with an old-style curve equals that stretch of the whole play after the opening fade-in (largest difference " + diff.ToString("0.0e0") + ")");
            Check(chain.modifiers[0].curveAnchor == CurveAnchor.Trim && !ReferenceEquals(plan.chain, chain),
                  "2. the play used a converted copy; the sound's own curve is untouched (still anchored to its trim)");
        }

        // ── 3: Play from here ──
        {
            var zeq = new Zequence(-9610) { name = "play-from check (in memory)", mode = CompositeZound.Mode.Parallel };
            var c0 = Copy(-9611, "early"); var c1 = Copy(-9612, "late"); var c2 = Copy(-9613, "over");
            zeq.localKlips.Add(c0); zeq.localKlips.Add(c1); zeq.localKlips.Add(c2);
            var e0 = new CompositeZound.ZoundEntry { zoundId = c0.id, local = true, ownTrim = true, trimStart = a, trimEnd = b, overridePitch = true, overrideVolume = true };
            var e1 = new CompositeZound.ZoundEntry { zoundId = c1.id, local = true, delay = 1.0f, ownTrim = true, trimStart = a, trimEnd = b, overridePitch = true, overrideVolume = true };
            var e2 = new CompositeZound.ZoundEntry { zoundId = c2.id, local = true, ownTrim = true, trimStart = 0f, trimEnd = 0.1f, overridePitch = true, overrideVolume = true };
            zeq.zoundEntries.Add(e0); zeq.zoundEntries.Add(e1); zeq.zoundEntries.Add(e2);
            var from = args; from.startAt = 0.25f;
            var t = ZoundEngine.PlayToken(zeq, from);
            Child(t, e0, out var t0);
            bool has1 = Child(t, e1, out var t1);
            bool has2 = Child(t, e2, out var t2) && t2 != null;
            float head0 = FirstHead(t0);
            float dur0 = t0 != null ? t0.duration : -1f, dur1 = t1 != null ? t1.duration : -1f;
            float whole = t.duration;
            t.Kill();
            Check(Mathf.Abs(head0 - (a + 0.25f)) < 0.03f && Mathf.Abs(dur0 - (b - a - 0.25f)) < 0.01f,
                  "3. the sounding track starts 0.25 s into its audio (head " + head0.ToString("0.000") + " s) and plays the rest (" + dur0.ToString("0.000") + " s)");
            Check(has1 && Mathf.Abs(dur1 - (b - a)) < 0.01f && Mathf.Abs(whole - (0.75f + (b - a))) < 0.02f,
                  "3. the later track plays whole, 0.75 s in (the Zequence lasts " + whole.ToString("0.000") + " s, expected " + (0.75f + b - a).ToString("0.000") + ")");
            Check(!has2, "3. the track that ends at 0.1 s does not play");
        }

        return (fail == 0 ? "PASS" : "FAIL (" + fail + ")") + " - playing part of a sound\n" + sb;
    }
}
