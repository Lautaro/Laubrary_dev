using System.Collections.Generic;
using UnityEngine;
namespace Laubrary.VisualDebug3D
{
    public class FlowArrow : MonoBehaviour
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="start">Start of the arrow</param>
        /// <param name="end">End of the arrow</param>
        /// <param name="scale">Scale for the spheres used to make the arrow</param>
        /// <param name="distanceBetweenPrimitives">Spacing. The bigger the distance between spheres.</param>
        /// <param name="speed">How fast the spheres move</param>
        /// <param name="duration">How long the arrow will last</param>
        /// <param name="color">Color of the spheres</param>
        /// <param name="blinkDuration">The duration of a blink</param>
        /// <param name="useUnlitMaterial">If true the spheres will not react to light.</param>
        /// <param name="distanceScaling">0.3 would mean that spheres will start 30% bigger and end 30% smaller</param>
        /// <returns></returns>
        public static FlowArrow Create(
            Transform start,
            Transform end,
            float scale = 0.1f,
            float distanceBetweenPrimitives = 0.5f,
            float speed = 1.0f,
            float duration = 5.0f,
            Color? color = null,
            float blinkDuration = 0.5f,
            bool useUnlitMaterial = false,
            float distanceScaling = 0.0f)
        {
            GameObject flowArrowObj = new GameObject("FlowArrow");
            FlowArrow flowArrow = flowArrowObj.AddComponent<FlowArrow>();

            flowArrow.start = start;
            flowArrow.end = end;
            flowArrow.primitiveScale = scale;
            flowArrow.distanceBetweenPrimitives = distanceBetweenPrimitives;
            flowArrow.speed = speed;
            flowArrow.duration = duration;
            flowArrow.color = color ?? Color.white;
            flowArrow.blinkSpeed = blinkDuration;
            flowArrow.useUnlitMaterial = useUnlitMaterial;
            flowArrow.scaleFactor = distanceScaling;

            flowArrow.Initialize();
            return flowArrow;
        }

        private Transform start;
        private Transform end;
        private float primitiveScale;
        private float distanceBetweenPrimitives;
        private float speed;
        private float duration;
        private Color color;
        private float blinkSpeed;
        private bool useUnlitMaterial;
        private float scaleFactor;
        private List<BubbleBlink> primitives = new List<BubbleBlink>();
        private float elapsedTime = 0f;
        private float interval;
        private float distanceBetweenPrimitivesSpeed;

        private void Initialize()
        {
            float distance = Vector3.Distance(start.position, end.position);
            int primitiveCount = Mathf.CeilToInt(distance / distanceBetweenPrimitives);

            for (int i = 0; i < primitiveCount; i++)
            {
                BubbleBlink primitive = BubbleBlink.Blink(
                    start.position,
                    color,
                    blinkSpeed > 0 ? blinkSpeed : float.MaxValue,
                    Mathf.CeilToInt(duration / (blinkSpeed > 0 ? blinkSpeed : 1)),
                    useUnlitMaterial);
                primitive.transform.localScale = Vector3.one * primitiveScale;
                primitive.transform.parent = transform;
                primitives.Add(primitive);
            }

            interval = distanceBetweenPrimitives / speed;
            distanceBetweenPrimitivesSpeed = distanceBetweenPrimitives / speed * primitives.Count;
        }

        private void Update()
        {
            elapsedTime += Time.deltaTime;

            for (int i = 0; i < primitives.Count; i++)
            {
                float t = (elapsedTime + i * interval) % distanceBetweenPrimitivesSpeed / distanceBetweenPrimitivesSpeed;
                primitives[i].transform.position = Vector3.Lerp(start.position, end.position, t);

                if (scaleFactor != 0)
                {
                    float scaleAdjustment = 1.0f + scaleFactor * (1 - t);
                    primitives[i].transform.localScale = Vector3.one * primitiveScale * scaleAdjustment;
                }
            }

            if (duration > 0 && elapsedTime >= duration)
            {
                DestroyArrow();
            }
        }

        public void Kill()
        {
            DestroyArrow();
        }

        private void DestroyArrow()
        {
            foreach (var primitive in primitives)
            {
                Destroy(primitive.gameObject);
            }

            Destroy(gameObject);
        }
    }
}