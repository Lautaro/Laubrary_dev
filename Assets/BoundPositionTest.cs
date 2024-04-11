using Laubrary.Dashboard;
using Laubrary.Cookbook2D;
using Sirenix.OdinInspector;
using UnityEngine;
using static Laubrary.Cookbook2D.Cookbook2D;

public class BoundPositionTest : MonoBehaviour
{
    public SpriteRenderer target;
    public NineSlicePosition boundsposition;


    [Button]
    void Place()
    {
        transform.position = target.GetWorldPosition2D(boundsposition);

    }
}
