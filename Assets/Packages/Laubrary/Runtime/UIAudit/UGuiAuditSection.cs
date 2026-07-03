using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Laubrary.UIAudit
{
    // The uGUI (Canvas / RectTransform / TMP) audit section. Self-contained so other UI systems (IMGUI,
    // UI Toolkit) can be added as sibling IUIAuditSection implementations without touching this or the core.
    public class UGuiAuditSection : IUIAuditSection
    {
        public string Name => "uGUI";

        public void Audit(UIAuditContext ctx, List<UIIssue> issues)
        {
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (canvas == null || !canvas.isRootCanvas) continue;
                if (!ctx.IncludeInactive && !canvas.gameObject.activeInHierarchy) continue;
                AuditCanvas(canvas, ctx, issues);
            }
        }

        void AuditCanvas(Canvas canvas, UIAuditContext ctx, List<UIIssue> issues)
        {
            var cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            float sw = ctx.ScreenWidth, sh = ctx.ScreenHeight, tol = ctx.Tolerance;

            // ── Off-screen interactables (elements inside a ScrollRect are reachable by scrolling → skipped).
            foreach (var s in canvas.GetComponentsInChildren<Selectable>(ctx.IncludeInactive))
            {
                if (s == null || (!ctx.IncludeInactive && !s.isActiveAndEnabled)) continue;
                if (!(s.transform is RectTransform rt)) continue;
                if (HasScrollAncestor(rt)) continue;
                var r = ScreenRect(rt, cam);
                bool outside = r.xMax < tol || r.yMax < tol || r.xMin > sw - tol || r.yMin > sh - tol
                            || r.xMin < -tol || r.yMin < -tol || r.xMax > sw + tol || r.yMax > sh + tol;
                if (outside)
                {
                    bool fully = r.xMax < 0 || r.yMax < 0 || r.xMin > sw || r.yMin > sh;
                    Add(issues, UIIssueKind.OffScreen, rt, $"{(fully ? "fully" : "partly")} off-screen: rect {Fmt(r)} vs {sw:0}x{sh:0}");
                }
            }

            // ── TMP text: tiny font + overflow/clip.
            foreach (var t in canvas.GetComponentsInChildren<TMP_Text>(ctx.IncludeInactive))
            {
                if (t == null || (!ctx.IncludeInactive && !t.isActiveAndEnabled) || string.IsNullOrEmpty(t.text)) continue;
                var rt = t.rectTransform;
                t.ForceMeshUpdate();

                if (t.fontSize > 0f && t.fontSize < ctx.TinyTextPx)
                    Add(issues, UIIssueKind.TinyText, rt, $"font {t.fontSize:0.#}px < {ctx.TinyTextPx:0}px: \"{Short(t.text)}\"");

                if (t.overflowMode != TextOverflowModes.Overflow)
                {
                    // preferredWidth is measured as ONE unwrapped line, so it only signals real
                    // overflow when wrapping is off — wrapped text overflows vertically instead.
                    bool noWrap = t.textWrappingMode == TextWrappingModes.NoWrap
                               || t.textWrappingMode == TextWrappingModes.PreserveWhitespaceNoWrap;
                    if (t.preferredHeight > rt.rect.height + tol)
                        Add(issues, UIIssueKind.TextOverflow, rt, $"vertical clip: needs {t.preferredHeight:0}px, has {rt.rect.height:0}px: \"{Short(t.text)}\"");
                    else if (noWrap && t.preferredWidth > rt.rect.width + tol)
                        Add(issues, UIIssueKind.TextOverflow, rt, $"horizontal overflow: needs {t.preferredWidth:0}px, has {rt.rect.width:0}px: \"{Short(t.text)}\"");
                }
            }

            // ── Legacy UnityEngine.UI.Text: tiny font + truncation.
            foreach (var t in canvas.GetComponentsInChildren<Text>(ctx.IncludeInactive))
            {
                if (t == null || (!ctx.IncludeInactive && !t.isActiveAndEnabled) || string.IsNullOrEmpty(t.text)) continue;
                var rt = t.rectTransform;
                if (t.fontSize > 0 && t.fontSize < ctx.TinyTextPx)
                    Add(issues, UIIssueKind.TinyText, rt, $"font {t.fontSize}px < {ctx.TinyTextPx:0}px");
                if (t.verticalOverflow == VerticalWrapMode.Truncate && t.preferredHeight > rt.rect.height + tol)
                    Add(issues, UIIssueKind.TextOverflow, rt, $"truncated: needs {t.preferredHeight:0}px, has {rt.rect.height:0}px");
            }

            // ── Layout containers whose content exceeds their rect with no ScrollRect to scroll it.
            foreach (var lg in canvas.GetComponentsInChildren<LayoutGroup>(ctx.IncludeInactive))
            {
                if (lg == null || (!ctx.IncludeInactive && !lg.gameObject.activeInHierarchy)) continue;
                if (!(lg.transform is RectTransform rt)) continue;
                if (HasScrollAncestor(rt)) continue;
                if (lg is VerticalLayoutGroup)
                {
                    float pref = LayoutUtility.GetPreferredHeight(rt);
                    if (pref > rt.rect.height + tol)
                        Add(issues, UIIssueKind.NeedsScrollView, rt, $"content {pref:0}px > container {rt.rect.height:0}px — add a ScrollRect");
                }
                else if (lg is HorizontalLayoutGroup)
                {
                    float pref = LayoutUtility.GetPreferredWidth(rt);
                    if (pref > rt.rect.width + tol)
                        Add(issues, UIIssueKind.NeedsScrollView, rt, $"content {pref:0}px > container {rt.rect.width:0}px — add a ScrollRect");
                }
            }
        }

        // ── helpers ────────────────────────────────────────────────────────────────────────────────────
        void Add(List<UIIssue> issues, UIIssueKind kind, Transform t, string detail)
            => issues.Add(new UIIssue { Kind = kind, Section = Name, Path = Path(t), Detail = detail });

        static Rect ScreenRect(RectTransform rt, Camera cam)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c); // 0=BL, 1=TL, 2=TR, 3=BR
            Vector2 bl = c[0], tr = c[2];
            if (cam != null)
            {
                bl = RectTransformUtility.WorldToScreenPoint(cam, c[0]);
                tr = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
            }
            return Rect.MinMaxRect(Mathf.Min(bl.x, tr.x), Mathf.Min(bl.y, tr.y), Mathf.Max(bl.x, tr.x), Mathf.Max(bl.y, tr.y));
        }

        static bool HasScrollAncestor(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
                if (p.GetComponent<ScrollRect>() != null) return true;
            return false;
        }

        static string Path(Transform t)
        {
            var stack = new List<string>();
            for (var p = t; p != null; p = p.parent) stack.Add(p.name);
            stack.Reverse();
            return string.Join("/", stack);
        }

        static string Short(string s) => s == null ? "" : (s.Length <= 40 ? s : s.Substring(0, 40) + "…").Replace("\n", " ");
        static string Fmt(Rect r) => $"({r.xMin:0},{r.yMin:0})→({r.xMax:0},{r.yMax:0})";
    }
}
