using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Laubrary.Switcheroo;

namespace Laubrary.Overture
{
    [Flags]
    public enum VisualDirection
    {
        /// <summary>Plays Enter animation on normal state enter.</summary>
        Enter        = 1 << 0,
        /// <summary>Plays Exit animation on normal state exit.</summary>
        Exit         = 1 << 1,
        /// <summary>Plays Exit animation when the superState transitions into a substate.</summary>
        ToSubstate   = 1 << 2,
        /// <summary>Plays Enter animation when the superState returns from a substate.</summary>
        FromSubstate = 1 << 3,
        /// <summary>Plays on all four occasions: normal enter/exit and substate enter/exit.</summary>
        Always       = Enter | Exit | ToSubstate | FromSubstate,
    }

    [Flags]
    public enum VisualTargetMode
    {
        None     = 0,
        This     = 1 << 0,
        Children = 1 << 1,
        Targets  = 1 << 2,
    }

    [Flags]
    public enum VisualAnimationType
    {
        None     = 0,
        Alpha    = 1 << 0,
        Position = 1 << 1,
        Scale    = 1 << 2,
    }

    /// <summary>
    /// Animates one or more GameObjects during OvertureState enter and exit transitions.
    /// Exit is always the mirror of enter. Backed by Switcheroo.
    /// </summary>
    public class OvertureVisual : MonoBehaviour
    {
        // ─── Targets ─────────────────────────────────────────────────────────
        [SerializeField] private VisualTargetMode targetMode = VisualTargetMode.This;
        [SerializeField] private int childDepth = 1;
        [SerializeField] private List<GameObject> targetList = new List<GameObject>();

        // ─── Direction & types ────────────────────────────────────────────────
        [SerializeField] private VisualDirection direction = VisualDirection.Enter;
        [SerializeField] private VisualAnimationType animationType = VisualAnimationType.Alpha;

        // ─── Enter "from" values ──────────────────────────────────────────────
        [SerializeField, Range(0f, 1f)] private float enterFromAlpha = 0f;
        [SerializeField] private Vector3 enterFromPositionOffset = Vector3.zero;
        [SerializeField] private Vector3 enterFromScale = Vector3.zero;

        // ─── Timing ───────────────────────────────────────────────────────────
        [SerializeField] private float enterDuration = 0.4f;
        [SerializeField] private AnimationCurve enterCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [Tooltip("Scales exit speed relative to enter duration. 2 = twice as fast. Applies to Exit, ToSubstate, and Always.")]
        [SerializeField] private float exitSpeedMultiplier = 1f;

        // ─── Blocking ─────────────────────────────────────────────────────────
        [Tooltip("When unchecked (default), this animation must finish before IsEntered is reached. " +
                 "Check to let the state proceed to IsEntered without waiting for this animation.")]
        [SerializeField] private bool dontBlockOnEnter = false;

        /// <summary>When false (default), this visual's Enter task contributes to the IsEntered gate.</summary>
        public bool DontBlockOnEnter => dontBlockOnEnter;

        // ─── Direction helpers ────────────────────────────────────────────────

        private bool PlaysOnEnter      => (direction & VisualDirection.Enter)        != 0;
        private bool PlaysOnExit       => (direction & VisualDirection.Exit)         != 0;
        private bool PlaysToSubstate   => (direction & VisualDirection.ToSubstate)   != 0;
        private bool PlaysFromSubstate => (direction & VisualDirection.FromSubstate) != 0;

        // ─── Runtime target cache ─────────────────────────────────────────────

        private struct TargetData
        {
            public Transform transform;
            public RectTransform rectTransform;
            public CanvasGroup canvasGroup;
            public Graphic graphic;
            public SpriteRenderer spriteRenderer;
            public bool isUI;
            public float originalAlpha;
            public Vector2 originalAnchoredPosition;
            public Vector3 originalLocalPosition;
            public Vector3 originalLocalScale;
        }

        private readonly List<TargetData> _targets = new List<TargetData>();
        private Switcheroo.Switcheroo _enterSwitcheroo;
        private Switcheroo.Switcheroo _exitSwitcheroo;

        // ─── Lifecycle ────────────────────────────────────────────────────────

        private void Awake()
        {
            CollectTargets();

            _enterSwitcheroo = new Switcheroo.Switcheroo();
            _enterSwitcheroo.AddTransition("enter", ApplyEnterProgress);

            _exitSwitcheroo = new Switcheroo.Switcheroo();
            _exitSwitcheroo.AddTransition("exit", ApplyExitProgress);
        }

        private void OnEnable()
        {
            // Snap to from-values only when this visual plays an Enter animation on state enter.
            // FromSubstate, ToSubstate, and Exit all start at their resting state.
            if (PlaysOnEnter)
                _enterSwitcheroo?.SetProgress(0f);
        }

        private void OnDisable()
        {
            // Restore all targets to their resting values so the next enable starts clean,
            // regardless of where the exit animation left them.
            ApplyEnterProgress(1f);
        }

        private void OnDestroy()
        {
            _enterSwitcheroo?.Dispose();
            _exitSwitcheroo?.Dispose();
        }

        // ─── Target collection ────────────────────────────────────────────────

        private void CollectTargets()
        {
            var seen = new HashSet<GameObject>();
            _targets.Clear();

            if ((targetMode & VisualTargetMode.This) != 0)
                TryAddTarget(gameObject, seen);

            if ((targetMode & VisualTargetMode.Children) != 0)
                CollectChildTargets(transform, childDepth, seen);

            if ((targetMode & VisualTargetMode.Targets) != 0)
                foreach (GameObject go in targetList)
                    if (go != null) TryAddTarget(go, seen);
        }

