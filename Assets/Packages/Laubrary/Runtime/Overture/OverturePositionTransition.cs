using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Laubrary.Overture
{

public class OverturePositionTransition : OvertureTransition
{
    [Header("Position Settings")]
    [SerializeField] private Vector3 targetPosition = Vector3.zero;
    [SerializeField] private bool useLocalPosition = true;

    private Dictionary<GameObject, Vector3> originalPositions = new Dictionary<GameObject, Vector3>();

    private void Awake()
    {
        foreach (GameObject obj in targetObjects)
        {
            if (obj != null)
            {
                originalPositions[obj] = useLocalPosition ? obj.transform.localPosition : obj.transform.position;
            }
        }
    }

    protected override async Task ExecuteTransition(bool isEnter)
    {
        await AnimateObjects(obj => AnimatePosition(obj, isEnter));
    }

    private async Task AnimatePosition(GameObject obj, bool isEnter)
    {
        if (!originalPositions.ContainsKey(obj))
        {
            return;
        }

        Vector3 startPosition = isEnter ? targetPosition : originalPositions[obj];
        Vector3 endPosition = isEnter ? originalPositions[obj] : targetPosition;

        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            if (obj == null)
            {
                break;
            }

            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / duration);
            float curveValue = curve.Evaluate(t);
            Vector3 newPosition = Vector3.LerpUnclamped(startPosition, endPosition, curveValue);

            if (useLocalPosition)
            {
                obj.transform.localPosition = newPosition;
            }
            else
            {
                obj.transform.position = newPosition;
            }

            await Task.Yield();
        }

        if (obj != null)
        {
            if (useLocalPosition)
            {
                obj.transform.localPosition = endPosition;
            }
            else
            {
                obj.transform.position = endPosition;
            }
        }
    }
}

} // namespace Laubrary.Overture
