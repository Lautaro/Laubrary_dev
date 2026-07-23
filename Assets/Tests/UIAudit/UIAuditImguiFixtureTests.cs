// Ground-truth check for UIAudit's IMGUI section: does it actually catch the issues a known-bad layout
// has, and does it stay quiet on a correctly-built equivalent? See ZUI_API_AND_RUNTIME_ROADMAP.md Task 3
// — this is the "test project that can evaluate tools for improving IMGUI auditing" the roadmap scoped,
// not a demo. Dev-host-only: not shipped with the package (Assets/Tests/, outside Packages/Laubrary/).
//
// PlayMode, not EditMode: ZuiAudit.Record only appends during an actual OnGUI Repaint pass
// (Event.current.type == EventType.Repaint), which requires a live player loop — there's no way to
// synthesize that from an EditMode test.

using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZuiRuntime;

namespace Laubrary.UIAudit.Tests
{
    public class UIAuditImguiFixtureTests
    {
        GameObject _go;
        ImguiFixtureHud _hud;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("ImguiFixtureHud");
            _hud = _go.AddComponent<ImguiFixtureHud>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.Destroy(_go);
            ZuiAudit.Recording = false;
        }

        [UnityTest]
        public IEnumerator BadFrame_ReportsAllFiveIssueKinds()
        {
            _hud.DrawBad = true;
            ZuiAudit.Reset();
            ZuiAudit.Recording = true;
            yield return null;
            yield return null; // guarantee at least one full Layout+Repaint pass has recorded

            var issues = UIAudit.Run();

            AssertHas(issues, UIIssueKind.TinyText);
            AssertHas(issues, UIIssueKind.TextOverflow);
            AssertHas(issues, UIIssueKind.OffScreen);
            AssertHas(issues, UIIssueKind.Overlap);
            AssertHas(issues, UIIssueKind.Crowded);
        }

        [UnityTest]
        public IEnumerator FixedFrame_ReportsNoIssues()
        {
            _hud.DrawBad = false;
            ZuiAudit.Reset();
            ZuiAudit.Recording = true;
            yield return null;
            yield return null;

            var issues = UIAudit.Run();
            var imguiIssues = issues.Where(i => i.Section == "IMGUI").ToList();

            Assert.IsEmpty(imguiIssues,
                "Fixed fixture should report clean — got: " + string.Join("; ", imguiIssues.Select(i => i.ToString())));
        }

        static void AssertHas(System.Collections.Generic.List<UIIssue> issues, UIIssueKind kind)
        {
            bool found = issues.Any(i => i.Kind == kind && i.Section == "IMGUI");
            Assert.IsTrue(found, $"Expected an IMGUI '{kind}' issue in the bad fixture, but none was reported. " +
                                  $"Got: {string.Join("; ", issues.Select(i => i.ToString()))}");
        }
    }
}
