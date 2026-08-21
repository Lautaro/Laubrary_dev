using System.Collections.Generic;
using UnityEngine;
using Laubrary.Zoetrope;
using Laubrary.Launimator;

namespace Laubrary.ZoetropeLaunimator
{
    /// <summary>
    /// Derives the list of MEANINGFULLY DISTINCT named poses a Zoe can show — "Idle N", "Moving E" — by
    /// reading its already-authored <see cref="MotionPose"/> rules and <see cref="LauminationSet"/>s, rather
    /// than a separately-authored list a designer would have to keep in sync by hand. Built for the Mirage
    /// "freeze on this exact game state" preview (<see cref="MotionPoseAnimator.SetPoseOverride"/>) — the
    /// need this solves: a composite Zoe like ProtoGuy has independently-timed parts (legs follow Heading,
    /// torso follows Aim), so there is no single "current clip" to preview by name; a POSE is the combination
    /// across every part that a real game state (walking east, idling northwest) actually produces.
    ///
    /// Deliberately assumes every part's direction channel reads the SAME shared angle for a given pose (the
    /// common "not actively aiming independently" case — real gameplay already defaults aim to heading when
    /// there is no separate mouse input, see TopDownMotionDriver) rather than a full (heading × aim) cross
    /// product. A future "independent aim" preview mode can extend this; today it would just make the list
    /// enormous (16 × 16 per bucket) for no benefit in the common case.
    /// </summary>
    public static class MotionPoseCatalog
    {
        static readonly string[] DirLabels16 =
            { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };

        /// One derived, named pose: a (bucket, angle) pair every authored part can resolve via
        /// <see cref="MotionPoseResolver.ResolveAt"/>.
        public readonly struct Pose
        {
            public readonly string label;
            public readonly MotionCondition bucket;
            public readonly float angleDeg;
            public Pose(string label, MotionCondition bucket, float angleDeg)
            { this.label = label; this.bucket = bucket; this.angleDeg = angleDeg; }
        }

        readonly struct PartRef
        {
            public readonly MotionPose pose;
            public readonly LauminaryVersion version;
            public PartRef(MotionPose pose, LauminaryVersion version) { this.pose = pose; this.version = version; }
        }

        /// Every authored (MotionPose, LauminaryVersion) pair on <paramref name="zoe"/> — one for a single-body
        /// Zoe (its own <see cref="Zoe.motionPose"/> + whatever LauminaryVersion its ZonedLauminaryView plays),
        /// one per part for a <see cref="CompositeLauminaryView"/>. Parts with no authored pose or no zoned
        /// view are skipped — they have nothing to contribute to a pose (matches ZoeSpawner's own "only attach
        /// MotionPoseAnimator when authored" rule).
        static List<PartRef> CollectParts(Zoe zoe)
        {
            var parts = new List<PartRef>();
            if (zoe == null) return parts;

            if (zoe.view is CompositeLauminaryView composite && composite.parts != null)
            {
                foreach (var p in composite.parts)
                {
                    if (p == null || p.motionPose == null || !p.motionPose.IsAuthored) continue;
                    if (p.view is ZonedLauminaryView zlv && zlv.version != null)
                        parts.Add(new PartRef(p.motionPose, zlv.version));
                }
            }
            else if (zoe.motionPose != null && zoe.motionPose.IsAuthored && zoe.view is ZonedLauminaryView single && single.version != null)
            {
                parts.Add(new PartRef(zoe.motionPose, single.version));
            }
            return parts;
        }

        /// Every distinct non-Always condition referenced by ANY part's rules — the buckets worth naming
        /// separately (Idle/Moving/...). Falls back to a single implicit bucket (Always) when no part
        /// distinguishes by state at all, so a Zoe with only one rule per part still gets a plain "N".."NNW"
        /// list instead of an empty catalog.
        static List<MotionCondition> CollectBuckets(List<PartRef> parts)
        {
            var buckets = new List<MotionCondition>();
            foreach (var part in parts)
            {
                if (part.pose.rules == null) continue;
                foreach (var rule in part.pose.rules)
                {
                    if (rule == null || rule.condition == MotionCondition.Always) continue;
                    if (!buckets.Contains(rule.condition)) buckets.Add(rule.condition);
                }
            }
            if (buckets.Count == 0) buckets.Add(MotionCondition.Always);
            return buckets;
        }

        /// For one part, in one bucket: the canonical-angle INDEX (0..15) representing each visually distinct
        /// outcome across all 16 canonical probe angles — e.g. a 3-member mirrored walk set collapses to a
        /// handful of representative indices even though every canonical angle resolves to SOMETHING (the
        /// nearest-member snap), because most of them repeat an already-seen outcome. The representative index
        /// is <see cref="LauminationResolution.ResolvedAngleDeg"/> — the AUTHORED member's own angle (already
        /// correctly reflecting NearestMember's mirroring, e.g. a mirrored member's resolved angle sits on the
        /// mirrored side) — not the probe angle that happened to be first to surface it, which for a sparse
        /// set can land on an arbitrary boundary angle (e.g. 67.5°) instead of the pose's own natural center
        /// (90° = E) and would otherwise produce a confusing label like "Moving ENE" for what is really "E".
        static List<int> RepresentativeIndices(PartRef part, MotionCondition bucket)
        {
            var seen = new HashSet<(string clip, string zone, bool flip, int rotSnap, int frame)>();
            var indices = new List<int>();
            for (int i = 0; i < 16; i++)
            {
                float probeAngle = i * 22.5f;
                var res = MotionPoseResolver.ResolveAt(part.pose, part.version, bucket, probeAngle);
                if (res.Laumination == null) continue;
                // FrameIndex belongs in the key alongside ZoneName: on a DirectionMode.Rotation sheet every
                // direction shares one clip name, an empty zone, no flip and no rotation, so without it all
                // 16 probes would dedup down to a SINGLE entry and the pose picker would offer one pose for
                // a 16-way character.
                var key = (res.Laumination.name, res.ZoneName ?? "", res.FlipX,
                           Mathf.RoundToInt(res.RotationDeg), res.FrameIndex);
                if (!seen.Add(key)) continue;
                int labelIndex = ((Mathf.RoundToInt(res.ResolvedAngleDeg / 22.5f) % 16) + 16) % 16;
                if (!indices.Contains(labelIndex)) indices.Add(labelIndex);
            }
            indices.Sort();
            return indices;
        }

        /// Every distinct pose this Zoe can show — buckets it actually authors × the COARSEST part's
        /// representative directions for that bucket (see class doc for why "coarsest wins": a fine-grained
        /// part just repeats labels a coarse part already distinguishes, so the coarse part's count IS the
        /// number of genuinely different outcomes to offer).
        public static List<Pose> Derive(Zoe zoe)
        {
            var result = new List<Pose>();
            var parts = CollectParts(zoe);
            if (parts.Count == 0) return result;

            var buckets = CollectBuckets(parts);
            bool singleImplicitBucket = buckets.Count == 1 && buckets[0] == MotionCondition.Always;

            foreach (var bucket in buckets)
            {
                List<int> coarsest = null;
                foreach (var part in parts)
                {
                    var rep = RepresentativeIndices(part, bucket);
                    if (rep.Count == 0) continue;
                    if (coarsest == null || rep.Count < coarsest.Count) coarsest = rep;
                }
                if (coarsest == null) continue;

                foreach (var i in coarsest)
                {
                    string label = singleImplicitBucket ? DirLabels16[i] : $"{bucket} {DirLabels16[i]}";
                    result.Add(new Pose(label, bucket, i * 22.5f));
                }
            }
            return result;
        }
    }
}
