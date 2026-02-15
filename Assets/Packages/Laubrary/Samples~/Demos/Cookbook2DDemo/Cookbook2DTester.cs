using Laubrary.Randomizers;
using UnityEngine;

namespace Laubrary.Cookbook2D.Samples
{
    public class Cookbook2DTester : MonoBehaviour
    {
        public NineSlicePosition position;
        public NineSlicePosition point;

        void Start()
        {

        }

        void Update()
        {
            PaintBounds();
        }

        void PaintBounds()
        {
            var newBound = Cookbook2D.GetScreenNineSlicePositionBounds(position, 0f);
            Cookbook2D.DebugDrawBounds2D(newBound, Color.red, 0.01f);
        }

        public void Move()
        {
            var newBound = Cookbook2D.GetScreenNineSlicePositionBounds(position, 0f);
            transform.position = newBound.GetPoint(point);
            PaintBounds();
        }

        public void RandomMove()
        {
            var newBound = Cookbook2D.GetScreenNineSlicePositionBounds(position, 0f);
            transform.position = newBound.GetRandomPositionWithin();
            PaintBounds();
        }
    }
}