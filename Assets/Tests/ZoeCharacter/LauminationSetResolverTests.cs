using NUnit.Framework;
using UnityEngine;
using Laubrary.Launimator;

namespace Laubrary.ZoeCharacter.Tests
{
    /// <summary>
    /// Unit tests for LauminationSetResolver — the animation spine the design docs call out as
    /// "the same mechanism for shooting AND walking". Phase 2's deliverable; pins down every
    /// DirectionMode, the mirror-built-in economics ("16 aim = 9 authored"), snap-step quantization,
    /// and LatchedDirection behavior.
    /// </summary>
    public class LauminationSetResolverTests
    {
        // Helper: build a laumination with a given name (the resolver reads no fields off Laumination
        // beyond identity, so an empty one is fine for resolution tests).
        static Laumination MakeAnim(string n) { var a = new Laumination { name = n }; return a; }
        static LauminationSetMember MakeMember(float angle, string n)
            => new LauminationSetMember { angleDegrees = angle, laumination = MakeAnim(n) };

        // ─────────────────────────────────────────────────────────────────────────────
        //  Null / empty inputs
        // ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Resolve_NullSet_ReturnsNone()
        {
            var r = LauminationSetResolver.Resolve(null, 90f);
            Assert.AreEqual(LauminationResolution.None, r);
        }

        [Test]
        public void Resolve_EmptyMembers_ReturnsNone()
        {
            var set = new LauminationSet { directionMode = DirectionMode.None, members = new System.Collections.Generic.List<LauminationSetMember>() };
            var r = LauminationSetResolver.Resolve(set, 0f);
            Assert.AreEqual(LauminationResolution.None, r);
        }

