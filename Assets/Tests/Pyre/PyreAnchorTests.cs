using NUnit.Framework;
using Laubrary.Pyre;
using UnityEngine;
using PyreAsset = Laubrary.Pyre.Pyre;

namespace Laubrary.Pyre.Tests
{
    /// The anchor placement maths (PyreAnchor): where a spawned Pyre's transform ends up for a few anchors,
    /// rotations and the mirrored case, and which rotation points a Vector anchor along an aim.
    public class PyreAnchorTests
    {
        const float Eps = 1e-4f;

        static PyreAsset Make(bool anchored, PyreAnchorKind kind, Vector2 origin, Vector2 dir)
        {
            var p = ScriptableObject.CreateInstance<PyreAsset>();
            p.canvasSize = 64; p.pixelsPerUnit = 16f;   // 4 units across
            p.anchorEnabled = anchored; p.anchorKind = kind; p.anchorOrigin = origin; p.anchorDirection = dir;
            return p;
        }

        [Test]
        public void NoAnchor_IsCentredAndFacesPlusX()
        {
            var p = Make(false, PyreAnchorKind.Vector, new Vector2(0f, 0f), Vector2.up);
            try
            {
                Assert.That(PyreAnchor.AnchorLocal(p), Is.EqualTo(Vector2.zero));
                Assert.That(PyreAnchor.ForwardLocal(p), Is.EqualTo(Vector2.right));
                Assert.That(PyreAnchor.RotationDeg(Vector2.right, 30f, false), Is.EqualTo(30f).Within(Eps));
                Assert.That(float.IsNaN(PyreAnchor.RotationDeg(Vector2.right, float.NaN, false)), Is.True);
            }
            finally { Object.DestroyImmediate(p); }
        }

        [Test]
        public void AnchorLocal_MeasuresFromTheCanvasCentreInUnits()
        {
            // Bottom-left corner of a 64px / 16ppu canvas = (-2, -2) units from the pivot; left-middle = (-2, 0).
            var p = Make(true, PyreAnchorKind.Position, new Vector2(0f, 0f), Vector2.right);
            try
            {
                Assert.That(PyreAnchor.AnchorLocal(p).x, Is.EqualTo(-2f).Within(Eps));
                Assert.That(PyreAnchor.AnchorLocal(p).y, Is.EqualTo(-2f).Within(Eps));
                p.anchorOrigin = new Vector2(0f, 0.5f);
                Assert.That(PyreAnchor.AnchorLocal(p).x, Is.EqualTo(-2f).Within(Eps));
                Assert.That(PyreAnchor.AnchorLocal(p).y, Is.EqualTo(0f).Within(Eps));
            }
            finally { Object.DestroyImmediate(p); }
        }

        [Test]
        public void SpawnPosition_PutsTheAnchorOnTheSpawnPoint_ForRotationsAndMirror()
        {
            var spawn = new Vector2(10f, 5f);
            var anchor = new Vector2(-2f, 0f);   // the base, left of centre

            // Upright: the transform sits 2 units right of the point, so the base lands on it.
            var pos = PyreAnchor.SpawnPosition(spawn, anchor, float.NaN, false, 1f);
            Assert.That(pos.x, Is.EqualTo(12f).Within(Eps)); Assert.That(pos.y, Is.EqualTo(5f).Within(Eps));

            // Rotated 90° (facing up): the base is now BELOW the centre, so the centre sits 2 units above the point.
            pos = PyreAnchor.SpawnPosition(spawn, anchor, 90f, false, 1f);
            Assert.That(pos.x, Is.EqualTo(10f).Within(Eps)); Assert.That(pos.y, Is.EqualTo(7f).Within(Eps));

            // Mirrored, unrotated: the base is now RIGHT of centre, so the centre sits 2 units left of the point.
            pos = PyreAnchor.SpawnPosition(spawn, anchor, 0f, true, 1f);
            Assert.That(pos.x, Is.EqualTo(8f).Within(Eps)); Assert.That(pos.y, Is.EqualTo(5f).Within(Eps));

            // Scaled ×2: the offset doubles.
            pos = PyreAnchor.SpawnPosition(spawn, anchor, 0f, false, 2f);
            Assert.That(pos.x, Is.EqualTo(14f).Within(Eps));

            // Round trip: the anchor really lands on the spawn point for an arbitrary angle + mirror.
            float rot = 37f; bool flip = true;
            pos = PyreAnchor.SpawnPosition(spawn, anchor, rot, flip, 1.5f);
            var a = anchor * 1.5f; if (flip) a.x = -a.x;
            var world = pos + (Vector2)(Quaternion.Euler(0f, 0f, rot) * a);
            Assert.That(world.x, Is.EqualTo(spawn.x).Within(Eps)); Assert.That(world.y, Is.EqualTo(spawn.y).Within(Eps));
        }

