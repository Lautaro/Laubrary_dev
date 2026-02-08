using Laubrary.Randomizers;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Randomizers.Samples
{
    public class RandomizersTester : MonoBehaviour
    {
        [Header("Random Int")]
        public List<int> MyNumbers = new();
        public List<int> RandomNumbers = new();

        [Header("Random String")]
        public List<string> MyStrings = new();
        public List<string> RandomStrings = new();

        [Header("Random Position")]
        public GameObject marker;
        [Range(0, 1)] public float vignette;
        public SpriteRenderer bounds;

        public void GetRandomInt()
        {
            var randInt = MyNumbers.GetRandomItem();
            RandomNumbers.Add(randInt);
        }

        public void GetRandomRoundRobin()
        {
            Randomizers.GetRoundRobinItem(MyStrings, ref RandomStrings);
        }

        public void GetRandomViewPortPosition()
        {
            marker.transform.position = Randomizers.GetRandomPositionInViewport(vignette);
        }

        public void GetRandomBoundsPosition()
        {
            marker.transform.position = bounds.bounds.GetRandomPositionWithin(vignette);
        }
    }
}