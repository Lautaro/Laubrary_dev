using NUnit.Framework;
using UnityEngine;
using Laubrary.Zoetrope;
using Laubrary.Launimator;
using Laubrary.ZoetropeLaunimator;

namespace Laubrary.ZoeCharacter.Tests
{
    public class MotionPoseTests
    {
        static MotionState Moving(Vector2 heading, Vector2 aim) =>
            new MotionState { speed = heading.magnitude, heading = heading, aim = aim, grounded = true, selfWilled = true };

        static MotionState Idle => MotionState.Idle;

        // ── MotionPose.Match: first-match-wins over an ordered rule list ──

        [Test]
        public void Match_PicksFirstRuleWhoseConditionHits()
        {
            var pose = new MotionPose
            {
                moveThreshold = 0.15f,
                rules = new System.Collections.Generic.List<MotionPoseRule>
                {
                    new MotionPoseRule { condition = MotionCondition.Moving, setName = "Move" },
                    new MotionPoseRule { condition = MotionCondition.Always, setName = "Idle" },
                }
            };

            var moving = pose.Match(Moving(new Vector2(0f, 1f), Vector2.zero));
            Assert.AreEqual("Move", moving.setName);

            var idle = pose.Match(Idle);
            Assert.AreEqual("Idle", idle.setName);
        }

        [Test]
        public void Match_IdleConditionMatchesAtOrBelowThreshold()
        {
            var pose = new MotionPose
            {
                moveThreshold = 0.5f,
                rules = new System.Collections.Generic.List<MotionPoseRule>
                {
                    new MotionPoseRule { condition = MotionCondition.Idle, setName = "Idle" },
                }
            };

            Assert.IsNotNull(pose.Match(new MotionState { speed = 0.5f }));   // at threshold: Idle
            Assert.IsNull(pose.Match(new MotionState { speed = 0.51f }));    // above threshold: no Idle match
        }

        [Test]
        public void Match_ReturnsNullWhenNoRuleMatchesAndNoFallback()
        {
            var pose = new MotionPose
            {
                moveThreshold = 0.15f,
                rules = new System.Collections.Generic.List<MotionPoseRule>
                {
                    new MotionPoseRule { condition = MotionCondition.Moving, setName = "Move" },
                }
            };
            Assert.IsNull(pose.Match(Idle));
        }

        [Test]
        public void Match_ReturnsNullOnUnauthoredPose()
        {
            var pose = new MotionPose();
            Assert.IsNull(pose.Match(Moving(Vector2.up, Vector2.zero)));
        }

        [Test]
        public void IsAuthored_FalseWithNoRules_TrueOnceRulesExist()
        {
            var pose = new MotionPose();
            Assert.IsFalse(pose.IsAuthored);
            pose.rules.Add(new MotionPoseRule());
            Assert.IsTrue(pose.IsAuthored);
        }

        // ── MotionPose.ResolveDirectionVector / UsesLatch ──

        [Test]
        public void ResolveDirectionVector_HeadingChannel_ReadsMotionStateHeading()
        {
            var pose = new MotionPose { channel = DirectionChannel.Heading };
            var state = Moving(new Vector2(1f, 0f), new Vector2(0f, -1f));
            Assert.AreEqual(new Vector2(1f, 0f), pose.ResolveDirectionVector(state));
            Assert.IsTrue(pose.UsesLatch);
        }

        [Test]
        public void ResolveDirectionVector_AimChannel_ReadsMotionStateAim()
        {
            var pose = new MotionPose { channel = DirectionChannel.Aim };
            var state = Moving(new Vector2(1f, 0f), new Vector2(0f, -1f));
            Assert.AreEqual(new Vector2(0f, -1f), pose.ResolveDirectionVector(state));
            Assert.IsTrue(pose.UsesLatch);
        }

