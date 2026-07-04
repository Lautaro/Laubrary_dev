using UnityEditor;
using UnityEngine;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// One-shot maintenance: make every zoe's DRAFT durable. Old version assets were "script-less"
    /// (ZoeVersion had no MonoScript) and could lose their recipes when edited then reloaded; this
    /// re-creates such drafts as fresh, properly-scripted assets at the correct name. New zoes are already
    /// durable, so this only needs running once after upgrading, or after importing pre-fix zoes.
    /// </summary>
    public static class ZoeMaintenance
    {
        [MenuItem("Laubrary/Zoetrope/Repair Zoe Assets")]
        public static void RepairAll()
        {
            int healed = 0, scanned = 0;
            foreach (var c in ZoeRepo.EnumerateZoes())
            {
                scanned++;
                try { if (ZoeRepo.HealDraft(c)) healed++; }
                catch (System.Exception ex) { Debug.LogError($"Zoetrope repair failed for '{c.zoeName}': {ex.Message}"); }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Zoetrope",
                $"Checked {scanned} zoe(s); made {healed} draft(s) durable.\n\n" +
                "Drafts are now safe to edit — changes persist and won't disappear on a reload.", "OK");
        }
    }
}
