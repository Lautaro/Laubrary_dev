using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Laubrary.Overture.Demo
{
    /// <summary>
    /// Drives a filled UI Image to reflect the progress of an OvertureTimedState.
    /// Attach to the LoadingState GameObject alongside OvertureTimedState.
    /// Set fillImage to an Image with ImageType = Filled and FillMethod = Horizontal.
    /// </summary>
    public class OvertureLoadingDisplay : MonoBehaviour
    {
        [SerializeField] private OvertureTimedState timedState;
        [SerializeField] private Image fillImage;
        [SerializeField] private TextMeshProUGUI progressText;

        private void OnEnable()
        {
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount = 0f;
            timedState.onProgressChanged.AddListener(UpdateProgress);
        }

        private void OnDisable()
        {
            timedState.onProgressChanged.RemoveListener(UpdateProgress);
        }

        private void UpdateProgress(float progress)
        {
            fillImage.fillAmount = progress;
            progressText.text = $"LOADING GAME {progress:P0}";
        }
    }
}
