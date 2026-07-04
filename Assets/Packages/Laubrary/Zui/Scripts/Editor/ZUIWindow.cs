// ZUIWindow.cs
// Base EditorWindow class for ZUI-based windows.
// Wires up: instant hover, sheet scoping, root box container.
//
// Consumer windows override ConsumerSheetName to use their registered sheet.
// ZUI's own editor windows (Style Editor) handle sheet selection internally.

using UnityEditor;
using UnityEngine;

public abstract partial class ZUIWindow : EditorWindow
{

    /// <summary>
    /// The sheet this window renders against. Override to return an explicit asset or a
    /// consumer-registry lookup. Return null to fall back to <see cref="ZUI.DefaultSheet"/>.
    /// This is the preferred way to bind a sheet to a window — instance-method draw calls
    /// (this.Button(...), this.Toggle(...), etc.) resolve against ResolvedSheet and are
    /// immune to ambient-sheet leakage from other windows.
    /// </summary>
    protected virtual ZUIStyleSheetAsset Sheet
        => !string.IsNullOrEmpty(ConsumerSheetName) ? ZUI.GetConsumerSheet(ConsumerSheetName) : null;

    /// <summary>The sheet used for this window's draws — Sheet if set, otherwise DefaultSheet.
    /// Never null (DefaultSheet is lazily created). Used by the instance-method wrappers.</summary>
    internal ZUIStyleSheetAsset ResolvedSheet => Sheet ?? ZUI.DefaultSheet;

    /// <summary>
    /// Legacy: override to return a registered consumer sheet name (e.g. "Zounds", "Showcase").
    /// Prefer overriding <see cref="Sheet"/> directly. When set, Sheet resolves through
    /// ZUI.GetConsumerSheet(ConsumerSheetName) by default.
    /// </summary>
    protected virtual string ConsumerSheetName => null;

    /// <summary>
    /// The box style name used for the root container that wraps the window content.
    /// Override to change the root box appearance. Set to null to disable the root box.
    /// </summary>
    protected virtual string RootBoxStyle => "Default";

    // Tracks the consumer sheet's version so we detect external modifications and repaint.
    [System.NonSerialized] private int _lastSheetVersion = -1;

    void OnEnable()
    {
        wantsMouseMove = true;
        OnZUIEnable();
    }

    void OnGUI()
    {
        if (Event.current.type == EventType.MouseMove)
            Repaint();

        // Scope the active sheet for this window. This makes ambient-reading static ZUI calls
        // inside OnZUI resolve against this window's sheet. Instance-method draws (this.Button,
        // this.Toggle, etc.) also use ResolvedSheet — they don't depend on ambient scope.
        ZUI.SheetScope scope = default;
        bool hasScope = false;
        var windowSheet = ResolvedSheet;
        if (windowSheet != null) { scope = ZUI.UseSheet(windowSheet); hasScope = true; }

        // Detect external sheet modifications and repaint with fresh caches.
        // BumpVersion() already invalidated all defs — we just need to notice the change
        // and schedule a repaint so the window redraws with the new values.
        if (windowSheet != null && windowSheet.Version != _lastSheetVersion)
        {
            _lastSheetVersion = windowSheet.Version;
            Repaint();
        }

        ZUI.TryShowPendingMenu();

        // Root box container
        string rootStyle = RootBoxStyle;
        if (!string.IsNullOrEmpty(rootStyle))
        {
            using (ZUI.BoxNamed(rootStyle))
            {
                OnZUI();
            }
        }
        else
        {
            OnZUI();
        }

        if (hasScope) scope.Dispose();
    }

    protected virtual void OnZUIEnable() { }
    protected virtual void OnZUI() { }
}
