using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Laubrary.Zoetrope;

namespace Laubrary.ZoeCharacter.Tests
{
    public class DirectionalFxEntryTests
    {
        sealed class Parts : MonoBehaviour, IPartLookup
        {
            public readonly Dictionary<string, Transform> values = new Dictionary<string, Transform>();
            public IEnumerable<Transform> PartTransforms => values.Values;
            public Transform FindPartTransform(string name) => values.TryGetValue(name, out var part) ? part : null;
        }

        [Test]
        public void ReactionRequest_NormalizesGeneralEventDirection()
        {
            var request = new ReactionRequest(direction: new Vector2(3f, 4f));
            Assert.That(request.Direction.x, Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(request.Direction.y, Is.EqualTo(0.8f).Within(0.0001f));
        }

        [Test]
        public void BodyPartPlacement_UsesMirroredOffsetAndRequestedRotation()
        {
            var root = new GameObject("Zoe");
            try
            {
                var parts = root.AddComponent<Parts>();
                var upper = new GameObject("Upper");
                upper.transform.SetParent(root.transform);
                upper.transform.position = new Vector3(10f, 2f, 0f);
                upper.AddComponent<SpriteRenderer>().flipX = true;
                parts.values.Add("Upper", upper.transform);
                var entry = new FxEntry
                {
                    placement = FxPlacementType.BodyPart, bodyPart = "Upper", localOffset = new Vector2(0.5f, 0f),
                    mirrorOffsetWithFacing = true, rotation = FxRotationMode.FaceEventDirection,
                    direction = DirectionParam.EventDirection, angleOffsetDeg = 15f, flipWithFacing = true,
                };
                var context = new EventContext { Transform = root.transform, PartLookup = parts, Direction = Vector2.up };

                Assert.That(context.TryResolvePosition(entry, out var point), Is.True);
                Assert.That(point.x, Is.EqualTo(9.5f).Within(0.0001f));
                Assert.That(point.y, Is.EqualTo(2f).Within(0.0001f));
                Assert.That(context.ResolveRotationDeg(entry), Is.EqualTo(105f).Within(0.001f));
                Assert.That(context.ResolveFlipX(entry), Is.True);

                entry.rotation = FxRotationMode.FixedAngle;
                entry.fixedAngleDeg = 42f;
                entry.angleOffsetDeg = -2f;
                Assert.That(context.ResolveRotationDeg(entry), Is.EqualTo(42f).Within(0.001f));
                entry.rotation = FxRotationMode.None;
                Assert.That(float.IsNaN(context.ResolveRotationDeg(entry)), Is.True);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
