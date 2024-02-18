using Lautaro.Cookbook2D;
using Lautaro.Dashboard;
using Sirenix.OdinInspector;
using UnityEngine;
using static Lautaro.Cookbook2D.Cookbook2D;

public class BoundPositionTest : MonoBehaviour
{
    public SpriteRenderer target;
    public BoundsPositions boundsposition;


    [Button]
    void Place()
    {
        transform.position = target.GetWorldPosition2D(boundsposition);
        Dashboard.Log(GetInstanceID().ToString(), transform.position.ToString());

    }
}
