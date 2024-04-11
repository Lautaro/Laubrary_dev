using Laubrary.Dashboard;
using Laubrary.Randomizers;
using Laubrary.Cookbook2D;
using Sirenix.OdinInspector;
using UnityEngine;

public class Cookbook2DTester : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        PaintBounds();
    }

    void PaintBounds()
    {
        var newBound = Cookbook2D.GetScreenNineSlicePositionBounds(position, 0f);
        Cookbook2D.DebugDrawBounds2D(newBound, Color.red, 0.01f);
    }
    
    public NineSlicePosition position;
    public NineSlicePosition point;

    [Button]
    public void Move()
    {
        var newBound = Cookbook2D.GetScreenNineSlicePositionBounds(position, 0f);
        transform.position =  newBound.GetPoint(point);
        PaintBounds();
    }

    [Button]
    public void RandomMove()
    {
        var newBound = Cookbook2D.GetScreenNineSlicePositionBounds(position, 0f);
        transform.position = newBound.GetRandomPositionWithin();
        PaintBounds();
    }
}
