// DotGenerator.cs
// One generator definition: an area, one active placement, and the ordered selector / mutator / drawer lists.
//
// Generators are held FLAT on the document with a `parentId`, not as nested children. Unity's serializer
// truncates a recursive [Serializable] class at about seven levels and warns; DotGen allows unlimited nesting,
// so the tree is an index rebuilt on demand (DotGenTree) over a flat list that serializes at a fixed depth.
//
// The placement BANK is the answer to "what happens to my grid settings when I try the radial method?".
// Switching method keeps every type's own dials, because each type keeps its own instance and the generator
// only records which one is active. A type visited for the first time is created from its field initializers,
// so it arrives with its registered defaults rather than blank.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.DotGen
{
    /// Where a child takes its scale and rotation from. The parent dot is always the attachment point; this
    /// only decides the BASIS. "Placement cell" is offered only when the parent's placement provides cells.
    public enum DotAreaBasis { Parent, Cell }

    [Serializable]
    public class DotGenerator
    {
        public string id = "";

        /// Empty for the one root generator. Sibling order is order of appearance among entries sharing a parent.
        public string parentId = "";

        [Tooltip("This generator's name in the hierarchy and the legend.")]
        public string name = "Generator";

        [Tooltip("Off stops this generator and everything below it: no areas, no dots, no drawers, no children. Settings are kept.")]
        public bool enabled = true;

        [Tooltip("The colour of this generator's dot markers, its legend entry and its gizmos.")]
        public Color color = DotGenMath.Hex("#62d8ff");

        [Range(1f, 8f)]
        [Tooltip("Radius of each dot marker, in pixels at the reference 900 px frame.")]
        public float dotSize = 3f;

        [Tooltip("Off hides the dot markers only — those dots still spawn children and feed drawers.")]
        public bool showDots = true;

        [Tooltip("The shape of this generator's area. Dots that fall outside it are not emitted.")]
        public DotShape shape = DotShape.Ellipse;

        [Range(2f, 200f)]
        [Tooltip("Area width as a percentage of the basis it is attached to.")]
        public float sizeX = 22f;

        [Range(2f, 600f)]
        [Tooltip("Area height as a percentage of the basis it is attached to. A child may grow far past its basis.")]
        public float sizeY = 22f;

        [Range(-180f, 180f)]
        [Tooltip("Rotation of the area, added to the rotation of whatever it is attached to.")]
        public float rotation = 0f;

        [Tooltip("Which point of the area sits on the attachment point. Bottom makes an area grow upward from its parent dot.")]
        public DotAnchor anchor = DotAnchor.Center;

        [Tooltip("Take size and rotation from the parent's whole area, or from the exact cell around the parent dot.")]
        public DotAreaBasis areaBasis = DotAreaBasis.Parent;

        [Range(1, 12)]
        [Tooltip("Spawn on every Nth surviving parent dot.")]
        public int spawnEvery = 1;

        [Range(0f, 100f)]
        [Tooltip("Chance that an eligible parent dot spawns this generator at all.")]
        public float spawnChance = 100f;

        [Range(1, 250)]
        [Tooltip("Hard ceiling on how many copies of this generator are evaluated.")]
        public int maxInstances = 80;

        /// Which placement in the bank is live. Stored as a type id so a bank reordering cannot change it.
        public string placementTypeId = "grid";

        /// One instance per placement type this generator has ever used, so switching method back restores the
        /// dials rather than wiping them.
        [SerializeReference] public List<DotPlacement> placementBank = new List<DotPlacement>();

        [SerializeReference] public List<DotSelector> selectors = new List<DotSelector>();
        [SerializeReference] public List<DotMutator> mutators = new List<DotMutator>();
        [SerializeReference] public List<DotDrawer> drawers = new List<DotDrawer>();

        public bool IsRoot => string.IsNullOrEmpty(parentId);

        /// The live placement, or null when the bank has not been normalized yet. Never creates — the evaluator
        /// must stay free of side effects.
        public DotPlacement ActivePlacement
        {
            get
            {
                if (placementBank == null) return null;
                for (int i = 0; i < placementBank.Count; i++)
                {
                    var p = placementBank[i];
                    if (p != null && p.Meta != null && p.Meta.TypeId == placementTypeId) return p;
                }
                return null;
            }
        }

        /// Switch method, creating the type's instance from its own defaults the first time it is chosen.
        /// Returns the now-active placement.
        public DotPlacement UsePlacement(string typeId)
        {
            placementTypeId = typeId;
            var existing = ActivePlacement;
            if (existing != null) return existing;

            var made = DotModuleRegistry.Create(DotModuleRegistry.Placements, typeId) as DotPlacement;
            if (made == null) return null;
            ApplyRolePlacementDefaults(made);
            placementBank.Add(made);
            return made;
        }

        /// The two Grid defaults that depend on role live HERE, not on the Grid type: an 8x7 root lattice and a
        /// 4x4 child lattice are two uses of one placement, and a type cannot know which one it is in.
        public void ApplyRolePlacementDefaults(DotPlacement p)
        {
            if (p is DotGridPlacement grid)
            {
                grid.columns = IsRoot ? 8 : 4;
                grid.rows = IsRoot ? 7 : 4;
            }
        }

        /// The role-sensitive defaults of a fresh generator. Ids are left blank; the document assigns them.
        public void ApplyDefaults(bool isRoot)
        {
            name = isRoot ? "Root Frame" : "Generator";
            enabled = true;
            color = DotGenMath.Hex(isRoot ? "#70e1a1" : "#62d8ff");
            dotSize = isRoot ? 4f : 3f;
            showDots = true;
            shape = isRoot ? DotShape.Rectangle : DotShape.Ellipse;
            sizeX = isRoot ? 100f : 22f;
            sizeY = isRoot ? 100f : 22f;
            rotation = 0f;
            anchor = DotAnchor.Center;
            areaBasis = DotAreaBasis.Parent;
            spawnEvery = 1;
            spawnChance = 100f;
            maxInstances = isRoot ? 1 : 80;

            placementBank = new List<DotPlacement>();
            selectors = new List<DotSelector>();
            mutators = new List<DotMutator>();
            drawers = new List<DotDrawer>();

            placementTypeId = "grid";
            var grid = new DotGridPlacement();
            ApplyRolePlacementDefaults(grid);
            placementBank.Add(grid);
        }

        /// Resolve a mutator's or drawer's selector reference. A blank reference, a missing selector and a
        /// disabled one all mean the same thing — All dots, a constant weight of 1 — so a consumer never has to
        /// special-case a broken reference.
        public DotSelector ResolveSelector(string selectorId)
        {
            if (string.IsNullOrEmpty(selectorId) || selectors == null) return null;
            for (int i = 0; i < selectors.Count; i++)
            {
                var s = selectors[i];
                if (s != null && s.id == selectorId) return s.enabled ? s : null;
            }
            return null;
        }
    }
}
