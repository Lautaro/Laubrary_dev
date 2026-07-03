// ZUIIconLibraryAsset.cs
// Stores a named set of icons — each with a user-defined string ID and a Texture2D.
// Referenced by ZUIStyleSheetAsset; icons are looked up at runtime via ZUI.FindIcon(id).

using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(menuName = "ZUI/Icon Library", fileName = "ZUIIconLibrary")]
public class ZUIIconLibraryAsset : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public string    id;               // user-defined lookup key
        public Texture2D texture;          // the baked/imported texture
        public string    sourceAssetPath;  // "Assets/Icons/star.svg" — for browser tracking
    }

    public List<Entry> icons = new List<Entry>();

    // ── Lookup ────────────────────────────────────────────────────────────────

    public Texture2D Find(string id)
    {
        for (int i = 0; i < icons.Count; i++)
            if (string.Equals(icons[i].id, id, StringComparison.Ordinal))
                return icons[i].texture;
        return null;
    }

    public int IndexOfId(string id)
    {
        for (int i = 0; i < icons.Count; i++)
            if (string.Equals(icons[i].id, id, StringComparison.Ordinal)) return i;
        return -1;
    }

    public int IndexOfSource(string assetPath)
    {
        for (int i = 0; i < icons.Count; i++)
            if (icons[i].sourceAssetPath == assetPath) return i;
        return -1;
    }

    public Entry FindBySource(string assetPath)
    {
        int i = IndexOfSource(assetPath);
        return i >= 0 ? icons[i] : null;
    }

#if UNITY_EDITOR
    // Create or update the entry for the given source path.
    // If another entry already holds the same ID it is replaced.
    public void Upsert(string id, Texture2D texture, string sourcePath)
    {
        // Remove stale entry sharing the same source path
        int si = IndexOfSource(sourcePath);
        if (si >= 0) icons.RemoveAt(si);

        // Remove stale entry sharing the same ID (different source)
        int ii = IndexOfId(id);
        if (ii >= 0) icons.RemoveAt(ii);

        icons.Add(new Entry { id = id, texture = texture, sourceAssetPath = sourcePath });
        EditorUtility.SetDirty(this);
    }

    public void RemoveBySource(string sourcePath)
    {
        int i = IndexOfSource(sourcePath);
        if (i >= 0) { icons.RemoveAt(i); EditorUtility.SetDirty(this); }
    }

    public void RemoveById(string id)
    {
        int i = IndexOfId(id);
        if (i >= 0) { icons.RemoveAt(i); EditorUtility.SetDirty(this); }
    }
#endif
}
