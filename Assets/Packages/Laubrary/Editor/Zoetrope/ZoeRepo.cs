using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Laubrary.Zoetrope;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// The single owner of the on-disk zoe layout. A zoe is a folder
    /// <c>Zoes/&lt;Name&gt;_&lt;shortId&gt;/</c> holding the root <c>&lt;Name&gt;.asset</c>
    /// (<see cref="Zoe"/> identity + bookkeeping), an editable <c>draft/</c> version, and immutable
    /// committed snapshots <c>v1/ v2/ …</c>. Each version folder holds a clearly-named version asset
    /// (<c>&lt;Name&gt;_draft.asset</c> / <c>&lt;Name&gt;_v2.asset</c>) plus its generated atlases/clips/prefab/
    /// controller. All version assets are REGENERATED from animation recipes via
    /// <see cref="ZoeBuilder.BuildVersionAssets"/> (never CopyAsset). Nothing else should touch the
    /// folder structure directly.
    /// </summary>
    public static class ZoeRepo
    {
        public const string Root = ZoeBuilder.RootFolder;
        private const string DraftLeaf = "draft";
        private static readonly Regex VersionLeaf = new Regex(@"^v(\d+)$", RegexOptions.Compiled);

        // ── identity / creation ──────────────────────────────────────────────
        public static Zoe CreateZoe(string displayName, float ppu = 16f)
        {
            ZoeBuilder.EnsureFolder(Root);
            string id = Guid.NewGuid().ToString("N");
            string safe = ZoeBuilder.Sanitize(displayName);
            string folder = $"{Root}/{safe}_{id.Substring(0, 8)}";
            ZoeBuilder.EnsureFolder(folder);

            var c = ScriptableObject.CreateInstance<Zoe>();
            c.zoeId = id;
            c.zoeName = string.IsNullOrWhiteSpace(displayName) ? "NewZoe" : displayName.Trim();
            c.latestVersion = 0;
            c.pixelsPerUnit = ppu;
            AssetDatabase.CreateAsset(c, $"{folder}/{safe}.asset");

            EnsureDraft(c); // empty draft so the structure is valid immediately

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return c;
        }

        public static List<Zoe> EnumerateZoes()
        {
            var list = new List<Zoe>();
            if (!AssetDatabase.IsValidFolder(Root)) return list;
            foreach (var guid in AssetDatabase.FindAssets("t:Zoe", new[] { Root }))
            {
                var c = AssetDatabase.LoadAssetAtPath<Zoe>(AssetDatabase.GUIDToAssetPath(guid));
                if (c != null) list.Add(c);
            }
            return list.OrderBy(c => c.zoeName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string FolderOf(Zoe c)
        {
            string p = AssetDatabase.GetAssetPath(c);
            return string.IsNullOrEmpty(p) ? null : Path.GetDirectoryName(p).Replace('\\', '/');
        }

        // ── version folders ──────────────────────────────────────────────────
        public static string DraftFolder(Zoe c) => $"{FolderOf(c)}/{DraftLeaf}";
        public static string VersionFolder(Zoe c, int versionNumber)
            => versionNumber <= 0 ? DraftFolder(c) : $"{FolderOf(c)}/v{versionNumber}";

        /// <summary>The draft version asset, created (empty + built) if missing.</summary>
        public static ZoeVersion EnsureDraft(Zoe c)
        {
            string folder = DraftFolder(c);
            string correctPath = $"{folder}/{VersionAssetName(c.zoeName, 0)}.asset";
            // Load by the KNOWN path first: FindAssets does not reliably see assets created earlier in the
            // same synchronous call, which would make us recreate (and overwrite) the draft and lose work.
            var v = AssetDatabase.LoadAssetAtPath<ZoeVersion>(correctPath) ?? FindVersionIn(folder);
            if (v != null) return v;

            // GUARD against creating an empty "shadow" draft over a real one. A draft asset can transiently
            // fail to load (the script-less-version load hiccup); if any .asset already exists in the folder,
            // force-reimport and retry rather than overwriting the user's draft with a fresh empty one.
            if (AssetDatabase.IsValidFolder(folder))
            {
                var existing = Directory.GetFiles(folder, "*.asset", SearchOption.TopDirectoryOnly);
                if (existing.Length > 0)
                {
                    foreach (var f in existing) AssetDatabase.ImportAsset(f.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
                    v = AssetDatabase.LoadAssetAtPath<ZoeVersion>(correctPath) ?? FindVersionIn(folder);
                    if (v != null) return v;
                    throw new InvalidOperationException(
                        $"A draft asset exists in '{folder}' but could not be loaded; refusing to replace it with an " +
                        "empty draft (that would lose work). Run Tools/Laubrary/Zoetrope/Repair Zoe Assets.");
                }
            }

            ZoeBuilder.EnsureFolder(folder);
            v = ScriptableObject.CreateInstance<ZoeVersion>();
            v.versionNumber = 0;
            v.createdUtc = NowUtc();
            AssetDatabase.CreateAsset(v, correctPath);
            ZoeBuilder.BuildVersionAssets(v, folder, c.zoeName, c.pixelsPerUnit);
            return v;
        }

        /// <summary>
        /// Make a zoe's DRAFT durable: if the draft asset is script-less (old <c>m_Script {fileID: 0}</c>
        /// assets lose their recipes when edited-then-reloaded) or its file name no longer matches the zoe
        /// (so it only path-loads via the fragile fallback), re-create it as a FRESH asset — which gets a real
        /// script reference — at the name-matching path, preserving its animations. Returns true if it healed.
        /// Never destroys data it cannot reload first.
        /// </summary>
        public static bool HealDraft(Zoe c)
        {
            string folder = DraftFolder(c);
            if (!AssetDatabase.IsValidFolder(folder)) return false;
            string correctPath = $"{folder}/{VersionAssetName(c.zoeName, 0)}.asset";

            var draft = AssetDatabase.LoadAssetAtPath<ZoeVersion>(correctPath) ?? FindVersionIn(folder);
            if (draft == null) return false; // nothing on disk to heal (EnsureDraft will mint a fresh one)

            string curPath = AssetDatabase.GetAssetPath(draft);
            bool misnamed = !string.Equals(curPath, correctPath, StringComparison.OrdinalIgnoreCase);
            if (!misnamed && !IsScriptLess(curPath)) return false; // already durable + correctly named

            // Preserve the editable source-of-truth (recipes survive the asset deletion as managed objects).
            var anims = draft.animations.Select(CopyAnimation).ToList();
            var prefab = draft.prefab; var controller = draft.controller;
            string created = string.IsNullOrEmpty(draft.createdUtc) ? NowUtc() : draft.createdUtc;

            if (!string.IsNullOrEmpty(curPath)) AssetDatabase.DeleteAsset(curPath);
            if (misnamed) AssetDatabase.DeleteAsset(correctPath); // drop any empty shadow sitting at the right name

            var fresh = ScriptableObject.CreateInstance<ZoeVersion>();
            fresh.versionNumber = 0; fresh.createdUtc = created;
            fresh.animations = anims; fresh.prefab = prefab; fresh.controller = controller;
            AssetDatabase.CreateAsset(fresh, correctPath);
            ZoeBuilder.BuildVersionAssets(fresh, folder, c.zoeName, c.pixelsPerUnit);
            EditorUtility.SetDirty(fresh);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return true;
        }

        /// <summary>True if the asset file serializes with no script reference (<c>m_Script {fileID: 0}</c>),
        /// i.e. the fragile script-less form. Reads only the small YAML header.</summary>
        private static bool IsScriptLess(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            try
            {
                foreach (var line in File.ReadLines(assetPath))
                    if (line.Contains("m_Script:")) return line.Contains("{fileID: 0}");
            }
            catch { /* unreadable → treat as not-script-less; healing is best-effort */ }
            return false;
        }

        public static ZoeVersion LoadVersion(Zoe c, int versionNumber)
        {
            if (versionNumber <= 0) return EnsureDraft(c);
            string folder = VersionFolder(c, versionNumber);
            return AssetDatabase.LoadAssetAtPath<ZoeVersion>($"{folder}/{VersionAssetName(c.zoeName, versionNumber)}.asset")
                   ?? FindVersionIn(folder);
        }

        /// <summary>Committed version numbers (1..N), numerically sorted. Excludes the draft.</summary>
        public static List<int> ListCommittedVersions(Zoe c)
        {
            var nums = new List<int>();
            string folder = FolderOf(c);
            if (folder == null) return nums;
            foreach (var sub in AssetDatabase.GetSubFolders(folder))
            {
                var m = VersionLeaf.Match(Path.GetFileName(sub));
                if (m.Success) nums.Add(int.Parse(m.Groups[1].Value));
            }
            nums.Sort();
            return nums;
        }

        // ── draft animation authoring ────────────────────────────────────────
        /// <summary>Add or overwrite (by name) an animation on the draft, then rebuild the draft's assets.</summary>
        public static void SaveAnimationToDraft(Zoe c, AnimationDef def)
        {
            if (def == null || string.IsNullOrWhiteSpace(def.name))
                throw new ArgumentException("Animation needs a name.", nameof(def));

            var draft = EnsureDraft(c);
            int idx = draft.animations.FindIndex(a => NameEq(a.name, def.name));
            if (idx >= 0) draft.animations[idx] = def;
            else draft.animations.Add(def);

            RebuildDraft(c, draft);
        }

        public static void RemoveAnimationFromDraft(Zoe c, string name)
        {
            var draft = EnsureDraft(c);
            if (draft.animations.RemoveAll(a => NameEq(a.name, name)) > 0) RebuildDraft(c, draft);
        }

        public static AnimationDef GetDraftAnimation(Zoe c, string name)
            => EnsureDraft(c).animations.FirstOrDefault(a => NameEq(a.name, name));

        /// <summary>Rename a draft animation in place (one rebuild). No-op if unchanged or missing; throws if the
        /// new name is blank or already taken by another animation.</summary>
        public static void RenameDraftAnimation(Zoe c, string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("Animation needs a name.", nameof(newName));
            if (NameEq(oldName, newName)) return;

            var draft = EnsureDraft(c);
            if (draft.animations.Any(a => NameEq(a.name, newName)))
                throw new InvalidOperationException($"An animation named '{newName}' already exists.");

            var def = draft.animations.FirstOrDefault(a => NameEq(a.name, oldName));
            if (def == null) return;
            def.name = newName;
            RebuildDraft(c, draft);
        }

        /// <summary>Set a draft animation's playback FPS and rebuild (regenerates its clip at the new rate). No-op
        /// if the animation is missing or the value is unchanged. Clamped to a sane range.</summary>
        public static void SetDraftAnimationFps(Zoe c, string name, float fps)
        {
            var draft = EnsureDraft(c);
            var def = draft.animations.FirstOrDefault(a => NameEq(a.name, name));
            if (def == null) return;
            fps = Mathf.Clamp(fps, 0.1f, 120f);
            if (Mathf.Approximately(def.fps, fps)) return;
            def.fps = fps;
            RebuildDraft(c, draft);
        }

        /// <summary>Duplicate a draft animation under a fresh unique name (e.g. "Run copy"). Returns the new
        /// name, or null if the source wasn't found.</summary>
        public static string DuplicateDraftAnimation(Zoe c, string name)
        {
            var draft = EnsureDraft(c);
            var src = draft.animations.FirstOrDefault(a => NameEq(a.name, name));
            if (src == null) return null;

            var copy = CopyAnimation(src);
            copy.name = UniqueDraftName(draft, $"{name} copy");
            draft.animations.Add(copy);
            RebuildDraft(c, draft);
            return copy.name;
        }

        private static string UniqueDraftName(ZoeVersion draft, string baseName)
        {
            string candidate = baseName;
            int n = 2;
            while (draft.animations.Any(a => NameEq(a.name, candidate))) candidate = $"{baseName} {n++}";
            return candidate;
        }

        private static void RebuildDraft(Zoe c, ZoeVersion draft)
        {
            draft.createdUtc = NowUtc();
            // Self-containment: re-own any cell still sourced from an external rip sheet into draft/Source/ BEFORE
            // baking, so the bake (and all future edits) read the zoe's own pixels — survives losing the sheet.
            int reowned = ZoeSources.Detach(draft, DraftFolder(c), c.zoeName, strict: false);
            if (reowned > 0) Debug.Log($"Zoetrope: '{c.zoeName}' draft owns {reowned} more frame(s) — detached from source sheet(s).");
            ZoeBuilder.BuildVersionAssets(draft, DraftFolder(c), c.zoeName, c.pixelsPerUnit);
            EditorUtility.SetDirty(draft);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // ── commit a new immutable version ───────────────────────────────────
        /// <summary>Snapshot the draft into <c>v{latest+1}/</c> by regenerating from its recipes. Returns the
        /// new version number.</summary>
        public static int CommitNewVersion(Zoe c)
        {
            var draft = EnsureDraft(c);
            if (draft.animations.Count == 0)
                throw new InvalidOperationException("Draft has no animations to snapshot.");

            int n = Mathf.Max(0, c.latestVersion) + 1;
            string folder = VersionFolder(c, n);
            ZoeBuilder.EnsureFolder(folder);

            var snap = ScriptableObject.CreateInstance<ZoeVersion>();
            snap.versionNumber = n;
            snap.createdUtc = NowUtc();
            snap.animations = draft.animations.Select(CopyAnimation).ToList();
            AssetDatabase.CreateAsset(snap, $"{folder}/{VersionAssetName(c.zoeName, n)}.asset");

            // A committed snapshot MUST be fully self-contained: re-own every frame into vN/Source/ (strict — a
            // missing source is a hard error here, not a silent skip). Then it survives losing the draft AND sheet.
            ZoeSources.Detach(snap, folder, c.zoeName, strict: true);
            ZoeBuilder.BuildVersionAssets(snap, folder, c.zoeName, c.pixelsPerUnit);

            c.latestVersion = n;
            EditorUtility.SetDirty(c);
            EditorUtility.SetDirty(snap);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return n;
        }

        // ── lifecycle ────────────────────────────────────────────────────────
        public static Zoe Duplicate(Zoe src, string newDisplayName)
        {
            var dst = CreateZoe(newDisplayName, src.pixelsPerUnit);

            var dstDraft = EnsureDraft(dst);
            dstDraft.animations = EnsureDraft(src).animations.Select(CopyAnimation).ToList();
            ZoeBuilder.BuildVersionAssets(dstDraft, DraftFolder(dst), dst.zoeName, dst.pixelsPerUnit);

            foreach (int n in ListCommittedVersions(src))
            {
                var srcVer = LoadVersion(src, n);
                if (srcVer == null) continue;
                string folder = VersionFolder(dst, n);
                ZoeBuilder.EnsureFolder(folder);
                var snap = ScriptableObject.CreateInstance<ZoeVersion>();
                snap.versionNumber = n;
                snap.createdUtc = srcVer.createdUtc;
                snap.animations = srcVer.animations.Select(CopyAnimation).ToList();
                AssetDatabase.CreateAsset(snap, $"{folder}/{VersionAssetName(dst.zoeName, n)}.asset");
                ZoeBuilder.BuildVersionAssets(snap, folder, dst.zoeName, dst.pixelsPerUnit);
            }

            dst.latestVersion = src.latestVersion;
            EditorUtility.SetDirty(dst);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return dst;
        }

        /// <summary>Change the display name and rename the asset files to match (the folder stays frozen).</summary>
        public static void Rename(Zoe c, string newDisplayName)
        {
            if (string.IsNullOrWhiteSpace(newDisplayName)) return;
            string newName = newDisplayName.Trim();
            string safe = ZoeBuilder.Sanitize(newName);

            // Rename the FILES first, then set the name field LAST. RenameAsset triggers a reimport that reloads
            // `c` from disk — if we set c.zoeName BEFORE renaming, that reload discards the change, leaving
            // the file renamed but the name field reverted to the old value. LoadVersion then builds version
            // paths from the (wrong) c.zoeName and can't find the renamed version assets → the zoe's
            // animations vanish. The version/prefab/controller renames below use the local `newName`/`safe`, so
            // they don't depend on c.zoeName and are unaffected by the reload.
            AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(c), safe);

            // Per-version prefab + controller (those are normal assets and rename safely). We deliberately do
            // NOT rename the ZoeVersion assets themselves: they serialize with m_Script {fileID: 0}
            // (ZoeVersion shares Zoe.cs, so it has no MonoScript of its own), and renaming such
            // an asset makes Unity unable to re-resolve its type on the rename reimport — the whole version, and
            // thus the zoe's animations, become unloadable. Their file names don't matter: FindVersionIn
            // loads them by path enumeration regardless of name. The names just drift from the zoe (cosmetic).
            var folders = new List<string> { DraftFolder(c) };
            folders.AddRange(ListCommittedVersions(c).Select(n => VersionFolder(c, n)));
            foreach (var folder in folders)
            {
                RenameFirstInFolder(folder, "t:GameObject", safe);
                RenameFirstInFolder(folder, "t:AnimatorController", safe);
            }

            // Now persist the new display name (survives because no further RenameAsset reload follows).
            c.zoeName = newName;
            EditorUtility.SetDirty(c);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>Delete the entire zoe folder (all versions). Irreversible — caller must confirm.</summary>
        public static void Delete(Zoe c)
        {
            string folder = FolderOf(c);
            if (!string.IsNullOrEmpty(folder)) AssetDatabase.DeleteAsset(folder);
            AssetDatabase.Refresh();
        }

        // ── helpers ──────────────────────────────────────────────────────────
        private static string VersionAssetName(string charName, int versionNumber)
        {
            string safe = ZoeBuilder.Sanitize(charName);
            return versionNumber <= 0 ? $"{safe}_draft" : $"{safe}_v{versionNumber}";
        }

        /// <summary>The ZoeVersion asset directly inside <paramref name="folder"/> (not a subfolder), or null.
        /// Enumerates the folder's own <c>.asset</c> files by PATH and load-checks each, rather than using a
        /// <c>t:ZoeVersion</c> search — that search index misses script-less ScriptableObjects (these
        /// version assets serialize with <c>m_Script {fileID: 0}</c>), so it would return null for a version
        /// asset whose file name no longer matches the zoe (e.g. after a rename), making it invisible.</summary>
        private static ZoeVersion FindVersionIn(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return null;
            foreach (var file in Directory.GetFiles(folder, "*.asset", SearchOption.TopDirectoryOnly))
            {
                string p = file.Replace('\\', '/');
                if (!p.EndsWith(".asset")) continue; // guard the legacy GetFiles extension-prefix quirk
                var v = AssetDatabase.LoadAssetAtPath<ZoeVersion>(p);
                if (v != null) return v;
            }
            return null;
        }

        private static void RenameFirstInFolder(string folder, string typeFilter, string newName)
        {
            foreach (var guid in AssetDatabase.FindAssets(typeFilter, new[] { folder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(p).Replace('\\', '/') == folder)
                {
                    AssetDatabase.RenameAsset(p, newName);
                    return;
                }
            }
        }

        /// <summary>A deep copy of an animation's editable data (recipe). Baked frames/atlas/clip are NOT
        /// copied — they are regenerated by <see cref="ZoeBuilder.BuildVersionAssets"/> into the target
        /// folder. Used for snapshots, duplication, and including orphaned animations.</summary>
        public static AnimationDef CopyAnimation(AnimationDef d) => new AnimationDef
        {
            name = d.name,
            fps = d.fps,
            recipe = d.recipe.Select(f => new FrameRef { sourceTextureGuid = f.sourceTextureGuid, cell = f.cell, pivot = f.pivot, transform = f.transform }).ToList(),
            events = d.events != null ? d.events.Select(e => new FrameEvent { frame = e.frame, name = e.name }).ToList() : new List<FrameEvent>(),
            sourceTextureGuid = d.sourceTextureGuid,
            bgKeyEnabled = d.bgKeyEnabled,
            bgKey = d.bgKey,
            bgKeyTolerance = d.bgKeyTolerance,
            fixedFrame = d.fixedFrame,
            frameWidth = d.frameWidth,
            frameHeight = d.frameHeight,
            framePivot = d.framePivot,
            zonesEnabled = d.zonesEnabled,
            zones = d.zones != null
                ? d.zones.Select(z => new AnimZone { name = z.name, startFrame = z.startFrame, endFrame = z.endFrame, behavior = z.behavior }).ToList()
                : new List<AnimZone>(),
            metaLayersEnabled = d.metaLayersEnabled,
            metaLayers = d.metaLayers != null
                ? d.metaLayers.Select(ml => new MetaLayer { id = ml.id, color = ml.color, frames = ml.frames.Select(mf => mf.Clone()).ToList() }).ToList()
                : new List<MetaLayer>(),
            asepriteSourcePath = d.asepriteSourcePath,
        };

        private static bool NameEq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        private static string NowUtc() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
    }
}
