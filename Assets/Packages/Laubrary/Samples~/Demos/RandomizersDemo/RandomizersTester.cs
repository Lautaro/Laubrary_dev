using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Laubrary.Randomizers.Samples
{
    public class RandomizersTester : MonoBehaviour
    {
        [Header("Random String")]
        public List<string> MyStrings = new();

        [Header("Random Position")]
        public GameObject marker;
        [Range(0, 1)] public float vignette;
        public SpriteRenderer bounds;

        public RoundRobin<int> RRNumbers;
        public RoundRobinUnity<int> RRNumbers2;


        public void Awake()
        {
            var ie = MyStrings.Select(s => int.Parse(s));
            RRNumbers = new RoundRobin<int>(ie);
            RRNumbers2 = new RoundRobinUnity<int>(ie);
        }

        [ContextMenu("Get Random Numbers")]
        public void RandomNumbers()
        {
            var log = "";
            for (int i = 0; i < RRNumbers.Collection.Count(); i++)    
                log += RRNumbers.GetNext() + " - ";


            Debug.Log(log);
        }

        [ContextMenu("Get Random Numbers2")]
        public void RandomNumbers2()
        {
            var log = "";
            for (int i = 0; i < RRNumbers.Collection.Count(); i++)
                log += RRNumbers.GetNext() + " - ";


            Debug.Log(log);
        }


        [ContextMenu("Get Random Viewport Position")]
        public void GetRandomViewPortPosition()
        {
            marker.transform.position = Randomizers.GetRandomPositionInViewport(vignette);
        }

        [ContextMenu("Get Random Bounds Position")]
        public void GetRandomBoundsPosition()
        {
            marker.transform.position = bounds.bounds.GetRandomPositionWithin(vignette);
        }
    }
}