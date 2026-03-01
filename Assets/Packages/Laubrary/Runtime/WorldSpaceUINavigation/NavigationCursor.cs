using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Laubrary.WorldSpaceUINavigation
{
    /// <summary>
    /// Smoothly animates a UI Image to the position of the currently selected UI element.
    /// Works with both static buttons and moving world-space proxies.
    /// Assign a sprite via the Image component on this GameObject.
    /// </summary>
    [RequireComponent(typeof(RectTransform), typeof(Image))]
    public class NavigationCursor : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 18f;

        private RectTransform rectTransform;
        private Image image;
        private bool hasPosition;

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            image         = GetComponent<Image>();
        }

        private void Update()
        {
            var selected = EventSystem.current?.currentSelectedGameObject;

            if (selected == null)
            {
                image.enabled = false;
                hasPosition   = false;
                return;
            }

            var selectedRect = selected.GetComponent<RectTransform>();
            if (selectedRect == null) return;

            image.enabled = true;

            // Snap to position on first frame so it doesn't fly in from the origin
            if (!hasPosition)
            {
                rectTransform.position = selectedRect.position;
                hasPosition            = true;
            }

            rectTransform.position = Vector3.Lerp(
                rectTransform.position,
                selectedRect.position,
                moveSpeed * Time.deltaTime);
        }
    }
}
