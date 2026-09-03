// DotGen.cs
// The document: a frame, a seed, and a flat list of generators that describes a whole recursive composition.
//
// Everything an author touches lives here and nowhere else, which is what makes a DotGen a real asset rather
// than an editor session — it can be picked, duplicated, diffed, and it renders the same picture next year.
// The window's own view state (zoom, pan, which card is hovered, which module is selected) is deliberately
// NOT here: those describe a person looking at the document, not the document.

using System.Collections.Generic;
using System.Text;
using Laubrary.PreviewKit;
using UnityEngine;

namespace Laubrary.DotGen
{
    /// Which process gizmos the preview shows.
    public enum DotGizmoMode { Hovered, Selected, All, Off }

    [CreateAssetMenu(menuName = "Laubrary/DotGen", fileName = "DotGen")]
    public class DotGen : ScriptableObject, IVisualPreview
    {
        [Range(0, 999998)]
        [Tooltip("The one number every random draw in this document comes from. Same seed, same picture.")]
        public int seed = 4821;

        [Range(64, 2048)]
        [Tooltip("Resolution of the rendered frame, in pixels. Exports at this size too.")]
        public int frameSize = 512;

        [Tooltip("The colour behind everything.")]
        public Color background = DotGenMath.Hex("#0c1017");

        [Tooltip("The colour of the ten-by-ten guide grid.")]
        public Color guideGrid = DotGenMath.Hex("#273140");

        [Tooltip("Shows a ten-by-ten grid over the background, for judging placement.")]
        public bool showGuideGrid = true;

        [Tooltip("The colour of the border drawn around the frame.")]
        public Color frameStroke = DotGenMath.Hex("#64748b");

        [Tooltip("Draws a border around the frame, so its edge is visible against dark artwork.")]
        public bool showFrameStroke = true;

        [Tooltip("Which module's process gizmos the preview draws.")]
        public DotGizmoMode gizmoMode = DotGizmoMode.Hovered;

        /// The generator the window reopens on. Persisted (unlike the selected module inside it, which is a
        /// property of looking rather than of the document).
        [HideInInspector] public string selectedGeneratorId = "";

        /// Every generator, flat. Hierarchy comes from `parentId`; see DotGenTree.
        public List<DotGenerator> generators = new List<DotGenerator>();

        /// The id counter. Ids must stay unique for the life of the document because selector references,
        /// gizmo targeting and the spawn hash all read them.
        [HideInInspector] public int nextId = 1;

        /// The preview backdrop, so a document can be judged against the surface it will really sit on.
        /// Lazily allocated by the window exactly like Pyre's, so an asset written before this field existed
        /// keeps loading.
        [HideInInspector] public Laubrary.BackSplash.BackSplashSettings previewBackSplash;

        // ── ids ────────────────────────────────────────────────────────────────────────────

        /// A fresh id. The shape (`gen_1`, `sel_3`, …) is load-bearing, not cosmetic: a child's spawn draw
        /// hashes the LENGTH of its id, so changing the naming scheme changes which dots spawn children for
        /// every existing document.
        public string NewId(string prefix)
        {
            string id = prefix + "_" + Base36(nextId);
            nextId++;
            return id;
        }

        public static string Base36(int value)
        {
            if (value == 0) return "0";
            const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            bool neg = value < 0;
            long v = neg ? -(long)value : value;
            var sb = new StringBuilder();
            while (v > 0) { sb.Insert(0, digits[(int)(v % 36)]); v /= 36; }
            return neg ? "-" + sb : sb.ToString();
        }

        static int FromBase36(string s)
        {
            if (string.IsNullOrEmpty(s)) return -1;
            long v = 0;
            foreach (char c in s)
            {
                int d;
                if (c >= '0' && c <= '9') d = c - '0';
                else if (c >= 'a' && c <= 'z') d = c - 'a' + 10;
                else return -1;
                v = v * 36 + d;
                if (v > int.MaxValue) return -1;
            }
            return (int)v;
        }

        // ── lookup ─────────────────────────────────────────────────────────────────────────

        public DotGenTree Tree() => new DotGenTree(generators);

        public DotGenerator Root
        {
            get
            {
                if (generators == null) return null;
                for (int i = 0; i < generators.Count; i++)
                    if (generators[i] != null && generators[i].IsRoot) return generators[i];
                return generators.Count > 0 ? generators[0] : null;
            }
        }

        public DotGenerator Find(string id)
        {
            if (generators == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < generators.Count; i++)
                if (generators[i] != null && generators[i].id == id) return generators[i];
            return null;
        }

        public DotGenerator Selected => Find(selectedGeneratorId) ?? Root;

        // ── defaults, demo, migration ──────────────────────────────────────────────────────

