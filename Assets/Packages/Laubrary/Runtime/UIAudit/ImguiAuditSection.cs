using System.Collections.Generic;
using UnityEngine;
using ZuiRuntime;

namespace Laubrary.UIAudit
{
    // The IMGUI audit section. Immediate mode has no retained element tree to walk, so instead ZuiRuntime
    // records what it draws (ZuiAudit) and this section lints that record. It therefore covers UI drawn
    // THROUGH ZuiRuntime helpers (panels, stacks, menus) — the whole point of routing prototype UI through
    // them. Raw GUI.Label/GUI.Button calls a project makes directly are not recorded and not covered.
    //
    // Requires ZuiAudit.Recording to have been on for a frame before UIAudit.Run() — use
    // UIAudit.BeginImguiCapture(), let a frame render, then Run(). If nothing was recorded this is a no-op.
    public class ImguiAuditSection : IUIAuditSection
    {
        public string Name => "IMGUI";

        public void Audit(UIAuditContext ctx, List<UIIssue> issues)
        {
            var draws = ZuiAudit.LastFrame;
            if (draws == null || draws.Count == 0) return;
            float sw = ctx.ScreenWidth, sh = ctx.ScreenHeight, tol = ctx.Tolerance;

            foreach (var d in draws)
            {
                // ── Tiny text: FontPx is already scaled, so compare straight to the legibility threshold.
                if (d.FontPx > 0 && d.FontPx < ctx.TinyTextPx)
                    Add(issues, UIIssueKind.TinyText, d, $"font {d.FontPx}px < {ctx.TinyTextPx:0}px: \"{Short(d.Text)}\"");

                // ── Text overflow: measured need vs the rect it was drawn into. Auto-sized helpers make
                //    these equal (proving they're trap-proof); LabelIn / narrow buttons can trip it.
                if (!string.IsNullOrEmpty(d.Text))
                {
                    if (d.NeededHeight > d.Rect.height + tol)
                        Add(issues, UIIssueKind.TextOverflow, d, $"vertical clip: needs {d.NeededHeight:0}px, has {d.Rect.height:0}px: \"{Short(d.Text)}\"");
                    else if (!d.Wrap && d.NeededWidth > d.Rect.width + tol)
                        Add(issues, UIIssueKind.TextOverflow, d, $"horizontal overflow: needs {d.NeededWidth:0}px, has {d.Rect.width:0}px: \"{Short(d.Text)}\"");
                }

                // ── Off-screen: any drawn element whose rect leaves the screen — a HUD player can't read
                //    or reach it. Scroll-view content is skipped (Clipped) since scrolling reveals it.
                if (!d.Clipped && d.Rect.width > 0f && d.Rect.height > 0f)
                {
                    var r = d.Rect;
                    bool outside = r.xMax < tol || r.yMax < tol || r.xMin > sw - tol || r.yMin > sh - tol
                                || r.xMin < -tol || r.yMin < -tol || r.xMax > sw + tol || r.yMax > sh + tol;
                    if (outside)
                    {
                        bool fully = r.xMax < 0 || r.yMax < 0 || r.xMin > sw || r.yMin > sh;
                        Add(issues, UIIssueKind.OffScreen, d, $"{(fully ? "fully" : "partly")} off-screen: rect {Fmt(r)} vs {sw:0}x{sh:0}");
                    }
                }
            }
        }

        void Add(List<UIIssue> issues, UIIssueKind kind, ZuiDrawRecord d, string detail)
            => issues.Add(new UIIssue { Kind = kind, Section = Name, Path = PathOf(d), Detail = detail });

        static string PathOf(ZuiDrawRecord d)
            => string.IsNullOrEmpty(d.Text) ? d.Kind : $"{d.Kind}:\"{Short(d.Text)}\"";

        static string Short(string s) => s == null ? "" : (s.Length <= 40 ? s : s.Substring(0, 40) + "…").Replace("\n", " ");
        static string Fmt(Rect r) => $"({r.xMin:0},{r.yMin:0})→({r.xMax:0},{r.yMax:0})";
    }
}
