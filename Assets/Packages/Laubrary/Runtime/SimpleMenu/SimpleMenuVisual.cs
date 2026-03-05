using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Laubrary.Switcheroo;

namespace Laubrary.SimpleMenu
{
    [Flags]
    public enum SimpleMenuAnimationType
    {
        None     = 0,
        Alpha    = 1 << 0,
        Position = 1 << 1,
        Scale    = 1 << 2,
    }

    /// <summary>
    /// Plays container-level and per-element enter/exit transition animations on a SimpleMenu.
    /// Added automatically by SimpleMenuBase when any transition is configured.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class SimpleMenuVisual : MonoBehaviour
    {
        // ─── Container transition ─────────────────────────────────────────────
        [SerializeField] private SimpleMenuAnimationType animationType = SimpleMenuAnimationType.None;
        [SerializeField, Range(0f, 1f)] private float enterFromAlpha = 0f;
        [SerializeField] private Vector2 enterFromPositionOffset = new Vector2(0f, -40f);
        [SerializeField] private Vector3 enterFromScale = Vector3.zero;
        [SerializeField] private float enterDuration = 0.35f;
        [SerializeField] private AnimationCurve enterCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private float exitSpeedMultiplier = 2f;

        // ─── Element animation ────────────────────────────────────────────────
        [SerializeField] private bool animateElements = false;
        [SerializeField] private ElementAnimationDirection elementDirection = ElementAnimationDirection.TopBottom;
        [SerializeField] private ElementPickOrder elementPickOrder = ElementPickOrder.Sequential;
        [SerializeField, Range(0f, 1f)] private float elementStagger = 0.5f;
        [SerializeField] private bool useAlternateElementConfig = false;
        [SerializeField] private SimpleMenuElementAnimConfig altElementConfig = new SimpleMenuElementAnimConfig();

        // ─── Container runtime ────────────────────────────────────────────────
        private CanvasGroup _canvasGroup;
        private RectTransform _rectTransform;
        private float _originalAlpha;
        private Vector2 _originalAnchoredPosition;
        private Vector3 _originalLocalScale;
        private Switcheroo.Switcheroo _enterSwitcheroo;
        private Switcheroo.Switcheroo _exitSwitcheroo;

        // ─── Element runtime ──────────────────────────────────────────────────
        private CancellationTokenSource _elementCts;

        // ─── Lifecycle ────────────────────────────────────────────────────────

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _rectTransform = GetComponent<RectTransform>();

            // Always treat 1f as the fully-visible resting alpha.
            // Capturing from the CG risks picking up a 0 if the user hid it in the editor.
            _originalAlpha            = 1f;
            _originalAnchoredPosition = _rectTransform != null ? _rectTransform.anchoredPosition : Vector2.zero;
            _originalLocalScale       = transform.localScale;

            _enterSwitcheroo = new Switcheroo.Switcheroo();
            _enterSwitcheroo.AddTransition("enter", ApplyEnterProgress);

            _exitSwitcheroo = new Switcheroo.Switcheroo();
            _exitSwitcheroo.AddTransition("exit", ApplyExitProgress);
        }

        private void OnEnable()
        {
            if (animateElements)
                ApplyEnterProgress(1f); // Container stays at rest; elements handle the transition.
            else
                _enterSwitcheroo?.SetProgress(0f); // Container snaps to from-state so Enter() can animate it in.
        }

        private void OnDisable()
        {
            ApplyEnterProgress(1f);
            RestoreElements();
        }

        private void OnDestroy()
        {
            _enterSwitcheroo?.Dispose();
            _exitSwitcheroo?.Dispose();
            _elementCts?.Cancel();
            _elementCts?.Dispose();
        }

        // ─── Container callbacks ──────────────────────────────────────────────

        private void ApplyEnterProgress(float t)
        {
            float ct = enterCurve.Evaluate(t);

            if ((animationType & SimpleMenuAnimationType.Alpha) != 0 && _canvasGroup != null)
                _canvasGroup.alpha = Mathf.Lerp(enterFromAlpha, _originalAlpha, ct);

            if ((animationType & SimpleMenuAnimationType.Position) != 0 && _rectTransform != null)
                _rectTransform.anchoredPosition = Vector2.Lerp(
                    _originalAnchoredPosition + enterFromPositionOffset,
                    _originalAnchoredPosition, ct);

            if ((animationType & SimpleMenuAnimationType.Scale) != 0)
                transform.localScale = Vector3.Lerp(enterFromScale, _originalLocalScale, ct);
        }

