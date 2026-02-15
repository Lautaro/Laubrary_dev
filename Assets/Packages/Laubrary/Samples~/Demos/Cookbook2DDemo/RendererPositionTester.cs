using UnityEngine;

namespace Laubrary.Cookbook2D.Samples
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class RendererPositionTester : MonoBehaviour
    {
        public RendererPositionTester other;
        public NineSlicePosition position;
        public GameObject marker;
        
        [Range(-3, 3)]
        public float space;

        public SpriteRenderer rendy => GetComponent<SpriteRenderer>();

        public void Position()
        {
            rendy.PlaceNextTo(position, other.rendy, other.position, space);
        }
    }
}
