using UnityEngine;

namespace Laubrary.WorldSpaceUINavigation.Demo
{
    /// <summary>Slowly wanders to random positions on the XZ plane within a radius.</summary>
    public class RandomWanderer : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 0.6f;
        [SerializeField] private float wanderRadius = 4f;
        [SerializeField] private float arrivalDistance = 0.15f;
        [SerializeField] private float minPauseTime = 1f;
        [SerializeField] private float maxPauseTime = 3.5f;

        private Vector3 targetPosition;
        private float pauseTimer;
        private float fixedY;

        private void Start()
        {
            fixedY = transform.position.y;
            PickNewTarget();
        }

        private void Update()
        {
            if (pauseTimer > 0f)
            {
                pauseTimer -= Time.deltaTime;
                return;
            }

            Vector3 delta = targetPosition - transform.position;

            if (delta.magnitude <= arrivalDistance)
            {
                pauseTimer = Random.Range(minPauseTime, maxPauseTime);
                PickNewTarget();
                return;
            }

            transform.position += delta.normalized * (moveSpeed * Time.deltaTime);
        }

        private void PickNewTarget()
        {
            Vector2 circle = Random.insideUnitCircle * wanderRadius;
            targetPosition = new Vector3(circle.x, fixedY, circle.y);
        }
    }
}
