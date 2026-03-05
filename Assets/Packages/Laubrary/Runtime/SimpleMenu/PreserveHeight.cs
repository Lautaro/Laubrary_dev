using UnityEngine;

[ExecuteAlways]
public class PreserveHeight : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        var height = GetComponent<RectTransform>().rect.height;
        GetComponent<UnityEngine.UI.LayoutElement>().minHeight = height;
    }
}
