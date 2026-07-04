using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Pyre
{
    /// A stack of Waves composited as one unit. Back-to-front order is the order layers appear in the blast's
    /// list, so a dark smoke layer authored first sits behind a bright fire layer authored after it.
    [System.Serializable]
    public class Layer
    {
        [Tooltip("Label shown in the editor's layer list. Cosmetic only.")]
        public string name = "Layer";

        [Tooltip("Hide this layer in the preview and the bake without deleting its waves.")]
        public bool visible = true;

        [Tooltip("The waves composited into this layer, drawn in list order.")]
        public List<Wave> waves = new();
    }
}
