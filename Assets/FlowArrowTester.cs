using Laubrary.VisualDebug3D;
using Sirenix.OdinInspector;
using UnityEngine;
public class FlowArrowTester : MonoBehaviour
{
    [Range(0.1f, 1f)] public float spacing;
    [Range(0.5f, 5f)] public float speed;
    [Range(0.1f, 10f)] public float duration;
    [Range(0.1f, 2f)] public float scale;
    [Range(0f, .5f)] public float blinkSpeed;
    [Range(0.01f, 4f)] public float distanceScaling;
    public bool isUnlit;
    public Color color;
    public Transform start;
    public Transform end;
    private FlowArrow flowArrow;

    [Button]
    void Pulse()
    {
        if (!Application.isPlaying) return;
        FlowArrow.Create(start, end, scale, spacing, speed, duration, color, blinkSpeed, isUnlit,distanceScaling);
    }

    [Button]
    void PersistentPulse()
    {
        if (!Application.isPlaying) return;
        if (flowArrow == null)
        {
          flowArrow= FlowArrow.Create(start, end, scale, spacing, speed, 0, color, blinkSpeed, isUnlit,distanceScaling);
        }
        else
        {
            flowArrow.Kill();
        }
    }
}