        private void ApplyExitProgress(float t)
        {
            float ct = enterCurve.Evaluate(t);

            if ((animationType & SimpleMenuAnimationType.Alpha) != 0 && _canvasGroup != null)
                _canvasGroup.alpha = Mathf.Lerp(_originalAlpha, enterFromAlpha, ct);

            if ((animationType & SimpleMenuAnimationType.Position) != 0 && _rectTransform != null)
                _rectTransform.anchoredPosition = Vector2.Lerp(
                    _originalAnchoredPosition,
                    _originalAnchoredPosition + enterFromPositionOffset, ct);

            if ((animationType & SimpleMenuAnimationType.Scale) != 0)
                transform.localScale = Vector3.Lerp(_originalLocalScale, enterFromScale, ct);
        }

        // ─── Container public API ─────────────────────────────────────────────

        /// <summary>Plays the container enter animation.</summary>
        public Task Enter()
        {
            if (animateElements || animationType == SimpleMenuAnimationType.None) return Task.CompletedTask;
            return RunContainerSwitcheroo(_enterSwitcheroo, enterDuration);
        }

        /// <summary>Plays the container exit animation.</summary>
        public Task Exit()
        {
            if (animateElements || animationType == SimpleMenuAnimationType.None) return Task.CompletedTask;
            float duration = enterDuration / Mathf.Max(exitSpeedMultiplier, 0.001f);
            _exitSwitcheroo.SetProgress(0f);
            return RunContainerSwitcheroo(_exitSwitcheroo, duration);
        }

        // ─── Element public API ───────────────────────────────────────────────

        /// <summary>Stagger-animates the direct children of container in.</summary>
        public Task EnterElements(Transform container)
        {
            if (!animateElements) return Task.CompletedTask;
            return RunElementAnimations(container, isEnter: true);
        }

        /// <summary>Stagger-animates the direct children of container out.</summary>
        public Task ExitElements(Transform container)
        {
            if (!animateElements) return Task.CompletedTask;
            return RunElementAnimations(container, isEnter: false);
        }

        // ─── Element orchestration ────────────────────────────────────────────

        private async Task RunElementAnimations(Transform container, bool isEnter)
        {
            _elementCts?.Cancel();
            _elementCts?.Dispose();
            _elementCts = new CancellationTokenSource();
            CancellationToken ct = _elementCts.Token;

            // Primary config mirrors the container transition settings.
            SimpleMenuElementAnimConfig primaryCfg = new SimpleMenuElementAnimConfig
            {
                animationType       = animationType,
                fromAlpha           = enterFromAlpha,
                fromPositionOffset  = enterFromPositionOffset,
                fromScale           = enterFromScale,
                duration            = enterDuration,
                curve               = enterCurve,
                exitSpeedMultiplier = exitSpeedMultiplier,
            };

            List<Transform> elements = GetOrderedElements(container);
            if (elements.Count == 0) return;

            // ── 1. Force layout to compute resting positions before capturing. ──
            RectTransform containerRect = container.GetComponent<RectTransform>();
            if (containerRect != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);

            // ── 2. Capture each element's layout-driven resting position. ──
            var restingPositions = new Dictionary<Transform, Vector2>(elements.Count);
            foreach (Transform elem in elements)
            {
                RectTransform rt = elem.GetComponent<RectTransform>();
                if (rt != null)
                    restingPositions[elem] = rt.anchoredPosition;
            }

            // ── 3. Disable layout so it doesn't fight with position animations. ──
            LayoutGroup layoutGroup = container.GetComponent<LayoutGroup>();
            bool layoutWasEnabled = layoutGroup != null && layoutGroup.enabled;
            if (layoutWasEnabled)
                layoutGroup.enabled = false;

            // ── 4. Ensure CanvasGroups exist on elements that need alpha. ──
            for (int i = 0; i < elements.Count; i++)
            {
                bool alt = useAlternateElementConfig && i % 2 == 1;
                SimpleMenuElementAnimConfig cfg = alt ? altElementConfig : primaryCfg;
                if ((cfg.animationType & SimpleMenuAnimationType.Alpha) != 0)
                {
                    if (elements[i].GetComponent<CanvasGroup>() == null)
                        elements[i].gameObject.AddComponent<CanvasGroup>();
                }
            }

            // ── 5. Start staggered animations. ──
            var tasks = new List<Task>(elements.Count);
            for (int i = 0; i < elements.Count; i++)
            {
                bool alt = useAlternateElementConfig && i % 2 == 1;
                SimpleMenuElementAnimConfig cfg = alt ? altElementConfig : primaryCfg;
                float dur = isEnter
                    ? cfg.duration
                    : cfg.duration / Mathf.Max(cfg.exitSpeedMultiplier, 0.001f);
                float delay = i * dur * elementStagger;

                restingPositions.TryGetValue(elements[i], out Vector2 restPos);

                tasks.Add(isEnter
                    ? AnimateElementEnter(elements[i], cfg, delay, restPos, ct)
                    : AnimateElementExit(elements[i], cfg, delay, restPos, ct));
            }

            await Task.WhenAll(tasks);

            // ── 6. Re-enable layout after animations finish. ──
            if (layoutWasEnabled && layoutGroup != null)
                layoutGroup.enabled = true;
        }

