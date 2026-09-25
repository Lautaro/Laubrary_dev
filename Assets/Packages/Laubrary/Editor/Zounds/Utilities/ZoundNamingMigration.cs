using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    /// <summary>
    /// Utility to migrate Zound names to a new hierarchical pattern: [Parent]_[Source]_(Index)
    /// This enhances project organization and makes rendered files easily identifiable.
    /// </summary>
    public static class ZoundNamingMigration {

        public static void RunMigration() {
            var project = ZoundsProject.Instance;
            if (project == null) return;

            bool confirmed = EditorUtility.DisplayDialog(
                "Migrate Zound Naming",
                "This will rename every LOCAL Klip and Zequence in your project to follow a hierarchical [Parent]_[Content] pattern. \n\n" +
                "Public (Root) Zounds will NOT be renamed. \n\n" +
                "Rendered files will be marked for re-render to match the new names. The old files will show up as Orphans.\n\n" +
                "Proceed?",
                "Migrate", "Cancel");

            if (!confirmed) return;

            ZoundsWindow.ModifyAndSaveZoundsProject("migrate zound naming", () => {
                PerformMigration(project.zoundLibrary);
            }, true);

            Debug.Log("[Zounds] Naming migration complete. Please check the 'Dep. Map' tab to clean up orphaned files.");
        }

        private static void PerformMigration(ZoundLibrary library) {
            // Track naming counts globally to ensure uniqueness if needed, 
            // but we'll primarily scope counters per parent during recursion.
            
            // 1. Ensure all Public Zounds have clean names (no leading/trailing spaces)
            var rootZounds = library.FindAllZounds(z => z.parentId == 0);
            foreach (var root in rootZounds) {
                root.name = root.name.Trim();
            }

            // 2. Rename Nested Objects (Local Klips and Local Zequences)
            // We iterate through root sequences and descend into their local children
            foreach (var zeq in library.zequences) {
                if (zeq.parentId == 0) {
                    RenameChildrenRecursive(zeq, library);
                }
            }
        }

        private static void RenameChildrenRecursive(Zequence parent, ZoundLibrary library) {
            string parentName = parent.name;
            var nameUsageCounters = new Dictionary<string, int>();

            // Handle Local Klips
            foreach (var klip in parent.localKlips) {
                string sourceName = "Unknown";
                if (!string.IsNullOrEmpty(klip.audioClipPath)) {
                    sourceName = Path.GetFileNameWithoutExtension(klip.audioClipPath);
                }

                string baseName = $"[{parentName}]_{sourceName}";
                klip.name = GenerateUniqueName(baseName, nameUsageCounters);
                
                // Mark for re-render so the file on disk matches the new name
                klip.needsRender = true;
            }

            // Handle Local Zequences
            foreach (var localZeqEntry in parent.localZequences) {
                Zequence childZeq = localZeqEntry.zequence;
                
                string baseName = $"[{parentName}]";
                childZeq.name = GenerateUniqueName(baseName, nameUsageCounters);

                // Recursively rename children of this local sequence
                RenameChildrenRecursive(childZeq, library);
            }
        }

        private static string GenerateUniqueName(string baseName, Dictionary<string, int> counters) {
            if (!counters.ContainsKey(baseName)) {
                counters[baseName] = 1;
            } else {
                counters[baseName]++;
            }

            int index = counters[baseName];
            return $"{baseName}_({index:D2})";
        }
    }
}
