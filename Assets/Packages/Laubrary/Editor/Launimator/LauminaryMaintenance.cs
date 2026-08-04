using UnityEditor;
using UnityEngine;

namespace Laubrary.Launimator.Editor
{
    /// <summary>
    /// One-shot maintenance: make every lauminary's DRAFT durable. Old version assets were "script-less"
    /// (LauminaryVersion had no MonoScript) and could lose their recipes when edited then reloaded; this
    /// re-creates such drafts as fresh, properly-scripted assets at the correct name. New lauminaries are already
    /// durable, so this only needs running once after upgrading, or after importing pre-fix lauminaries.
    /// </summary>
    public static class LauminaryMaintenance
    {
        [MenuItem("Laubrary/Launimator/Repair Lauminary Assets")]
        public static void RepairAll()
        {
            int healed = 0, scanned = 0;
            foreach (var c in LauminaryRepo.EnumerateLauminaries())
            {
                scanned++;
                try { if (LauminaryRepo.HealDraft(c)) healed++; }
                catch (System.Exception ex) { Debug.LogError($"Launimator repair failed for '{c.lauminaryName}': {ex.Message}"); }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Launimator",
                $"Checked {scanned} lauminary(s); made {healed} draft(s) durable.\n\n" +
                "Drafts are now safe to edit — changes persist and won't disappear on a reload.", "OK");
        }
    }
}