        /// The bare state: one root generator on a plain grid, nothing else.
        void Reset()
        {
            seed = 4821;
            frameSize = 512;
            background = DotGenMath.Hex("#0c1017");
            guideGrid = DotGenMath.Hex("#273140");
            showGuideGrid = true;
            frameStroke = DotGenMath.Hex("#64748b");
            showFrameStroke = true;
            gizmoMode = DotGizmoMode.Hovered;
            nextId = 1;

            generators = new List<DotGenerator>();
            var root = new DotGenerator { parentId = "" };
            root.ApplyDefaults(true);
            root.id = NewId("gen");
            root.placementBank[0].id = NewId("place");
            generators.Add(root);
            selectedGeneratorId = root.id;
        }

        /// The documented demonstration composition. A brand new document is this, not an empty frame: a
        /// generator system that shows nothing on the first screen teaches nothing about itself.
        ///
        /// The ids are written out rather than allocated. They are part of the composition's identity — the
        /// child spawn draw hashes each child's id LENGTH — so a demo built with different ids would be a
        /// different picture wearing the same settings.
        public void ApplyDemo()
        {
            seed = 4821;
            gizmoMode = DotGizmoMode.Hovered;
            generators = new List<DotGenerator>();

            var root = new DotGenerator { parentId = "" };
            root.ApplyDefaults(true);
            root.id = "gen_1";
            root.name = "Root Frame";
            root.showDots = false;
            ((DotGridPlacement)root.placementBank[0]).id = "place_2";
            ((DotGridPlacement)root.placementBank[0]).columns = 8;
            ((DotGridPlacement)root.placementBank[0]).rows = 7;
            root.selectors.Add(new DotMarginSelector
            {
                id = "sel_3", name = "Safe frame",
                bandWidth = 8f, softness = 3f, edgeBandValue = 0f, innerCoreValue = 100f
            });
            root.mutators.Add(new DotNudgeMutator
            {
                id = "mut_4", name = "Loose lattice", selectorId = "sel_3",
                strength = 2.2f, directionBias = 18f, biasAngle = -12f
            });
            generators.Add(root);

            var rings = new DotGenerator { parentId = "gen_1" };
            rings.ApplyDefaults(false);
            rings.id = "gen_5";
            rings.name = "Radial Clusters";
            rings.color = DotGenMath.Hex("#62d8ff");
            rings.shape = DotShape.Ellipse;
            rings.sizeX = 20f; rings.sizeY = 20f;
            rings.spawnEvery = 2; rings.spawnChance = 78f; rings.maxInstances = 24;
            rings.placementBank[0].id = "place_6";
            rings.placementBank.Add(new DotRadialPlacement { id = "place_7" });
            rings.placementTypeId = "radial";
            rings.selectors.Add(new DotGradientSelector
            {
                id = "sel_8", name = "Light sweep",
                angle = -28f, offset = 10f, contrast = 125f
            });
            rings.mutators.Add(new DotWarpMutator
            {
                id = "mut_9", name = "Gather", selectorId = "sel_8",
                fieldSource = DotWarpField.PointCenters, force = DotWarpForce.Pull,
                strength = 4.5f, centers = 1, influenceRadius = 55f, seedOffset = 12
            });
            rings.mutators.Add(new DotCullMutator
            {
                id = "mut_a", name = "Fade out", selectorId = "sel_8",
                cullAction = DotCullAction.CullUnselected, seedOffset = 92
            });
            generators.Add(rings);

            var diamonds = new DotGenerator { parentId = "gen_1" };
            diamonds.ApplyDefaults(false);
            diamonds.id = "gen_b";
            diamonds.name = "Diamond Grids";
            diamonds.color = DotGenMath.Hex("#ffcc66");
            diamonds.shape = DotShape.Diamond;
            diamonds.sizeX = 13f; diamonds.sizeY = 13f;
            diamonds.spawnEvery = 4; diamonds.spawnChance = 70f; diamonds.maxInstances = 12;
            var dgrid = (DotGridPlacement)diamonds.placementBank[0];
            dgrid.id = "place_c";
            dgrid.columns = 4; dgrid.rows = 4;
            dgrid.rotation = 45f; dgrid.gapX = 7f; dgrid.gapY = 7f;
            diamonds.selectors.Add(new DotRandomSelector
            {
                id = "sel_d", name = "Broken cells", amount = 72f, seedOffset = 22
            });
            diamonds.mutators.Add(new DotCullMutator
            {
                id = "mut_e", name = "Cull", selectorId = "sel_d",
                cullAction = DotCullAction.CullUnselected, seedOffset = 28
            });
            generators.Add(diamonds);

            nextId = 15;
            selectedGeneratorId = "gen_5";
        }

