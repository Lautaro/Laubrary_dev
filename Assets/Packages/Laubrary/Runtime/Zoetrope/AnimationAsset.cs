using UnityEngine;

namespace Laubrary.Zoetrope
{
    /// <summary>
    /// A standalone "orphaned" animation: an <see cref="AnimationDef"/> that does not yet belong to any
    /// zoe. Authored in the Animation Builder (opened standalone) and stored under
    /// <c>Assets/Zoetrope/Animations/</c>. The Zoe Browser can later INCLUDE one into a
    /// zoe's draft (copying its recipe). Orphans carry only the editable recipe — they are not baked
    /// into clips/atlases until included in a zoe version.
    /// </summary>
    public class AnimationAsset : ScriptableObject
    {
        public AnimationDef animation = new AnimationDef();
    }
}
