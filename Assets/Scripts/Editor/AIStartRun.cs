using System.Linq;
using UnityEngine;
using AssetInventory;

// Re-enable the Asset Store download action and start the full "Run Actions" pass (overnight).
// Creating this file forces a domain reload that fully re-initializes Asset Inventory's action registry,
// so RunActions() below has the registered actions available. The download flag is set via a direct
// config edit (not the DownloadAssets property) so it can't NRE on an uninitialized registry.
public static class AIStartRun
{
    // rev3: forces a domain reload so AI rebuilds its in-memory asset list from the DB after purging the
    // 138 pipe('|')-path sub-package entries that were crashing the UpdateObserver (the real hang cause).
    public static string Execute()
    {
        AI.Actions.Init();  // build the action registry (normally done when the AI window opens)

        var actions = AI.Config.actionStates[0].actions;
        var st = actions.FirstOrDefault(x => x.key == "AssetStoreDownloads");
        if (st != null) st.enabled = true;
        else actions.Add(new UpdateActionState { key = "AssetStoreDownloads", enabled = true });
        AI.SaveConfig();

        int registered = AI.Actions.Actions.Count;
        AI.Actions.RunActions();  // async void — starts the background pass, returns immediately
        return "dataPath=" + Application.dataPath + " | registeredActions=" + registered
             + " | inProgress=" + AI.Actions.AnyActionsInProgress;
    }
}