        /// Repair anything a document could be missing without ever overwriting something it already says.
        /// Called on load; safe to call repeatedly.
        public void Normalize()
        {
            if (generators == null) generators = new List<DotGenerator>();
            generators.RemoveAll(g => g == null);

            if (generators.Count == 0)
            {
                Reset();
                return;
            }

            // Exactly one root, and it is the first entry that claims to be one.
            DotGenerator root = null;
            for (int i = 0; i < generators.Count; i++)
                if (generators[i].IsRoot) { root = generators[i]; break; }
            if (root == null) { root = generators[0]; root.parentId = ""; }

            int maxCounter = 0;
            for (int i = 0; i < generators.Count; i++) TrackId(generators[i].id, ref maxCounter);

            for (int i = 0; i < generators.Count; i++)
            {
                var g = generators[i];
                if (g.selectors == null) g.selectors = new List<DotSelector>();
                if (g.mutators == null) g.mutators = new List<DotMutator>();
                if (g.drawers == null) g.drawers = new List<DotDrawer>();
                if (g.placementBank == null) g.placementBank = new List<DotPlacement>();
                g.selectors.RemoveAll(s => s == null);
                g.mutators.RemoveAll(m => m == null);
                g.drawers.RemoveAll(d => d == null);
                g.placementBank.RemoveAll(p => p == null);

                for (int k = 0; k < g.placementBank.Count; k++) TrackId(g.placementBank[k].id, ref maxCounter);
                for (int k = 0; k < g.selectors.Count; k++) TrackId(g.selectors[k].id, ref maxCounter);
                for (int k = 0; k < g.mutators.Count; k++) TrackId(g.mutators[k].id, ref maxCounter);
                for (int k = 0; k < g.drawers.Count; k++) TrackId(g.drawers[k].id, ref maxCounter);
            }

            if (nextId <= maxCounter) nextId = maxCounter + 1;

            for (int i = 0; i < generators.Count; i++)
            {
                var g = generators[i];
                if (string.IsNullOrEmpty(g.id)) g.id = NewId("gen");
                if (g != root && string.IsNullOrEmpty(g.parentId)) g.parentId = root.id;
                if (g != root && Find(g.parentId) == null) g.parentId = root.id;

                if (string.IsNullOrEmpty(g.placementTypeId)) g.placementTypeId = "grid";
                if (g.ActivePlacement == null && g.UsePlacement(g.placementTypeId) == null) g.UsePlacement("grid");

                for (int k = 0; k < g.placementBank.Count; k++)
                    if (string.IsNullOrEmpty(g.placementBank[k].id)) g.placementBank[k].id = NewId("place");
                for (int k = 0; k < g.selectors.Count; k++)
                    if (string.IsNullOrEmpty(g.selectors[k].id)) g.selectors[k].id = NewId("sel");
                for (int k = 0; k < g.mutators.Count; k++)
                    if (string.IsNullOrEmpty(g.mutators[k].id)) g.mutators[k].id = NewId("mut");

                for (int k = 0; k < g.drawers.Count; k++)
                {
                    var d = g.drawers[k];
                    if (string.IsNullOrEmpty(d.id)) d.id = NewId("draw");
                    if (d is DotFillDrawer fd)
                    {
                        if (fd.fill == null) fd.fill = DotGenFills.Solid("#26384a");
                        if (fd.fills == null || fd.fills.Count == 0) fd.fills = DotGenFills.DefaultList();
                        fd.fills.RemoveAll(f => f == null);
                        if (fd.fills.Count == 0) fd.fills = DotGenFills.DefaultList();
                    }
                }
            }

            if (Find(selectedGeneratorId) == null) selectedGeneratorId = root.id;
            if (frameSize < 64) frameSize = 64;
            if (frameSize > 2048) frameSize = 2048;
        }

        static void TrackId(string id, ref int maxCounter)
        {
            if (string.IsNullOrEmpty(id)) return;
            int us = id.LastIndexOf('_');
            if (us < 0 || us == id.Length - 1) return;
            int v = FromBase36(id.Substring(us + 1));
            if (v > maxCounter) maxCounter = v;
        }

        // ── IVisualPreview ─────────────────────────────────────────────────────────────────

        public Texture2D RenderPreviewTexture()
        {
            Normalize();
            var res = DotGenEvaluator.Evaluate(this);
            return DotGenRenderer.Render(this, res, 128, withDots: true);
        }

        /// A document is a still picture — there is nothing to animate.
        public bool CanAnimatePreview => false;
        public float PreviewFps => 0f;
        public void UpdateAnimatedPreview(Texture2D tex, double time) { }
    }
}
