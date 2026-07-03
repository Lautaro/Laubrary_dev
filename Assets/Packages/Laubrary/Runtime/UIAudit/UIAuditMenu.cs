#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Laubrary.UIAudit
{
    // Editor convenience wrappers. The real entry point for an agent is UIAudit.Report() via a script call,
    // but these menu items make it a two-click check for a human too.
    static class UIAuditMenu
    {
        [MenuItem("Laubrary/UI Audit/Audit Current UI")]
        static void AuditNow()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[UIAudit] Enter Play mode and navigate to the screen you want to audit first.");
                return;
            }
            Debug.Log("[UIAudit]\n" + UIAudit.Report());
        }

        // IMGUI is immediate-mode: it must be recorded for a frame before it can be audited. Toggle this on,
        // let the screen you want draw a frame, then run "Audit Current UI".
        [MenuItem("Laubrary/UI Audit/Toggle IMGUI Recording")]
        static void ToggleImgui()
        {
            ZuiRuntime.ZuiAudit.Recording = !ZuiRuntime.ZuiAudit.Recording;
            Debug.Log($"[UIAudit] IMGUI recording {(ZuiRuntime.ZuiAudit.Recording ? "ON — draw the screen, then Audit Current UI" : "OFF")}");
        }

        // Issues are resolution-dependent — audit at the shipping target, not a maximized editor view.
        [MenuItem("Laubrary/UI Audit/Set Game View 1280x800 (Steam Deck)")]
        static void SetDeck() => PlayModeWindow.SetCustomRenderingResolution(1280, 800, "SteamDeck");

        [MenuItem("Laubrary/UI Audit/Set Game View 1920x1080")]
        static void Set1080() => PlayModeWindow.SetCustomRenderingResolution(1920, 1080, "1080p");
    }
}
#endif
