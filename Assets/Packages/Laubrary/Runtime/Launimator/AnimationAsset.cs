using UnityEngine;

namespace Laubrary.Launimator
{
    /// <summary>
    /// A standalone "orphaned" animation: an <see cref="AnimationDef"/> that does not yet belong to any
    /// reel. Authored in the Animation Builder (opened standalone) and stored under
    /// <c>Assets/Launimator/Animations/</c>. The Reel Browser can later INCLUDE one into a
    /// reel's draft (copying its recipe). Orphans carry only the editable recipe — they are not baked
    /// into clips/atlases until included in a reel version.
    /// </summary>
    public class AnimationAsset : ScriptableObject
    {
        public AnimationDef animation = new AnimationDef();
    }
}