        [Test]
        public void RotationDeg_PointsTheAnchorDirectionAlongTheAim()
        {
            // Art authored facing UP: aiming right needs -90°, aiming up 0°, aiming down-right -135°.
            Assert.That(PyreAnchor.RotationDeg(Vector2.up, 0f, false), Is.EqualTo(-90f).Within(Eps));
            Assert.That(PyreAnchor.RotationDeg(Vector2.up, 90f, false), Is.EqualTo(0f).Within(Eps));
            Assert.That(PyreAnchor.RotationDeg(Vector2.up, -45f, false), Is.EqualTo(-135f).Within(Eps));
            // Art facing +X aimed left WITHOUT a mirror is the upside-down 180° the mirror exists to avoid.
            Assert.That(Mathf.Abs(PyreAnchor.RotationDeg(Vector2.right, 180f, false)), Is.EqualTo(180f).Within(Eps));
        }

        [Test]
        public void RotationDeg_MirroredShot_IsASmallAngleNotOneEighty()
        {
            // Right-facing art, shot aimed left: mirrored, it needs NO rotation.
            Assert.That(PyreAnchor.RotationDeg(Vector2.right, 180f, true), Is.EqualTo(0f).Within(Eps));
            // Aimed up-left (135°): mirrored art already points left (180°), so rotate -45° to reach 135°.
            Assert.That(PyreAnchor.RotationDeg(Vector2.right, 135f, true), Is.EqualTo(-45f).Within(Eps));
            // Aimed down-left (-135°): +45°.
            Assert.That(PyreAnchor.RotationDeg(Vector2.right, -135f, true), Is.EqualTo(45f).Within(Eps));
        }

        [Test]
        public void Place_WithVectorAnchor_LandsTheAnchorAndFacesTheAim()
        {
            // Anchor at the left-middle edge, art facing +X — a muzzle flash whose base is its left edge.
            var p = Make(true, PyreAnchorKind.Vector, new Vector2(0f, 0.5f), Vector2.right);
            var go = new GameObject("blast");
            try
            {
                var spawn = new Vector2(3f, 1f);
                // Fire right: upright, centre 2 units right of the muzzle.
                PyreAnchor.Place(go.transform, p, spawn, 0f, false);
                Assert.That(go.transform.position.x, Is.EqualTo(5f).Within(Eps));
                Assert.That(go.transform.rotation.eulerAngles.z, Is.EqualTo(0f).Within(Eps));
                // Fire left, mirrored: still no rotation, centre 2 units LEFT of the muzzle.
                PyreAnchor.Place(go.transform, p, spawn, 180f, true);
                Assert.That(go.transform.position.x, Is.EqualTo(1f).Within(Eps));
                Assert.That(go.transform.position.y, Is.EqualTo(1f).Within(Eps));
                Assert.That(Mathf.DeltaAngle(go.transform.rotation.eulerAngles.z, 0f), Is.EqualTo(0f).Within(Eps));
                // Fire up: rotated 90°, centre 2 units ABOVE the muzzle.
                PyreAnchor.Place(go.transform, p, spawn, 90f, false);
                Assert.That(go.transform.position.x, Is.EqualTo(3f).Within(Eps));
                Assert.That(go.transform.position.y, Is.EqualTo(3f).Within(Eps));
                Assert.That(go.transform.rotation.eulerAngles.z, Is.EqualTo(90f).Within(Eps));
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(p); }
        }
    }
}
