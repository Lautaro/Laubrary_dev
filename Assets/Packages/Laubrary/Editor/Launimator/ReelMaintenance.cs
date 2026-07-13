using UnityEditor;
using UnityEngine;

namespace Laubrary.Launimator.Editor
{
    /// <summary>
    /// One-shot maintenance: make every reel's DRAFT durable. Old version assets were "script-less"
    /// (ReelVersion had no MonoScript) and could lose their recipes when edited then reloaded; this
    /// re-creates such drafts as fresh, properly-scripted assets at the correct name. New reels are already
    /// durable, so this only needs running once after upgrading, or after importing pre-fix reels.
    /// </summary>
    public static class ReelMaintenance
    {
        [MenuItem("Laubrary/Launimator/Repair Reel Assets")]
        public static void RepairAll()
        {
            int healed = 0, scanned = 0;
            foreach (var c in ReelRepo.EnumerateReels())
            {
                scanned++;
                try { if (ReelRepo.HealDraft(c)) healed++; }
                catch (System.Exception ex) { Debug.LogError($"Launimator repair failed for '{c.reelName}': {ex.Message}"); }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Launimator",
                $"Checked {scanned} reel(s); made {healed} draft(s) durable.\n\n" +
                "Drafts are now safe to edit — changes persist and won't disappear on a reload.", "OK");
        }
    }
}
