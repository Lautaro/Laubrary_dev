using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Laubrary.UIAudit
{
    // A vision-free UI linter — it reports layout problems from the UI's own metrics (rects, text sizes) so an
    // agent (or a test) can catch them WITHOUT a screenshot. See the ClaudeUI UI-Testing-Philosophy doc for why.
    //
    // The core is UI-system-agnostic: it aggregates "sections". Each section inspects one UI system —
    // uGUI (Canvas/RectTransform) and IMGUI (ZuiRuntime-drawn OnGUI) ship today; UI Toolkit could be added
    // as another sibling section. UIAudit.Register(section) adds coverage.

    public enum UIIssueKind { OffScreen, TextOverflow, TinyText, NeedsScrollView, Overlap, Crowded, OverWidth }

    public class UIIssue
    {
        public UIIssueKind Kind;
        public string Section;   // which UI system reported it ("uGUI", "IMGUI", …)
        public string Path;      // hierarchy path (uGUI) or kind+text (IMGUI) of the offending element
        public string Detail;    // human-readable specifics (measured numbers)
        public override string ToString() => $"[{Kind}] ({Section}) {Path} — {Detail}";
    }

    // Shared knobs + the current screen size, handed to every section.
    public class UIAuditContext
    {
        public bool IncludeInactive;
        public float TinyTextPx = 12f;    // effective font size below this is flagged
        public float Tolerance = 2f;      // px slack before flagging overflow / off-screen
        public float MinControlGap = 2f;  // px gap below this between two adjacent interactive controls is "Crowded"
        public float ScreenWidth;
        public float ScreenHeight;
    }

    // One inspectable UI system. Implement + Register to extend coverage.
    public interface IUIAuditSection
    {
        string Name { get; }
        void Audit(UIAuditContext ctx, List<UIIssue> issues);
    }

    public static class UIAudit
    {
        // uGUI + IMGUI are registered by default; add more sections (e.g. UI Toolkit) via Register.
        static readonly List<IUIAuditSection> _sections = new List<IUIAuditSection>
        {
            new UGuiAuditSection(),
            new ImguiAuditSection(),
        };
        public static IReadOnlyList<IUIAuditSection> Sections => _sections;

        public static void Register(IUIAuditSection section)
        {
            if (section == null) return;
            if (!_sections.Exists(s => s.GetType() == section.GetType())) _sections.Add(section);
        }

        // NOTE: run this in Play mode (runtime menus build then), and at your TARGET resolution — issues are
        // resolution-dependent, so a maximized 4K editor view hides overflow that bites on a 1280x800 handheld.
        // For the IMGUI section, ZuiRuntime.ZuiAudit.Recording must have been on for a frame before this call
        // (BeginImguiCapture handles that for a scripted run).
        public static List<UIIssue> Run(bool includeInactive = false, float tinyTextPx = 12f)
        {
            var ctx = new UIAuditContext
            {
                IncludeInactive = includeInactive,
                TinyTextPx = tinyTextPx,
                ScreenWidth = Screen.width,
                ScreenHeight = Screen.height,
            };
            var issues = new List<UIIssue>();
            foreach (var s in _sections)
            {
                try { s.Audit(ctx, issues); }
                catch (Exception e) { Debug.LogWarning($"[UIAudit] section '{s.Name}' threw: {e.Message}"); }
            }
            return issues;
        }

        // Turn on IMGUI draw recording. Call this, let at least one frame render, then Run()/Report().
        public static void BeginImguiCapture()
        {
            ZuiRuntime.ZuiAudit.Reset();
            ZuiRuntime.ZuiAudit.Recording = true;
        }

        public static void EndImguiCapture() => ZuiRuntime.ZuiAudit.Recording = false;

        public static string Report(bool includeInactive = false)
        {
            var issues = Run(includeInactive);
            var sb = new StringBuilder();
            sb.AppendLine($"UIAudit @ {Screen.width}x{Screen.height} — sections: {string.Join(", ", SectionNames())}");
            if (issues.Count == 0) { sb.Append("No issues found."); return sb.ToString(); }
            sb.AppendLine($"{issues.Count} issue(s):");
            foreach (UIIssueKind k in Enum.GetValues(typeof(UIIssueKind)))
            {
                var of = issues.FindAll(i => i.Kind == k);
                if (of.Count == 0) continue;
                sb.AppendLine($"── {k} ({of.Count}) ──");
                foreach (var i in of) sb.AppendLine($"  ({i.Section}) {i.Path} — {i.Detail}");
            }
            return sb.ToString();
        }

        static IEnumerable<string> SectionNames()
        {
            foreach (var s in _sections) yield return s.Name;
        }
    }
}
