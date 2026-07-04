using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    /// <summary>
    /// Asset materialization and dependency management for preview generation.
    /// Extracted from PreviewManager to separate concerns.
    /// </summary>
    public static class PreviewAssetManager
    {
        /// <summary>
        /// Represents a file to be bulk imported.
        /// </summary>
        private sealed class BulkImportItem
        {
            public string Guid;
            public string SourcePath;
            public string TargetPath;
            public int AssetId;
            public string Path; // AssetFile.Path - used as fallback key when Guid is null

            /// <summary>
            /// Returns the effective dictionary key for this item.
            /// Uses GUID when available, falls back to a path-based key to avoid
            /// null-key collisions when multiple files lack GUIDs.
            /// </summary>
            public string EffectiveKey => !string.IsNullOrEmpty(Guid) ? Guid : $"path:{AssetId}_{Path}";
        }

        /// <summary>
        /// Materializes all files and their dependencies in a single batch operation.
        /// 
        /// NEW FLOW (dramatically reduces refreshes):
        /// 1. Get ALL non-script files from the package
        /// 2. Bulk import them to preview work folder - ONE refresh (files become YAML)
        /// 3. Store paths in cache so dependency analysis can find them
        /// 4. Run dependency analysis - now finds in-project files, skips temp folder dance
        /// 5. Materialize any cross-package dependencies
        /// </summary>
        /// <param name="files">Files to materialize</param>
        /// <param name="cache">Cache for dependency memoization and materialized path storage</param>
        /// <param name="selectiveMode">When true, only imports dependency-needing file types (prefabs, materials, FBX, shaders, etc.)
        /// and their discovered dependencies. Non-dependency files (images, audio, video) are left for per-file handling.
        /// This avoids importing thousands of files that don't need Unity's import pipeline for preview generation.</param>
        /// <param name="onProgress">Optional progress callback (message, current, total). Called between batches during
        /// dependency analysis to update progress bars.</param>
        /// <param name="isCancelled">Optional cancellation check. Return true to abort processing.</param>
        /// <returns>Dictionary mapping AssetInfo.Id to materialized project-relative path</returns>
        public static async Task<Dictionary<int, string>> MaterializePackageFilesAsync(
            List<AssetInfo> files, DependencyResultCache cache, bool selectiveMode = false,
            Action<string, int, int> onProgress = null, Func<bool> isCancelled = null)
        {
            Dictionary<int, string> results = new Dictionary<int, string>();
            try
            {
                if (files == null || files.Count == 0) return results;

                string targetFolder = UnityPreviewGenerator.GetPreviewWorkFolder();
                int assetId = files[0].AssetId;

                // Prevent Unity from importing files while we copy both main package and SRP files.
                // A single StartAssetEditing/StopAssetEditing block ensures Unity does not import anything
                // until ALL files (with SRP replacements in their native paths) are on disk.
                Dictionary<string, string> guidToPath;

                // Check for SRP support package FIRST so Phase 1 can skip files
                // that will be provided by the SRP package instead.
                Asset srpSupportPackage = DependencyAnalysis.FindSRPSupportPackage(assetId, warnOnMultiple: false);
                HashSet<string> srpGuids = null;
                if (srpSupportPackage != null)
                {
                    // Collect ALL non-script GUIDs from the SRP package. Files in the main package
                    // with matching GUIDs will be SKIPPED so only the SRP version exists on disk.
                    // This includes FBX/model files: the SRP version has correct pipeline-specific
                    // material references in its .meta and must be used instead of the main version.
                    string sprScriptFilter = DependencyAnalysis.ScriptRelatedSqlNotFilter();
                    List<AssetFile> srpFiles = DBAdapter.DB.Query<AssetFile>(
                        $"SELECT Guid FROM AssetFile WHERE AssetId = ? AND {sprScriptFilter}",
                        srpSupportPackage.Id).ToList();
                    srpGuids = new HashSet<string>(srpFiles.Where(f => !string.IsNullOrEmpty(f.Guid)).Select(f => f.Guid));
                }

                AssetDatabase.StartAssetEditing();
                try
                {
                    // PHASE 1: Bulk copy non-script files from the main package.
                    // Only shader/material/include files whose GUIDs appear in the SRP support package
                    // are SKIPPED. FBX, textures, prefabs etc. always come from the main package.
                    // In selective mode, only dependency-needing types are imported.
                    guidToPath = await BulkImportPackageFilesAsync(assetId, targetFolder, cache, srpGuids, selectiveMode);

                    // PHASE 1b: Copy ALL SRP support package files to their native paths.
                    // SRP files keep their original folder structure (e.g. Pack/URPSupport/Shaders/)
                    // so relative shader #include directives between URP files resolve correctly.
                    // FBX/model files from the SRP package are also imported here — they have
                    // correct pipeline-specific .meta (externalObjects referencing URP materials).
                    if (srpSupportPackage != null)
                    {
                        // SRP support package is always fully imported (it provides shader replacements)
                        Dictionary<string, string> srpGuidToPath = await BulkImportPackageFilesAsync(
                            srpSupportPackage.Id, targetFolder, cache, null, false);
                        foreach (KeyValuePair<string, string> kvp in srpGuidToPath)
                        {
                            guidToPath[kvp.Key] = kvp.Value;
                        }
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                // ONE refresh after all files are on disk in their final layout.
                // SRP shaders are at their native paths, main-package files fill in the rest.
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                // Wait for async shader compilation to finish before any preview rendering.
                // ForceSynchronousImport imports files synchronously, but Unity may still compile
                // shader variants on background threads. With hundreds of files imported at once,
                // complex shaders (URP Lit, particles, etc.) can take longer than the fixed 48ms
                // warmup in RenderFrame. FBX materials referencing still-compiling shaders appear
                // pink. ProcessCopyBatch avoids this by doing multiple Refreshes (2-3) across its
                // phases, giving shaders time to compile between passes. We wait explicitly instead.
                await WaitForShaderCompilation();

                // PHASE 2: Map the scheduled files to their imported paths
                foreach (AssetInfo info in files)
                {
                    // Check if file is already in project
                    if (info.InProject && !string.IsNullOrEmpty(info.ProjectPath))
                    {
                        results[info.Id] = info.ProjectPath;
                        string cacheKey = DependencyResultCache.GetMaterializedPathKey(info);
                        cache?.StoreMaterializedPath(cacheKey, info.ProjectPath);
                        continue;
                    }

                    // Look up by GUID in our imported paths
                    string lookupKey = !string.IsNullOrEmpty(info.Guid)
                        ? info.Guid
                        : $"path:{info.AssetId}_{info.Path}"; // fallback for files without GUIDs

                    if (guidToPath.TryGetValue(lookupKey, out string importedPath))
                    {
                        results[info.Id] = importedPath;

                        // Store in cache so EnsureDependenciesAsync can short-circuit.
                        // Without this, each FBX/prefab falls through to a per-file CopyTo + Refresh,
                        // which (a) negates the bulk optimization entirely and (b) causes concurrent
                        // Refresh calls when files are processed in parallel batches.
                        string cacheKey2 = DependencyResultCache.GetMaterializedPathKey(info);
                        cache?.StoreMaterializedPath(cacheKey2, importedPath);
                        continue;
                    }

                    // Fallback: file wasn't in bulk import (maybe script or missing)
                    // This shouldn't happen often if we imported all non-scripts
                }

                // PHASE 3: Run dependency analysis in batches
                // Pre-filter to only files that actually need scanning to avoid iterating the full list repeatedly
                List<AssetInfo> depScanFiles = files.Where(f =>
                    DependencyAnalysis.NeedsScan(f.Type) &&
                    (f.DependencyState == AssetInfo.DependencyStateOptions.Unknown ||
                        f.DependencyState == AssetInfo.DependencyStateOptions.Partial)).ToList();

                // Create a CancellationTokenSource that respects the external cancellation check
                using (CancellationTokenSource cts = new CancellationTokenSource())
                {
                    int batchSize = Math.Max(1, AI.Config.parallelPreviewBatchSize);
                    for (int i = 0; i < depScanFiles.Count; i += batchSize)
                    {
                        // Check cancellation between batches
                        if (isCancelled?.Invoke() == true)
                        {
                            cts.Cancel();
                            break;
                        }

                        // Report progress
                        onProgress?.Invoke($"Analyzing dependencies ({i}/{depScanFiles.Count})...", i, depScanFiles.Count);

                        int currentBatchSize = Math.Min(batchSize, depScanFiles.Count - i);
                        List<Task> batchTasks = new List<Task>(currentBatchSize);
                        for (int j = 0; j < currentBatchSize; j++)
                        {
                            batchTasks.Add(AI.CalculateDependencies(depScanFiles[i + j], cts.Token, cache));
                        }

                        await Task.WhenAll(batchTasks);

                        // Let the editor breathe and manage memory between batches
                        await Task.Yield();
                        await AI.Cooldown.Do();
                        AI.MemoryObserver.Do(currentBatchSize * 50000); // ~50KB estimate per dependency analysis
                    }

                    onProgress?.Invoke($"Dependency analysis complete ({depScanFiles.Count} files)", depScanFiles.Count, depScanFiles.Count);
                }

                // Wait for any ongoing calculations to finish (edge case: tasks started before cancellation)
                foreach (AssetInfo info in depScanFiles)
                {
                    if (isCancelled?.Invoke() == true) break;
                    while (info.DependencyState == AssetInfo.DependencyStateOptions.Calculating)
                    {
                        await Task.Yield();
                    }
                }

                // PHASE 4: Collect cross-package dependencies that need materialization
                // Same-package deps should already be imported; only cross-package needs work
                List<AssetInfo> crossPackageDeps = new List<AssetInfo>();
                // In selective mode, also collect intra-package dependencies (textures etc.
                // referenced by FBX/prefabs that were not imported in Phase 1)
                List<AssetFile> intraPackageDeps = new List<AssetFile>();
                HashSet<string> processedGuids = new HashSet<string>(guidToPath.Keys);

                foreach (AssetInfo info in files)
                {
                    if (info.Dependencies == null) continue;

                    foreach (AssetFile dep in info.Dependencies)
                    {
                        // Skip script-related files — importing them causes a domain reload
                        // which must be avoided during preview generation. Phase 1 already
                        // excludes scripts from the main package; apply the same filter here.
                        if (DependencyAnalysis.IsScriptRelated(dep)) continue;

                        // Skip if already processed or same package (already imported)
                        if (string.IsNullOrEmpty(dep.Guid)) continue;
                        if (processedGuids.Contains(dep.Guid)) continue;

                        if (dep.AssetId == assetId)
                        {
                            // Same package: in selective mode, this dependency wasn't imported
                            // in Phase 1 (e.g. a texture referenced by an FBX). Collect it.
                            if (selectiveMode)
                            {
                                intraPackageDeps.Add(dep);
                                processedGuids.Add(dep.Guid);
                            }
                            continue; // In full mode, already imported
                        }

                        // This is a cross-package dependency
                        Asset crossAsset = info.CrossPackageDependencies?.FirstOrDefault(a => a.Id == dep.AssetId);
                        if (crossAsset != null)
                        {
                            AssetInfo depInfo = new AssetInfo().CopyFrom(crossAsset, dep);
                            crossPackageDeps.Add(depInfo);
                            processedGuids.Add(dep.Guid);
                        }
                    }
                }

                // PHASE 5: Batch materialize cross-package dependencies if any
                if (crossPackageDeps.Count > 0)
                {
                    Dictionary<int, string> crossResults = await Assets.CopyToBatch(
                        crossPackageDeps, targetFolder, false, true); // withDependencies=false, we already know deps

                    foreach (KeyValuePair<int, string> kvp in crossResults)
                    {
                        AssetInfo depInfo = crossPackageDeps.FirstOrDefault(d => d.Id == kvp.Key);
                        if (depInfo != null)
                        {
                            string cacheKey = DependencyResultCache.GetMaterializedPathKey(depInfo);
                            cache?.StoreMaterializedPath(cacheKey, kvp.Value);
                        }
                    }
                }

                // PHASE 5b: Import intra-package dependencies discovered in selective mode.
                // These are files from the same package (typically textures) that were skipped
                // in Phase 1 but are needed by dependency-needing files (FBX, prefabs, etc.).
                if (selectiveMode && intraPackageDeps.Count > 0)
                {
                    Asset asset = DBAdapter.DB.Find<Asset>(assetId);
                    if (asset != null)
                    {
                        List<BulkImportItem> depWorkItems = new List<BulkImportItem>();
                        foreach (AssetFile dep in intraPackageDeps)
                        {
                            // Check if already in project by GUID
                            string existingPath = Assets.GetExistingAssetPathForGuid(dep.Guid, true);
                            if (!string.IsNullOrEmpty(existingPath))
                            {
                                guidToPath[dep.Guid] = existingPath;
                                string ck = DependencyResultCache.GetMaterializedPathKey(assetId, dep.Guid, dep.Path);
                                cache?.StoreMaterializedPath(ck, existingPath);
                                continue;
                            }

                            string sourcePath = await Assets.EnsureMaterialized(asset, dep);
                            if (sourcePath == null) continue;

                            string targetPath = Path.Combine(targetFolder, dep.Path).Replace("~", "");
                            depWorkItems.Add(new BulkImportItem
                            {
                                Guid = dep.Guid,
                                SourcePath = sourcePath,
                                TargetPath = targetPath,
                                AssetId = assetId,
                                Path = dep.Path
                            });
                        }

                        if (depWorkItems.Count > 0)
                        {
                            AssetDatabase.StartAssetEditing();
                            try
                            {
                                Dictionary<string, string> depResults = await ProcessBulkImport(depWorkItems);
                                foreach (KeyValuePair<string, string> kvp in depResults)
                                {
                                    guidToPath[kvp.Key] = kvp.Value;
                                    string ck = kvp.Key.StartsWith("path:")
                                        ? kvp.Key
                                        : DependencyResultCache.GetMaterializedPathKey(assetId, kvp.Key);
                                    cache?.StoreMaterializedPath(ck, kvp.Value);
                                }
                            }
                            finally
                            {
                                AssetDatabase.StopAssetEditing();
                            }

                            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                            await WaitForShaderCompilation();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error during bulk materialization: {e.Message}");
            }

            return results;
        }

        /// <summary>
        /// Bulk imports all non-script files from a package to the target folder.
        /// This is the key optimization: ONE refresh for the entire package.
        /// </summary>
        /// <param name="skipGuids">Optional set of GUIDs to skip (files provided by SRP support package instead).</param>
        /// <param name="selectiveMode">When true, only imports files whose types need dependency scanning
        /// (as determined by DependencyAnalysis.NeedsScan). Non-dependency types are skipped.</param>
        private static async Task<Dictionary<string, string>> BulkImportPackageFilesAsync(
            int assetId, string targetFolder, DependencyResultCache cache,
            HashSet<string> skipGuids = null, bool selectiveMode = false)
        {
            Dictionary<string, string> guidToPath = new Dictionary<string, string>();

            // Get all non-script files from the package
            string scriptNotFilter = DependencyAnalysis.ScriptRelatedSqlNotFilter();
            List<AssetFile> packageFiles = DBAdapter.DB.Query<AssetFile>(
                $"SELECT * FROM AssetFile WHERE AssetId = ? AND {scriptNotFilter}",
                assetId).ToList();

            if (packageFiles.Count == 0) return guidToPath;

            // Get the asset info for materialization
            Asset asset = DBAdapter.DB.Find<Asset>(assetId);
            if (asset == null) return guidToPath;

            // Collect work items for all files
            List<BulkImportItem> workItems = new List<BulkImportItem>();

            foreach (AssetFile af in packageFiles)
            {
                // In selective mode, skip files that don't need dependency scanning.
                // These will be handled per-file (images via ImagePreviewHandler, audio/video via Localize).
                if (selectiveMode && !DependencyAnalysis.NeedsScan(af.Type))
                {
                    continue;
                }

                // Skip files that have SRP replacements — the SRP version will be imported
                // at its native path to preserve relative shader #include directives.
                if (af.Guid != null && skipGuids != null && skipGuids.Contains(af.Guid)) continue;

                // Check if already in project by GUID (only possible when GUID is available)
                if (!string.IsNullOrEmpty(af.Guid))
                {
                    string existingPath = Assets.GetExistingAssetPathForGuid(af.Guid, true);
                    if (!string.IsNullOrEmpty(existingPath))
                    {
                        guidToPath[af.Guid] = existingPath;

                        // Store in cache for dependency analysis
                        string cacheKey = DependencyResultCache.GetMaterializedPathKey(assetId, af.Guid, af.Path);
                        cache?.StoreMaterializedPath(cacheKey, existingPath);
                        continue;
                    }
                }

                // Materialize the source file (extracts from archive to cache folder - no Unity refresh)
                string sourcePath = await Assets.EnsureMaterialized(asset, af);
                if (sourcePath == null) continue;

                // Calculate target path preserving the package's folder structure.
                // SRP files keep their native paths (e.g. Pack/URPSupport/Shaders/) which is
                // essential for relative shader #include directives to resolve correctly.
                string targetPath = Path.Combine(targetFolder, af.Path);
                targetPath = targetPath.Replace("~", ""); // Remove sample flag

                workItems.Add(new BulkImportItem
                {
                    Guid = af.Guid,
                    SourcePath = sourcePath,
                    TargetPath = targetPath,
                    AssetId = assetId,
                    Path = af.Path
                });
            }

            if (workItems.Count == 0) return guidToPath;

            // Batch copy all files - ONE refresh for everything
            Dictionary<string, string> copyResults = await ProcessBulkImport(workItems);

            // Store results in guidToPath and cache
            // Keys are either GUIDs or "path:{assetId}_{filePath}" for files without GUIDs
            foreach (KeyValuePair<string, string> kvp in copyResults)
            {
                guidToPath[kvp.Key] = kvp.Value;

                // Store in cache for dependency analysis to find
                // The key from ProcessBulkImport is already the effective key (GUID or path-based)
                // so use it directly as the cache key with the assetId prefix
                string cacheKey = kvp.Key.StartsWith("path:")
                    ? kvp.Key  // Already prefixed with path: and contains assetId
                    : DependencyResultCache.GetMaterializedPathKey(assetId, kvp.Key);
                cache?.StoreMaterializedPath(cacheKey, kvp.Value);
            }

            return guidToPath;
        }

        /// <summary>
        /// Copies all files in one batch to the target folder (file-system level only).
        /// </summary>
        /// <remarks>
        /// Does NOT call StartAssetEditing/StopAssetEditing or Refresh.
        /// The caller is responsible for wrapping multiple ProcessBulkImport calls
        /// in a single StartAssetEditing/StopAssetEditing block and issuing ONE Refresh
        /// after all files (including SRP overrides) are in their final state.
        /// </remarks>
        private static async Task<Dictionary<string, string>> ProcessBulkImport(List<BulkImportItem> items)
        {
            Dictionary<string, string> results = new Dictionary<string, string>();
            if (items.Count == 0) return results;

            ApplyCaseSensitivePathGuard(items);

            foreach (BulkImportItem item in items)
            {
                string targetDir = Path.GetDirectoryName(item.TargetPath);
                Directory.CreateDirectory(targetDir);

                if (await IOUtils.TryCopyFile(item.SourcePath, item.TargetPath, true))
                {
                    // Copy meta file if exists to preserve GUID
                    string metaPath = item.SourcePath + ".meta";
                    if (File.Exists(metaPath))
                    {
                        await IOUtils.TryCopyFile(metaPath, item.TargetPath + ".meta", true);
                    }
                    results[item.EffectiveKey] = AssetUtils.RemoveProjectRoot(item.TargetPath);
                }
            }

            return results; 
        }

        private static void ApplyCaseSensitivePathGuard(List<BulkImportItem> items)
        {
            if (items == null || items.Count == 0) return;
            string targetRoot = Path.GetDirectoryName(items[0].TargetPath) ?? items[0].TargetPath;

            List<CaseSensitivePathGuard.PathCandidate> adjustedPaths = CaseSensitivePathGuard.AdjustPaths(
                items.Select(item => new CaseSensitivePathGuard.PathCandidate
                {
                    Path = item.TargetPath,
                    IsDirectory = false,
                    Tag = item
                }),
                targetRoot,
                "preview import");

            items.Clear();
            foreach (CaseSensitivePathGuard.PathCandidate entry in adjustedPaths)
            {
                BulkImportItem item = (BulkImportItem) entry.Tag;
                item.TargetPath = entry.Path;
                items.Add(item);
            }
        }

        /// <summary>
        /// Waits for Unity's background shader compilation to finish.
        /// After a bulk AssetDatabase.Refresh importing hundreds of files, shader variant
        /// compilation continues asynchronously on background threads even though the Refresh
        /// call itself has returned. Materials loaded before compilation finishes will have
        /// null/error shaders (pink). This method polls ShaderUtil.anythingCompiling and
        /// yields until all compilation is done.
        /// </summary>
        private static async Task WaitForShaderCompilation()
        {
            // ShaderUtil.anythingCompiling is only available in Unity 2019.1+
            // If it returns false immediately, no waiting is needed (all shaders compiled during Refresh)
            const int maxWaitMs = 30000; // Safety cap: 30 seconds
            const int pollIntervalMs = 100;
            int waited = 0;

            while (ShaderUtil.anythingCompiling && waited < maxWaitMs)
            {
                await Task.Delay(pollIntervalMs);
                waited += pollIntervalMs;
            }
        }

        /// <summary>
        /// Ensure all dependencies for an asset are materialized before preview generation
        /// </summary>
        public static async Task<string> EnsureDependenciesAsync(AssetInfo info, string sourcePath, DependencyResultCache cache = null)
        {
            // Early cache check - if bulk materialization already ran, use cached path
            if (cache != null)
            {
                string cacheKey = DependencyResultCache.GetMaterializedPathKey(info);
                if (cache.TryGetMaterializedPath(cacheKey, out string cachedPath))
                {
                    return cachedPath;
                }
            }

            // Calculate and materialize dependencies if needed
            if (DependencyAnalysis.NeedsScan(info.Type))
            {
                if (info.DependencyState == AssetInfo.DependencyStateOptions.Unknown ||
                    info.DependencyState == AssetInfo.DependencyStateOptions.Partial)
                {
                    // Pass cache to dependency calculation
                    await AI.CalculateDependencies(info, CancellationToken.None, cache);
                }

                // Force recalculation if previous analysis found 0 deps but an SRP replacement exists.
                // This handles FBX files with legacy material mode (materialLocation: 0, externalObjects: {})
                // where materials are resolved by name, not by GUID. A prior calculation may have missed
                // these name-based dependencies before the scanner was extended to detect them.
                if (info.DependencyState == AssetInfo.DependencyStateOptions.Done &&
                    (info.Dependencies == null || info.Dependencies.Count == 0) &&
                    info.SRPMainReplacement != null)
                {
                    info.DependencyState = AssetInfo.DependencyStateOptions.Unknown;
                    await AI.CalculateDependencies(info, CancellationToken.None, cache);
                }

                // In case calculation was already ongoing, wait for it to finish
                while (info.DependencyState == AssetInfo.DependencyStateOptions.Calculating)
                {
                    await Task.Yield();
                }

                if (info.Dependencies.Count > 0 || info.SRPMainReplacement != null)
                {
                    sourcePath = await Assets.CopyTo(info, UnityPreviewGenerator.GetPreviewWorkFolder(), true, 0, false, false, false, true);
                }

                if (sourcePath == null) // can happen when file system issues occur
                {
                    if (info.PreviewState != AssetFile.PreviewOptions.Provided)
                    {
                        info.PreviewState = AssetFile.PreviewOptions.Error;
                        DBAdapter.DB.Execute("update AssetFile set PreviewState=? where Id=?", AssetFile.PreviewOptions.Error, info.Id);
                    }
                    return null;
                }
            }

            return AssetUtils.GetAssetDatabasePath(sourcePath) ?? sourcePath;
        }

        /// <summary>
        /// Derive the preview file path from a source asset path
        /// </summary>
        public static string DerivePreviewFile(string sourcePath)
        {
            return Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(sourcePath)), "preview.png");
        }
    }
}