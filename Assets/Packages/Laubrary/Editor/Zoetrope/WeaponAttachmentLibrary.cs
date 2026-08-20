using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using Laubrary.Zoetrope;
using Laubrary.ZoetropeLaunimator;
using Laubrary.Launimator;
using Laubrary.Launimator.Editor;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// Discovery for a ZoeWeaponSlot's two "pick, don't type" fields — <see cref="ZoeWeaponSlot.attachToPartName"/>
    /// and <see cref="ZoeWeaponSlot.muzzleLayerId"/>. Scoped to ONE Zoe (the slot's own owner), not the whole
    /// project — a weapon SLOT is inherently Zoe-specific data now (moved off WeaponDef precisely so the SAME
    /// weapon asset stays reusable across characters; the per-Zoe wiring that used to force it onto one
    /// character lives here instead). Editor-only: reads Launimator types directly, which core Zoetrope
    /// (Zoe's own runtime asmdef) may not depend on.
    /// </summary>
    public static class WeaponAttachmentLibrary
    {
        /// Every named composite body part on THIS Zoe. A single-body Zoe returns an empty list — it has no
        /// named parts, attachToPartName has no meaning for it (the slot just uses the root).
        public static List<string> FindPartNames(Zoe zoe)
        {
            if (zoe == null || !(zoe.view is CompositeLauminaryView composite) || composite.parts == null)
                return new List<string>();
            return composite.parts.Where(p => p != null && !string.IsNullOrEmpty(p.name))
                .Select(p => p.name).OrderBy(n => n).ToList();
        }

        /// Every (layerId, mode) pair found on any Point- or Vector-mode MetaLayer, on any animation, on any
        /// LauminaryVersion reachable from THIS Zoe (single-body or composite alike). Shape-mode layers are
        /// excluded — they carry no single spawn point, so they can't drive a muzzle.
        public static List<(string id, MetaLayerMode mode)> FindMuzzleLayerCandidates(Zoe zoe)
        {
            var seen = new HashSet<(string, MetaLayerMode)>();
            if (zoe == null) return new List<(string, MetaLayerMode)>();
            foreach (var version in ReachableVersions(zoe))
            {
                if (version?.animations == null) continue;
                foreach (var anim in version.animations)
                {
                    if (anim?.metaLayers == null) continue;
                    foreach (var layer in anim.metaLayers)
                    {
                        if (layer == null || string.IsNullOrEmpty(layer.id)) continue;
                        if (layer.mode == MetaLayerMode.Point || layer.mode == MetaLayerMode.Vector)
                            seen.Add((layer.id, layer.mode));
                    }
                }
            }
            return seen.OrderBy(p => p.Item1).ThenBy(p => p.Item2).ToList();
        }

        /// The Lauminary + a reasonable landing clip name for a given part name (or "" for a single-body
        /// Zoe's own view) on THIS Zoe — what a "jump to paint this" button needs to open the Laumination
        /// Builder on the right asset. Lands on the version's FIRST animation (the button gets you to the
        /// right Lauminary; switching to the exact clip you want is one click on its own name button, same as
        /// the Builder's own animation-picker row already shows). Returns (null, null) if unresolvable.
        public static (Lauminary lauminary, string animName) ResolveLauminary(Zoe zoe, string partName)
        {
            if (zoe == null) return (null, null);
            LauminaryVersion version = null;
            if (!string.IsNullOrEmpty(partName) && zoe.view is CompositeLauminaryView composite && composite.parts != null)
            {
                var part = composite.parts.FirstOrDefault(p => p != null && p.name == partName);
                if (part?.view is ZonedLauminaryView zlv) version = zlv.version;
            }
            else if (zoe.view is ZonedLauminaryView single)
            {
                version = single.version;
            }
            if (version == null || version.animations == null || version.animations.Count == 0) return (null, null);

            string versionPath = AssetDatabase.GetAssetPath(version);
            var lauminary = LauminaryRepo.EnumerateLauminaries()
                .FirstOrDefault(c => versionPath.StartsWith(LauminaryRepo.FolderOf(c) + "/", System.StringComparison.Ordinal));
            return lauminary != null ? (lauminary, version.animations[0].name) : (null, null);
        }

        static IEnumerable<LauminaryVersion> ReachableVersions(Zoe zoe)
        {
            if (zoe.view is CompositeLauminaryView composite && composite.parts != null)
            {
                foreach (var p in composite.parts)
                    if (p?.view is ZonedLauminaryView zlv && zlv.version != null) yield return zlv.version;
            }
            else if (zoe.view is ZonedLauminaryView single && single.version != null)
            {
                yield return single.version;
            }
        }
    }
}
