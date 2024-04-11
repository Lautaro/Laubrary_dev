using Laubrary.Randomizers;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

public class RandomizersTester : MonoBehaviour
{
    [BoxGroup("Random Int")]public List<int> MyNumbers = new();
    [BoxGroup("Random Int")] public List<int> RandomNumbers = new();

    [BoxGroup("Random Int")][Button]
    public void GetRandomInt ()
    {
        var randInt =  MyNumbers.GetRandomItem();
        RandomNumbers.Add(randInt);
    }

    [BoxGroup("Random String")] public List<string> MyStrings = new ();
    [BoxGroup("Random String")] public List<string> RandomStrings= new();

    [BoxGroup("Random String")][Button]
    public void GetRandomRoundRobin()
    {
        Randomizers.GetRoundRobinItem(MyStrings, ref RandomStrings);
    }

    [BoxGroup("Random Position")] public GameObject marker;
    [BoxGroup("Random Position"), Range(0,1)] public float vignette;

    [BoxGroup("Random Position")][Button]
    public void GetRandomViewPortPosition()
    {
         marker.transform.position=  Randomizers.GetRandomPositionInViewport(vignette);
    }


    [BoxGroup("Random Position")] public SpriteRenderer bounds;

    [BoxGroup("Random Position")]
    [Button]
    public void GetRandomBoundsPosition()
    {
        marker.transform.position = bounds.bounds.GetRandomPositionWithin(vignette);
    }
}