        private async Task AnimateElementEnter(
            Transform element, SimpleMenuElementAnimConfig cfg,
            float delay, Vector2 restingPos, CancellationToken ct)
        {
            RectTransform rt = element.GetComponent<RectTransform>();
            CanvasGroup cg   = element.GetComponent<CanvasGroup>();

            // Resting state: where the element should end up.
            float   origAlpha = 1f;
            Vector2 origPos   = restingPos;
            Vector3 origScale = Vector3.one;

            // Snap to from-state immediately (synchronous, before any delay).
            ApplyElementState(rt, cg, cfg, 0f, origAlpha, origPos, origScale, isEnter: true);

            if (delay > 0f)
            {
                await Task.Delay(TimeSpan.FromSeconds(delay));
                if (ct.IsCancellationRequested) return;
            }

            if (ct.IsCancellationRequested || !element.gameObject.activeInHierarchy) return;

            await RunElementSwitcheroo(
                t => ApplyElementState(rt, cg, cfg, t, origAlpha, origPos, origScale, isEnter: true),
                cfg.duration, ct);
        }

        private async Task AnimateElementExit(
            Transform element, SimpleMenuElementAnimConfig cfg,
            float delay, Vector2 restingPos, CancellationToken ct)
        {
            RectTransform rt = element.GetComponent<RectTransform>();
            CanvasGroup cg   = element.GetComponent<CanvasGroup>();

            // Resting state: where the element starts the exit from.
            float   origAlpha = 1f;
            Vector2 origPos   = restingPos;
            Vector3 origScale = Vector3.one;

            if (delay > 0f)
            {
                await Task.Delay(TimeSpan.FromSeconds(delay));
                if (ct.IsCancellationRequested) return;
            }

            if (ct.IsCancellationRequested || !element.gameObject.activeInHierarchy) return;

            float dur = cfg.duration / Mathf.Max(cfg.exitSpeedMultiplier, 0.001f);
            await RunElementSwitcheroo(
                t => ApplyElementState(rt, cg, cfg, t, origAlpha, origPos, origScale, isEnter: false),
                dur, ct);
        }

        private static void ApplyElementState(
            RectTransform rt, CanvasGroup cg,
            SimpleMenuElementAnimConfig cfg, float t,
            float origAlpha, Vector2 origPos, Vector3 origScale,
            bool isEnter)
        {
            float ct = cfg.curve != null ? cfg.curve.Evaluate(t) : t;

            if ((cfg.animationType & SimpleMenuAnimationType.Alpha) != 0 && cg != null)
                cg.alpha = isEnter
                    ? Mathf.Lerp(cfg.fromAlpha, origAlpha, ct)
                    : Mathf.Lerp(origAlpha, cfg.fromAlpha, ct);

            if ((cfg.animationType & SimpleMenuAnimationType.Position) != 0 && rt != null)
                rt.anchoredPosition = isEnter
                    ? Vector2.Lerp(origPos + cfg.fromPositionOffset, origPos, ct)
                    : Vector2.Lerp(origPos, origPos + cfg.fromPositionOffset, ct);

            if ((cfg.animationType & SimpleMenuAnimationType.Scale) != 0)
            {
                Transform t2 = rt != null ? rt.transform : (cg != null ? cg.transform : null);
                if (t2 != null)
                    t2.localScale = isEnter
                        ? Vector3.Lerp(cfg.fromScale, origScale, ct)
                        : Vector3.Lerp(origScale, cfg.fromScale, ct);
            }
        }

