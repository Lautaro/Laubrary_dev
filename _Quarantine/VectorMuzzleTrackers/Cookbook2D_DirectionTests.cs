using NUnit.Framework;
using UnityEngine;
using Laubrary.Cookbook2D;

namespace Laubrary.ZoeCharacter.Tests
{
    /// <summary>
    /// Unit tests for the direction quantization utilities on Cookbook2D. These are the
    /// platform's primitive for any 8-way / 16-way sprite picker — every later phase
    /// (LauminationSet, per-part aim channels) routes through these, so the threshold,
    /// wrap-around and mirror behaviour MUST be pinned down by tests before we build on them.
    /// </summary>
    public class Cookbook2D_DirectionTests
    {
        // ─────────────────────────────────────────────────────────────────────────────
        //  HasDirection
        // ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public void HasDirection_FalseForZero()
        {
            Assert.IsFalse(Vector2.zero.HasDirection());
        }

        [Test]
        public void HasDirection_TrueForAboveEpsilon()
        {
            // HasDirection's internal epsilon is 1e-6 on sqrMagnitude. 1e-3 magnitude gives 1e-6 sqrMagnitude,
            // exactly at the boundary; pick something safely above (1e-2 magnitude → 1e-4 sqr).
            Assert.IsTrue(new Vector2(1e-2f, 0f).HasDirection());
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //  ToDir8 — eight cardinals + four diagonals + zero
        // ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public void ToDir8_ZeroIsUp()
        {
            // Stationary character faces up by convention, so Dir8.Up is always defined.
            Assert.AreEqual(Dir8.Up, Vector2.zero.ToDir8());
        }

        [Test]
        public void ToDir8_EightCardinals()
        {
            // 0 = up, increasing clockwise.
            Assert.AreEqual(Dir8.Up, new Vector2(0f, 1f).ToDir8());
            Assert.AreEqual(Dir8.UpRight, new Vector2(1f, 1f).ToDir8());
            Assert.AreEqual(Dir8.Right, new Vector2(1f, 0f).ToDir8());
            Assert.AreEqual(Dir8.DownRight, new Vector2(1f, -1f).ToDir8());
            Assert.AreEqual(Dir8.Down, new Vector2(0f, -1f).ToDir8());
            Assert.AreEqual(Dir8.DownLeft, new Vector2(-1f, -1f).ToDir8());
            Assert.AreEqual(Dir8.Left, new Vector2(-1f, 0f).ToDir8());
            Assert.AreEqual(Dir8.UpLeft, new Vector2(-1f, 1f).ToDir8());
        }

        [Test]
        public void ToDir8_TinyOffsetStillUp()
        {
            // 1° clockwise from straight up rounds to bin 0 (Up).
            // Edge of bin 0 is at 22.5° (half-way to UpRight).
            float rad = 1f * Mathf.Deg2Rad;
            var v = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            Assert.AreEqual(Dir8.Up, v.ToDir8());
        }

        [Test]
        public void ToDir8_BoundaryAt45()
        {
            // At exactly 45°, both Up and UpRight are equidistant. Mathf.RoundToInt(45/45) = 1 → UpRight.
            float rad = 45f * Mathf.Deg2Rad;
            var v = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            Assert.AreEqual(Dir8.UpRight, v.ToDir8());
        }

        [Test]
        public void ToDir8_WrapsAt359()
        {
            // 359° is 1° short of full circle → rounds to bin 0 (Up).
            float rad = 359f * Mathf.Deg2Rad;
            var v = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            Assert.AreEqual(Dir8.Up, v.ToDir8());
        }

        [Test]
        public void ToDir8_DoesNotRequireNormalisation()
        {
            // A 1000-magnitude vector should quantize identically to a unit vector.
            var small = new Vector2(1f, 1f).ToDir8();
            var large = new Vector2(1000f, 1000f).ToDir8();
            Assert.AreEqual(small, large);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //  ToDir16 — sixteen bins + boundaries + wrap
        // ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public void ToDir16_ZeroIsUp()
        {
            Assert.AreEqual(Dir16.Up, Vector2.zero.ToDir16());
        }

        [Test]
        public void ToDir16_EightCardinalsMatch()
        {
            // The 8 cardinals and 4 diagonals should land on the same-direction bins as ToDir8.
            Assert.AreEqual((Dir16)((int)Dir8.Up * 2), new Vector2(0f, 1f).ToDir16());
            Assert.AreEqual((Dir16)((int)Dir8.UpRight * 2), new Vector2(1f, 1f).ToDir16());
            Assert.AreEqual((Dir16)((int)Dir8.Right * 2), new Vector2(1f, 0f).ToDir16());
            Assert.AreEqual((Dir16)((int)Dir8.DownRight * 2), new Vector2(1f, -1f).ToDir16());
            Assert.AreEqual((Dir16)((int)Dir8.Down * 2), new Vector2(0f, -1f).ToDir16());
            Assert.AreEqual((Dir16)((int)Dir8.DownLeft * 2), new Vector2(-1f, -1f).ToDir16());
            Assert.AreEqual((Dir16)((int)Dir8.Left * 2), new Vector2(-1f, 0f).ToDir16());
            Assert.AreEqual((Dir16)((int)Dir8.UpLeft * 2), new Vector2(-1f, 1f).ToDir16());
        }

        [Test]
        public void ToDir16_HalfwayBetweenCardinals()
        {
            // 22.5° (halfway between Up and UpRight) rounds to UpUpRight (index 1).
            float rad = 22.5f * Mathf.Deg2Rad;
            var v = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            Assert.AreEqual(Dir16.UpUpRight, v.ToDir16());
        }

        [Test]
        public void ToDir16_WrapsAtBoundary()
        {
            // 348.75° is 11.25° below UpRight (the UpRight bin ends at 348.75° + 11.25° = 360° = Up).
            // So 348.75° should round to Up (index 0).
            float rad = 348.75f * Mathf.Deg2Rad;
            var v = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            Assert.AreEqual(Dir16.Up, v.ToDir16());
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //  AngleDeg / ToVector — round-trip
        // ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Dir8_RoundTripsThroughVectorAndAngle()
        {
            for (int i = 0; i < 8; i++)
            {
                var d = (Dir8)i;
                float angle = d.AngleDeg();
                var v = d.ToVector();
                Assert.AreEqual(d, v.ToDir8(), $"Bin {i} (angle {angle}) did not round-trip");
            }
        }

        [Test]
        public void Dir16_RoundTripsThroughVectorAndAngle()
        {
            for (int i = 0; i < 16; i++)
            {
                var d = (Dir16)i;
                var v = d.ToVector();
                Assert.AreEqual(d, v.ToDir16(), $"Bin {i} (angle {d.AngleDeg()}) did not round-trip");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //  MirrorHorizontal
        // ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Dir8_MirrorHorizontal_SwapsLeftRight()
        {
            Assert.AreEqual(Dir8.Right, Dir8.Left.MirrorHorizontal());
            Assert.AreEqual(Dir8.Left, Dir8.Right.MirrorHorizontal());
            Assert.AreEqual(Dir8.UpRight, Dir8.UpLeft.MirrorHorizontal());
            Assert.AreEqual(Dir8.DownLeft, Dir8.DownRight.MirrorHorizontal());
        }

        [Test]
        public void Dir8_MirrorHorizontal_DoubleMirrorIsIdentity()
        {
            for (int i = 0; i < 8; i++)
            {
                var d = (Dir8)i;
                Assert.AreEqual(d, d.MirrorHorizontal().MirrorHorizontal(), $"Bin {i} did not return after double mirror");
            }
        }

        [Test]
        public void Dir8_MirrorHorizontal_UpAndDownFixed()
        {
            // Up and Down are on the mirror axis — they map to themselves.
            Assert.AreEqual(Dir8.Up, Dir8.Up.MirrorHorizontal());
            Assert.AreEqual(Dir8.Down, Dir8.Down.MirrorHorizontal());
        }

        [Test]
        public void Dir16_MirrorHorizontal_SwapsUpRightAndUpLeft()
        {
            // UpRight (45°) → 360-45 = 315° → UpLeft (14).
            Assert.AreEqual(Dir16.UpLeft, Dir16.UpRight.MirrorHorizontal());
            Assert.AreEqual(Dir16.UpRight, Dir16.UpLeft.MirrorHorizontal());
        }

        [Test]
        public void Dir16_MirrorHorizontal_UpAndDownFixed()
        {
            Assert.AreEqual(Dir16.Up, Dir16.Up.MirrorHorizontal());
            Assert.AreEqual(Dir16.Down, Dir16.Down.MirrorHorizontal());
            Assert.AreEqual(Dir16.Right, Dir16.Left.MirrorHorizontal());
        }

        [Test]
        public void Dir16_MirrorHorizontal_DoubleMirrorIsIdentity()
        {
            for (int i = 0; i < 16; i++)
            {
                var d = (Dir16)i;
                Assert.AreEqual(d, d.MirrorHorizontal().MirrorHorizontal(), $"Bin {i} did not return after double mirror");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //  WithinBins — debouncer helper
        // ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Dir16_WithinBins_SameBinIsWithinZeroThreshold()
        {
            // threshold of 1 means "bins within 1 of each other" — same bin counts.
            Assert.IsTrue(Dir16.Up.WithinBins(Dir16.Up, 1));
        }

        [Test]
        public void Dir16_WithinBins_AdjacentWithThreshold3()
        {
            // Bins 0 and 2 are 2 apart; WithinBins uses strict less-than (diff < threshold), so
            // threshold must be 3 to include an "adjacent" bin (diff=2). Threshold 2 excludes it.
            Assert.IsTrue(Dir16.UpRight.WithinBins(Dir16.Up, 3));
            Assert.IsFalse(Dir16.UpRight.WithinBins(Dir16.Up, 2));
            Assert.IsFalse(Dir16.UpRight.WithinBins(Dir16.Up, 1));
        }

        [Test]
        public void Dir16_WithinBins_WrapsAround()
        {
            // UpUpLeft (15) is only 1 bin from Up (0) going the short way around.
            Assert.IsTrue(Dir16.Up.WithinBins(Dir16.UpUpLeft, 2));
        }

        [Test]
        public void Dir16_WithinBins_ZeroThresholdIsNeverWithin()
        {
            Assert.IsFalse(Dir16.Up.WithinBins(Dir16.Up, 0));
        }

        [Test]
        public void Dir8_WithinBins_Works()
        {
            Assert.IsTrue(Dir8.UpRight.WithinBins(Dir8.Up, 2));
            Assert.IsFalse(Dir8.Right.WithinBins(Dir8.Up, 2));
            // Wrap: UpLeft (7) is 1 bin from Up (0) the short way.
            Assert.IsTrue(Dir8.Up.WithinBins(Dir8.UpLeft, 2));
        }
    }
}
