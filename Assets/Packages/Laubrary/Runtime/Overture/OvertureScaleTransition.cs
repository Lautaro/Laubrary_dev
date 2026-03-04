using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Laubrary.Overture
{

public class OvertureScaleTransition : OvertureTransition
{
    [Header("Scale Settings")]
    [SerializeField] private Vector3 targetScale = Vector3.zero;

    private Dictionary<GameObject, Vector3> originalScales = new Dictionary<GameObject, Vector3>();

    private void Awake()
    {
        foreach (GameObject obj in targetObjects)
        {
            if (obj != null)
            {
                originalScales[obj] = obj.transform.localScale;
            }
        }
    }

    protected override async Task ExecuteTransition(bool isEnter)
    {
        await AnimateObjects(obj => AnimateScale(obj, isEnter));
    }

    private async Task AnimateScale(GameObject obj, bool isEnter)
    {
        if (!originalScales.ContainsKey(obj))
        {
            return;
        }

        Vector3 startScale = isEnter ? targetScale : originalScales[obj];
        Vector3 endScale = isEnter ? originalScales[obj] : targetScale;

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
            obj.transform.localScale = Vector3.LerpUnclamped(startScale, endScale, curveValue);

            await Task.Yield();
        }

        if (obj != null)
        {
            obj.transform.localScale = endScale;
        }
    }
}

} // namespace Laubrary.Overture
