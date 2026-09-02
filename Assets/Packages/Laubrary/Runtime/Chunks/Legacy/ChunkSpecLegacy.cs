// The shape a Chunk Spec had before it became a stack of capabilities: a fixed set of optional module slots,
// each a serializable object hanging off the spec, plus a timeline that scheduled them by name.
//
// Nothing here runs. These types exist ONLY so an asset authored against the old layout still deserializes,
// so the one-time upgrade can read what it said, and so a project that has not yet re-saved its assets loses
// nothing. They carry fields and no behaviour: every line of logic that used to live in them now lives in the
// capability that replaced it, and having it in one place is the point — two copies would drift and the
// migrated recipe would stop matching the asset it came from.
//
// They come out with the legacy fields on ChunkSpec, one release after the upgrade has run everywhere.
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Chunks
{
    /// Legacy: became a <see cref="PaletteSplash"/> capability.
    [System.Serializable]
    public class ParticleSplashModule
    {
        public bool enabled = false;
        public string layerName = "Splash";
        public Sprite sprite;
        public bool emitFromFootprint = true;
        public int countMin = 6;
        public int countMax = 14;
        public float sizePxMin = 1f;
        public float sizePxMax = 3f;
        public float speedMin = 1.5f;
        public float speedMax = 5f;
        public bool inheritBurstDirection = false;
        public float directionDeg = 90f;
        public float spreadDeg = 180f;
        public float gravity = 6f;
        public float drag = 0.4f;
        public float lifeMin = 0.15f;
        public float lifeMax = 0.4f;
        public AnimationCurve alphaOverLife;
        public int seed = 0;
    }

    /// Legacy: became a <see cref="PyreBlast"/> capability. A spec's extra blast groups were a list of these.
    [System.Serializable]
    public class PyreSpawnModule
    {
        public bool enabled = false;
        public string layerName = "Blast";
        public string label = "";
        public bool useFormation = false;
        public SpawnFormation formation = new SpawnFormation();
        public Object source;
        public List<Object> pool = new List<Object>();
        public Vector2 offset = Vector2.zero;
        public PyreSpawnRotation rotationMode = PyreSpawnRotation.InheritBurst;
        public float fixedAngleDeg = 0f;
        public float randomAngleMinDeg = 0f;
        public float randomAngleMaxDeg = 360f;
        public float scaleMin = 1f;
        public float scaleMax = 1f;
        public int seed = 0;
    }

    /// Legacy: became a <see cref="Trajectory"/> modifier.
    [System.Serializable]
    public class PyreMotionModule
    {
        public bool enabled = false;
        public float speedMin = 3f;
        public float speedMax = 7f;
        public bool inheritBurstDirection = true;
        public float directionDeg = 90f;
        public float spreadDeg = 20f;
        public float upwardBias = 0f;
        public float gravity = 20f;
        public float drag = 0.6f;
        public bool faceVelocity = false;
        public bool untilTargetEnds = true;
        public float lifeSeconds = 1f;
        public int seed = 0;
    }

    /// Legacy: became a <see cref="FragmentFracture"/> capability.
    [System.Serializable]
    public class FragmentSlicerModule
    {
        public bool enabled = false;
        public string layerName = "Fragments";
        public Object sourceVisual;
        public Sprite source;
        public int pieceCount = 3;
        public int seed = 0;
        public int minPieceAreaPx = 24;
        public float speedMin = 1.5f;
        public float speedMax = 3.5f;
        public bool useBurstDirection = true;
        public float directionDeg = 90f;
        public float spreadDeg = 180f;
        public float gravity = 8f;
        public float drag = 0.4f;
        public float angularSpeedMin = 30f;
        public float angularSpeedMax = 180f;
        public float lifeMin = 1.2f;
        public float lifeMax = 2f;
        public AnimationCurve alphaOverLife;
    }

    /// Legacy: a standalone placement slot that laid out the spec's single blast. It became a
    /// <see cref="PyreBlast"/> whose pattern is a Line or a Ring.
    [System.Serializable]
    public class SpawnFormationModule
    {
        public bool enabled = false;
        public string layerName = "Blast";
        public SpawnFormation formation = new SpawnFormation();
    }

    /// Legacy: one module's scheduled slot, keyed by the module's name. Per-capability absolute delays replaced
    /// the whole idea — a lane keyed by name could only ever schedule the fixed set of slots that had names.
    [System.Serializable]
    public class ChunkTimelineTrack
    {
        public string moduleName = "";
        public float delay = 0f;
    }

    /// Legacy: the schedule half became each capability's own <see cref="ChunkCapability.delay"/>; the marker
    /// half became a <see cref="Cues"/> capability, reading the very same serialized fields.
    [System.Serializable]
    public class ChunkTimeline
    {
        public bool enabled = false;
        public List<ChunkTimelineTrack> tracks = new List<ChunkTimelineTrack>();
        public List<ChunkCue> markers = new List<ChunkCue>();
        public float windowSeconds = 2f;

        /// The delay one named module slot was scheduled at, or 0 when the timeline was off or silent about
        /// it. Read only by the upgrade, which is why it survives the rest of the class's behaviour.
        public float DelayFor(string moduleName)
        {
            if (!enabled || tracks == null || string.IsNullOrEmpty(moduleName)) return 0f;
            for (int i = 0; i < tracks.Count; i++)
            {
                var t = tracks[i];
                if (t != null && t.moduleName == moduleName) return Mathf.Max(0f, t.delay);
            }
            return 0f;
        }
    }
}
