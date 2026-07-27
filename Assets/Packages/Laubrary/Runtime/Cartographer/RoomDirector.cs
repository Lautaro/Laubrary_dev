using System;
using UnityEngine;

namespace Laubrary.Cartographer
{
    /// Drives play through a level's Rooms: advances the view, decides when a Room is done, and moves on.
    ///
    /// The whole thing is a pure function of (dt, player position) applied to its own state, exposed as
    /// `Tick` so it can be driven from a test or a cutscene exactly as Update drives it. That is deliberate —
    /// scroll behaviour is the kind of thing that is miserable to verify if the only way to run it is to enter
    /// play mode and watch.
    ///
    /// It owns no camera. `ViewCenter` is the answer; binding that to a Camera is the game's business (or
    /// `RoomCameraBinder`'s), so a Room can equally drive a cutscene, a minimap, or nothing at all.
    [RequireComponent(typeof(CartographerLevel))]
    public class RoomDirector : MonoBehaviour
    {
        [Tooltip("Half-width and half-height of the view, in world units. The catch-up rules measure against " +
                 "this, so it should match whatever the camera actually shows.")]
        public Vector2 viewExtents = new(8f, 4.5f);

        [Tooltip("Start the first room automatically on Awake.")]
        public bool playOnAwake = true;

        /// Which room is running, or -1 before the first / after the last.
        public int CurrentRoomIndex { get; private set; } = -1;

        /// Centre of the view in world space — what a camera should be looking at.
        public Vector3 ViewCenter { get; private set; }

        /// Seconds the current room has been running, and world units the view has advanced through it.
        public float RoomElapsed { get; private set; }
        public float RoomDistance { get; private set; }

        /// The game answers "is this room's enemy set dead yet" — Cartographer has no idea what an enemy is.
        /// Left null, a ClearEnemies room ends immediately rather than hanging forever.
        public Func<bool> RoomCleared;

        public event Action<int> RoomStarted;
        public event Action LevelFinished;
        /// Raised when the trailing edge kills the player (ScrollCatchUp.Kill). The game decides what dying means.
        public event Action PlayerCaught;

        CartographerLevel level;
        public CartographerLevel Level => level != null ? level : level = GetComponent<CartographerLevel>();

        bool running;
        Vector3 roomStartCenter;

        // Kill fires on the CROSSING, not every frame the player is behind the edge — otherwise a player who
        // stays there raises it once per tick and the game gets told it died dozens of times.
        bool caughtLatch;

        CartographerRoom Room =>
            Level != null && CurrentRoomIndex >= 0 && CurrentRoomIndex < Level.rooms.Count
                ? Level.rooms[CurrentRoomIndex] : null;

        void Awake() { if (playOnAwake) Play(); }

        /// Begin at the first room. Safe to call again to restart.
        public void Play()
        {
            CurrentRoomIndex = -1;
            running = true;
            Advance();
        }

        public void Stop() => running = false;

        void Advance()
        {
            CurrentRoomIndex++;
            RoomElapsed = 0f;
            RoomDistance = 0f;
            roomStartCenter = ViewCenter;
            caughtLatch = false;

            if (Room == null) { running = false; LevelFinished?.Invoke(); return; }
            RoomStarted?.Invoke(CurrentRoomIndex);
        }

        void Update()
        {
            if (!running) return;
            var player = PlayerPosition();
            Tick(Time.deltaTime, player);
        }

        /// Overridden by whoever knows where the player is. The base looks for a tagged Player once — a
        /// convenience, not a requirement, since `Tick` takes the position explicitly.
        protected virtual Vector3 PlayerPosition()
        {
            if (playerTransform == null)
            {
                var go = GameObject.FindGameObjectWithTag("Player");
                if (go != null) playerTransform = go.transform;
            }
            return playerTransform != null ? playerTransform.position : ViewCenter;
        }

        [Tooltip("Who the catch-up rules measure against. Found by the Player tag when left empty.")]
        public Transform playerTransform;

        /// Advance the view and the current room by `dt`, with the player at `playerPos`. Returns the position
        /// the player should be at afterwards — unchanged unless a catch-up rule pushed or locked them.
        public Vector3 Tick(float dt, Vector3 playerPos)
        {
            if (!running || Room == null) return playerPos;

            var room = Room;
            RoomElapsed += dt;

            // 1. move the view
            var axis = (Vector3)room.ScrollAxis;
            switch (room.scroll)
            {
                case RoomScroll.Auto:
                    var step = axis * (room.scrollSpeed * dt);
                    ViewCenter += step;
                    RoomDistance += step.magnitude;
                    break;

                case RoomScroll.PlayerPushed:
                    // The view only ever moves forward, and only as far as the player has pushed it.
                    float ahead = Vector3.Dot(playerPos - ViewCenter, axis);
                    float slack = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(viewExtents, axis)));
                    float push = ahead - slack;
                    if (push > 0f) { ViewCenter += axis * push; RoomDistance += push; }
                    break;

                case RoomScroll.Locked:
                    break;
            }

            // 2. apply the catch-up rule against the trailing edge
            playerPos = ApplyCatchUp(room, playerPos, axis);

            // 3. is the room done?
            if (IsRoomComplete(room)) Advance();

            return playerPos;
        }

        /// How far behind the view centre the player is allowed to be before the trailing edge acts.
        float TrailingSlack(Vector3 axis) => Mathf.Abs(Vector3.Dot(viewExtents, axis));

        Vector3 ApplyCatchUp(CartographerRoom room, Vector3 playerPos, Vector3 axis)
        {
            float slack = TrailingSlack(axis);
            float behind = -Vector3.Dot(playerPos - ViewCenter, axis);   // >0 = player is behind the centre
            float over = behind - slack;                                  // >0 = past the trailing edge
            if (over <= 0f) { caughtLatch = false; return playerPos; }

            switch (room.catchUp)
            {
                case ScrollCatchUp.Lock:
                case ScrollCatchUp.Push:
                    // Both pin the player to the edge; the difference is what the GAME does about being pinned
                    // (Push is where a crush check belongs — Cartographer will not guess at that).
                    return playerPos + axis * over;

                case ScrollCatchUp.Kill:
                    if (!caughtLatch) { caughtLatch = true; PlayerCaught?.Invoke(); }
                    return playerPos;

                case ScrollCatchUp.Wait:
                    // Give the distance back: the view waits rather than leaving anyone behind.
                    ViewCenter -= axis * over;
                    RoomDistance = Mathf.Max(0f, RoomDistance - over);
                    return playerPos;
            }
            return playerPos;
        }

        bool IsRoomComplete(CartographerRoom room)
        {
            switch (room.exit)
            {
                case RoomExit.Distance: return RoomDistance >= room.exitDistance;
                case RoomExit.Time:     return RoomElapsed >= room.exitSeconds;
                case RoomExit.ClearEnemies: return RoomCleared == null || RoomCleared();
            }
            return false;
        }
    }
}
