using UnityEngine;

namespace Laubrary.Launimator
{
    /// <summary>
    /// A standalone "orphaned" animation: an <see cref="Laumination"/> that does not yet belong to any
    /// lauminary. Authored in the Laumination Builder (opened standalone) and stored under
    /// <c>Assets/Launimator/Animations/</c>. The Lauminary Browser can later INCLUDE one into a
    /// lauminary's draft (copying its recipe). Orphans carry only the editable recipe — they are not baked
    /// into clips/atlases until included in a lauminary version.
    /// </summary>
    public class AnimationAsset : ScriptableObject
    {
        public Laumination animation = new Laumination();
    }
}
