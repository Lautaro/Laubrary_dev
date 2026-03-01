using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Laubrary.WorldSpaceUINavigation
{
    /// <summary>
    /// Spawns and manages screen-space UI proxy buttons for a set of world-space targets.
    /// Attach to a RectTransform inside a Canvas. Proxies are created as children on Awake.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class WorldSpaceUIProxyManager : MonoBehaviour
    {
        [SerializeField] private List<Transform> trackedObjects = new();
        [SerializeField] private Camera trackingCamera;
        [SerializeField] private Vector2 proxySize = new Vector2(60f, 60f);
        [SerializeField] private Color proxyColor = new Color(1f, 1f, 1f, 0.35f);
        [SerializeField] private NavigationCursor navigationCursor;

        private readonly List<WorldSpaceUIProxy> activeProxies = new();
        private RectTransform containerRect;

        private void Awake()
        {
            containerRect = GetComponent<RectTransform>();

            if (trackingCamera == null)
                trackingCamera = Camera.main;

            SpawnProxies();
        }

        private void SpawnProxies()
        {
            foreach (Transform target in trackedObjects)
            {
                if (target == null) continue;
                activeProxies.Add(CreateProxy(target));
            }
        }

        private WorldSpaceUIProxy CreateProxy(Transform target)
        {
            var go = new GameObject($"Proxy_{target.name}", typeof(RectTransform));
            go.transform.SetParent(containerRect, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = proxySize;

            var image = go.AddComponent<Image>();
            image.color = proxyColor;
            image.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };

            var proxy = go.AddComponent<WorldSpaceUIProxy>();
            proxy.Initialize(target, trackingCamera);

            return proxy;
        }
    }
}
