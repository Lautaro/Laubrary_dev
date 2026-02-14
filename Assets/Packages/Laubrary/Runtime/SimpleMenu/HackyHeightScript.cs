using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
            layoutElement.minHeight = text.preferredHeight;
    }
}