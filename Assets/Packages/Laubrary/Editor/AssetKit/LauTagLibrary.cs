using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Laubrary.AssetKit.Editor
{
    /// A fresh, independent implementation inspired by Zounds' tag system — NOT shared code, deliberately.
    /// HH2 depends on an older, not-yet-Laubrary Zounds that must never be touched; this exists purely so the
    /// rest of Laubrary (LauAssets) can be tagged/filtered without coupling to that. Same core semantics as
    /// Zounds (Tag{id,name}, "key:value" is an opaque string with ':' a pure UI convention, unique by name),
    /// one deliberate deviation: tags are stored in a GUID-keyed side table here, not a field on each tagged
    /// object — LauAssets are editor-authoring data, not runtime game data, so annotating by GUID means zero
    /// existing LauAsset type needs to change to become taggable (fully additive, per the plan this follows).
    [Serializable]
    public class LauTag
    {
        public int id;
        public string name;
    }

    [Serializable]
    class AssetTagEntry
    {
        public string guid;
        public List<int> tagIds = new List<int>();
    }

    [CreateAssetMenu(menuName = "Laubrary/AssetKit/LauTag Library", fileName = "LauTagLibrary")]
    public class LauTagLibrary : ScriptableObject
    {
        [SerializeField] List<LauTag> tags = new List<LauTag>();
        [SerializeField] List<AssetTagEntry> assetTags = new List<AssetTagEntry>();
        [SerializeField] int nextId = 1;

        public IReadOnlyList<LauTag> Tags => tags;

        public LauTag FindTag(string name) => tags.FirstOrDefault(t => t.name == name);

        /// Unique by name (matches Zounds' own CreateNewTag enforcement) — returns the existing tag if the
        /// name's already taken instead of creating a duplicate.
        public LauTag GetOrCreateTag(string name)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name)) return null;
            var existing = FindTag(name);
            if (existing != null) return existing;
            var t = new LauTag { id = nextId++, name = name };
            tags.Add(t);
            return t;
        }

        public void DeleteTag(int tagId)
        {
            tags.RemoveAll(t => t.id == tagId);
            foreach (var entry in assetTags) entry.tagIds.Remove(tagId);
        }

        public List<int> GetTagIds(string guid)
        {
            var entry = assetTags.FirstOrDefault(e => e.guid == guid);
            return entry != null ? new List<int>(entry.tagIds) : new List<int>();
        }

        public void SetTagIds(string guid, List<int> ids)
        {
            var entry = assetTags.FirstOrDefault(e => e.guid == guid);
            if (ids == null || ids.Count == 0)
            {
                if (entry != null) assetTags.Remove(entry);
                return;
            }
            if (entry == null) { entry = new AssetTagEntry { guid = guid }; assetTags.Add(entry); }
            entry.tagIds = new List<int>(ids);
        }
    }
}
