using Laubrary.WorldSpaceUINavigation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Laubrary.WorldSpaceUINavigation.Demo
{
    /// <summary>
    /// Demo implementation of IWorldSpaceProxyTarget.
    /// On selection: enables an inverted-hull outline child, swaps to a per-instance
    /// highlighted material with a subtle pulsing emission, and smoothly scales up.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class WorldSpaceHighlightTarget : MonoBehaviour, IWorldSpaceProxyTarget
    {
        [SerializeField] private Material normalMaterial;
        [SerializeField] private Material highlightedMaterial;
        [SerializeField] private Material outlineMaterial;

        [Header("Scale")]
        [SerializeField] private float highlightScaleMultiplier = 1.15f;
        [SerializeField] private float scaleTransitionSpeed = 10f;

        [Header("Emission Pulse")]
        [SerializeField] private float emissionPulseSpeed = 2f;
        [SerializeField] private float emissionPulseIntensity = 0.5f;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private MeshRenderer meshRenderer;
        private GameObject outlineObject;
        private Material materialInstance;
        private Color emissionBaseColor;

        private Vector3 baseScale;
        private Vector3 targetScale;
        private bool isSelected;

        private void Awake()
        {
            meshRenderer = GetComponent<MeshRenderer>();
            baseScale    = transform.localScale;
            targetScale  = baseScale;

            if (normalMaterial == null)
                normalMaterial = meshRenderer.sharedMaterial;

            if (highlightedMaterial != null)
                emissionBaseColor = highlightedMaterial.GetColor("_Color");

            BuildOutlineChild();
        }

        private void BuildOutlineChild()
        {
            if (outlineMaterial == null) return;

            outlineObject = new GameObject("Outline");
            outlineObject.transform.SetParent(transform, false);

            var mf            = outlineObject.AddComponent<MeshFilter>();
            mf.sharedMesh     = GetComponent<MeshFilter>().sharedMesh;

            var mr            = outlineObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = outlineMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            outlineObject.SetActive(false);
        }

        private void Update()
        {
            transform.localScale = Vector3.Lerp(
                transform.localScale,
                targetScale,
                scaleTransitionSpeed * Time.deltaTime);

            if (isSelected && materialInstance != null)
            {
                // Pulse between 0.1 and 0.9 so emission never fully disappears
                float t = Mathf.Sin(Time.time * emissionPulseSpeed) * 0.4f + 0.5f;
                materialInstance.SetColor(EmissionColorId, emissionBaseColor * (t * emissionPulseIntensity));
            }
        }

        public void OnProxySelected()   => SetSelected(true);
        public void OnProxyDeselected() => SetSelected(false);

        private void SetSelected(bool selected)
        {
            if (isSelected == selected) return;
            isSelected = selected;

            targetScale = selected ? baseScale * highlightScaleMultiplier : baseScale;

            if (outlineObject != null)
                outlineObject.SetActive(selected);

            if (selected && highlightedMaterial != null)
            {
                meshRenderer.material = highlightedMaterial;
                materialInstance      = meshRenderer.material;
                materialInstance.EnableKeyword("_EMISSION");
            }
            else
            {
                DestroyMaterialInstance();
                meshRenderer.sharedMaterial = normalMaterial;
            }
        }

        private void DestroyMaterialInstance()
        {
            if (materialInstance == null) return;
            Destroy(materialInstance);
            materialInstance = null;
        }

        private void OnDestroy() => DestroyMaterialInstance();
    }
}
