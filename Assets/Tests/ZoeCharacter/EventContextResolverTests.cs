using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Laubrary.Zoetrope;

namespace Laubrary.ZoeCharacter.Tests
{
    public class EventContextResolverTests
    {
        sealed class Parts : MonoBehaviour, IPartLookup
        {
            public readonly Dictionary<string, Transform> parts = new Dictionary<string, Transform>();
            public IEnumerable<Transform> PartTransforms => parts.Values;
            public Transform FindPartTransform(string partName) => parts.TryGetValue(partName, out var part) ? part : null;
        }

        [Test]
        public void ReactionRequest_NormalizesGeneralEventDirection()
        {
            var request = new ReactionRequest(direction: new Vector2(3f, 4f));
            Assert.That(request.Direction.x, Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(request.Direction.y, Is.EqualTo(0.8f).Within(0.0001f));

            var context = new EventContext { Direction = request.Direction };
            Assert.That(context.ResolveDirectionDeg(DirectionParam.EventDirection), Is.EqualTo(53.1301f).Within(0.001f));
            Assert.That(context.ResolveDirectionDeg(DirectionParam.HitDirection), Is.EqualTo(53.1301f).Within(0.001f));
        }

        [Test]
        public void RotationResolver_UsesFacingOffsetFixedAndNone()
        {
            var context = new EventContext { Direction = Vector2.right };
            var entry = new FxEntry { direction = DirectionParam.EventDirection, angleOffsetDeg = 15f };

            Assert.That(context.ResolveRotationDeg(entry), Is.EqualTo(15f));
            entry.rotation = FxRotationMode.FixedAngle;
            entry.fixedAngleDeg = 135f;
            Assert.That(context.ResolveRotationDeg(entry), Is.EqualTo(135f));
            entry.rotation = FxRotationMode.None;
            Assert.That(float.IsNaN(context.ResolveRotationDeg(entry)), Is.True);
        }

        [Test]
        public void BodyPartResolver_MirrorsLocalOffsetWithSpriteAndTransformFacing()
        {
            var root = new GameObject("Zoe");
            try
            {
                var parts = root.AddComponent<Parts>();
                var upper = new GameObject("Upper");
                upper.transform.SetParent(root.transform);
                upper.transform.position = new Vector3(10f, 0f, 0f);
                var sprite = upper.AddComponent<SpriteRenderer>();
                parts.parts.Add("Upper", upper.transform);

                var context = new EventContext { Transform = root.transform, PartLookup = parts };
                var entry = new FxEntry { placement = FxPlacementType.BodyPart, bodyPart = "Upper", localOffset = new Vector2(2f, 0f) };

                sprite.flipX = true;
                Assert.That(context.TryResolvePosition(entry, out var spriteMirrored), Is.True);
                Assert.That(spriteMirrored.x, Is.EqualTo(8f).Within(0.001f));

                sprite.flipX = false;
                upper.transform.localScale = new Vector3(-1f, 1f, 1f);
                Assert.That(context.TryResolvePosition(entry, out var transformMirrored), Is.True);
                Assert.That(transformMirrored.x, Is.EqualTo(8f).Within(0.001f));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
