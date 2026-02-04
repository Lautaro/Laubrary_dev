using UnityEngine;

public class SmoothRandomMovement : MonoBehaviour
{
    [Header("Position Settings")]
    [Range(0.1f, 10f)]
    public float minPositionSpeed = 1f;
    [Range(0.1f, 10f)]
    public float maxPositionSpeed = 5f;

    [Header("Rotation Settings")]
    [Range(0.1f, 10f)]
    public float minRotationSpeed = 1f;
    [Range(0.1f, 10f)]
    public float maxRotationSpeed = 5f;

    private Vector3 targetPosition;
    private Vector3 targetRotation;
    private float currentPositionSpeed;
    private float currentRotationSpeed;

    private void Start()
    {
        SetNewTargetPosition();
        SetNewTargetRotation();
    }

    private void Update()
    {
        // Smoothly move to the target position
        transform.position = Vector3.Lerp(transform.position, targetPosition, currentPositionSpeed * Time.deltaTime);

        // Smoothly rotate to the target rotation
        Quaternion targetRotationQuat = Quaternion.Euler(targetRotation);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotationQuat, currentRotationSpeed * Time.deltaTime);

        // If close enough to target, set a new target
        if (Vector3.Distance(transform.position, targetPosition) < 0.1f)
        {
            SetNewTargetPosition();
        }

        if (Quaternion.Angle(transform.rotation, targetRotationQuat) < 1f)
        {
            SetNewTargetRotation();
        }
    }

    private void SetNewTargetPosition()
    {
        targetPosition = new Vector3(
            Random.Range(-5f, 5f),
            Random.Range(-5f, 5f),
            Random.Range(-5f, 5f)
        );

        currentPositionSpeed = Random.Range(minPositionSpeed, maxPositionSpeed);
    }

    private void SetNewTargetRotation()
    {
        targetRotation = new Vector3(
            Random.Range(0f, 360f),
            Random.Range(0f, 360f),
            Random.Range(0f, 360f)
        );

        currentRotationSpeed = Random.Range(minRotationSpeed, maxRotationSpeed);
    }
}
