using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Launimator
{
    /// <summary>
    /// A named, organised set of sprites sliced from one source sheet — Launimator's answer to "construct a sprite
    /// sheet and name every sprite", for sprites that don't need to animate. It is a plain runtime asset: the game
    /// looks a sprite up by name (<see cref="Get"/>), and the editor tools (and the Laumination Builder) can pick
    /// sprites FROM it, so one catalog doubles as a shareable sprite source. The sprites are real sub-assets of the
    /// source sheet (sliced via the same registration pipeline as animations), so this catalog carries only names +
    /// references.
    /// </summary>
    [CreateAssetMenu(menuName = "Laubrary/Launimator/Sprite Catalog", fileName = "SpriteCatalog")]
    public class SpriteCatalog : ScriptableObject
    {
        [Tooltip("The source sheet these sprites are sliced from (for re-editing).")]
        public Texture2D sheet;

        [Tooltip("Pixels-per-unit used when slicing the sheet.")]
        public float pixelsPerUnit = 16f;

        [Tooltip("The named sprites in this catalog.")]
        public List<CatalogEntry> entries = new List<CatalogEntry>();

        Dictionary<string, Sprite> _lookup;

        /// <summary>The sprite named <paramref name="spriteName"/>, or null.</summary>
        public Sprite Get(string spriteName)
        {
            EnsureLookup();
            return spriteName != null && _lookup.TryGetValue(spriteName, out var s) ? s : null;
        }

        public bool Contains(string spriteName) { EnsureLookup(); return spriteName != null && _lookup.ContainsKey(spriteName); }

        public IReadOnlyList<CatalogEntry> Entries => entries;

        /// <summary>Call after the catalog's entries change (the editor does this on slice).</summary>
        public void InvalidateLookup() => _lookup = null;

        void EnsureLookup()
        {
            if (_lookup != null) return;
            _lookup = new Dictionary<string, Sprite>();
            foreach (var e in entries)
                if (e != null && e.sprite != null && !string.IsNullOrEmpty(e.name))
                    _lookup[e.name] = e.sprite;
        }

        void OnEnable() => _lookup = null;
    }

    /// <summary>One named sprite in a <see cref="SpriteCatalog"/>: its name, the sliced sprite, and the source
    /// cell it came from (texture px, bottom-left origin) plus its registration pivot, for re-slicing.</summary>
    [System.Serializable]
    public class CatalogEntry
    {
        public string name;
        public Sprite sprite;
        public Rect cell;
        public Vector2 pivot = new Vector2(0.5f, 0.5f);
    }
}