        [Test]
        public void Resolve_MemberWithNullLaumination_IsSkipped()
        {
            var set = new LauminationSet
            {
                directionMode = DirectionMode.None,
                members = new System.Collections.Generic.List<LauminationSetMember>
                {
                    new LauminationSetMember { angleDegrees = 0f, laumination = null },
                    new LauminationSetMember { angleDegrees = 90f, laumination = MakeAnim("E") }
                }
            };
            var r = LauminationSetResolver.Resolve(set, 90f);
            Assert.IsNotNull(r.Laumination);
            Assert.AreEqual("E", r.Laumination.name);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //  DirectionMode.None
        // ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public void None_AlwaysReturnsSameMemberRegardlessOfAngle()
        {
            var set = new LauminationSet
            {
                directionMode = DirectionMode.None,
                members = new System.Collections.Generic.List<LauminationSetMember> { MakeMember(0f, "Idle") }
            };
            foreach (float a in new[] { 0f, 45f, 90f, 180f, 270f, 359f })
            {
                var r = LauminationSetResolver.Resolve(set, a);
                Assert.AreEqual("Idle", r.Laumination.name, $"angle {a}°");
                Assert.IsFalse(r.FlipX, $"angle {a}°");
                Assert.AreEqual(0f, r.RotationDeg, $"angle {a}°");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //  DirectionMode.Mirror
        // ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Mirror_AnglesBelow180_NoFlip()
        {
            var set = new LauminationSet
            {
                directionMode = DirectionMode.Mirror,
                members = new System.Collections.Generic.List<LauminationSetMember> { MakeMember(0f, "Body") }
            };
            foreach (float a in new[] { 0f, 45f, 90f, 179.9f })
            {
                var r = LauminationSetResolver.Resolve(set, a);
                Assert.AreEqual("Body", r.Laumination.name);
                Assert.IsFalse(r.FlipX, $"angle {a}° should not flip");
            }
        }

        [Test]
        public void Mirror_AnglesAbove180_Flips()
        {
            var set = new LauminationSet
            {
                directionMode = DirectionMode.Mirror,
                members = new System.Collections.Generic.List<LauminationSetMember> { MakeMember(0f, "Body") }
            };
            foreach (float a in new[] { 180.1f, 225f, 270f, 359f })
            {
                var r = LauminationSetResolver.Resolve(set, a);
                Assert.AreEqual("Body", r.Laumination.name);
                Assert.IsTrue(r.FlipX, $"angle {a}° should flip");
            }
        }

        [Test]
        public void Mirror_AtExactly180_DoesNotFlip()
        {
            // Boundary: 180° is the mirror axis itself, the design treats it as "no flip".
            var set = new LauminationSet
            {
                directionMode = DirectionMode.Mirror,
                members = new System.Collections.Generic.List<LauminationSetMember> { MakeMember(0f, "Body") }
            };
            var r = LauminationSetResolver.Resolve(set, 180f);
            Assert.IsFalse(r.FlipX);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //  DirectionMode.Members — straight 16 authored, no mirror
        // ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Members_FullyAuthored16_NoMirroring_ExactAnglePicksThatMember()
        {
            var set = new LauminationSet
            {
                directionMode = DirectionMode.Members,
                mirrorBuiltIn = false, // fully authored, no mirror economy
                members = new System.Collections.Generic.List<LauminationSetMember>()
            };
            string[] names = { "Up", "UUR", "UR", "RUR", "R", "RDR", "DR", "DDR",
                              "Down", "DDL", "DL", "LDL", "L", "LUL", "UL", "UUL" };
            for (int i = 0; i < 16; i++)
                set.members.Add(MakeMember(i * 22.5f, names[i]));

            for (int i = 0; i < 16; i++)
            {
                var r = LauminationSetResolver.Resolve(set, i * 22.5f);
                Assert.AreEqual(names[i], r.Laumination.name, $"angle {i*22.5}°");
                Assert.IsFalse(r.FlipX);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //  DirectionMode.Members — 9-authored mirror case (the player's upper body)
        // ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Members_Mirrored9_AngleInAuthoredHalf_PicksThatMember_NoFlip()
        {
            // 9 members at 0, 22.5, 45, ..., 180. Mirror built in. Request at 22.5° = the UpUpRight member.
            var set = BuildMirrored9Set();
            var r = LauminationSetResolver.Resolve(set, 22.5f);
            Assert.IsFalse(r.FlipX);
            Assert.AreEqual("UUR", r.Laumination.name);
        }

        [Test]
        public void Members_Mirrored9_AngleInMirroredHalf_PicksMirrorMember_Flips()
        {
            // 202.5° is in the mirrored half; mirrored target = 22.5°; nearest authored = UUR.
            // The resolver returns the PICKED member's name (UUR) and a flipX flag. The visual
            // mirror (UUL on screen) is produced by the runtime applying flipX to UUR — the
            // resolver itself only emits data.
            var set = BuildMirrored9Set();
            var r = LauminationSetResolver.Resolve(set, 202.5f);
            Assert.IsTrue(r.FlipX, "202.5° is in the mirrored half, must flip");
            Assert.AreEqual("UUR", r.Laumination.name, "picked member is the authored one, not the visual mirror");
        }

        [Test]
        public void Members_Mirrored9_AngleAt180_DoesNotMirror()
        {
            // 180° (Down) is the mirror axis. The authored member at exactly 180° should win,
            // no flip needed — the design's mirror economy is for the half-space STRICTLY past 180.
            var set = BuildMirrored9Set();
            var r = LauminationSetResolver.Resolve(set, 180f);
            Assert.IsFalse(r.FlipX);
            Assert.AreEqual("Down", r.Laumination.name);
        }

        [Test]
        public void Members_Mirrored9_AngleJustAbove180_MirrorsToFirstMatch()
        {
            var set = BuildMirrored9Set();
            var r = LauminationSetResolver.Resolve(set, 180.1f);
            // 180.1° is in the mirrored half; mirrored search runs against 0..180 with target 0.1°.
            // Up (0°) and Down (180°) are EQUIDISTANT (0.1° each). First-match-wins picks Up because
            // it comes earlier in the authored list. The C# resolver and node verifier agree on this
            // tiebreak. Visually the player facing Up vs Down (both unflippable on the mirror axis)
            // is only distinguishable by the flipX flag — the test verifies the flag, not the name.
            Assert.IsTrue(r.FlipX, "180.1° is in the mirrored half, must flip");
        }

        [Test]
        public void Members_Mirrored9_AngleExactlyAtMirror_PicksMirrorOf180()
        {
            // 0° (Up) sits on the mirror axis too. Mirror-of-Up = Up.
            var set = BuildMirrored9Set();
            var r = LauminationSetResolver.Resolve(set, 0f);
            Assert.IsFalse(r.FlipX);
            Assert.AreEqual("Up", r.Laumination.name);
        }

        [Test]
        public void Members_Mirrored9_AngleAt270_PicksMirrorOf90()
        {
            // 270° (Left) is in the mirrored half; mirrored search target = 90°; nearest = Right (90°).
            var set = BuildMirrored9Set();
            var r = LauminationSetResolver.Resolve(set, 270f);
            Assert.IsTrue(r.FlipX);
            Assert.AreEqual("Right", r.Laumination.name);
        }

        [Test]
        public void Members_Mirrored9_AngleAt359_PicksMirrorOf179()
        {
            // 359° is in the mirrored half; mirrored target = 179°; nearest = Down (180°, delta 1°).
            // Mirror of Down = Down (Down sits on the mirror axis). Visually no different from Down
            // unmirrored; the resolver still emits flipX=true so the runtime sees the symmetry.
            var set = BuildMirrored9Set();
            var r = LauminationSetResolver.Resolve(set, 359f);
            Assert.IsTrue(r.FlipX);
            Assert.AreEqual("Down", r.Laumination.name);
        }

        [Test]
        public void Members_Mirrored9_NegativeAngleWrapsAndMirrors()
        {
            // -22.5° wraps to 337.5°; mirrored target = 157.5°; nearest = DDR (157.5°).
            var set = BuildMirrored9Set();
            var r = LauminationSetResolver.Resolve(set, -22.5f);
            Assert.IsTrue(r.FlipX);
            Assert.AreEqual("DDR", r.Laumination.name);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //  DirectionMode.Rotate
        // ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Rotate_FreeRotation_ReturnsExactAngle()
        {
            var set = new LauminationSet
            {
                directionMode = DirectionMode.Rotate,
                snapStepDeg = 0f,
                members = new System.Collections.Generic.List<LauminationSetMember> { MakeMember(0f, "Ship") }
            };
            foreach (float a in new[] { 0f, 37.3f, 123.7f, 270f })
            {
                var r = LauminationSetResolver.Resolve(set, a);
                Assert.AreEqual("Ship", r.Laumination.name);
                Assert.AreEqual(a, r.RotationDeg, 0.001f);
            }
        }

        [Test]
        public void Rotate_SnapStep22_5_QuantisesTo16Steps()
        {
            var set = new LauminationSet
            {
                directionMode = DirectionMode.Rotate,
                snapStepDeg = 22.5f,
                members = new System.Collections.Generic.List<LauminationSetMember> { MakeMember(0f, "Ship") }
            };
            // 23° → 22.5°. 46° → 45°. 0° → 0°. 270° → 270°.
            Assert.AreEqual(22.5f, LauminationSetResolver.Resolve(set, 23f).RotationDeg, 0.001f);
            Assert.AreEqual(45f, LauminationSetResolver.Resolve(set, 46f).RotationDeg, 0.001f);
            Assert.AreEqual(0f, LauminationSetResolver.Resolve(set, 0f).RotationDeg, 0.001f);
            Assert.AreEqual(270f, LauminationSetResolver.Resolve(set, 270f).RotationDeg, 0.001f);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //  DirectionMode.MembersRotate
        // ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public void MembersRotate_PicksMemberPlusDelta()
        {
            // 8 members at every 45°, request at 60° → nearest member is 45° (RightUpRight etc),
            // rotation delta is 60 - 45 = 15°.
            var set = new LauminationSet
            {
                directionMode = DirectionMode.MembersRotate,
                members = new System.Collections.Generic.List<LauminationSetMember>()
            };
            for (int i = 0; i < 8; i++)
                set.members.Add(MakeMember(i * 45f, $"Bin{i}"));
            var r = LauminationSetResolver.Resolve(set, 60f);
            Assert.AreEqual("Bin1", r.Laumination.name);
            Assert.AreEqual(15f, r.RotationDeg, 0.001f);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //  Edge cases — angle wrap, negative angles
        // ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public void NegativeAngle_WrapsToPositive()
        {
            var set = BuildMirrored9Set();
            // -22.5° wraps to 337.5°; mirrored half; target = 157.5°; nearest authored = DDR.
            var r = LauminationSetResolver.Resolve(set, -22.5f);
            Assert.IsTrue(r.FlipX);
            Assert.AreEqual("DDR", r.Laumination.name);
        }

        [Test]
        public void AngleAbove360_Wraps()
        {
            var set = BuildMirrored9Set();
            // 380° = 20°. Nearest authored is 22.5° (UUR).
            var r = LauminationSetResolver.Resolve(set, 380f);
            Assert.AreEqual("UUR", r.Laumination.name);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //  LatchedDirection
        // ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Latch_InitialAngleIsUp()
        {
            var l = new LatchedDirection();
            Assert.AreEqual(0f, l.AngleDeg, 0.001f);
        }

        [Test]
        public void Latch_NaNHoldsLastValue()
        {
            var l = new LatchedDirection(90f);
            l.Update(180f);
            l.Update(float.NaN);
            l.Update(float.NaN);
            Assert.AreEqual(180f, l.AngleDeg, 0.001f);
        }

        [Test]
        public void Latch_ZeroVectorHoldsLastValue()
        {
            var l = new LatchedDirection(45f);
            l.Update(Vector2.zero);
            Assert.AreEqual(45f, l.AngleDeg, 0.001f);
        }

        [Test]
        public void Latch_NonZeroVectorUpdates()
        {
            var l = new LatchedDirection();
            l.Update(new Vector2(1f, 0f)); // 90° (Right)
            Assert.AreEqual(90f, l.AngleDeg, 0.001f);
            l.Update(new Vector2(0f, 1f)); // 0° (Up)
            Assert.AreEqual(0f, l.AngleDeg, 0.001f);
            l.Update(new Vector2(-1f, -1f)); // 225° (DownLeft)
            Assert.AreEqual(225f, l.AngleDeg, 0.001f);
        }

        [Test]
        public void Latch_NegativeAngleWraps()
        {
            var l = new LatchedDirection();
            l.Update(-90f); // -90° wraps to 270°
            Assert.AreEqual(270f, l.AngleDeg, 0.001f);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //  DirectionMode.Rotation — one sheet whose FRAMES are the directions
        // ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Rotation_ExactAngles_MapToTheirOwnFrame()
        {
            var set = BuildRotationSet("UpperAimRotation", 16);
            for (int i = 0; i < 16; i++)
            {
                var r = LauminationSetResolver.Resolve(set, i * 22.5f);
                Assert.IsTrue(r.HasFrame, $"angle {i * 22.5}° should resolve a frame");
                Assert.AreEqual(i, r.FrameIndex, $"angle {i * 22.5}°");
                Assert.AreEqual("UpperAimRotation", r.Laumination.name);
                Assert.AreEqual(i * 22.5f, r.ResolvedAngleDeg, 0.01f);
            }
        }

        [Test]
        public void Rotation_HalfStepBoundary_RoundsToNearestFrame()
        {
            // Step is 22.5°, so each frame owns ±11.25°. Either side of that boundary must land differently,
            // which is what stops a direction from sticking one frame past where the art changes.
            var set = BuildRotationSet("Sheet", 16);
            Assert.AreEqual(0, LauminationSetResolver.Resolve(set, 11.24f).FrameIndex);
            Assert.AreEqual(1, LauminationSetResolver.Resolve(set, 11.26f).FrameIndex);
            Assert.AreEqual(1, LauminationSetResolver.Resolve(set, 33.74f).FrameIndex);
            Assert.AreEqual(2, LauminationSetResolver.Resolve(set, 33.76f).FrameIndex);
        }

        [Test]
        public void Rotation_WrapsAtFullCircleAndHandlesNegatives()
        {
            var set = BuildRotationSet("Sheet", 16);
            // The wrap must land on frame 0, never on frame n (which would be out of range).
            Assert.AreEqual(0, LauminationSetResolver.Resolve(set, 359.99f).FrameIndex);
            Assert.AreEqual(0, LauminationSetResolver.Resolve(set, 360f).FrameIndex);
            Assert.AreEqual(0, LauminationSetResolver.Resolve(set, 720f).FrameIndex);
            Assert.AreEqual(15, LauminationSetResolver.Resolve(set, -22.5f).FrameIndex);
            Assert.AreEqual(0, LauminationSetResolver.Resolve(set, -0.01f).FrameIndex);
        }

        [Test]
        public void Rotation_DirectionCountComesFromTheSheet_Not16()
        {
            // The whole point of deriving N from frames.Count: re-bake the sheet at a different direction
            // count and the mapping follows with no edit to the set.
            var set8 = BuildRotationSet("Eight", 8);
            Assert.AreEqual(0, LauminationSetResolver.Resolve(set8, 0f).FrameIndex);
            Assert.AreEqual(1, LauminationSetResolver.Resolve(set8, 45f).FrameIndex);
            Assert.AreEqual(4, LauminationSetResolver.Resolve(set8, 180f).FrameIndex);
            Assert.AreEqual(7, LauminationSetResolver.Resolve(set8, 315f).FrameIndex);
            Assert.AreEqual(0, LauminationSetResolver.Resolve(set8, 359f).FrameIndex);

            var set32 = BuildRotationSet("ThirtyTwo", 32);
            Assert.AreEqual(1, LauminationSetResolver.Resolve(set32, 11.25f).FrameIndex);
            Assert.AreEqual(16, LauminationSetResolver.Resolve(set32, 180f).FrameIndex);
        }

        [Test]
        public void Rotation_EmptySheet_ReturnsNone()
        {
            // 0 frames would divide by zero in the step calculation, and genuinely has no direction to give.
            var set = BuildRotationSet("Empty", 0);
            var r = LauminationSetResolver.Resolve(set, 90f);
            Assert.AreEqual(LauminationResolution.None, r);
            Assert.IsFalse(r.HasFrame);
        }

        [Test]
        public void Rotation_NeverFlipsOrRotates()
        {
            // A full-circle sheet has nothing to mirror and the art carries the facing.
            var set = BuildRotationSet("Sheet", 16);
            foreach (float a in new[] { 0f, 90f, 200f, 270f, 350f })
            {
                var r = LauminationSetResolver.Resolve(set, a);
                Assert.IsFalse(r.FlipX, $"angle {a}°");
                Assert.AreEqual(0f, r.RotationDeg, 0.001f, $"angle {a}°");
            }
        }

        [Test]
        public void DefaultResolution_RequestsNoFrame()
        {
            // FrameIndex is stored offset by one precisely so `default` reads as -1 rather than frame 0 —
            // a bare int would make LauminationResolution.None look like a valid request to hold frame 0.
            LauminationResolution none = default;
            Assert.IsFalse(none.HasFrame);
            Assert.AreEqual(-1, none.FrameIndex);
            Assert.IsFalse(LauminationResolution.None.HasFrame);
        }

        [Test]
        public void NonRotationModes_RequestNoFrame()
        {
            // Every pre-existing mode must keep going down the old path untouched.
            Assert.IsFalse(LauminationSetResolver.Resolve(BuildMirrored9Set(), 45f).HasFrame);

            var none = new LauminationSet
            {
                directionMode = DirectionMode.None,
                members = new System.Collections.Generic.List<LauminationSetMember> { MakeMember(0f, "A") }
            };
            Assert.IsFalse(LauminationSetResolver.Resolve(none, 123f).HasFrame);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────────────────

        /// A rotation sheet: ONE laumination with n frames. The resolver reads only frames.Count off it, so
        /// null sprite slots are enough — no atlas or import needed to pin down the arithmetic.
        static LauminationSet BuildRotationSet(string name, int n)
        {
            var sheet = MakeAnim(name);
            for (int i = 0; i < n; i++) sheet.frames.Add(null);
            return new LauminationSet
            {
                directionMode = DirectionMode.Rotation,
                mirrorBuiltIn = false,
                members = new System.Collections.Generic.List<LauminationSetMember>
                {
                    new LauminationSetMember { angleDegrees = 0f, laumination = sheet }
                }
            };
        }

        static LauminationSet BuildMirrored9Set()
        {
            // 9 members at 0, 22.5, 45, ..., 180. The design doc's "16 aim directions from 9 members"
            // example. Names follow the project's Dir16 convention.
            var set = new LauminationSet
            {
                directionMode = DirectionMode.Members,
                mirrorBuiltIn = true,
                members = new System.Collections.Generic.List<LauminationSetMember>()
            };
            string[] names = { "Up", "UUR", "UR", "RUR", "Right", "RDR", "DR", "DDR", "Down" };
            for (int i = 0; i < 9; i++)
                set.members.Add(MakeMember(i * 22.5f, names[i]));
            return set;
        }
    }
}
