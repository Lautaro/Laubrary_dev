using Lautaro.Cookbook2D;
using Sirenix.OdinInspector;
using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class RendererPositionTester : MonoBehaviour
{
    public RendererPositionTester other;
    public BoundsPositions position;
    public GameObject marker;
    [Range(-3,3)]public float space;
    public SpriteRenderer rendy => GetComponent<SpriteRenderer>();


    [Button]
    public void Position()
    {
        rendy.PositionByBound(position,other.rendy, other.position, space);
    }
}
