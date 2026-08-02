using UnityEngine;
using Laubrary.Zoetrope;
using Laubrary.Zounds;

namespace Laubrary.ZoetropeZounds
{
    /// Plays a Zound as a Zoe effect — so "make a noise" becomes one more thing an event can trigger,
    /// alongside a Pyre blast or a Chunks burst, chosen from the same effect list.
    ///
    /// Lives in a BRIDGE module for the usual reason: Zoetrope's core must not depend on Zounds, so a project
    /// that has no audio tool still compiles. Same shape as SpawnPyreFx (Zoetrope+Pyre) and the
    /// LaunimatorZounds frame-event bridge (Launimator+Zounds) — Zoetrope only knows it has an IEffect.
    ///
    /// A Zound is referenced BY NAME rather than by asset reference, because that is how Zounds itself
    /// addresses them (ZoundEngine.PlayZound(name), and the picker returns a name). The editor supplies a
    /// picker so nobody has to type it, but the stored value is deliberately the same string Zounds uses.
    [System.Serializable]
    public class PlayZoundEffect : IEffect
    {
        [Tooltip("Which Zound to play. Pick it rather than typing it — a misspelt name fails silently.")]
        public string zoundName = "";

        // Deliberately ONE field. An `atEventPosition` toggle was here and did nothing: ZoundEngine.PlayZound
        // takes a name and no position, so the control promised spatial audio the effect could not deliver
        // and cost a row of an already-tall effect card. Routing is a Zound's own business in Zounds; if
        // positional playback is ever wanted it belongs behind a real API, not a dead checkbox.
        public bool IsEmpty => string.IsNullOrEmpty(zoundName);

        public void Apply(EventContext ctx)
        {
            if (IsEmpty) return;
            ZoundEngine.PlayZound(zoundName);
        }
    }
}
