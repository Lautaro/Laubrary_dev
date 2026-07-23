// Ground-truth check for UIAudit's editor-window section (EditorWindowAuditSection): does it actually
// catch the issues BadEditorWindowFixture plants? The edit-mode sibling of
// UIAuditImguiFixtureTests — but unlike that one, THIS can be a plain EditMode [Test]: an EditorWindow's
// OnGUI can be driven synchronously via EditorWindow.SendEvent (a Layout pass then a Repaint pass), no
// live player loop required. Verified live before writing this test (ScriptableObject.CreateInstance +
// ShowUtility + SendEvent) — CreateInstance alone throws on SendEvent (no native view yet); ShowUtility
// first is required, same "don't use GetWindow<T> for a throwaway instance" gotcha noted elsewhere for
// this style of probe (GetWindow can reuse a stale docked window with leftover state).
//
// Dev-host-only: not shipped with the package (Assets/Tests/, outside Packages/Laubrary/).

using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Laubrary.UIAudit.Tests
{
    public class UIAuditEditorWindowFixtureTests
    {
        BadEditorWindowFixture _win;

        [SetUp]
        public void SetUp()
        {
            _win = ScriptableObject.CreateInstance<BadEditorWindowFixture>();
            _win.position = new Rect(0, 0, 420, 500);
            _win.ShowUtility();
            EditorZuiAudit.Reset();
            EditorZuiAudit.Recording = true;
        }

        [TearDown]
        public void TearDown()
        {
            EditorZuiAudit.Recording = false;
            if (_win != null) _win.Close();
        }

        [Test]
        public void BadFixture_ReportsAllMechanicallyCheckableIssueKinds()
        {
            _win.SendEvent(new Event { type = EventType.Layout });
            _win.SendEvent(new Event { type = EventType.Repaint });

            var issues = EditorUIAudit.Run(_win);

            AssertHas(issues, UIIssueKind.OverWidth);
            AssertHas(issues, UIIssueKind.TinyText);
            AssertHas(issues, UIIssueKind.Crowded);
            AssertHas(issues, UIIssueKind.Overlap);
        }

        static void AssertHas(System.Collections.Generic.List<UIIssue> issues, UIIssueKind kind)
        {
            bool found = issues.Any(i => i.Kind == kind && i.Section == "ZUI.Editor");
            Assert.IsTrue(found, $"Expected a ZUI.Editor '{kind}' issue in the bad fixture, but none was reported. " +
                                  $"Got: {string.Join("; ", issues.Select(i => i.ToString()))}");
        }
    }
}