        private void CollectChildTargets(Transform parent, int depth, HashSet<GameObject> seen)
        {
            if (depth <= 0) return;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                TryAddTarget(child.gameObject, seen);
                CollectChildTargets(child, depth - 1, seen);
            }
        }

        private void TryAddTarget(GameObject go, HashSet<GameObject> seen)
        {
            if (!seen.Add(go)) return;

            var rt  = go.GetComponent<RectTransform>();
            var cg  = go.GetComponent<CanvasGroup>();
            var gfx = go.GetComponent<Graphic>();
            var sr  = go.GetComponent<SpriteRenderer>();

            float alpha = 1f;
            if      (cg  != null) alpha = cg.alpha;
            else if (gfx != null) alpha = gfx.color.a;
            else if (sr  != null) alpha = sr.color.a;

            _targets.Add(new TargetData
            {
                transform                = go.transform,
                rectTransform            = rt,
                canvasGroup              = cg,
                graphic                  = gfx,
                spriteRenderer           = sr,
                isUI                     = rt != null,
                originalAlpha            = alpha,
                originalAnchoredPosition = rt != null ? rt.anchoredPosition : Vector2.zero,
                originalLocalPosition    = go.transform.localPosition,
                originalLocalScale       = go.transform.localScale,
            });
        }

        // ─── Animation callbacks ──────────────────────────────────────────────

        // Enter: t=0 → from-values, t=1 → original/resting values.
        private void ApplyEnterProgress(float t)
        {
            float ct = enterCurve.Evaluate(t);
            foreach (TargetData target in _targets)
            {
                if ((animationType & VisualAnimationType.Alpha) != 0)
                    WriteAlpha(target, Mathf.Lerp(enterFromAlpha, target.originalAlpha, ct));

                if ((animationType & VisualAnimationType.Position) != 0)
                {
                    if (target.isUI && target.rectTransform != null)
                    {
                        Vector2 from = target.originalAnchoredPosition + new Vector2(enterFromPositionOffset.x, enterFromPositionOffset.y);
                        target.rectTransform.anchoredPosition = Vector2.Lerp(from, target.originalAnchoredPosition, ct);
                    }
                    else
                    {
                        target.transform.localPosition = Vector3.Lerp(
                            target.originalLocalPosition + enterFromPositionOffset,
                            target.originalLocalPosition, ct);
                    }
                }

                if ((animationType & VisualAnimationType.Scale) != 0)
                    target.transform.localScale = Vector3.Lerp(enterFromScale, target.originalLocalScale, ct);
            }
        }

        // Exit: always the mirror of enter — t=0 → original/resting values, t=1 → from-values.
        private void ApplyExitProgress(float t)
        {
            float ct = enterCurve.Evaluate(t);
            foreach (TargetData target in _targets)
            {
                if ((animationType & VisualAnimationType.Alpha) != 0)
                    WriteAlpha(target, Mathf.Lerp(target.originalAlpha, enterFromAlpha, ct));

                if ((animationType & VisualAnimationType.Position) != 0)
                {
                    if (target.isUI && target.rectTransform != null)
                    {
                        Vector2 to = target.originalAnchoredPosition + new Vector2(enterFromPositionOffset.x, enterFromPositionOffset.y);
                        target.rectTransform.anchoredPosition = Vector2.Lerp(target.originalAnchoredPosition, to, ct);
                    }
                    else
                    {
                        target.transform.localPosition = Vector3.Lerp(
                            target.originalLocalPosition,
                            target.originalLocalPosition + enterFromPositionOffset, ct);
                    }
                }

                if ((animationType & VisualAnimationType.Scale) != 0)
                    target.transform.localScale = Vector3.Lerp(target.originalLocalScale, enterFromScale, ct);
            }
        }

        private static void WriteAlpha(TargetData target, float alpha)
        {
            if (target.canvasGroup    != null) { target.canvasGroup.alpha = alpha; return; }
            if (target.graphic        != null) { Color c = target.graphic.color; c.a = alpha; target.graphic.color = c; return; }
            if (target.spriteRenderer != null) { Color c = target.spriteRenderer.color; c.a = alpha; target.spriteRenderer.color = c; }
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>Plays Enter animation during normal state enter. No-op if Direction is not Enter or Always.</summary>
        public Task Enter()
        {
            if (!PlaysOnEnter) return Task.CompletedTask;
            return RunSwitcheroo(_enterSwitcheroo, enterDuration);
        }

        /// <summary>Plays Exit animation during normal state exit. No-op if Direction is not Exit or Always.</summary>
        public Task Exit()
        {
            if (!PlaysOnExit) return Task.CompletedTask;
            return RunExitSwitcheroo();
        }

        /// <summary>
        /// Plays Exit animation when the superState transitions into a substate.
        /// No-op if Direction is not ToSubstate or Always.
        /// </summary>
        public Task ExitToSubstate()
        {
            if (!PlaysToSubstate) return Task.CompletedTask;
            return RunExitSwitcheroo();
        }

        /// <summary>
        /// Plays Enter animation when the superState returns from a substate.
        /// No-op if Direction is not FromSubstate or Always.
        /// </summary>
        public Task EnterFromSubstate()
        {
            if (!PlaysFromSubstate) return Task.CompletedTask;
            return RunSwitcheroo(_enterSwitcheroo, enterDuration);
        }

        private Task RunExitSwitcheroo()
        {
            float duration = enterDuration / Mathf.Max(exitSpeedMultiplier, 0.001f);
            _exitSwitcheroo.SetProgress(0f);
            return RunSwitcheroo(_exitSwitcheroo, duration);
        }

        private async Task RunSwitcheroo(Switcheroo.Switcheroo switcheroo, float duration)
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
        }
    }
}
