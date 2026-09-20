using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Laubrary.AssetKit.Editor
{
    /// Public (a shared library asset) vs Private (a copy embedded as a SUB-ASSET inside the owner's own
    /// .asset file) for ANY LauAsset type — the generalization of what T-0250/T-0371 built by hand for one
    /// hardcoded case (a Zoe's own ChunkSpec) in ZoetropeWindows.cs. A recipe often needs a bespoke Pyre,
    /// Chunks or SpriteFx stack that only that one recipe will ever use; a private copy keeps it out of the
    /// shared browser instead of cluttering every picker with one-off assets nobody can reuse.
    ///
    /// <para><b>Private needs no serialized flag.</b> A private asset is simply a sub-asset of the owner's
    /// file, and everything the shared browser walks — <see cref="AssetLibrary{T}.Enumerate"/> and
    /// <see cref="AssetLibraryUntyped.Enumerate"/>, both of which skip <c>AssetDatabase.IsSubAsset</c> — only
    /// ever surfaces a path's MAIN asset. So an embedded copy is invisible to every LauAsset picker of every
    /// type without any exclude-list, and "Public vs Private" is read straight off where the currently
    /// assigned asset physically lives.</para>
    ///
    /// <para><b>Undo contract (lifted verbatim from T-0371, which verified it live — do not "simplify" it).</b>
    /// Unity's Undo cannot safely reverse AssetDatabase work: undoing a RegisterCreatedObjectUndo destroys the
    /// in-memory object of an asset that is already on disk (a created .asset then loads as a DefaultAsset, a
    /// sub-asset vanishes from its file), redo brings back a copy that belongs to no file, and
    /// RemoveObjectFromAsset/DestroyImmediate are not recorded at all, so an older record can re-point a field
    /// at a destroyed object. So:
    /// <list type="bullet">
    /// <item>creating the library asset or the embedded sub-asset is never recorded — it is plain, permanent
    /// asset work, saved at once;</item>
    /// <item>nothing asset-backed is ever removed or destroyed by a switch — the asset a field leaves stays
    /// where it was (a public asset in the library, a private one inside the owner's file);</item>
    /// <item>the ONLY recorded step is re-pointing the one field, as its own named undo group.</item>
    /// </list>
    /// Every value any undo or redo record can put back is therefore a live, persistent asset. To keep
    /// repeated switching from piling up copies, each direction first looks for a copy it made earlier with
    /// identical tuning and re-points to that instead of making another.</para>
    /// </summary>
    public static class LauPrivateAsset
    {
        public const string PrivateSuffix = " (Private)";

        /// Can this object host embedded private assets? It must be a ScriptableObject that is the MAIN asset
        /// of a real file — a sub-asset cannot own sub-assets of its own, and an unsaved instance has no file
        /// to embed into. (So opening a private Pyre in PyreWindow and looking at ITS reference fields
        /// correctly offers no further private copies.)
        public static bool CanHost(Object owner)
        {
            if (owner == null || !(owner is ScriptableObject)) return false;
            string path = AssetDatabase.GetAssetPath(owner);
            return !string.IsNullOrEmpty(path) && AssetDatabase.IsMainAsset(owner);
        }

        /// Is <paramref name="asset"/> a private copy living inside <paramref name="owner"/>'s own file?
        public static bool IsPrivate(Object asset, Object owner) =>
            asset != null && owner != null && AssetDatabase.IsSubAsset(asset) &&
            AssetDatabase.GetAssetPath(asset) == AssetDatabase.GetAssetPath(owner);

        /// Is this asset embedded in SOMEONE's file (not necessarily this owner's)? Used for wording, never
        /// for a switch — a private copy is only ever managed from the owner it belongs to.
        public static bool IsEmbedded(Object asset) => asset != null && AssetDatabase.IsSubAsset(asset);

        /// <summary>Embed a private copy inside <paramref name="owner"/> and re-point the field at it.</summary>
        /// <param name="source">The asset whose tuning is carried over (CopySerialized). Null makes a blank
        /// one of <paramref name="concreteType"/> — the "nothing picked yet, give me a bespoke one" case.</param>
        /// <param name="concreteType">Only consulted when <paramref name="source"/> is null.</param>
        /// <param name="label">The FIELD's name, used in the copy's own name ("Floating Disc — Blast (Private)").</param>
        /// <param name="repoint">Assigns the result to the field — the one undoable step (see the contract above).</param>
        /// <param name="freshCopy">True for Clone: never reuse a spare, always embed a brand-new copy under a
        /// name no other sub-asset in the file is using.</param>
        public static Object MakePrivate(Object owner, Object source, Type concreteType, string label,
                                         Action<Object> repoint, bool freshCopy = false,
                                         string undoName = "Make Private")
        {
            if (!CanHost(owner)) return null;
            string ownerPath = AssetDatabase.GetAssetPath(owner);

            var type = source != null ? source.GetType() : concreteType;
            if (type == null || !typeof(ScriptableObject).IsAssignableFrom(type)) return null;

            var made = ScriptableObject.CreateInstance(type);
            if (made == null) return null;
            // CopySerialized also copies the source's own m_Name, so it runs BEFORE the name is set.
            if (source != null) EditorUtility.CopySerialized(source, made);
            made.name = PrivateNameFor(owner, label, type);
            if (freshCopy) made.name = UniqueSubAssetName(ownerPath, made.name);

            Object target = freshCopy ? null : FindSpare(owner, ownerPath, made);
            if (target != null)
                Object.DestroyImmediate(made);   // a never-persisted scratch copy, in no file and no undo record
            else
            {
                AssetDatabase.AddObjectToAsset(made, owner);
                target = made;
            }

            Repoint(repoint, target, undoName);
            EditorUtility.SetDirty(owner);
            AssetDatabase.SaveAssetIfDirty(owner);
            return target;
        }

        /// <summary>Promote an embedded private copy into a shared library asset of the same tuning, and
        /// re-point the field at it. The embedded copy STAYS in the owner's file, so an undo can always point
        /// the field back at it. Reuses a library asset an earlier promotion made for the same name when its
        /// tuning is identical, instead of piling up duplicates on repeated switching.</summary>
        /// <param name="fallbackFolder">Where to write it if no asset of this type exists anywhere yet —
        /// see <see cref="ResolveLibraryFolder"/>.</param>
        public static Object MakePublic(Object owner, Object current, string fallbackFolder, Action<Object> repoint)
        {
            if (current == null) return null;
            var type = current.GetType();
            string folder = ResolveLibraryFolder(type, fallbackFolder);

            string niceName = PublicNameFor(current.name);

            var made = ScriptableObject.CreateInstance(type);
            if (made == null) return null;
            EditorUtility.CopySerialized(current, made);   // before naming: it copies m_Name too
            made.name = niceName;

            string outcome;
            var target = FindLibraryCopy(folder, niceName, made);
            if (target != null)
            {
                Object.DestroyImmediate(made);   // a never-persisted scratch copy, in no file and no undo record
                outcome = "reused";
            }
            else
            {
                folder = AssetFolders.EnsureFolder(folder);
                string newPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{niceName}.asset");
                made.name = Path.GetFileNameWithoutExtension(newPath);
                AssetDatabase.CreateAsset(made, newPath);
                AssetDatabase.SaveAssetIfDirty(made);
                target = made;
                outcome = "saved as";
            }

            Repoint(repoint, target, "Make Public");
            if (owner != null)
            {
                EditorUtility.SetDirty(owner);
                AssetDatabase.SaveAssetIfDirty(owner);
            }
            Debug.Log($"[AssetKit] {niceName} made public ({outcome}): {AssetDatabase.GetAssetPath(target)}");
            return target;
        }

        /// <summary>Where a promoted asset of this type belongs: the type's own HOME folder, read off where
        /// its existing library assets already live — "put it where the rest of them are" is the only answer
        /// that stays right per type without every tool registering a folder of its own.</summary>
        /// <remarks>Shallowest wins, not most populated: a bulk import can easily leave more Pyres in
        /// <c>Assets/Pyre/Imported</c> than in <c>Assets/Pyre</c>, and the home folder is the one the
        /// sub-folders hang off, not whichever happens to be fullest. Ties go to the fuller folder, then
        /// alphabetically, so the answer never depends on enumeration order. <c>Assets</c> itself is only
        /// accepted when nothing of that type lives in a folder at all. The picker's own folder hint, then
        /// <c>Assets</c>, are the last resorts — reached only when the project has no asset of the type
        /// yet.</remarks>
        public static string ResolveLibraryFolder(Type concreteType, string fallbackFolder)
        {
            var counts = new Dictionary<string, int>();
            foreach (var existing in AssetLibraryUntyped.Enumerate(concreteType))
            {
                string dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(existing))?.Replace('\\', '/');
                if (string.IsNullOrEmpty(dir)) continue;
                counts.TryGetValue(dir, out int n);
                counts[dir] = n + 1;
            }

            string best = null;
            int bestDepth = int.MaxValue, bestCount = 0;
            foreach (var kv in counts)
            {
                if (kv.Key == "Assets" && counts.Count > 1) continue;
                int depth = kv.Key.Count(c => c == '/');
                if (depth > bestDepth) continue;
                if (depth == bestDepth && kv.Value < bestCount) continue;
                if (depth == bestDepth && kv.Value == bestCount &&
                    string.CompareOrdinal(kv.Key, best) >= 0) continue;
                best = kv.Key; bestDepth = depth; bestCount = kv.Value;
            }
            if (!string.IsNullOrEmpty(best)) return best;
            return string.IsNullOrEmpty(fallbackFolder) ? "Assets" : fallbackFolder;
        }

        /// The library name a private copy is promoted under: its own name with the "(Private)" marker (and
        /// any " 2"/" 3" a Clone added after it) taken off, so a promoted clone lands as "Blast", not
        /// "Owner — Blast (Private) 2".
        public static string PublicNameFor(string privateName)
        {
            var m = PrivateNamePattern.Match(privateName ?? string.Empty);
            return m.Success ? m.Groups[1].Value : privateName;
        }

        static readonly Regex PrivateNamePattern =
            new Regex("^(.*)" + Regex.Escape(PrivateSuffix) + "(?: \\d+)?$");

        /// The name a private copy of <paramref name="label"/> on <paramref name="owner"/> gets. Matches the
        /// name T-0371's hand-built Zoe/Chunks row produced, so an existing private ChunkSpec keeps being
        /// recognised as the spare for its own field after the port.
        public static string PrivateNameFor(Object owner, string label, Type type)
        {
            string stem = (label ?? string.Empty).Trim();
            if (stem.StartsWith("New ", StringComparison.Ordinal)) stem = stem.Substring(4);
            if (string.IsNullOrEmpty(stem)) stem = ObjectNames.NicifyVariableName(type != null ? type.Name : "Asset");
            return $"{owner.name} — {stem}{PrivateSuffix}";
        }

        // ── the one undoable step ───────────────────────────────────────────────────────────

        static void Repoint(Action<Object> repoint, Object target, string undoName)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            repoint?.Invoke(target);
            Undo.SetCurrentGroupName(undoName);
            Undo.CollapseUndoOperations(group);
        }

        // ── spare / duplicate reuse ─────────────────────────────────────────────────────────

        /// An embedded copy in the owner's file that nothing on the owner references, of the same type, with
        /// the wanted name and identical tuning — safe to point the field at instead of embedding another.
        static Object FindSpare(Object owner, string ownerPath, Object wanted)
        {
            var inUse = ReferencedObjects(owner);
            string json = TuningJson(wanted);
            var type = wanted.GetType();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ownerPath))
            {
                if (o == null || ReferenceEquals(o, owner)) continue;
                if (o.GetType() != type || !AssetDatabase.IsSubAsset(o)) continue;
                if (o.name != wanted.name || inUse.Contains(o)) continue;
                if (TuningJson(o) == json) return o;
            }
            return null;
        }

        /// A library asset in <paramref name="folder"/> named like the ones a promotion makes for this asset
        /// ("stem", "stem 1", "stem 2"…) whose tuning is identical to <paramref name="wanted"/>.
        static Object FindLibraryCopy(string folder, string stem, Object wanted)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return null;
            var type = wanted.GetType();
            var namePattern = new Regex("^" + Regex.Escape(stem) + "( \\d+)?$");
            string json = TuningJson(wanted);
            foreach (var guid in AssetDatabase.FindAssets("t:" + type.Name, new[] { folder }))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!namePattern.IsMatch(Path.GetFileNameWithoutExtension(assetPath))) continue;
                var candidate = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (candidate != null && candidate.GetType() == type && TuningJson(candidate) == json)
                    return candidate;
            }
            return null;
        }

        /// A name no sub-asset in <paramref name="ownerPath"/> is already using — Clone's guarantee that it
        /// produced a genuinely new copy rather than a same-named twin nobody can tell apart.
        static string UniqueSubAssetName(string ownerPath, string wanted)
        {
            var taken = new HashSet<string>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ownerPath))
                if (o != null) taken.Add(o.name);
            if (!taken.Contains(wanted)) return wanted;
            for (int i = 2; i < 1000; i++)
            {
                string candidate = $"{wanted} {i}";
                if (!taken.Contains(candidate)) return candidate;
            }
            return wanted;
        }

        /// Every object an ObjectReference field anywhere on <paramref name="owner"/> currently points at.
        static HashSet<Object> ReferencedObjects(Object owner)
        {
            var set = new HashSet<Object>();
            using (var so = new SerializedObject(owner))
            {
                var it = so.GetIterator();
                while (it.Next(true))
                {
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue != null)
                        set.Add(it.objectReferenceValue);
                }
            }
            return set;
        }

        /// An asset's tuning as JSON, minus the object's own name and hide flags, which differ between a
        /// library asset and an embedded copy of the same content. A mismatch only costs one extra copy.
        static string TuningJson(Object asset) => IdentityFields.Replace(EditorJsonUtility.ToJson(asset), "");

        static readonly Regex IdentityFields = new Regex(
            "\"(?:m_Name\"\\s*:\\s*\"(?:[^\"\\\\]|\\\\.)*\"|m_ObjectHideFlags\"\\s*:\\s*\\d+),?");
    }
}
