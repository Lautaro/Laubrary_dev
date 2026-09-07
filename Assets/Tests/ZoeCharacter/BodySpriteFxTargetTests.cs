using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Laubrary.Zoetrope;

namespace Laubrary.ZoeCharacter.Tests
{
    public class BodySpriteFxTargetTests
    {
        sealed class DeclaredParts : MonoBehaviour, IPartLookup
        {
            public readonly List<Transform> parts = new List<Transform>();
            public IEnumerable<Transform> PartTransforms => parts;
            public Transform FindPartTransform(string partName) => parts.Find(part => part.name == partName);
        }

        [Test]
        public void WholeBody_DefaultTargetsEveryDeclaredRenderer_NotArbitraryDescendants()
        {
            var root = new GameObject("Zoe");
            try
            {
                var declared = root.AddComponent<DeclaredParts>();
                var legs = Part(root.transform, "Legs", declared);
                var upper = Part(root.transform, "Upper", declared);
                var equippedFx = new GameObject("Equipped FX");
                equippedFx.transform.SetParent(root.transform);
                equippedFx.AddComponent<SpriteRenderer>();

                var renderers = ReactionFxPlayer.ResolveBodyRenderers(root.transform, declared);
                var effect = new BodySpriteFxEffect();
                var targets = new List<SpriteRenderer>(effect.TargetRenderers(new EventContext
                {
                    BodyRenderers = renderers,
                    PartLookup = declared,
                }));

                CollectionAssert.AreEquivalent(new[] { legs, upper }, targets);
                CollectionAssert.DoesNotContain(targets, equippedFx.GetComponent<SpriteRenderer>());
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void NamedPart_RestrictsBodySpriteFxToThatDeclaredRenderer()
        {
            var root = new GameObject("Zoe");
            try
            {
                var declared = root.AddComponent<DeclaredParts>();
                Part(root.transform, "Legs", declared);
                var upper = Part(root.transform, "Upper", declared);
                var effect = new BodySpriteFxEffect { targetPart = "Upper" };

                var targets = new List<SpriteRenderer>(effect.TargetRenderers(new EventContext
                {
                    BodyRenderers = ReactionFxPlayer.ResolveBodyRenderers(root.transform, declared),
                    PartLookup = declared,
                }));

                CollectionAssert.AreEqual(new[] { upper }, targets);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static SpriteRenderer Part(Transform root, string name, DeclaredParts declared)
        {
            var part = new GameObject(name);
            part.transform.SetParent(root);
            declared.parts.Add(part.transform);
            return part.AddComponent<SpriteRenderer>();
        }
    }
}
