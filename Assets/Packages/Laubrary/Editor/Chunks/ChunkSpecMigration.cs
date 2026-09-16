using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Chunks.Editor
{
    /// Writes the capability-stack upgrade to disk, once per recipe.
    ///
    /// The recipe upgrades itself in memory whenever it loads, so a build and a play session are always
    /// correct — but an in-memory upgrade is paid again on every load and, more importantly, is invisible: the
    /// asset on disk still says the old thing, so anything reading the YAML (a diff, a merge, another tool)
    /// sees a recipe that does not match what the game runs. This walks every recipe in the project once and
    /// saves the upgraded form through the normal asset pipeline.
    ///
    /// It must go through the editor's own asset save and nothing else: a stack of managed references is only
    /// written correctly by the editor's own serializer, and saving one from outside it destroys the reference
    /// ids — which are the keys every modifier target and every card's view state hang off, so losing them
    /// silently unpicks the recipe rather than failing loudly. It saves only the recipes it upgraded:
    /// <see cref="AssetDatabase.SaveAssets"/> would also write every other unsaved asset in the project,
    /// behind the author's back.
    ///
    /// Self-limiting: an already-current recipe is skipped, so this costs one asset search per domain load and
    /// writes nothing ever again.
    [InitializeOnLoad]
    static class ChunkSpecMigration
    {
        static ChunkSpecMigration()
        {
            // Deferred: at static-constructor time the asset database may still be importing, and a save
            // issued mid-import can be dropped without a word.
            EditorApplication.delayCall += () => UpgradeAllAssets(silent: true);
        }

        /// Upgrades and saves every ChunkSpec asset that is not already current. Returns a one-line report of
        /// what it touched, so a probe can read the outcome rather than infer it from the console.
        public static string UpgradeAllAssets(bool silent = false)
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(ChunkSpec));
            var upgraded = new List<string>();
            var toSave = new List<ChunkSpec>();

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var spec = AssetDatabase.LoadAssetAtPath<ChunkSpec>(path);
                if (spec == null) continue;

                // Loading it already ran the upgrade in memory, so the question is not "is this recipe
                // current?" (it always is by now) but "was it current when it was read?".
                spec.UpgradeIfNeeded();
                if (!spec.NeedsSaving) continue;

                EditorUtility.SetDirty(spec);
                upgraded.Add(path);
                toSave.Add(spec);
            }

            if (upgraded.Count == 0) return "ChunkSpec migration: nothing to upgrade.";

            foreach (var spec in toSave)
            {
                AssetDatabase.SaveAssetIfDirty(spec);
                spec.MarkSaved();
            }

            string report = $"ChunkSpec migration: upgraded {upgraded.Count} recipe(s) — {string.Join(", ", upgraded)}";
            if (!silent) Debug.Log("[Chunks] " + report);
            return report;
        }
    }
}