        /// <summary>
        /// Restores all direct children to their natural resting state and re-enables
        /// the layout group so positions are correct on the next show cycle.
        /// </summary>
        private void RestoreElements()
        {
            LayoutGroup lg = GetComponent<LayoutGroup>();
            if (lg != null && !lg.enabled)
                lg.enabled = true;

            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);

                CanvasGroup cg = child.GetComponent<CanvasGroup>();
                if (cg != null) cg.alpha = 1f;

                child.localScale = Vector3.one;
            }
        }

        private static async Task RunElementSwitcheroo(Action<float> apply, float duration, CancellationToken ct)
        {
            var sw = new Switcheroo.Switcheroo();
            sw.AddTransition("t", t =>
            {
                if (ct.IsCancellationRequested) { sw.Dispose(); return; }
                apply(t);
            });

            if (duration <= 0f)
            {
                apply(1f);
                sw.Dispose();
                return;
            }

            sw.transitionSpeed = duration;
            var tcs = new TaskCompletionSource<bool>();
            Action whenOn = null;
            whenOn = () => { sw.WhenOn -= whenOn; sw.Dispose(); tcs.TrySetResult(true); };
            sw.WhenOn += whenOn;
            sw.SwitchToOn();

            await tcs.Task;
        }

        // ─── Ordering ─────────────────────────────────────────────────────────

        private List<Transform> GetOrderedElements(Transform container)
        {
            var children = new List<Transform>(container.childCount);
            for (int i = 0; i < container.childCount; i++)
                children.Add(container.GetChild(i));

            if (elementDirection == ElementAnimationDirection.BottomTop)
                children.Reverse();

            switch (elementPickOrder)
            {
                case ElementPickOrder.EveryOther:
                {
                    var reordered = new List<Transform>(children.Count);
                    for (int i = 0; i < children.Count; i += 2) reordered.Add(children[i]);
                    for (int i = 1; i < children.Count; i += 2) reordered.Add(children[i]);
                    children = reordered;
                    break;
                }
                case ElementPickOrder.Ripple:
                {
                    var reordered = new List<Transform>(children.Count);
                    int lo = 0, hi = children.Count - 1;
                    while (lo <= hi)
                    {
                        reordered.Add(children[lo++]);
                        if (lo <= hi) reordered.Add(children[hi--]);
                    }
                    children = reordered;
                    break;
                }
            }

            return children;
        }

        // ─── Configuration ────────────────────────────────────────────────────

        /// <summary>Configures the container-level transition.</summary>
        public void Configure(
            SimpleMenuAnimationType type, float fromAlpha, Vector2 fromPositionOffset,
            Vector3 fromScale, float duration, AnimationCurve curve, float exitMultiplier)
        {
            animationType           = type;
            enterFromAlpha          = fromAlpha;
            enterFromPositionOffset = fromPositionOffset;
            enterFromScale          = fromScale;
            enterDuration           = duration;
            enterCurve              = curve;
            exitSpeedMultiplier     = exitMultiplier;
        }

        /// <summary>Configures per-element stagger animation.</summary>
        public void ConfigureElements(
            bool animEach, ElementAnimationDirection direction, ElementPickOrder pickOrder,
            float stagger, bool useAlt, SimpleMenuElementAnimConfig alt)
        {
            animateElements           = animEach;
            elementDirection          = direction;
            elementPickOrder          = pickOrder;
            elementStagger            = stagger;
            useAlternateElementConfig = useAlt;
            altElementConfig          = alt;
        }

        // ─── Container Switcheroo runner ──────────────────────────────────────

        private async Task RunContainerSwitcheroo(Switcheroo.Switcheroo switcheroo, float duration)
        {
            switcheroo.SetProgress(0f);

            if (duration <= 0f)
            {
                switcheroo.SetProgress(1f);
                return;
            }

            switcheroo.transitionSpeed = duration;
            var tcs = new TaskCompletionSource<bool>();
            Action handler = null;
            handler = () => { switcheroo.WhenOn -= handler; tcs.TrySetResult(true); };
            switcheroo.WhenOn += handler;
            switcheroo.SwitchToOn();

            await tcs.Task;

            // Guarantee the final state is applied regardless of when WhenOn fires.
            switcheroo.SetProgress(1f);
        }
    }
}