        [Test]
        public void ResolveDirectionVector_FixedChannel_IgnoresStateAndNeverLatches()
        {
            var pose = new MotionPose { channel = DirectionChannel.Fixed, fixedAngleDeg = 90f };
            var v = pose.ResolveDirectionVector(Idle);
            Assert.AreEqual(1f, v.x, 1e-4f);
            Assert.AreEqual(0f, v.y, 1e-4f);
            Assert.IsFalse(pose.UsesLatch);
        }

        // ── MotionPoseResolver: end-to-end through a real LauminaryVersion ──

        LauminaryVersion _version;

        [SetUp]
        public void SetUp()
        {
            _version = ScriptableObject.CreateInstance<LauminaryVersion>();
            _version.sets = new System.Collections.Generic.List<LauminationSet>
            {
                new LauminationSet
                {
                    name = "Idle",
                    directionMode = DirectionMode.None,
                    members = new System.Collections.Generic.List<LauminationSetMember>
                    {
                        new LauminationSetMember { laumination = MakeLaumination("Idle_Anim") },
                    }
                },
                new LauminationSet
                {
                    name = "Move",
                    directionMode = DirectionMode.Mirror,
                    members = new System.Collections.Generic.List<LauminationSetMember>
                    {
                        new LauminationSetMember { laumination = MakeLaumination("Move_Anim") },
                    }
                },
            };
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_version);

        static Laumination MakeLaumination(string name) => new Laumination { name = name };

        [Test]
        public void Resolver_PicksRuleThenSet_AndReturnsResolvedLaumination()
        {
            var pose = new MotionPose
            {
                channel = DirectionChannel.Heading,
                moveThreshold = 0.15f,
                rules = new System.Collections.Generic.List<MotionPoseRule>
                {
                    new MotionPoseRule { condition = MotionCondition.Moving, setName = "Move" },
                    new MotionPoseRule { condition = MotionCondition.Always, setName = "Idle" },
                }
            };
            var latch = new LatchedDirection();

            var idleRes = MotionPoseResolver.Resolve(pose, _version, Idle, latch);
            Assert.AreEqual("Idle_Anim", idleRes.Laumination.name);

            var moveRes = MotionPoseResolver.Resolve(pose, _version, Moving(new Vector2(0f, 1f), Vector2.zero), latch);
            Assert.AreEqual("Move_Anim", moveRes.Laumination.name);
        }

        [Test]
        public void Resolver_NoneWhenPoseUnauthored()
        {
            var pose = new MotionPose();
            var res = MotionPoseResolver.Resolve(pose, _version, Moving(Vector2.up, Vector2.zero), new LatchedDirection());
            Assert.IsNull(res.Laumination);
        }

        [Test]
        public void Resolver_NoneWhenRuleNamesUnknownSet()
        {
            var pose = new MotionPose
            {
                rules = new System.Collections.Generic.List<MotionPoseRule>
                {
                    new MotionPoseRule { condition = MotionCondition.Always, setName = "NoSuchSet" },
                }
            };
            var res = MotionPoseResolver.Resolve(pose, _version, Idle, new LatchedDirection());
            Assert.IsNull(res.Laumination);
        }

        [Test]
        public void Resolver_LatchesHeadingWhenStationary()
        {
            var pose = new MotionPose
            {
                channel = DirectionChannel.Heading,
                rules = new System.Collections.Generic.List<MotionPoseRule>
                {
                    new MotionPoseRule { condition = MotionCondition.Always, setName = "Move" }, // Mirror mode: flips past 180
                }
            };
            var latch = new LatchedDirection();

            // Moving left (west, 270 deg clockwise-from-up, the mirrored half) then going idle: the resolved
            // angle should stay mirrored rather than snapping back to the unmirrored default (angle 0).
            var moving = MotionPoseResolver.Resolve(pose, _version, Moving(new Vector2(-1f, 0f), Vector2.zero), latch);
            Assert.IsTrue(moving.FlipX);

            var stationary = MotionPoseResolver.Resolve(pose, _version, Idle, latch);
            Assert.IsTrue(stationary.FlipX, "a stationary body should keep facing the last direction it moved in");
        }
    }
}
