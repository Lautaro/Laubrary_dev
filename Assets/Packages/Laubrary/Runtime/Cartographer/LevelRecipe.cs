using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Cartographer
{
    /// One clump the generator may scatter, and how eagerly.
    [System.Serializable]
    public class ScatterEntry
    {
        [Tooltip("The clump to scatter. Must also be allowed by the biome, or it is skipped.")]
        public Clump clump;

        [Tooltip("Relative likelihood against the other entries.")]
        [Min(0f)] public float weight = 1f;

        [Tooltip("Roughly how many of these per 100 cells of level width.")]
        [Min(0f)] public float per100Cells = 4f;

        [Tooltip("Lowest row this may be placed on, measured from the ground surface.")]
        public int minHeight;

        [Tooltip("Highest row this may be placed on, measured from the ground surface.")]
        public int maxHeight = 6;
    }

    /// Seed + dials → a level. The same "deterministic from a seed and a handful of numbers" shape the track
    /// generator uses, ported onto the tile grid: `Generate(recipe, seed, level)` always produces exactly the
    /// same level for the same inputs, so a world map can hand out level seeds and get reproducible places.
    ///
    /// A recipe describes a KIND of level, not one level. It is an asset so several Sites can share "ruined
    /// street" without copying dials around.
    [CreateAssetMenu(menuName = "Laubrary/Cartographer/Level Recipe", fileName = "LevelRecipe")]
    public class LevelRecipe : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Name shown in browsers and pickers.")]
        public string displayName = "New Recipe";

        [Tooltip("Which tiles and clumps generation may draw from. A clump not allowed by the biome is never " +
                 "placed, however the dials are set.")]
        public CartographerBiome biome;

        [Header("Extent")]
        [Tooltip("Level width in cells.")]
        [Min(1)] public int width = 64;

        [Tooltip("Row the ground surface sits on when the terrain is flat.")]
        public int groundLevel;

        [Header("Ground")]
        [Tooltip("The clump laid end to end to make the ground. Its own width sets the step, so a wide strip " +
                 "clump makes fewer, larger placements.")]
        public Clump groundClump;

        [Tooltip("How much the ground surface wanders up and down, in cells. 0 is dead flat.")]
        [Min(0)] public int groundVariation = 2;

        [Tooltip("How often the ground is allowed to change height, as a chance per ground step.")]
        [Range(0f, 1f)] public float groundRoughness = 0.35f;

        [Header("Scatter")]
        [Tooltip("Clumps sprinkled across the level, each with its own density and height band.")]
        public List<ScatterEntry> scatter = new();

        [Tooltip("Cells of clearance kept between scattered clumps, so a level does not read as a pile.")]
        [Min(0)] public int scatterSpacing = 2;

        [Header("Rooms")]
        [Tooltip("Sections the generated level is divided into. Copied onto the level as-is; leave empty for " +
                 "one continuous space.")]
        public List<CartographerRoom> rooms = new();
    }
}
