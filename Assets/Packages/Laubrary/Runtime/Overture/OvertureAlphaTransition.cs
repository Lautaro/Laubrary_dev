using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Laubrary.Overture
{

public class OvertureAlphaTransition : OvertureTransition
{
    [Header("Alpha Settings")]
    [SerializeField] private float targetAlpha = 0f;
    
    [Header("Search Settings")]
    [SerializeField] private int searchDepth = -1;
    [SerializeField] private bool includeSpriteRenderers = true;
    [SerializeField] private bool includeMeshRenderers = false;
    [SerializeField] private bool includeCanvasGroups = true;

    private List<IFadeable> fadeables = new List<IFadeable>();

    private interface IFadeable
    {
        float Alpha { get; set; }
        bool IsValid { get; }
    }

    private class SpriteRendererFadeable : IFadeable
    {
        private SpriteRenderer spriteRenderer;
        private float originalAlpha;

        public SpriteRendererFadeable(SpriteRenderer renderer)
        {
            spriteRenderer = renderer;
            originalAlpha = renderer.color.a;
        }

        public float Alpha
        {
            get => spriteRenderer != null ? spriteRenderer.color.a : 0f;
            set
            {
                if (spriteRenderer != null)
                {
                    Color color = spriteRenderer.color;
                    color.a = value;
                    spriteRenderer.color = color;
                }
            }
        }

        public bool IsValid => spriteRenderer != null;
        public float OriginalAlpha => originalAlpha;
    }

    private class MeshRendererFadeable : IFadeable
    {
        private MeshRenderer meshRenderer;
        private float originalAlpha;
        private Material materialInstance;

        public MeshRendererFadeable(MeshRenderer renderer)
        {
            meshRenderer = renderer;
            if (meshRenderer != null && meshRenderer.material != null)
            {
                materialInstance = meshRenderer.material;
                originalAlpha = materialInstance.color.a;
            }
        }

        public float Alpha
        {
            get => materialInstance != null ? materialInstance.color.a : 0f;
            set
            {
                if (materialInstance != null)
                {
                    Color color = materialInstance.color;
                    color.a = value;
                    materialInstance.color = color;
                }
            }
        }

        public bool IsValid => meshRenderer != null && materialInstance != null;
        public float OriginalAlpha => originalAlpha;
    }

    private class CanvasGroupFadeable : IFadeable
    {
        private CanvasGroup canvasGroup;
        private float originalAlpha;

        public CanvasGroupFadeable(CanvasGroup group)
        {
            canvasGroup = group;
            originalAlpha = group.alpha;
        }

        public float Alpha
        {
            get => canvasGroup != null ? canvasGroup.alpha : 0f;
            set
            {
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = value;
                }
            }
        }

        public bool IsValid => canvasGroup != null;
        public float OriginalAlpha => originalAlpha;
    }

    private void Awake()
    {
        CollectFadeables();
        
        if (fadeables.Count == 0)
        {
            Debug.LogWarning($"OvertureAlphaTransition on '{gameObject.name}' found no fadeables. " +
                           $"Check that targetObjects have CanvasGroup/SpriteRenderer/MeshRenderer components.", this);
        }
    }

    private void CollectFadeables()
    {
        fadeables.Clear();

        foreach (GameObject obj in targetObjects)
        {
            if (obj != null)
            {
                CollectFromHierarchy(obj.transform, 0);
            }
        }
    }

    private void CollectFromHierarchy(Transform root, int currentDepth)
    {
        if (searchDepth >= 0 && currentDepth > searchDepth)
        {
            return;
        }

        if (includeSpriteRenderers)
        {
            SpriteRenderer spriteRenderer = root.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                fadeables.Add(new SpriteRendererFadeable(spriteRenderer));
            }
        }

        if (includeMeshRenderers)
        {
            MeshRenderer meshRenderer = root.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                fadeables.Add(new MeshRendererFadeable(meshRenderer));
            }
        }

        if (includeCanvasGroups)
        {
            CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                fadeables.Add(new CanvasGroupFadeable(canvasGroup));
            }
        }

        for (int i = 0; i < root.childCount; i++)
        {
            CollectFromHierarchy(root.GetChild(i), currentDepth + 1);
        }
    }

    protected override async Task ExecuteTransition(bool isEnter)
    {
        await AnimateFadeables(isEnter);
        Done();
    }

    private async Task AnimateFadeables(bool isEnter)
    {
        float elapsedTime = 0f;

        List<float> originalAlphas = new List<float>();
        foreach (var fadeable in fadeables)
        {
            if (fadeable is SpriteRendererFadeable sprite)
            {
                originalAlphas.Add(sprite.OriginalAlpha);
            }
            else if (fadeable is MeshRendererFadeable mesh)
            {
                originalAlphas.Add(mesh.OriginalAlpha);
            }
            else if (fadeable is CanvasGroupFadeable canvas)
            {
                originalAlphas.Add(canvas.OriginalAlpha);
            }
        }

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / duration);
            float curveValue = curve.Evaluate(t);

            for (int i = 0; i < fadeables.Count; i++)
            {
                if (fadeables[i].IsValid)
                {
                    float startAlpha = isEnter ? targetAlpha : originalAlphas[i];
                    float endAlpha = isEnter ? originalAlphas[i] : targetAlpha;
                    fadeables[i].Alpha = Mathf.LerpUnclamped(startAlpha, endAlpha, curveValue);
                }
            }

            await Task.Yield();
        }

        for (int i = 0; i < fadeables.Count; i++)
        {
            if (fadeables[i].IsValid)
            {
                float endAlpha = isEnter ? originalAlphas[i] : targetAlpha;
                fadeables[i].Alpha = endAlpha;
            }
        }
    }
}

} // namespace Laubrary.Overture
