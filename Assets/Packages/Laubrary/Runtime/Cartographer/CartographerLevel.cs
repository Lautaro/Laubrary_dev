using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Laubrary.Cartographer
{
    /// One named drawing layer of a level, bound to the Tilemap that renders it.
    [System.Serializable]
    public class LevelLayer
    {
        [Tooltip("Name clumps address this layer by, e.g. Terrain or Structures.")]
        public string layerName = CartographerLevel.TerrainLayer;

        [Tooltip("The Tilemap this layer draws into.")]
        public Tilemap tilemap;
    }

    /// The runtime side of a Cartographer level: the Grid, its named Tilemap layers, the Rooms play moves
    /// through, and the named spots placed content has published.
    ///
    /// Hand-authored levels and generated ones both end up here — that is the point of having one runtime
    /// representation. A generator fills this in from a seed; an authored level was saved with it already full.
    [RequireComponent(typeof(Grid))]
    public class CartographerLevel : MonoBehaviour
    {
        /// The layer name a cell targets when its author didn't say otherwise.
        public const string TerrainLayer = "Terrain";

        [Header("Layers")]
        [Tooltip("The level's drawing layers, in back-to-front order.")]
        public List<LevelLayer> layers = new();

        [Header("Rooms")]
        [Tooltip("The sections play moves through, in order.")]
        public List<CartographerRoom> rooms = new();

        readonly Dictionary<string, Vector3> spots = new();

        Grid grid;
        public Grid Grid => grid != null ? grid : grid = GetComponent<Grid>();

        /// The Tilemap registered under `layerName`, or null if this level has no such layer.
        public Tilemap GetLayer(string layerName)
        {
            if (string.IsNullOrEmpty(layerName) || layers == null) return null;
            for (int i = 0; i < layers.Count; i++)
                if (layers[i] != null && layers[i].layerName == layerName) return layers[i].tilemap;
            return null;
        }

        /// Publish a named point at a world position. Placement code calls this for every spot on every clump it
        /// stamps, so whoever wants "landing-pad" can find it without knowing which clump supplied it.
        /// A repeated name overwrites the earlier one — last placement wins.
        public void RegisterSpot(string spotName, Vector3 worldPosition)
        {
            if (string.IsNullOrEmpty(spotName)) return;
            spots[spotName] = worldPosition;
        }

        /// Look up a named point published by placed content.
        public bool TryGetSpot(string spotName, out Vector3 worldPosition)
        {
            worldPosition = default;
            return !string.IsNullOrEmpty(spotName) && spots.TryGetValue(spotName, out worldPosition);
        }

        /// Every spot name currently published, for tooling and for diagnosing a sequence that asked for a spot
        /// this level does not have.
        public IEnumerable<string> SpotNames => spots.Keys;

        /// Drop every published spot. Called before a level is rebuilt or regenerated.
        public void ClearSpots() => spots.Clear();

        /// Stamp a clump's cells into this level's layers, publishing its spots, and return the prefab choice
        /// that was rolled (null when the clump spawns nothing). `pick` picks an index from a weight list, which
        /// is how a generator keeps placement deterministic from its own seed; pass null to take the first
        /// non-zero-weight entry.
        public ClumpPrefabChoice PlaceClump(Clump clump, Vector2Int origin, System.Func<IList<float>, int> pick = null)
        {
            if (clump == null) return null;

            if (clump.cells != null)
            {
                foreach (var c in clump.cells)
                {
                    if (c == null || c.tile == null) continue;
                    var map = GetLayer(string.IsNullOrEmpty(c.layer) ? TerrainLayer : c.layer);
                    if (map == null) continue;
                    map.SetTile(new Vector3Int(origin.x + c.offset.x, origin.y + c.offset.y, 0), c.tile);
                }
            }

            if (clump.spots != null)
            {
                foreach (var s in clump.spots)
                {
                    if (s == null || string.IsNullOrEmpty(s.spotName)) continue;

                    // +0.5 puts the spot at the centre of its cell rather than the corner, so a spot authored at
                    // a whole-number offset lands where the tile looks like it is.
                    var cell = new Vector3(origin.x + s.offset.x + 0.5f, origin.y + s.offset.y + 0.5f, 0f);
                    var g = Grid;
                    RegisterSpot(s.spotName, g != null ? g.LocalToWorld(g.CellToLocalInterpolated(cell)) : cell);
                }
            }

            return ChoosePrefab(clump, pick);
        }

        static ClumpPrefabChoice ChoosePrefab(Clump clump, System.Func<IList<float>, int> pick)
        {
            var choices = clump.prefabChoices;
            if (choices == null || choices.Count == 0) return null;

            var weights = new List<float>(choices.Count);
            for (int i = 0; i < choices.Count; i++) weights.Add(choices[i] != null ? Mathf.Max(0f, choices[i].weight) : 0f);

            if (pick != null)
            {
                int i = pick(weights);
                return i >= 0 && i < choices.Count ? choices[i] : null;
            }

            for (int i = 0; i < choices.Count; i++) if (weights[i] > 0f) return choices[i];
            return null;
        }
    }
}
