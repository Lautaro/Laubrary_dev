using System.Collections.Generic;
using UnityEngine;
using ZuiRuntime;

namespace Laubrary.UIAudit
{
    // Lints ZUI.Editor tool-window draws (EditorZuiAudit's buffer) — the edit-mode sibling of
    // ImguiAuditSection, which only sees runtime OnGUI HUDs drawn through ZuiRuntime.Zui. Not an
    // IUIAuditSection registered in UIAudit's default Sections list: this needs a per-window screen
    // size (the EditorWindow's own rect), not Screen.width/height, so it's run directly via
    // EditorUIAuditMenu instead of through UIAudit.Run().
    //
    // Scope note: unlike the runtime sections, this does NOT check vertical off-screen — an
    // EditorWindow routinely has content below the fold inside a legitimate ScrollView, and without
    // a per-control "clipped" flag (ZuiDrawRecord.Clipped is never set by the ZUI.Editor recorders
    // added for this section) that would just be constant false-positive noise. Horizontal overflow
    // has no such legitimate case here — a vertical ScrollView doesn't clip or offset a control's
    // X-rect — so a control whose Rect.xMax exceeds the window width is a real problem either way:
    // content pushed off the visible area, or (inside a horizontal-scrolling container) exactly the
    // "control sized against the ScrollView's virtual width, not the physically visible window" bug
    // documented in EDITOR_TOOL_CONVENTIONS.md. That doubles as the mechanical proxy for "would need
    // a horizontal scrollbar" — no separate check for that smell is needed.
    public class EditorWindowAuditSection
    {
        public const string Name = "ZUI.Editor";

        // Soft width caps by draw Kind, from EDITOR_TOOL_CONVENTIONS.md's "no infinite-width
        // controls" targets (~150px slider, ~130px colour field), roughly doubled here to tolerate a
        // legitimately-packed control (label + track + value field) and only catch genuine overflow —
        // a forced min-width bleeding through, or an unconstrained ExpandWidth. Kinds with no entry
        // (button, label, ...) aren't width-capped; they're not the pattern this catches.
        static readonly Dictionary<string, float> s_WidthCaps = new Dictionary<string, float>
        {
            { "slider", 260f },
            { "microslider", 220f },
            { "toggle", 220f },
        };

        public void Audit(IReadOnlyList<ZuiDrawRecord> draws, UIAuditContext ctx, List<UIIssue> issues)
        {
            if (draws == null || draws.Count == 0) return;
            float sw = ctx.ScreenWidth, tol = ctx.Tolerance;
            var proximityItems = new List<UIAuditGeometry.Item>();

            foreach (var d in draws)
            {
                if (d.FontPx > 0 && d.FontPx < ctx.TinyTextPx)
                    Add(issues, UIIssueKind.TinyText, d, $"font {d.FontPx}px < {ctx.TinyTextPx:0}px: \"{Short(d.Text)}\"");

                if (!string.IsNullOrEmpty(d.Text))
                {
                    if (d.NeededHeight > d.Rect.height + tol)
                        Add(issues, UIIssueKind.TextOverflow, d, $"vertical clip: needs {d.NeededHeight:0}px, has {d.Rect.height:0}px: \"{Short(d.Text)}\"");
                    else if (!d.Wrap && d.NeededWidth > d.Rect.width + tol)
                        Add(issues, UIIssueKind.TextOverflow, d, $"horizontal overflow: needs {d.NeededWidth:0}px, has {d.Rect.width:0}px: \"{Short(d.Text)}\"");
                }

                // Horizontal-only off-screen — see class comment for why vertical is excluded here.
                if (d.Rect.width > 0f && d.Rect.height > 0f && (d.Rect.xMax > sw + tol || d.Rect.xMin < -tol))
                {
                    Add(issues, UIIssueKind.OffScreen, d,
                        $"extends past the window's width ({Fmt(d.Rect)} vs {sw:0}px wide) — off-screen, or would force a horizontal scrollbar");
                }

                if (s_WidthCaps.TryGetValue(d.Kind, out float cap) && d.Rect.width > cap)
                {
                    Add(issues, UIIssueKind.OverWidth, d,
                        $"{d.Kind} rendered at {d.Rect.width:0}px, over the ~{cap:0}px typical cap — check for a forced min-width or unconstrained ExpandWidth (see the laubrary skill's ui-layout-rules.md)");
                }

                if (d.Interactive)
                    proximityItems.Add(new UIAuditGeometry.Item { Path = PathOf(d), Rect = d.Rect, AllowOverlap = d.AllowOverlap });
            }

            UIAuditGeometry.CheckProximity(proximityItems, Name, ctx, issues);
        }

        void Add(List<UIIssue> issues, UIIssueKind kind, ZuiDrawRecord d, string detail)
            => issues.Add(new UIIssue { Kind = kind, Section = Name, Path = PathOf(d), Detail = detail });

        static string PathOf(ZuiDrawRecord d) => string.IsNullOrEmpty(d.Text) ? d.Kind : $"{d.Kind}:\"{Short(d.Text)}\"";
        static string Short(string s) => s == null ? "" : (s.Length <= 40 ? s : s.Substring(0, 40) + "…").Replace("\n", " ");
        static string Fmt(Rect r) => $"({r.xMin:0},{r.yMin:0})→({r.xMax:0},{r.yMax:0})";
    }
}
