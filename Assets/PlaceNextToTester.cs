using Laubrary.Cookbook2D;
using UnityEngine;

public class PlaceNextToTester : MonoBehaviour
{
    public SpriteRenderer source;
    public SpriteRenderer target;
    public NineSlicePosition sourcePoint;
    public NineSlicePosition targetPoint;
    [Range(0,1)]public float space;

    // Update is called once per frame
    void Update()
    {
        source.PlaceNextTo(sourcePoint, target, targetPoint,space);


        //var point = source.bounds.GetPoint(sourcePoint);
        //DrawDebugCircle(point, 0.05f, Color.green);
    }

    // Draws a debug circle at the specified position, with the given radius and color.
    void DrawDebugCircle(Vector3 center, float radius, Color color, int segments = 36, float duration = 0.02f)
    {
        float angleStep = 360f / segments;
        Quaternion rotation = Quaternion.AngleAxis(angleStep, Vector3.forward);

        Vector3 startDirection = radius * Vector3.right;
        Vector3 previousPoint = center + startDirection;
        Vector3 nextPoint;

        for (int i = 1; i <= segments; i++)
        {
            nextPoint = center + rotation * startDirection;
            Debug.DrawLine(previousPoint, nextPoint, color, duration);
            previousPoint = nextPoint;
            startDirection = rotation * startDirection;
        }
    }
}
