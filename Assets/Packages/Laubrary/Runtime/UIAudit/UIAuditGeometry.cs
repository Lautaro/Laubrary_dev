using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.UIAudit
{
    // Shared pairwise geometry checks (Overlap / Crowded) used by both the uGUI and IMGUI sections, so the
    // heuristic is defined exactly once instead of drifting between the two.
    //
    // Scoped to INTERACTIVE elements only, deliberately: a panel background legitimately sits behind its
    // own content (that's not a bug, it's how every panel is drawn), and dense labels butting together is
    // normal layout density. It's two INDEPENDENTLY-clickable controls overlapping, or crammed edge-to-edge
    // with no room to tell them apart, that's actually a problem for a player/user.
    public static class UIAuditGeometry
    {
        public struct Item
        {
            public string Path;
            public Rect Rect;
            public bool AllowOverlap; // deliberate opt-out (e.g. a transparent full-area catcher behind real buttons)
        }

        public static void CheckProximity(List<Item> items, string sectionName, UIAuditContext ctx, List<UIIssue> issues)
        {
            for (int i = 0; i < items.Count; i++)
            {
                for (int j = i + 1; j < items.Count; j++)
                {
                    var a = items[i];
                    var b = items[j];
                    if (a.AllowOverlap || b.AllowOverlap) continue;

                    if (a.Rect.Overlaps(b.Rect))
                    {
                        issues.Add(new UIIssue
                        {
                            Kind = UIIssueKind.Overlap,
                            Section = sectionName,
                            Path = $"{a.Path} ↔ {b.Path}",
                            Detail = $"interactive rects overlap: {Fmt(a.Rect)} ↔ {Fmt(b.Rect)}",
                        });
                        continue; // already flagged as overlapping — don't also report it as "crowded"
                    }

                    // Two rects are "adjacent" (worth a gap check) only if they line up in a row (Y ranges
                    // intersect) or a column (X ranges intersect) — diagonal neighbors aren't a meaningful pair.
                    bool sameRow = a.Rect.yMin < b.Rect.yMax && b.Rect.yMin < a.Rect.yMax;
                    bool sameCol = a.Rect.xMin < b.Rect.xMax && b.Rect.xMin < a.Rect.xMax;
                    if (!sameRow && !sameCol) continue;

                    float gap = float.PositiveInfinity;
                    if (sameRow) gap = Mathf.Min(gap, Mathf.Max(a.Rect.xMin - b.Rect.xMax, b.Rect.xMin - a.Rect.xMax));
                    if (sameCol) gap = Mathf.Min(gap, Mathf.Max(a.Rect.yMin - b.Rect.yMax, b.Rect.yMin - a.Rect.yMax));

                    if (gap >= 0f && gap < ctx.MinControlGap)
                    {
                        issues.Add(new UIIssue
                        {
                            Kind = UIIssueKind.Crowded,
                            Section = sectionName,
                            Path = $"{a.Path} ↔ {b.Path}",
                            Detail = $"{gap:0.#}px gap < {ctx.MinControlGap:0}px minimum",
                        });
                    }
                }
            }
        }

        static string Fmt(Rect r) => $"({r.xMin:0},{r.yMin:0})→({r.xMax:0},{r.yMax:0})";
    }
}
