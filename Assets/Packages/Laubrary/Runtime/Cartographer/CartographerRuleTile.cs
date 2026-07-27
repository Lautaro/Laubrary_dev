// RuleTile ships in the OPTIONAL com.unity.2d.tilemap.extras package, not the engine. Guarded so Cartographer
// never forces that dependency on a consumer that does not want it — the rest of the tool works without this,
// and a project that installs the package gets the rule tile automatically. Same versionDefines pattern
// Zounds uses for Addressables.
#if TILEMAP_EXTRAS_INSTALLED
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Laubrary.Cartographer
{
    /// A RuleTile whose neighbour checks match a tile FAMILY rather than one exact tile asset.
    ///
    /// Unity's own `RuleTile` compares neighbours by reference, so a "roof" rule that should read "is my
    /// neighbour any kind of roof" has to list every sibling tile by hand — and re-list them every time a
    /// variant is added. Giving tiles a shared `family` string turns that into one comparison, so a roof
    /// interior, a roof edge and three roof variants all satisfy each other's rules automatically.
    ///
    /// Everything else is stock RuleTile: the Random output mode still picks among a matched rule's sprites,
    /// so rule-matching (interior vs edge) and variant-picking stay the independent axes they already were.
    [CreateAssetMenu(menuName = "Laubrary/Cartographer/Rule Tile", fileName = "CartographerRuleTile")]
    public class CartographerRuleTile : RuleTile<CartographerRuleTile.Neighbor>
    {
        [Tooltip("The family this tile belongs to, e.g. \"roof\" or \"ground\". Any two tiles sharing a family " +
                 "count as the same tile for neighbour matching. Leave empty to fall back to exact-asset " +
                 "matching, which is stock RuleTile behaviour.")]
        public string family = "";

        public class Neighbor : RuleTile.TilingRule.Neighbor
        {
            /// Matches any tile of the same family. Extra rule options start above RuleTile's own values.
            public const int SameFamily = 3;
            public const int NotSameFamily = 4;
        }

        public override bool RuleMatch(int neighbor, TileBase other)
        {
            switch (neighbor)
            {
                case Neighbor.SameFamily:    return IsSameFamily(other);
                case Neighbor.NotSameFamily: return !IsSameFamily(other);
            }

            // "This" and "Not this" also honour the family, which is the point — an author who set a family
            // gets family matching from the ordinary rule buttons without learning two extra ones.
            if (!string.IsNullOrEmpty(family))
            {
                switch (neighbor)
                {
                    case TilingRuleOutput.Neighbor.This:     return IsSameFamily(other);
                    case TilingRuleOutput.Neighbor.NotThis:  return !IsSameFamily(other);
                }
            }

            return base.RuleMatch(neighbor, other);
        }

        bool IsSameFamily(TileBase other)
        {
            if (other == null) return false;
            if (string.IsNullOrEmpty(family)) return other == this;
            return other is CartographerRuleTile r && r.family == family;
        }
    }
}
#endif
