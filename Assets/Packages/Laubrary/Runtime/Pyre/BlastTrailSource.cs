using UnityEngine;
using Laubrary.Chunks;

namespace Laubrary.Pyre
{
    /// Adapts a BlastSpec as a Chunk trail puff — spawn a short-lived Pyre blast (e.g. a two-layer fire→smoke
    /// blast) at a chunk's own position on a timer while it's flying. Pools its instances through the same
    /// PyreBlastPool every other Pyre effect uses, so a busy trail doesn't out-cost a normal blast.
    [CreateAssetMenu(menuName = "Laubrary/Pyre/Blast Trail Source", fileName = "BlastTrail")]
    public class BlastTrailSource : ScriptableObject, IChunkTrailSource
    {
        [Tooltip("The blast played for each puff.")]
        public BlastSpec spec;
        [Tooltip("Playback speed in frames per second.")]
        public float fps = 24f;

        public void SpawnPuff(Vector3 worldPos, int sortingOrder)
        {
            if (spec == null) return;
            var player = PyreBlastPool.Get();
            player.transform.position = worldPos;
            player.spec = spec;
            player.fps = fps;
            player.loop = false;
            var sr = player.GetComponent<SpriteRenderer>();
            if (sr != null) sr.sortingOrder = sortingOrder;

            void OnFinished()
            {
                player.Finished -= OnFinished;
                PyreBlastPool.Release(player);
            }
            player.Finished += OnFinished;
            player.Play();
        }
    }
}
