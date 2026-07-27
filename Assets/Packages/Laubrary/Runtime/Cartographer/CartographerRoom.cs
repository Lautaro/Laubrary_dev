using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Cartographer
{
    /// What has to happen before a Room hands over to the next one.
    public enum RoomExit
    {
        /// Travel a set distance along the scroll axis.
        Distance,
        /// Hold out for a set time while the Room keeps populating itself.
        Time,
        /// Clear every enemy the Room spawned.
        ClearEnemies,
    }

    /// How the view moves through a Room. Deliberately independent of the exit condition — any exit can pair
    /// with any scroll, and the interesting rooms are the ones that mix them.
    public enum RoomScroll
    {
        /// The view advances on its own, regenerating the Room's content until the exit condition is met.
        Auto,
        /// The view stays put; the Room plays out in one screen.
        Locked,
        /// The player pushes the view along by walking into its edge.
        PlayerPushed,
    }

    /// What happens when an advancing view catches up with the player. Per-Room rather than global, because a
    /// corridor that shoves you forward and a boss approach that simply won't let you retreat want different
    /// answers to the same situation.
    public enum ScrollCatchUp
    {
        /// The player cannot leave the view. Nothing else happens. Safe default — no death by surprise.
        Lock,
        /// The trailing edge pushes the player along, and can crush them against solid geometry.
        Push,
        /// Touching the trailing edge kills.
        Kill,
        /// The view waits for the player to catch up. Honest option, but it stops being a forced scroll.
        Wait,
    }

    /// One placed section of a level: which Biome it draws from, when it ends, and how the view moves through it.
    ///
    /// Stored inline on the level rather than as its own asset. A Room is specific to the level it sits in, so
    /// making it an asset would fill the library with one-offs that are never reused — the same reason a bespoke
    /// cutscene path is authored inline instead of as a shared Choreography.
    [System.Serializable]
    public class CartographerRoom
    {
        [Header("Identity")]
        [Tooltip("Name for this section, shown in the level's inspector and in authoring tools.")]
        public string roomName = "Room";

        [Tooltip("Which biome's tiles and clumps this section is built from.")]
        public CartographerBiome biome;

        [Header("Exit")]
        [Tooltip("What ends this section and moves play into the next one.")]
        public RoomExit exit = RoomExit.Distance;

        [Tooltip("Distance to travel before the section ends, in world units. Used when Exit is Distance.")]
        [Min(0f)] public float exitDistance = 40f;

        [Tooltip("Seconds to survive before the section ends. Used when Exit is Time.")]
        [Min(0f)] public float exitSeconds = 20f;

        [Header("Scroll")]
        [Tooltip("How the view moves through this section.")]
        public RoomScroll scroll = RoomScroll.Auto;

        [Tooltip("View speed in world units per second. Used when Scroll is Auto.")]
        [Min(0f)] public float scrollSpeed = 3f;

        [Tooltip("Direction the view travels. Normalised on use; defaults to rightwards.")]
        public Vector2 scrollDirection = Vector2.right;

        [Tooltip("What happens when an advancing view catches up with the player.")]
        public ScrollCatchUp catchUp = ScrollCatchUp.Lock;

        [Header("Reserved space")]
        [Tooltip("Areas, in grid cells, that generation must leave traversable. Scripted sequences that need " +
                 "room to move claim their space here, so a procedurally dressed level can never wall one in.")]
        public List<RectInt> reservedAreas = new();

        /// Normalised scroll direction, falling back to rightwards if the authored value is zero.
        public Vector2 ScrollAxis =>
            scrollDirection.sqrMagnitude > 0.0001f ? scrollDirection.normalized : Vector2.right;

        /// True if the cell at `cell` lies inside any reserved area, and so must stay traversable.
        public bool IsReserved(Vector2Int cell)
        {
            if (reservedAreas == null) return false;
            for (int i = 0; i < reservedAreas.Count; i++)
                if (reservedAreas[i].Contains(cell)) return true;
            return false;
        }
    }
}
