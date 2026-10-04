using System.Collections.Generic;
using UnityEngine;
using Laubrary.GoreLab;
using Laubrary.Zoetrope;
using Laubrary.ZoetropeLaunimator;

namespace Laubrary.GoreLabDemo
{
    /// <summary>
    /// Stands the eight-direction imp in a grid, one imp per direction, each walking in place, and gives every one the gore body that lets it be wounded.
    /// The imp does not know about gore: the body is added to it here.
    /// </summary>
    public sealed class GoreLabDemoSpawner : MonoBehaviour
    {
        [Header("Content")]
        [Tooltip("The imp character to spawn.")]
        public Zoe imp;
        [Tooltip("The gore rig for the imp: where its head and torso are on every frame.")]
        public GoreRig rig;

        [Header("Layout")]
        [Tooltip("The direction each imp faces, in degrees (0 = away from the camera, 180 = toward it, clockwise). Listed from the bottom row up, left to right.")]
        public float[] headings = { 180f, 225f, 270f, 315f, 0f, 45f, 90f, 135f };
        [Tooltip("How many imps stand in one row.")]
        public int perRow = 4;
        [Tooltip("Distance between neighbouring imps, in world units.")]
        public Vector2 spacing = new Vector2(5f, 6.2f);
        [Tooltip("Where the bottom-left imp stands (its feet).")]
        public Vector2 origin = new Vector2(-7.5f, -5.2f);

        public readonly List<GoreBody> bodies = new List<GoreBody>();

        void Start()
        {
            if (imp == null || rig == null) { Debug.LogWarning("GoreLabDemoSpawner needs an imp and a rig.", this); return; }
            for (int i = 0; i < headings.Length; i++)
            {
                var pos = new Vector3(origin.x + (i % perRow) * spacing.x, origin.y + (i / perRow) * spacing.y, 0f);
                GameObject go = ZoeSpawner.SpawnCharacter(imp, pos, transform);
                go.name = "Imp " + headings[i] + "°";
                var pose = go.GetComponent<MotionPoseAnimator>();
                if (pose != null) pose.SetPoseOverride(MotionCondition.Always, headings[i]);
                var body = GoreBody.Attach(go, rig);
                bodies.Add(body);
            }
        }

        public void ResetAll()
        {
            foreach (var b in bodies) if (b != null) b.ResetWounds();
        }
    }
}
