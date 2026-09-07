using System.Collections.Generic;
using System.Reflection;
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

        // ── T-0239 rework: a followed effect tracks its whole POSE (position + aim + mirror), not just a point ──
        // EventContext.DirectionSource / CurrentDirection let a resolver re-ask "which way NOW" instead of being
        // stuck with the direction stamped at raise time. FxFollowTarget carries that pose to the instance every
        // frame. DirectionParam.Random is deliberately excluded — see the last test in this section.

        [Test]
        public void CurrentDirection_ReturnsRaiseTimeDirection_WhenSourceIsNull()
        {
            var ctx = new EventContext { Direction = Vector2.right, DirectionSource = null };
            Assert.That(ctx.CurrentDirection.x, Is.EqualTo(1f).Within(Eps));
            Assert.That(ctx.CurrentDirection.y, Is.EqualTo(0f).Within(Eps));
        }

        [Test]
        public void CurrentDirection_TracksTheLiveSource_NotTheRaiseTimeValue()
        {
            // The raise-time Direction is deliberately something the source never answers, so a pass here can only
            // mean the source was actually consulted.
            Vector2 live = Vector2.right;
            var ctx = new EventContext { Direction = Vector2.up, DirectionSource = () => live };

            Assert.That(ctx.CurrentDirection.x, Is.EqualTo(1f).Within(Eps));
            Assert.That(ctx.CurrentDirection.y, Is.EqualTo(0f).Within(Eps));

            // Re-asking after the source's answer changes must give the NEW answer — this is the whole point of a
            // followed effect: it re-samples every frame rather than trusting a value frozen at spawn.
            live = Vector2.down;
            Assert.That(ctx.CurrentDirection.x, Is.EqualTo(0f).Within(Eps));
            Assert.That(ctx.CurrentDirection.y, Is.EqualTo(-1f).Within(Eps));
        }

        [Test]
        public void CurrentDirection_FallsBackToRaiseTimeDirection_WhenSourceGoesQuiet()
        {
            // A source answering zero means "nothing to say right now" (e.g. a weapon with no live aim this
            // frame), which must never snap the effect to a meaningless atan2(0,0) angle.
            var ctx = new EventContext { Direction = Vector2.up, DirectionSource = () => Vector2.zero };
            Assert.That(ctx.CurrentDirection.x, Is.EqualTo(0f).Within(Eps));
            Assert.That(ctx.CurrentDirection.y, Is.EqualTo(1f).Within(Eps));
        }

        [Test]
        public void ResolveOrientation_ReResolvesFaceEventDirection_AsTheLiveSourceTurns()
        {
            Vector2 live = Vector2.right;
            var ctx = new EventContext { Direction = Vector2.right, DirectionSource = () => live };
            var entry = new FxEntry { rotation = FxRotationMode.FaceEventDirection, direction = DirectionParam.EventDirection };

            ctx.ResolveOrientation(entry, out float rot, out _);
            Assert.That(rot, Is.EqualTo(0f).Within(Eps));

            // Same context, same entry, just a later read after the live source turned — this is what a follower's
            // per-frame re-sample does.
            live = Vector2.up;
            ctx.ResolveOrientation(entry, out rot, out _);
            Assert.That(rot, Is.EqualTo(90f).Within(Eps));
        }

        [Test]
        public void ResolveOrientation_MirrorReResolvesWithTheLiveSourceToo()
        {
            // Per ResolveOrientation's own doc comment: with flipWithFacing on, a resolved rotation mirrors
            // whenever it points strictly LEFT (cos < 0), regardless of whether that rotation came from the
            // raise-time Direction or a live DirectionSource re-read later.
            Vector2 live = Vector2.right;
            var ctx = new EventContext { Direction = Vector2.right, DirectionSource = () => live };
            var entry = new FxEntry { rotation = FxRotationMode.FaceEventDirection, direction = DirectionParam.EventDirection, flipWithFacing = true };

            ctx.ResolveOrientation(entry, out float rot, out bool flip);
            Assert.That(rot, Is.EqualTo(0f).Within(Eps));
            Assert.That(flip, Is.False);

            live = Vector2.left;
            ctx.ResolveOrientation(entry, out rot, out flip);
            Assert.That(Mathf.Abs(rot), Is.EqualTo(180f).Within(Eps));
            Assert.That(flip, Is.True);

            live = Vector2.right;
            ctx.ResolveOrientation(entry, out rot, out flip);
            Assert.That(rot, Is.EqualTo(0f).Within(Eps));
            Assert.That(flip, Is.False);
        }

        [Test]
        public void FxFollowTarget_AppliesEverySampledPose_UntilStopped()
        {
            // FxFollowTarget's LateUpdate is private (a normal MonoBehaviour message) and there is no Play mode
            // here to drive Unity's update loop, so it is invoked directly by reflection — the same message Unity
            // itself would send each frame once the object is actually playing.
            var go = new GameObject("Follower");
            try
            {
                var follower = go.AddComponent<FxFollowTarget>();
                var lateUpdate = typeof(FxFollowTarget).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.That(lateUpdate, Is.Not.Null, "FxFollowTarget.LateUpdate not found by reflection");

                FxPose sampled = new FxPose(new Vector3(1f, 2f, 3f), 45f, true);
                FxPose? applied = null;
                follower.Init(() => sampled, pose => applied = pose);

                lateUpdate.Invoke(follower, null);
                Assert.That(applied.HasValue, Is.True);
                Assert.That(applied.Value.Position, Is.EqualTo(sampled.Position));
                Assert.That(applied.Value.AimDeg, Is.EqualTo(45f).Within(Eps));
                Assert.That(applied.Value.FlipX, Is.True);

                // A later sample is applied too — the whole point of following is re-sampling every frame.
                sampled = new FxPose(new Vector3(9f, 9f, 9f), -30f, false);
                lateUpdate.Invoke(follower, null);
                Assert.That(applied.Value.Position, Is.EqualTo(sampled.Position));
                Assert.That(applied.Value.AimDeg, Is.EqualTo(-30f).Within(Eps));
                Assert.That(applied.Value.FlipX, Is.False);

                // Stop() must make LateUpdate a no-op — nothing further gets applied.
                applied = null;
                follower.Stop();
                lateUpdate.Invoke(follower, null);
                Assert.That(applied.HasValue, Is.False);

                // OnDisable() must clear the follow too — that is what keeps a POOLED instance, reused later by a
                // non-following effect, from still being dragged around by this one's sampler.
                //
                // Sent by reflection, like LateUpdate above, and for the same reason: measured in this editor
                // (probe, 2026-09-07), GameObject.SetActive(false) does NOT deliver OnDisable to a plain
                // MonoBehaviour outside Play mode, so driving it through SetActive here would assert an engine
                // behaviour that only exists in Play mode and fail for a reason that has nothing to do with this
                // class. What is testable HERE is the contract this class owns: receiving OnDisable disarms it.
                // That it is actually received on pool release is a Play-mode fact, verified there instead.
                var onDisable = typeof(FxFollowTarget).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.That(onDisable, Is.Not.Null, "FxFollowTarget.OnDisable not found by reflection");
                follower.Init(() => sampled, pose => applied = pose);
                onDisable.Invoke(follower, null);
                lateUpdate.Invoke(follower, null);
                Assert.That(applied.HasValue, Is.False, "OnDisable did not clear the follow");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void DirectionParam_Random_ReRollsPerCall_WhichIsWhyFollowMustFreezeIt()
        {
            // ReactionFxPlayer.Fire() freezes a Random direction's rotation/flip at spawn time (captured into
            // local spawnRot/spawnFlip BEFORE follower.Init's closure is built) and never re-resolves it while
            // following, precisely because DirectionParam.Random re-rolls a fresh angle on every ResolveDirectionDeg
            // call — re-resolving it live would spin the effect every frame instead of scattering it once. Fire()
            // itself is a private method on a MonoBehaviour that requires a fully armed ReactionFxPlayer + an
            // ICombatFx's PlayFollowable/pooling machinery to reach, which is integration-test territory, not a
            // pure edit-mode unit test — so this pins the one fact that actually justifies the freeze: Random is
            // non-deterministic per call, unlike every other DirectionParam re-read in the tests above.
            var ctx = new EventContext { Direction = Vector2.right };
            float a = ctx.ResolveDirectionDeg(DirectionParam.Random);
            float b = ctx.ResolveDirectionDeg(DirectionParam.Random);
            float c = ctx.ResolveDirectionDeg(DirectionParam.Random);
            Assert.That(a >= 0f && a <= 360f, Is.True);
            Assert.That(b >= 0f && b <= 360f, Is.True);
            Assert.That(c >= 0f && c <= 360f, Is.True);
            Assert.That(a != b || b != c, Is.True, "Random direction did not vary across calls (astronomically unlikely if truly random)");
        }
    }
}
