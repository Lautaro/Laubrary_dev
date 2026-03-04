using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Laubrary.Overture
{

[Serializable]
public class TransitionTarget
{
    public GameObject gameObject;
    public bool alpha = true;
    public bool position = true;
    public bool scale = true;
}

public class OvertureMultiTransition : MonoBehaviour, IOvertureTransition
{
    [SerializeField] private TransitionType _transitionType;

    public TransitionType transitionType
    {
        get => _transitionType;
        set => _transitionType = value;
    }

    [SerializeField] private List<TransitionTarget> transitionTargets = new List<TransitionTarget>();

    private TaskCompletionSource<bool> completionSource;

    public async Task Execute(bool isEnter)
    {
        completionSource = new TaskCompletionSource<bool>();

        if (isEnter)
        {
            EnableTransitionObjects();
        }

        await ExecuteTransition(isEnter);
        await completionSource.Task;

        if (!isEnter)
        {
            DisableTransitionObjects();
        }
    }

    [SerializeField] private bool enableScale = false;
    [SerializeField] private Vector3 scaleTarget = Vector3.zero;
    [SerializeField] private float scaleDuration = 0.5f;
    [SerializeField] private AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [SerializeField] private bool enablePosition = false;
    [SerializeField] private Vector3 positionTarget = Vector3.zero;
    [SerializeField] private bool useLocalPosition = true;
    [SerializeField] private float positionDuration = 0.5f;
    [SerializeField] private AnimationCurve positionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [SerializeField] private bool enableAlpha = false;
    [SerializeField] [Range(0f, 1f)] private float alphaTarget = 0f;
    [SerializeField] private float alphaDuration = 0.5f;
    [SerializeField] private AnimationCurve alphaCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private bool includeSpriteRenderers = true;
    [SerializeField] private bool includeMeshRenderers = true;
    [SerializeField] private bool includeCanvasGroups = true;
    [SerializeField] [Range(0, 10)] private int hierarchyDepth = 0;

    private Dictionary<Transform, Vector3> originalScales = new Dictionary<Transform, Vector3>();
    private Dictionary<Transform, Vector3> originalPositions = new Dictionary<Transform, Vector3>();
    private List<IFadeable> fadeables = new List<IFadeable>();

    private interface IFadeable
    {
        float Alpha { get; set; }
        float OriginalAlpha { get; }
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

        public float OriginalAlpha => originalAlpha;
        public bool IsValid => spriteRenderer != null;
    }

    private class MeshRendererFadeable : IFadeable
    {
        private MeshRenderer meshRenderer;
        private Material materialInstance;
        private float originalAlpha;
        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");

        public MeshRendererFadeable(MeshRenderer renderer)
        {
            meshRenderer = renderer;
            materialInstance = renderer.material;

            if (materialInstance.HasProperty(ColorPropertyId))
            {
                originalAlpha = materialInstance.color.a;
            }
            else if (materialInstance.HasProperty(BaseColorPropertyId))
            {
                originalAlpha = materialInstance.GetColor(BaseColorPropertyId).a;
            }
            else
            {
                originalAlpha = 1f;
            }
        }

        public float Alpha
        {
            get
            {
                if (materialInstance == null) return 0f;
                if (materialInstance.HasProperty(ColorPropertyId))
                    return materialInstance.color.a;
                if (materialInstance.HasProperty(BaseColorPropertyId))
                    return materialInstance.GetColor(BaseColorPropertyId).a;
                return 1f;
            }
            set
            {
                if (materialInstance == null) return;

                if (materialInstance.HasProperty(ColorPropertyId))
                {
                    Color color = materialInstance.color;
                    color.a = value;
                    materialInstance.color = color;
                }
                else if (materialInstance.HasProperty(BaseColorPropertyId))
                {
                    Color color = materialInstance.GetColor(BaseColorPropertyId);
                    color.a = value;
                    materialInstance.SetColor(BaseColorPropertyId, color);
                }
            }
        }

        public float OriginalAlpha => originalAlpha;
        public bool IsValid => meshRenderer != null && materialInstance != null;
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

        public float OriginalAlpha => originalAlpha;
        public bool IsValid => canvasGroup != null;
    }

    private async Task ExecuteTransition(bool isEnter)
    {
        if (enableScale && originalScales.Count == 0)
        {
            InitializeScaleTransitions();
        }

        if (enablePosition && originalPositions.Count == 0)
        {
            InitializePositionTransitions();
        }

        if (enableAlpha && fadeables.Count == 0)
        {
            CollectFadeables();
        }

        List<Task> tasks = new List<Task>();

        if (enableScale)
        {
            tasks.Add(AnimateScale(isEnter));
        }

        if (enablePosition)
        {
            tasks.Add(AnimatePosition(isEnter));
        }

        if (enableAlpha)
        {
            tasks.Add(AnimateAlpha(isEnter));
        }

        await Task.WhenAll(tasks);
        Done();
    }

    private void EnableTransitionObjects()
    {
        foreach (TransitionTarget target in transitionTargets)
        {
            if (target.gameObject != null)
            {
                target.gameObject.SetActive(true);
            }
        }
    }

    private void DisableTransitionObjects()
    {
        foreach (TransitionTarget target in transitionTargets)
        {
            if (target.gameObject != null)
            {
                target.gameObject.SetActive(false);
            }
        }
    }

    private void Done()
    {
        completionSource?.TrySetResult(true);
    }

    private void InitializeScaleTransitions()
    {
        originalScales.Clear();
        foreach (TransitionTarget target in transitionTargets)
        {
            if (target.gameObject != null && target.scale)
            {
                originalScales[target.gameObject.transform] = target.gameObject.transform.localScale;
            }
        }
    }

    private void InitializePositionTransitions()
    {
        originalPositions.Clear();
        foreach (TransitionTarget target in transitionTargets)
        {
            if (target.gameObject != null && target.position)
            {
                Vector3 pos = useLocalPosition
                    ? target.gameObject.transform.localPosition
                    : target.gameObject.transform.position;
                originalPositions[target.gameObject.transform] = pos;
            }
        }
    }

    private void CollectFadeables()
    {
        fadeables.Clear();
        foreach (TransitionTarget target in transitionTargets)
        {
            if (target.gameObject != null && target.alpha)
            {
                CollectFromHierarchy(target.gameObject.transform, 0);
            }
        }
    }

    private void CollectFromHierarchy(Transform root, int currentDepth)
    {
        if (currentDepth > hierarchyDepth)
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

    private async Task AnimateScale(bool isEnter)
    {
        float elapsedTime = 0f;
        while (elapsedTime < scaleDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / scaleDuration);
            float curveValue = scaleCurve.Evaluate(t);

            foreach (var kvp in originalScales)
            {
                if (kvp.Key != null)
                {
                    Vector3 startScale = isEnter ? scaleTarget : kvp.Value;
                    Vector3 endScale = isEnter ? kvp.Value : scaleTarget;
                    kvp.Key.localScale = Vector3.LerpUnclamped(startScale, endScale, curveValue);
                }
            }

            await Task.Yield();
        }
    }

    private async Task AnimatePosition(bool isEnter)
    {
        float elapsedTime = 0f;
        while (elapsedTime < positionDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / positionDuration);
            float curveValue = positionCurve.Evaluate(t);

            foreach (var kvp in originalPositions)
            {
                if (kvp.Key != null)
                {
                    Vector3 startPos = isEnter ? positionTarget : kvp.Value;
                    Vector3 endPos = isEnter ? kvp.Value : positionTarget;
                    Vector3 newPos = Vector3.LerpUnclamped(startPos, endPos, curveValue);

                    if (useLocalPosition)
                    {
                        kvp.Key.localPosition = newPos;
                    }
                    else
                    {
                        kvp.Key.position = newPos;
                    }
                }
            }

            await Task.Yield();
        }
    }

    private async Task AnimateAlpha(bool isEnter)
    {
        float elapsedTime = 0f;
        while (elapsedTime < alphaDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / alphaDuration);
            float curveValue = alphaCurve.Evaluate(t);

            foreach (var fadeable in fadeables)
            {
                if (fadeable.IsValid)
                {
                    float startAlpha = isEnter ? alphaTarget : fadeable.OriginalAlpha;
                    float endAlpha = isEnter ? fadeable.OriginalAlpha : alphaTarget;
                    fadeable.Alpha = Mathf.LerpUnclamped(startAlpha, endAlpha, curveValue);
                }
            }

            await Task.Yield();
        }
    }
}

} // namespace Laubrary.Overture
