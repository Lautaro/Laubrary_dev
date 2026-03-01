using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Laubrary.WorldSpaceUINavigation
{
    /// <summary>
    /// A screen-space UI Button that tracks a world-space Transform.
    /// Participates in Unity's automatic UI navigation.
    /// Relays OnSelect and OnDeselect to any IWorldSpaceProxyTarget on the world object.
    /// Spawned and owned by WorldSpaceUIProxyManager.
    /// </summary>
    [RequireComponent(typeof(RectTransform), typeof(Button))]
    public class WorldSpaceUIProxy : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        private Transform worldTarget;
        private Camera trackingCamera;
        private RectTransform rectTransform;
        private IWorldSpaceProxyTarget proxyTarget;

        /// <summary>The world-space Transform this proxy is tracking.</summary>
        public Transform WorldTarget => worldTarget;

        /// <summary>Initializes the proxy with its target and the camera used for projection.</summary>
        public void Initialize(Transform target, Camera camera)
        {
            worldTarget    = target;
            trackingCamera = camera;
            rectTransform  = GetComponent<RectTransform>();
            proxyTarget    = target.GetComponent<IWorldSpaceProxyTarget>();
        }

        private void LateUpdate()
        {
            if (worldTarget == null || trackingCamera == null)
                return;

            Vector3 screenPos = trackingCamera.WorldToScreenPoint(worldTarget.position);
            rectTransform.position = screenPos;
        }

        public void OnSelect(BaseEventData eventData)
        {
            proxyTarget?.OnProxySelected();
        }

        public void OnDeselect(BaseEventData eventData)
        {
            proxyTarget?.OnProxyDeselected();
        }
    }
}
