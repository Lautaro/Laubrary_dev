using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EditableLabel : MonoBehaviour
{
    public TextMeshProUGUI labelText;
    public TMP_InputField inputField;
    public Button button;

    private void Start()
    {
        button.onClick.AddListener(ActivateInputField);
        inputField.onEndEdit.AddListener(DeactivateInputField);
    }

    private void ActivateInputField()
    {
        labelText.gameObject.SetActive(false);
        inputField.text = labelText.text;
        inputField.gameObject.SetActive(true);
        inputField.Select();
        inputField.ActivateInputField();
    }

    private void DeactivateInputField(string newText)
    {
        inputField.gameObject.SetActive(false);
        labelText.gameObject.SetActive(true);
        labelText.text = newText;
    }
}
