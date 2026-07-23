// ZUIEditorAudit.cs — records ZUI.Editor (tool-window IMGUI) draws, the edit-mode sibling of
// ZuiRuntime.ZuiAudit (which only sees runtime OnGUI HUDs drawn through ZuiRuntime.Zui). Lives in the
// ZUI.Editor assembly, not ZuiRuntime — this is meaningless outside an EditorWindow and must never be
// a dependency of anything that ships in a player build (see EDITOR_TOOL_CONVENTIONS.md's rule that
// runtime code never depends on editor code).
//
// A Unity EditorWindow's OnGUI has no per-frame counter the way Time.frameCount tracks Play-mode
// frames, so buffer promotion is explicit: ZUIWindow.OnGUI calls EndRepaintPass() once per
// Repaint-type pass instead of detecting a frame boundary implicitly the way ZuiAudit does.
//
// Consumed by Laubrary.UIAudit's editor-only EditorWindowAuditSection (Editor/UIAudit/) — kept a
// separate assembly reference direction (that one references ZUI.Editor, not the other way around)
// so the split matches the runtime/editor boundary exactly.

using System.Collections.Generic;
using UnityEngine;
using ZuiRuntime;

namespace Laubrary.UIAudit
{
    public static class EditorZuiAudit
    {
        /// <summary>When true, ZUI.Editor draws append records. Turn on, let the window repaint, then read LastFrame.</summary>
        public static bool Recording;

        static readonly List<ZuiDrawRecord> _current = new List<ZuiDrawRecord>();
        static readonly List<ZuiDrawRecord> _last = new List<ZuiDrawRecord>();

        /// <summary>The most recently completed Repaint pass's draws.</summary>
        public static IReadOnlyList<ZuiDrawRecord> LastFrame => _last;

        public static void Record(in ZuiDrawRecord r)
        {
            if (!Recording) return;
            var e = Event.current;
            if (e == null || e.type != EventType.Repaint) return;
            _current.Add(r);
        }

        /// <summary>Call once per Repaint-type OnGUI pass (ZUIWindow.OnGUI does this) to publish this pass's draws.</summary>
        public static void EndRepaintPass()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            _last.Clear();
            _last.AddRange(_current);
            _current.Clear();
        }

        /// <summary>Forget everything (used when a scripted audit run starts fresh).</summary>
        public static void Reset()
        {
            _current.Clear();
            _last.Clear();
        }
    }
}
