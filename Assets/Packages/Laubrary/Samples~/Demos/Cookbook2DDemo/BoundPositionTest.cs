using UnityEngine;

namespace Laubrary.Cookbook2D.Samples
{
    public class BoundPositionTest : MonoBehaviour
    {
        public SpriteRenderer target;
        public NineSlicePosition boundsposition;

        void Place()
        {
            transform.position = target.GetWorldPosition2D(boundsposition);

        }
    }

}