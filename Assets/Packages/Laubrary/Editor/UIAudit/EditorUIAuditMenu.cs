using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Laubrary.UIAudit
{
    // Programmatic entry point for the editor-window audit — shared by the menu items below and by
    // any script/tool call that wants a report without going through EditorWindow.focusedWindow
    // (e.g. auditing a specific window instance right after creating it).
    public static class EditorUIAudit
    {
        /// <summary>Lints whatever EditorZuiAudit recorded, sized against the given window's own rect.
        /// Returns an empty list if nothing was recorded yet (recording off, or the window hasn't
        /// repainted since Recording was turned on).</summary>
        public static List<UIIssue> Run(EditorWindow window)
        {
            var ctx = new UIAuditContext { ScreenWidth = window.position.width, ScreenHeight = window.position.height };
            var issues = new List<UIIssue>();
            new EditorWindowAuditSection().Audit(EditorZuiAudit.LastFrame, ctx, issues);
            return issues;
        }

        public static string Report(EditorWindow window)
        {
            var issues = Run(window);
            var sb = new StringBuilder();
            sb.AppendLine($"[UIAudit] '{window.titleContent.text}' @ {window.position.width:0}x{window.position.height:0}");
            if (issues.Count == 0) { sb.Append("No issues found."); return sb.ToString(); }
            sb.AppendLine($"{issues.Count} issue(s):");
            foreach (var i in issues) sb.AppendLine("  " + i);
            return sb.ToString();
        }
    }

    // Editor-window convenience wrappers — the edit-mode sibling of Runtime/UIAudit/UIAuditMenu.cs
    // (which only works in Play mode, against runtime HUDs). Same two-step flow: toggle recording,
    // let the window you want checked repaint (move the mouse over it — ZUIWindow sets
    // wantsMouseMove, so that alone forces a Repaint pass), then run the audit.
    static class EditorUIAuditMenu
    {
        [MenuItem("Laubrary/Toggle Editor UI Recording")]
        static void ToggleRecording()
        {
            EditorZuiAudit.Recording = !EditorZuiAudit.Recording;
            Debug.Log($"[UIAudit] Editor window recording {(EditorZuiAudit.Recording ? "ON — hover/click the ZUI window you want checked, then Audit Focused Editor Window" : "OFF")}");
        }

        [MenuItem("Laubrary/Audit Focused Editor Window")]
        static void AuditFocused()
        {
            var window = EditorWindow.focusedWindow;
            if (window == null) { Debug.LogWarning("[UIAudit] No focused EditorWindow."); return; }
            if (!(window is ZUIWindow))
            {
                Debug.LogWarning($"[UIAudit] '{window.titleContent.text}' isn't a ZUIWindow — only ZUI.Editor-drawn windows are recorded.");
                return;
            }
            if (EditorZuiAudit.LastFrame.Count == 0)
            {
                Debug.LogWarning("[UIAudit] No draws recorded yet. Turn on 'Toggle Editor UI Recording', then move the mouse over this window to force a repaint, then run this again.");
                return;
            }
            Debug.Log(EditorUIAudit.Report(window));
        }
    }
}
