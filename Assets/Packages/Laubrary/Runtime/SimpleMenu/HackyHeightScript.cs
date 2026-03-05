using TMPro;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public class HackyHeightScript : MonoBehaviour
{
    TextMeshProUGUI text;
    LayoutElement layoutElement;

    private void Start()
    {
        text = GetComponentInChildren<TextMeshProUGUI>();
        if (text == null)
            text = GetComponent<TextMeshProUGUI>();

        layoutElement = GetComponent<LayoutElement>();
    }

    private void Update()
    {
        if (text != null && layoutElement != null)
            layoutElement.minHeight = text.preferredHeight;
    }
}