using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
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

        const float Eps = 1e-3f;

        [Test]
        public void ReactionRequest_NormalizesGeneralEventDirection()
        {
            var request = new ReactionRequest(direction: new Vector2(3f, 4f));
            Assert.That(request.Direction.x, Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(request.Direction.y, Is.EqualTo(0.8f).Within(0.0001f));

            var context = new EventContext { Direction = request.Direction };
            Assert.That(context.ResolveDirectionDeg(DirectionParam.EventDirection), Is.EqualTo(53.1301f).Within(Eps));
            Assert.That(context.ResolveDirectionDeg(DirectionParam.HitDirection), Is.EqualTo(53.1301f).Within(Eps));
        }

        [Test]
        public void FxEntry_DefaultsToNoRotation()
        {
            // An entry authored before rotation existed must keep spawning upright.
            Assert.That(new FxEntry().rotation, Is.EqualTo(FxRotationMode.None));
            Assert.That(float.IsNaN(new EventContext { Direction = Vector2.right }.ResolveRotationDeg(new FxEntry())), Is.True);
        }

        [Test]
        public void RotationResolver_UsesFacingOffsetFixedAndNone()
        {
            var context = new EventContext { Direction = Vector2.right };
            var entry = new FxEntry { rotation = FxRotationMode.FaceEventDirection, direction = DirectionParam.EventDirection, angleOffsetDeg = 15f };

            Assert.That(context.ResolveRotationDeg(entry), Is.EqualTo(15f).Within(Eps));
            entry.rotation = FxRotationMode.FixedAngle;
            entry.fixedAngleDeg = 135f;
            Assert.That(context.ResolveRotationDeg(entry), Is.EqualTo(135f).Within(Eps));
            entry.rotation = FxRotationMode.None;
            Assert.That(float.IsNaN(context.ResolveRotationDeg(entry)), Is.True);
        }

        [Test]
        public void Orientation_FaceEventDirection_RightLeftDiagonal_MirrorsWhenPointingLeft()
        {
            var entry = new FxEntry { rotation = FxRotationMode.FaceEventDirection, direction = DirectionParam.EventDirection, flipWithFacing = true };

            // Fire right: 0°, no mirror.
            var ctx = new EventContext { Direction = Vector2.right };
            ctx.ResolveOrientation(entry, out float rot, out bool flip);
            Assert.That(rot, Is.EqualTo(0f).Within(Eps)); Assert.That(flip, Is.False);

            // Fire left: the aim is 180° and the effect is asked to MIRROR (it compensates the angle itself).
            ctx.Direction = Vector2.left;
            ctx.ResolveOrientation(entry, out rot, out flip);
            Assert.That(Mathf.Abs(rot), Is.EqualTo(180f).Within(Eps)); Assert.That(flip, Is.True);

            // Fire up-right: 45°, no mirror. Fire up-left: 135°, mirrored. Straight up: 90°, not mirrored.
            ctx.Direction = new Vector2(1f, 1f).normalized;
            ctx.ResolveOrientation(entry, out rot, out flip);
            Assert.That(rot, Is.EqualTo(45f).Within(Eps)); Assert.That(flip, Is.False);
            ctx.Direction = new Vector2(-1f, 1f).normalized;
            ctx.ResolveOrientation(entry, out rot, out flip);
            Assert.That(rot, Is.EqualTo(135f).Within(Eps)); Assert.That(flip, Is.True);
            ctx.Direction = Vector2.up;
            ctx.ResolveOrientation(entry, out rot, out flip);
            Assert.That(rot, Is.EqualTo(90f).Within(Eps)); Assert.That(flip, Is.False);

            // Mirroring off: a left shot rotates the full 180° and nothing is mirrored.
            entry.flipWithFacing = false;
            ctx.Direction = Vector2.left;
            ctx.ResolveOrientation(entry, out rot, out flip);
            Assert.That(Mathf.Abs(rot), Is.EqualTo(180f).Within(Eps)); Assert.That(flip, Is.False);
        }

        [Test]
        public void Orientation_NoRotation_MirrorsWithBodyFacing()
        {
            var root = new GameObject("Zoe");
            try
            {
                var sr = root.AddComponent<SpriteRenderer>();
                var ctx = new EventContext { Transform = root.transform, Renderer = sr, Direction = Vector2.left };
                var entry = new FxEntry { rotation = FxRotationMode.None, flipWithFacing = true };
                ctx.ResolveOrientation(entry, out float rot, out bool flip);
                Assert.That(float.IsNaN(rot), Is.True); Assert.That(flip, Is.False);
                sr.flipX = true;
                ctx.ResolveOrientation(entry, out rot, out flip);
                Assert.That(flip, Is.True);
            }
            finally { Object.DestroyImmediate(root); }
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
                Assert.That(spriteMirrored.x, Is.EqualTo(8f).Within(Eps));

                sprite.flipX = false;
                upper.transform.localScale = new Vector3(-1f, 1f, 1f);
                Assert.That(context.TryResolvePosition(entry, out var transformMirrored), Is.True);
                Assert.That(transformMirrored.x, Is.EqualTo(8f).Within(Eps));

                // The part-less overload still spawns SOMEWHERE for a BodyPart placement (the sprite centre).
                Assert.That(context.TryResolvePosition(FxPlacementType.BodyPart, "", out _), Is.True);
            }
            finally { Object.DestroyImmediate(root); }
        }

        // ── ProtoGuy ships exactly ONE muzzle flash ───────────────────────────────────────────────
        // Two spawn paths can each play a blast on a shot: the weapon's own muzzle slot (WeaponDef.muzzle, via
        // WeaponMuzzleCue) and the character's Fire event (a Zoe event row). Both used to be authored on
        // ProtoGuy with the same blast, pixel-coincident, so "the flash" was really two. This pins the shipped
        // data to one owner: the Fire event. Flipping to the weapon-owned model means the reverse of this test.
        const string ProtoGuyPath = "Assets/Demos/ProtoGuyDemo/ProtoGuy.asset";
        const string ProtoGuyGunPath = "Assets/Demos/ProtoGuyDemo/ProtoGuy Gun.asset";

        [Test]
        public void ProtoGuy_FireSpawnsExactlyOneBlast()
        {
            var zoe = AssetDatabase.LoadAssetAtPath<Zoe>(ProtoGuyPath);
            var gun = AssetDatabase.LoadAssetAtPath<WeaponDef>(ProtoGuyGunPath);
            Assert.That(zoe, Is.Not.Null, ProtoGuyPath);
            Assert.That(gun, Is.Not.Null, ProtoGuyGunPath);

            // The weapon's own muzzle slot is empty — the gun does not spawn a blast of its own.
            Assert.That(gun.muzzle == null || gun.muzzle.IsEmpty, Is.True, "ProtoGuy Gun still has a weapon-owned muzzle flash");

            // The character's declared Fire event spawns exactly one point effect (the body relight is not one).
            var fire = zoe.events.Find(e => string.Equals(e.id, WeaponMuzzleCue.FireEventId, System.StringComparison.OrdinalIgnoreCase));
            Assert.That(fire, Is.Not.Null, "ProtoGuy declares no Fire event");
            int blasts = 0;
            foreach (var entry in fire.reaction.fx)
                if (entry != null && entry.enabled && entry.fx is ICombatFx fx && !fx.IsEmpty) blasts++;
            Assert.That(blasts, Is.EqualTo(1));

            // …and that one row is the painted muzzle point, turned with the shot and mirrored for a left shot.
            var row = fire.reaction.fx.Find(e => e != null && e.fx is ICombatFx);
            Assert.That(row.placement, Is.EqualTo(FxPlacementType.MetaPoint));
            Assert.That(row.metaLayerId, Is.EqualTo("Muzzle"));
            Assert.That(row.direction, Is.EqualTo(DirectionParam.EventDirection));
            Assert.That(row.rotation, Is.EqualTo(FxRotationMode.FaceEventDirection));
            Assert.That(row.flipWithFacing, Is.True);
        }
    }
}
