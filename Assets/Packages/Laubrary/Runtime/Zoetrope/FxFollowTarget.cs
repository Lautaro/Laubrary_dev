using System;
using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// <summary>Attached to a spawned FX instance when its <see cref="FxEntry.follow"/> is true — re-samples
    /// the placement source every frame and moves the instance with it (a shockwave tracking a moving Point
    /// marker, rather than one fire-and-forget spawn). Self-contained: destroy/pool-release the followed
    /// object as normal and this component goes with it, nothing else to clean up.</summary>
    public class FxFollowTarget : MonoBehaviour
    {
        Func<Vector3> _sample;

        public void Init(Func<Vector3> sample) => _sample = sample;

        void LateUpdate()
        {
            if (_sample != null) transform.position = _sample();
        }
    }
}
