using UnityEngine;

namespace Laubrary.WorldSpaceUINavigation.Demo
{
    /// <summary>Slowly orbits around a look target, picking new random positions over time.</summary>
    public class CameraRoamer : MonoBehaviour
    {
        [SerializeField] private Vector3 lookTarget = new Vector3(0f, 0.5f, 0f);
        [SerializeField] private float minOrbitRadius = 8f;
        [SerializeField] private float maxOrbitRadius = 13f;
        [SerializeField] private float minHeight = 4f;
        [SerializeField] private float maxHeight = 9f;
        [SerializeField] private float moveSpeed = 1.2f;
        [SerializeField] private float arrivalDistance = 0.3f;

        private Vector3 targetPosition;

        private void Start()
        {
            PickNewTarget();
            transform.position = targetPosition;
            transform.LookAt(lookTarget);
        }

        private void Update()
        {
            if (Vector3.Distance(transform.position, targetPosition) <= arrivalDistance)
                PickNewTarget();

            transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);
            transform.LookAt(lookTarget);
        }

        private void PickNewTarget()
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float radius = Random.Range(minOrbitRadius, maxOrbitRadius);
            float height = Random.Range(minHeight, maxHeight);
            targetPosition = new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
        }
    }
}
