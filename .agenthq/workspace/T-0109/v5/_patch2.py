# -*- coding: utf-8 -*-
import io
p = u"D:/UNITY/Laubrary Dev - Shaper/Assets/Packages/Laubrary/Editor/Shaper/ShaperHeightAudit.cs"
src = io.open(p, encoding='utf-8').read()

a = u"""                sb.AppendLine("      (pre-fix, same two rows: 4 omissions worst 5.2395 px, and 8 omissions worst 5.1270 px)");
"""
b = u"""                sb.AppendLine("      (pre-fix, same two rows, counted the old way: 4 unmatched worst 5.2395 px, and 8 worst 5.1270 px)");
sb.AppendLine("      (post-fix the AIMED row still shows 2 unmatched crossings, and they are NOT a regression:");
sb.AppendLine("       n=27 tilt 30 zf 0.1923 has a 0.0154 px AIR SLIVER between two Stepped treads, which is");
sb.AppendLine("       below SurfaceResolution and so inside HS-5.5's declared limit. Counting the FEATURE");
sb.AppendLine("       EXTENT rather than the distance to the nearest emitted crossing is what makes that");
sb.AppendLine("       visible - 2.566 px was never the size of anything, only how far away the march's");
sb.AppendLine("       nearest answer happened to be.)");
"""
assert src.count(a) == 1, "a %d" % src.count(a)
src = src.replace(a, b, 1)
io.open(p, 'w', encoding='utf-8', newline='').write(src)

q = u"D:/UNITY/Laubrary Dev - Shaper/Assets/Packages/Laubrary/Runtime/Shaper/ShaperResolve.cs"
r = io.open(q, encoding='utf-8').read()
c = u"""        /// Zero means every step the march took was a proof. Non-zero means the accuracy guarantee for those
        /// steps is the resolution one ("no feature of ray-extent \u2265 SurfaceResolution is missed") rather than
        /// the exact one. It is reported rather than swallowed because the pre-fix code did the opposite: it
        /// fell back on EVERY step, silently, and lost whole features."""
d = u"""        /// Non-zero means the accuracy guarantee for those steps is the resolution one ("no feature of
        /// ray-extent \u2265 SurfaceResolution is missed") rather than the exact one. It is reported rather than
        /// swallowed because the pre-fix code did the opposite: it fell back on EVERY step, silently, and
        /// lost whole features.
        ///
        /// <b>T-0109 FIX V4 \u2014 zero does NOT mean every step was a proof, and this comment used to say it did.</b>
        /// The bracket loop leaves by <c>if (sigma &lt;= SurfaceResolution) break;</c> as well as by exhausting
        /// <see cref="MaxBracketDepth"/>, and only the second is counted here. A step that narrowed to
        /// <c>SurfaceResolution</c> without ever closing the ambiguous shell is a SAMPLED step, not a proved
        /// one, and it increments nothing. Measured live: a ray at Stepped n = 27 reports
        /// <c>bracketCapped = 0</c> while stepping straight over a genuine 0.0154 px air sliver between two
        /// treads \u2014 correct under the declared guarantee, but not a proof. If a caller ever needs "was this
        /// answer exact?", a separate resolution-floored counter is what it would have to read."""
assert r.count(c) == 1, "c %d" % r.count(c)
r = r.replace(c, d, 1)
io.open(q, 'w', encoding='utf-8', newline='').write(r)
print("ok")
