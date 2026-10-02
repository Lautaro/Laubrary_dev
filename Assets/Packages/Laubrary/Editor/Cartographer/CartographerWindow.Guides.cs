using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Cartographer.Editor
{
    /// RULER GUIDES — the alignment and symmetry aid for the level canvas.
    ///
    /// Three things, one master switch ("Guides"):
    ///
    /// 1. RULERS along the canvas's top and left edges, numbering CELL BOUNDARIES, out of which guides are
    ///    dragged. They live in a gutter the view is fitted inside (ZuiPanZoom.Inset), never over the level —
    ///    a ruler that hides the top row of cells would be worse than no ruler.
    /// 2. GUIDES: full-height / full-width lines that sit ON a cell boundary — always, there is no free
    ///    placement. A boundary is what you align TO: a guide sits BETWEEN two cells, so "the wall starts
    ///    here" is a statement about an edge, not about a cell.
    /// 3. A MIRROR AXIS: one vertical and/or one horizontal guide promoted (by clicking it) to reflect every
    ///    edit. See <see cref="EditImages"/> for what is exact and <see cref="MirrorStamp"/> for what is not.
    ///
    /// ☠️ GUIDES ARE NOT LEVEL DATA. They live in this window's EditorPrefs blob, keyed by the level's GUID,
    /// and deliberately NOT on the LevelAsset: they are an authoring aid like a marquee selection or a folded
    /// card, and putting them in the asset would dirty the level on every guide drag (making "I nudged a
    /// construction line" a Ctrl+S-worthy change), park them in version control where two authors editing the
    /// same level would collide over each other's scaffolding, and ship them in the build. The stored set is
    /// BOUNDED to the <see cref="MaxGuideSets"/> most recently used levels, because a prefs string nothing
    /// ever prunes is a slow leak in a machine-global store.
    public partial class CartographerWindow
    {
        /// One level's guides. Serialized inside <see cref="WindowState"/>, which is why it is a flat
        /// [Serializable] class of primitives — EditorJsonUtility writes an object reference as a
        /// session-local instanceID, so the level travels as a GUID like every other reference in that DTO.
        [System.Serializable]
        internal class GuideSet
        {
            public string levelGuid = "";

            /// Vertical guides, as the X of the cell boundary they sit on (the left edge of cell X).
            public List<int> vertical = new();

            /// Horizontal guides, as the Y of the cell boundary they sit on (the bottom edge of cell Y).
            public List<int> horizontal = new();

            /// The promoted VERTICAL guide — the axis that mirrors left↔right. At most one.
            public bool hasAxisX;
            public int axisX;

            /// The promoted HORIZONTAL guide — the axis that mirrors up↔down. At most one.
            public bool hasAxisY;
            public int axisY;

            /// DateTime.UtcNow.Ticks at the last touch — the eviction key. Deliberately not
            /// EditorApplication.timeSinceStartup, which restarts at zero every session and would therefore
            /// rank a set touched an hour ago in THIS session below one touched a year ago in another.
            public long used;

            public bool IsEmpty => vertical.Count == 0 && horizontal.Count == 0;
        }

        /// How many levels' guides the prefs blob keeps. Sixteen is "everything you touched this week"
        /// without letting the string grow forever.
        const int MaxGuideSets = 16;

        /// How far, in CELLS, a paint may be pulled onto a guide when snapping is on. Small on purpose:
        /// snapping that reaches across a room stops feeling like alignment and starts feeling like the
        /// canvas fighting you.
        const int SnapRadiusCells = 2;

        readonly List<GuideSet> guideSets = new();
        GuideSet activeGuides;

        /// The open level's guides — null only when no level is open. Not automatically in
        /// <see cref="guideSets"/>: an untouched set is never stored, so visiting a level does not park an
        /// empty entry in the prefs blob.
        internal GuideSet ActiveGuides => activeGuides;

        // The three switches. Global (they are a working style, not a property of a level) and persisted in
        // WindowState with the other canvas dials.
        [SerializeField] bool showGuides = true;
        [SerializeField] bool snapToGuides = true;
        [SerializeField] bool mirrorGuides;

        internal bool ShowGuides => showGuides;

        /// Whether an edit gets reflected right now: the master switch, the mirror switch, and an actual
        /// promoted axis to reflect about. All three, so turning mirroring on before promoting anything
        /// cannot silently do nothing.
        internal bool MirrorActive => showGuides && mirrorGuides && MirrorAxisExists;

        internal bool MirrorAxisExists => activeGuides != null && (activeGuides.hasAxisX || activeGuides.hasAxisY);

        /// Whether the hovered cell gets pulled onto a guide. Separate from SHOWING guides on purpose —
        /// plenty of authoring wants the lines as a visual reference with the cursor left exactly where it is.
        internal bool SnapActive => showGuides && snapToGuides && activeGuides != null
                                    && (activeGuides.vertical.Count > 0 || activeGuides.horizontal.Count > 0);

        // ── the store ──────────────────────────────────────────────────────────
        /// Point <see cref="activeGuides"/> at the open level's set, minting an unstored one if this level has
        /// never had guides. Called from BuildAsset (which runs for every level the window shows) rather than
        /// only from OnAssetChanged, because restoring a window's last level fills the `asset` field directly
        /// and never raises an asset-changed event.
        void LoadGuidesForLevel()
        {
            string guid = GuidOf(level);
            activeGuides = null;
            if (string.IsNullOrEmpty(guid)) return;
            foreach (var g in guideSets)
                if (g != null && g.levelGuid == guid) { activeGuides = g; return; }
            activeGuides = new GuideSet { levelGuid = guid };
        }

        /// THE one door every guide mutation leaves by. Files the set in the store (first mutation only),
        /// stamps it for eviction ordering, keeps the mirror switch honest, refreshes the card and the canvas,
        /// and PERSISTS IMMEDIATELY — a guide has to survive a domain reload, and a reload is exactly the
        /// event that can arrive between two user actions with no chance to save on the way out.
        internal void GuidesChanged()
        {
            if (activeGuides != null)
            {
                if (!guideSets.Contains(activeGuides)) guideSets.Add(activeGuides);
                activeGuides.used = System.DateTime.UtcNow.Ticks;
            }
            // The axis the mirror was riding on can be deleted out from under it. Leaving the switch lit over
            // nothing would be the "why is this doing nothing?" state the disabled-with-a-reason rule exists
            // to prevent.
            if (mirrorGuides && !MirrorAxisExists) mirrorGuides = false;
            toolCard?.Rebuild();
            canvas?.RepaintOverlay();
            UpdateCanvasStatus();
            SaveWindowState();
        }

        /// Drop empty sets and evict all but the most recently used <see cref="MaxGuideSets"/>. Run on save,
        /// so the bound applies to what is actually written rather than to what is held in memory.
        void TrimGuideSets()
        {
            guideSets.RemoveAll(g => g == null || string.IsNullOrEmpty(g.levelGuid) || g.IsEmpty);
            if (guideSets.Count <= MaxGuideSets) return;
            guideSets.Sort((a, b) => b.used.CompareTo(a.used));   // most recent first
            guideSets.RemoveRange(MaxGuideSets, guideSets.Count - MaxGuideSets);
        }

        /// Restore the store from a WindowState DTO, then re-point the active set at the open level (the
        /// level may already be known — OnEnable restores it before the UI is built).
        void RestoreGuideSets(List<GuideSet> stored)
        {
            guideSets.Clear();
            if (stored != null)
                foreach (var g in stored)
                    if (g != null && !string.IsNullOrEmpty(g.levelGuid)) guideSets.Add(g);
            LoadGuidesForLevel();
        }

        internal void ClearGuides()
        {
            if (activeGuides == null) return;
            int n = activeGuides.vertical.Count + activeGuides.horizontal.Count;
            if (n == 0) return;
            activeGuides.vertical.Clear();
            activeGuides.horizontal.Clear();
            activeGuides.hasAxisX = activeGuides.hasAxisY = false;
            GuidesChanged();
            ReportCanvasStatus($"Cleared {n} guide{(n == 1 ? "" : "s")}. Guides are not undoable — drag new " +
                               "ones out of the rulers.");
        }

        void SetShowGuides(bool v)
        {
            showGuides = v;
            canvas?.RefitView();   // the ruler gutter appears or goes; the level re-fits around it
            toolCard?.Rebuild();
            UpdateCanvasStatus();
            SaveWindowState();
        }

        // ── snapping ───────────────────────────────────────────────────────────
        /// Pull a cell onto a nearby guide so what lands BUTTS against the line instead of merely landing
        /// near it. Applied once, in <see cref="LevelCanvas.UpdateHover"/>, so the readout, the ghost and the
        /// commit all read the same already-snapped cell and cannot disagree.
        ///
        /// It is the gesture's FOOTPRINT that is snapped, not its anchor: a 3-wide clump snaps so that its
        /// left OR right edge lands on the guide, whichever is nearer, which is what "the wall butts against
        /// the line" actually means.
        ///
        /// ⚠️ ONLY GESTURES THAT PLACE SOMETHING SNAP. Erase, Pick and the right-drag marquee are exempt, and
        /// the reason is a trap rather than a preference: a snap radius of two cells makes the cells JUST
        /// inside it unreachable — with a guide on x=10 the anchor can be 9 or 10 but never 8 — which is
        /// exactly right for laying a wall against the line and exactly wrong for rubbing out one stray tile
        /// beside it. Corrective and sampling work must be able to reach every cell without the author having
        /// to go and turn a switch off first.
        internal Vector2Int SnapCellToGuides(Vector2Int cell, bool suppress)
        {
            if (suppress || !SnapActive || dragErasing) return cell;
            if (tool != CanvasTool.Paint && tool != CanvasTool.Line && tool != CanvasTool.Rect
                && tool != CanvasTool.Fill && tool != CanvasTool.Stamp) return cell;
            var f = GestureFootprint();
            return new Vector2Int(
                SnapAxis(cell.x, f.xMin, f.xMax, activeGuides.vertical),
                SnapAxis(cell.y, f.yMin, f.yMax, activeGuides.horizontal));
        }

        /// One axis of the snap. `lo`/`hi` are the footprint's leading and trailing edges relative to the
        /// anchor, so the two candidate anchors per guide are "leading edge on the line" and "trailing edge
        /// on the line" — the nearest of all candidates wins, and only within the radius.
        static int SnapAxis(int v, int lo, int hi, List<int> guides)
        {
            int best = v, bestD = SnapRadiusCells + 1;
            foreach (int g in guides)
            {
                int a = g - lo, b = g - hi;
                int da = Mathf.Abs(a - v), db = Mathf.Abs(b - v);
                if (da < bestD) { bestD = da; best = a; }
                if (db < bestD) { bestD = db; best = b; }
            }
            return bestD <= SnapRadiusCells ? best : v;
        }

        /// The rect the current gesture would cover, RELATIVE to its anchor cell: the transformed footprint
        /// for a stamp, the brush's own bounding box for a pattern or clump, 1×1 for a single tile.
        internal RectInt GestureFootprint()
        {
            if (tool == CanvasTool.Stamp && !dragErasing) return StampFootprintAt(Vector2Int.zero);
            return BrushFootprint();
        }

        // ── mirroring ──────────────────────────────────────────────────────────
        /// Where ONE edit must land: the cell itself, plus its reflection in each live axis (and in both at
        /// once when a vertical AND a horizontal guide have been promoted). `flipX`/`flipY` say whether the
        /// brush's own ARRANGEMENT is reversed getting there.
        ///
        /// No image can ever collide with another. A cell's reflection about a BOUNDARY at `a` is
        /// `2a − c − 1`, which equals `c` only when `2c = 2a − 1` — impossible in integers. So the list needs
        /// no de-duplication and one paint always writes exactly as many cells as it has images. (That is the
        /// payoff of putting guides on boundaries rather than on cells: a cell-centred axis would have a
        /// fixed point, and every edit on it would double-write.)
        internal List<EditImage> EditImages(Vector2Int cell)
        {
            var list = new List<EditImage>(4) { new EditImage(cell, false, false) };
            if (!MirrorActive) return list;
            var g = activeGuides;
            if (g.hasAxisX) list.Add(new EditImage(new Vector2Int(MirrorX(cell.x), cell.y), true, false));
            if (g.hasAxisY) list.Add(new EditImage(new Vector2Int(cell.x, MirrorY(cell.y)), false, true));
            if (g.hasAxisX && g.hasAxisY)
                list.Add(new EditImage(new Vector2Int(MirrorX(cell.x), MirrorY(cell.y)), true, true));
            return list;
        }

        /// The same reflection over a whole list of cells — a Line/Rect span's anchors, a flood's region, the
        /// ghost's outline. Each entry keeps its flip flags so a caller that lays a multi-cell BRUSH at every
        /// entry can reverse the arrangement too.
        ///
        /// ONE reflection, two equivalent readings, and that equivalence is why nothing here can drift: the
        /// mirrored anchor plus the negated offset IS the mirror of the original absolute cell. So a commit
        /// that reflects anchors and flips offsets writes exactly the cells a ghost that reflects absolute
        /// cells outlines.
        internal List<EditImage> MirrorImagesOf(List<Vector2Int> cells)
        {
            var all = new List<EditImage>(cells == null ? 0 : cells.Count * 4);
            if (cells == null) return all;
            foreach (var c in cells) all.Add(new EditImage(c, false, false));
            if (!MirrorActive) return all;
            var g = activeGuides;
            int n = cells.Count;
            for (int i = 0; i < n; i++)
            {
                var c = cells[i];
                if (g.hasAxisX) all.Add(new EditImage(new Vector2Int(MirrorX(c.x), c.y), true, false));
                if (g.hasAxisY) all.Add(new EditImage(new Vector2Int(c.x, MirrorY(c.y)), false, true));
                if (g.hasAxisX && g.hasAxisY)
                    all.Add(new EditImage(new Vector2Int(MirrorX(c.x), MirrorY(c.y)), true, true));
            }
            return all;
        }

        /// The positions alone — for the callers that place ONE tile per cell (a flood, the selection
        /// multi-edit) or merely draw an outline, where there is no arrangement to reverse.
        internal List<Vector2Int> MirrorCells(List<Vector2Int> cells)
        {
            if (!MirrorActive || cells == null || cells.Count == 0) return cells;
            var images = MirrorImagesOf(cells);
            var all = new List<Vector2Int>(images.Count);
            foreach (var im in images) all.Add(im.cell);
            return all;
        }

        /// The ghost's ART under the mirror: the same sprite at the reflected cell. Deliberately the SAME
        /// sprite — that is what the commit does, because a tile cannot flip, so the preview shows the
        /// author the mirrored-position-only result BEFORE they click rather than after.
        internal List<(Vector2Int cell, Sprite sprite)> MirrorArt(List<(Vector2Int cell, Sprite sprite)> art)
        {
            if (!MirrorActive || art == null || art.Count == 0) return art;
            var g = activeGuides;
            int n = art.Count;
            var all = new List<(Vector2Int, Sprite)>(art);
            for (int i = 0; i < n; i++)
            {
                var (c, s) = art[i];
                if (g.hasAxisX) all.Add((new Vector2Int(MirrorX(c.x), c.y), s));
                if (g.hasAxisY) all.Add((new Vector2Int(c.x, MirrorY(c.y)), s));
                if (g.hasAxisX && g.hasAxisY) all.Add((new Vector2Int(MirrorX(c.x), MirrorY(c.y)), s));
            }
            return all;
        }

        int MirrorX(int x) => 2 * activeGuides.axisX - x - 1;
        int MirrorY(int y) => 2 * activeGuides.axisY - y - 1;

        /// One edit's landing place under the mirror. A struct rather than a tuple because it is threaded
        /// through five call sites and "which bool was which" is exactly the bug this feature cannot afford.
        internal readonly struct EditImage
        {
            public readonly Vector2Int cell;
            public readonly bool flipX, flipY;
            public EditImage(Vector2Int cell, bool flipX, bool flipY)
            {
                this.cell = cell;
                this.flipX = flipX;
                this.flipY = flipY;
            }
            public bool IsPrimary => !flipX && !flipY;
        }

        /// Flip a brush offset for a mirrored image. Paired with the mirrored ANCHOR this reproduces the
        /// mirror of every absolute cell exactly — see <see cref="MirrorCells"/>.
        internal static Vector2Int FlipOffset(Vector2Int off, bool flipX, bool flipY) =>
            new(flipX ? -off.x : off.x, flipY ? -off.y : off.y);

        /// The rotation + mirror flag a STAMP needs so its footprint reflects about the axis.
        ///
        /// A placement stores `rotation` and `mirrorX`, and those two COMPOSE: mirrorX negates the offset's x
        /// BEFORE the quarter-turns, so at 0° and 180° toggling it flips the footprint in X, while at 90° and
        /// 270° the same toggle flips it in Y. The corrections fall out of that:
        ///   flip X  → toggle mirrorX, and add a half-turn when the rotation is odd;
        ///   flip Y  → toggle mirrorX, and add a half-turn when the rotation is even;
        ///   both    → a half-turn, mirrorX unchanged (a point reflection).
        ///
        /// ⚠️ This mirrors the ARRANGEMENT of a stamp's cells, not the ART inside them. `mirrorX` on a
        /// placement is a real flip of the layout; each cell still draws its own unflipped sprite, because a
        /// LevelTile has no flip. A mirrored wall corner therefore lands in the right CELL wearing the wrong
        /// corner. Nothing here can fix that — it is a property of the tile model.
        internal static void MirrorStamp(int rotation, bool mirrorX, bool flipX, bool flipY,
            out int rot, out bool mir)
        {
            rot = ((rotation % 4) + 4) % 4;
            mir = mirrorX;
            if (flipX && flipY) { rot = (rot + 2) % 4; return; }
            if (flipX) { if ((rot & 1) == 1) rot = (rot + 2) % 4; mir = !mir; return; }
            if (flipY) { if ((rot & 1) == 0) rot = (rot + 2) % 4; mir = !mir; }
        }

        /// A one-line description of the live mirror, for the status bar — "Mirror x=16" / "Mirror x=16,y=9".
        internal string MirrorSummary()
        {
            if (!MirrorActive) return null;
            var g = activeGuides;
            if (g.hasAxisX && g.hasAxisY) return $"Mirror x={g.axisX}, y={g.axisY}";
            return g.hasAxisX ? $"Mirror x={g.axisX}" : $"Mirror y={g.axisY}";
        }

        // ── the Tool card's guides row ─────────────────────────────────────────
        /// The guides row: master switch, snap, mirror, and an explicit Clear. Its own row rather than joined
        /// to the grid-lines/prop-markers dials above because all three are NEW ideas that have to be named —
        /// three more unlabelled glyphs on a strip that already has two would be unreadable, and this row is
        /// the only place the whole feature announces that it exists.
        VisualElement GuidesRow()
        {
            int nV = activeGuides?.vertical.Count ?? 0;
            int nH = activeGuides?.horizontal.Count ?? 0;
            int n = nV + nH;
            string lvl = level != null ? level.name : "this level";

            const string what =
                "RULER GUIDES — lines that sit on CELL BOUNDARIES, for aligning and for symmetry. Turns on " +
                "the rulers along the canvas's top and left edges: DRAG OUT of a ruler to make a guide, drag " +
                "a guide to move it, drag it back onto its ruler to delete it, and CLICK one to make it the " +
                "mirror axis. Guides are per level, are saved with the window rather than in the level asset, " +
                "and are not undoable.  ";
            var guidesToggle = Z.ToggleButton("Guides",
                what + (showGuides
                    ? n == 0 ? "On, and this level has none yet — drag one out of a ruler."
                             : $"On, with {nV} vertical and {nH} horizontal on '{lvl}'."
                    : "Off: the rulers, the lines, snapping and mirroring are all hidden."),
                showGuides, SetShowGuides);

            string snapTip =
                "SNAP TO GUIDES: pull what you are about to place onto the nearest guide, so a wall butts " +
                $"exactly against the line instead of one cell off. Reaches {SnapRadiusCells} cells, and snaps " +
                "the brush's whole footprint — a 3-wide clump lands with an EDGE on the line, not its centre. " +
                "Separate from showing guides, because some authoring wants the lines as a reference only.  ";
            snapTip += !showGuides ? "Disabled: guides are off."
                : n == 0 ? "Disabled: nothing to snap to yet — drag a guide out of a ruler."
                : snapToGuides ? "On." : "Off — the lines are a visual reference only.";
            var snapToggle = Z.ToggleButton("Snap", snapTip, snapToGuides,
                v => { snapToGuides = v; canvas?.RepaintOverlay(); UpdateCanvasStatus(); SaveWindowState(); });
            snapToggle.SetEnabled(showGuides && n > 0);
            snapToggle.tooltip = snapTip;

            string mirrorTip =
                "MIRROR: every paint, erase, fill and stamp also lands reflected in the mirror axis, in ONE " +
                "undo step. Promote a guide to the axis by CLICKING it on the canvas (a vertical one mirrors " +
                "left↔right, a horizontal one up↔down; both at once gives four-way symmetry).  " +
                "⚠️ It mirrors POSITION and brush ARRANGEMENT exactly — it cannot mirror the ART, because a " +
                "tile has no flip. A mirrored wall corner lands in the right cell wearing the same corner " +
                "sprite, so hand-swap the corner tiles afterwards.  ";
            mirrorTip += !showGuides ? "Disabled: guides are off."
                : !MirrorAxisExists ? "Disabled: no axis yet — click a guide on the canvas to promote one."
                : mirrorGuides ? MirrorSummary() + "." : "Off.";
            var mirrorToggle = Z.ToggleButton("Mirror", mirrorTip, mirrorGuides,
                v => { mirrorGuides = v; canvas?.RepaintOverlay(); UpdateCanvasStatus(); SaveWindowState(); });
            mirrorToggle.SetEnabled(showGuides && MirrorAxisExists);
            mirrorToggle.tooltip = mirrorTip;

            // The discoverable twin of "drag it off the edge": dragging a thing away to destroy it is only
            // findable by someone who already knows the idiom, so the same outcome gets a named button.
            var clear = Z.Button("Clear guides",
                n == 0 ? "Remove every guide from this level. Nothing to remove — this level has none."
                       : $"Remove all {n} guide{(n == 1 ? "" : "s")} from '{lvl}', and the mirror axis " +
                         "with them. NOT undoable — guides are window state, not level data.",
                () => { ClearGuides(); AfterToolCardAction(); });
            clear.SetEnabled(n > 0);

            return Z.Row(guidesToggle, snapToggle, mirrorToggle, Z.HSpace(), clear);
        }
    }

    public partial class CartographerWindow
    {
        /// The canvas half of guides: the ruler strips, the lines, and the pull/move/delete gesture. Kept in
        /// this file with the model rather than in Canvas.cs so the whole feature reads in one place; the only
        /// hooks Canvas.cs owns are the four call sites (build, view, the three pointer handlers, the painter).
        internal partial class LevelCanvas
        {
            /// The ruler gutter, in host pixels. Wide enough for a three-character number at 9pt in the LEFT
            /// strip, which is the binding constraint (the top strip could be thinner, but two different
            /// widths would make the corner a wedge).
            internal const float RulerPx = 18f;

            float RulerSize => w.showGuides ? RulerPx : 0f;

            VisualElement rulerLayer;
            readonly List<Label> rulerPool = new();

            // The gesture in flight. A guide that is being dragged is REMOVED from the set and held here, so
            // there is exactly one drawing path for a committed guide and one for the one in the hand.
            bool guideDrag;
            bool guideVertical;      // the guide in flight runs top-to-bottom (a constant X)
            int guideValue;          // its current, always-snapped, boundary value
            int guideOrigin;         // where it started; irrelevant for a new one
            bool guideNew;           // pulled out of a ruler — cancelling it leaves nothing behind
            bool guideWasAxis;       // it was the mirror axis, and must still be after the move
            bool guideMoved;         // it left its starting value: a click is what did NOT happen
            bool guideKill;          // the pointer is over its own ruler — releasing deletes it

            // Hover feedback: which guide is grabbable right now, and which ruler is under the pointer.
            bool guideHover, guideHoverVertical;
            int guideHoverValue;
            int rulerHover;          // 0 none · 1 top · 2 left

            /// Guides and the mirror axis draw in MAGENTA, and nothing else on this canvas does. Every other
            /// hue is already spoken for and would be read as something it is not: the bounds rect is white,
            /// the ghost orange, the eraser red, the marquee and the selection blue, prop markers wear each
            /// prop's own colour and tag tints wear each tag author's. Magenta is the one strong hue left —
            /// and colour is only half of it, because tag and marker colours are arbitrary and one of them
            /// COULD be magenta: a guide is also the only thing here drawn as an unbroken line across the
            /// whole pane, while markers and tags are always cell-sized boxes. Hue plus shape, so neither has
            /// to carry it alone.
            static readonly Color GuideColor = new(1f, 0.24f, 0.78f, 0.80f);
            static readonly Color GuideHotColor = new(1f, 0.55f, 0.95f, 1f);
            static readonly Color GuideKillColor = new(1f, 0.35f, 0.28f, 0.95f);
            static readonly Color RulerBg = new(0.10f, 0.10f, 0.12f, 0.95f);
            static readonly Color RulerInk = new(0.72f, 0.73f, 0.78f, 0.85f);

            /// The pane the level is fitted into: the canvas less the ruler gutters. ZuiPanZoom is handed the
            /// same inset, so a strip can never cover a cell.
            Rect Viewport()
            {
                var r = contentRect;
                float g = RulerSize;
                return new Rect(g, g, Mathf.Max(0f, r.width - g), Mathf.Max(0f, r.height - g));
            }

            float Step => cellPx * view.Scale;

            float GuideXPx(int g) => view.Origin.x + (g - viewRect.xMin) * Step;
            float GuideYPx(int g) => view.Origin.y + (viewRect.yMax - g) * Step;

            int GuideXAt(float localX) => Mathf.Clamp(
                Mathf.RoundToInt((localX - view.Origin.x) / Step + viewRect.xMin), viewRect.xMin, viewRect.xMax);

            int GuideYAt(float localY) => Mathf.Clamp(
                Mathf.RoundToInt(viewRect.yMax - (localY - view.Origin.y) / Step), viewRect.yMin, viewRect.yMax);

            /// HOW NEAR THE POINTER MUST BE to grab a guide instead of painting. This number is the whole
            /// difference between a help and a hindrance: a generous grab zone would swallow the paint
            /// gesture along every guide the author has placed, and a feature that makes it harder to paint
            /// beside the line you drew to paint beside is worse than not having it.
            ///
            /// So: 4 device pixels, and never more than a QUARTER of a cell. At a working zoom that is 4px
            /// out of a 40px cell — a tenth of the cell either side of the boundary. Zoomed out to 8px cells
            /// it shrinks to 2px, so the cell beside a guide stays clickable at every zoom the canvas has.
            /// It is also only ever consulted for a plain left press: Alt (erase), Ctrl (pick), right-drag
            /// (marquee) and middle (pan) never grab a guide at all.
            float GrabPx => Mathf.Min(4f, Mathf.Max(1.5f, Step * 0.25f));

            /// The nearest guide within grabbing distance of `local`, if any.
            bool FindGuideNear(Vector2 local, out bool vertical, out int value)
            {
                vertical = false;
                value = 0;
                var gs = w.ActiveGuides;
                if (gs == null || !w.showGuides || Step <= 0.001f) return false;
                if (!Viewport().Contains(local)) return false;

                float best = GrabPx;
                bool found = false;
                foreach (int g in gs.vertical)
                {
                    float d = Mathf.Abs(GuideXPx(g) - local.x);
                    if (d > best) continue;
                    best = d; vertical = true; value = g; found = true;
                }
                foreach (int g in gs.horizontal)
                {
                    float d = Mathf.Abs(GuideYPx(g) - local.y);
                    if (d > best) continue;
                    best = d; vertical = false; value = g; found = true;
                }
                return found;
            }

            /// The ruler a guide of this orientation can reach by MOVING, which is also the one it came out
            /// of: a horizontal guide only travels in Y, so the top strip is the only one it can be dragged
            /// back into; a vertical guide only travels in X, so the left strip is its own.
            bool InOwningRuler(Vector2 local) =>
                guideVertical ? local.x < RulerSize : local.y < RulerSize;

            // ── the gesture ────────────────────────────────────────────────────
            /// A left press that belongs to guides: in a ruler (pull a new one out) or on a guide (move it).
            /// Returns false for everything else, and the canvas's own press handling continues untouched.
            internal bool TryBeginGuide(PointerDownEvent e)
            {
                if (e.button != 0 || e.ctrlKey || e.altKey) return false;
                if (!BeginGuideAt(e.localPosition)) return false;
                Focus();
                this.CapturePointer(e.pointerId);
                RepaintOverlay();
                w.UpdateCanvasStatus();
                e.StopPropagation();
                return true;
            }

            /// THE DECISION, with no pointer plumbing around it: does a plain left press at `local` belong to
            /// guides, and if so which one is now in the hand? Split out from <see cref="TryBeginGuide"/> so
            /// the "does the grab zone steal the paint gesture?" question can be ASKED — of the real
            /// hit-testing, at real coordinates — instead of argued about. Its twins are
            /// <see cref="DragGuideTo"/> and <see cref="CommitGuideDrag"/>.
            internal bool BeginGuideAt(Vector2 local)
            {
                if (!w.showGuides || w.level == null) return false;
                if (viewRect.width <= 0 || Step <= 0.001f) return false;
                if (w.ActiveGuides == null) return false;

                float g = RulerSize;
                // The corner square belongs to neither strip: what you would be pulling out of it is
                // ambiguous, so it does nothing rather than guessing.
                bool top = local.y < g && local.x >= g;
                bool left = local.x < g && local.y >= g;

                if (top) StartGuide(false, GuideYAt(local.y), true);
                else if (left) StartGuide(true, GuideXAt(local.x), true);
                else if (FindGuideNear(local, out bool v, out int val)) StartGuide(v, val, false);
                else return false;

                guideKill = InOwningRuler(local);
                return true;
            }

            void StartGuide(bool vertical, int value, bool isNew)
            {
                var gs = w.ActiveGuides;
                guideDrag = true;
                guideVertical = vertical;
                guideValue = value;
                guideOrigin = value;
                guideNew = isNew;
                guideMoved = false;
                guideWasAxis = false;
                if (isNew) return;
                (vertical ? gs.vertical : gs.horizontal).Remove(value);
                guideWasAxis = vertical ? gs.hasAxisX && gs.axisX == value
                                        : gs.hasAxisY && gs.axisY == value;
            }

            internal bool UpdateGuideDrag(PointerMoveEvent e)
            {
                if (!guideDrag) return false;
                DragGuideTo(e.localPosition);
                e.StopPropagation();
                return true;
            }

            /// The move half. Snapped by construction — <see cref="GuideXAt"/>/<see cref="GuideYAt"/> round to
            /// a cell boundary and clamp to the view, so a guide cannot be dragged off the grid or off the map.
            internal void DragGuideTo(Vector2 local)
            {
                if (!guideDrag) return;
                int v = guideVertical ? GuideXAt(local.x) : GuideYAt(local.y);
                if (v != guideValue) { guideValue = v; guideMoved = true; }
                guideKill = InOwningRuler(local);
                if (guideKill) guideMoved = true;   // a trip to the ruler was never a click
                UpdateHover(local);                 // the cell readout keeps working while dragging a guide
                RepaintOverlay();
                w.UpdateCanvasStatus();
            }

            internal bool EndGuideDrag(PointerUpEvent e)
            {
                if (!guideDrag) return false;
                this.ReleasePointer(e.pointerId);
                CommitGuideDrag();
                e.StopPropagation();
                return true;
            }

            /// The release half: delete, place, or promote. See <see cref="BeginGuideAt"/> for why the three
            /// halves are callable without an event.
            internal void CommitGuideDrag()
            {
                if (!guideDrag) return;
                guideDrag = false;
                var gs = w.ActiveGuides;
                var list = guideVertical ? gs.vertical : gs.horizontal;
                string axisName = guideVertical ? "x" : "y";

                if (guideKill)
                {
                    // Dropped back on its own ruler. A guide that was never committed simply never happens.
                    if (!guideNew)
                    {
                        if (guideVertical && gs.hasAxisX && gs.axisX == guideOrigin) gs.hasAxisX = false;
                        if (!guideVertical && gs.hasAxisY && gs.axisY == guideOrigin) gs.hasAxisY = false;
                        w.ReportCanvasStatus($"Guide {axisName}={guideOrigin} deleted.");
                    }
                }
                else
                {
                    if (!list.Contains(guideValue)) list.Add(guideValue);
                    list.Sort();
                    if (!guideNew && !guideMoved) TogglePromote(gs, guideVertical, guideValue, axisName);
                    else if (guideWasAxis) Promote(gs, guideVertical, guideValue);
                    else if (guideNew)
                        w.ReportCanvasStatus($"Guide {axisName}={guideValue} — click it to make it the mirror " +
                                             "axis, drag it back onto the ruler to delete it.");
                }
                w.GuidesChanged();
                RepaintOverlay();
                w.UpdateCanvasStatus();
            }

            /// A CLICK on a guide (a press and release that never moved it) is what promotes it to the mirror
            /// axis, and clicking it again demotes it. The action lives on the object it acts on rather than
            /// in a menu somewhere else, and it costs nothing: a press within grabbing distance of a guide was
            /// never going to paint anyway.
            void TogglePromote(GuideSet gs, bool vertical, int value, string axisName)
            {
                bool isAxis = vertical ? gs.hasAxisX && gs.axisX == value : gs.hasAxisY && gs.axisY == value;
                if (isAxis)
                {
                    if (vertical) gs.hasAxisX = false; else gs.hasAxisY = false;
                    w.ReportCanvasStatus($"Guide {axisName}={value} is a plain guide again.");
                    return;
                }
                Promote(gs, vertical, value);
                w.ReportCanvasStatus($"Guide {axisName}={value} is the mirror axis — turn Mirror on in the " +
                                     "Tool card (right-click) and edits land on both sides.");
            }

            static void Promote(GuideSet gs, bool vertical, int value)
            {
                // At most one axis per orientation: promoting a second vertical guide demotes the first,
                // because "mirror about two different vertical lines at once" has no meaning.
                if (vertical) { gs.hasAxisX = true; gs.axisX = value; }
                else { gs.hasAxisY = true; gs.axisY = value; }
            }

            /// Esc during a guide drag puts it back where it was (or throws away one that was never placed).
            internal bool CancelGuideDrag()
            {
                if (!guideDrag) return false;
                guideDrag = false;
                var gs = w.ActiveGuides;
                if (!guideNew && gs != null)
                {
                    var list = guideVertical ? gs.vertical : gs.horizontal;
                    if (!list.Contains(guideOrigin)) list.Add(guideOrigin);
                    list.Sort();
                    if (guideWasAxis) Promote(gs, guideVertical, guideOrigin);
                }
                RepaintOverlay();
                w.UpdateCanvasStatus();
                return true;
            }

            /// Recompute what the pointer is over, for the grab highlight and the status line's hint.
            internal void UpdateGuideHover(Vector2 local)
            {
                int wasRuler = rulerHover;
                bool wasHover = guideHover;
                int wasValue = guideHoverValue;
                bool wasVertical = guideHoverVertical;

                rulerHover = 0;
                guideHover = false;
                if (w.showGuides && !guideDrag && w.level != null && viewRect.width > 0)
                {
                    float g = RulerSize;
                    if (local.y < g && local.x >= g) rulerHover = 1;
                    else if (local.x < g && local.y >= g) rulerHover = 2;
                    else guideHover = FindGuideNear(local, out guideHoverVertical, out guideHoverValue);
                }
                if (rulerHover != wasRuler || guideHover != wasHover
                    || guideHoverValue != wasValue || guideHoverVertical != wasVertical)
                    RepaintOverlay();
            }

            internal void ClearGuideHover()
            {
                if (rulerHover == 0 && !guideHover) return;
                rulerHover = 0;
                guideHover = false;
                RepaintOverlay();
            }

            /// The pointer is inside a ruler strip, so the canvas's own press/paint handling must not run —
            /// a click on the chrome is not a click on the level.
            internal bool PointerInRuler(Vector2 local)
            {
                float g = RulerSize;
                return g > 0f && (local.x < g || local.y < g);
            }

            /// True while a guide gesture owns the pointer — the paint paths sit out until it is over.
            internal bool GuideDragging => guideDrag;

            /// What the status line says about guides right now: the TEACHING channel. Someone who has never
            /// been told this feature exists finds it by putting the pointer on a ruler and reading the line
            /// that is always there — the same way they find out what a prop marker is.
            internal string GuideHint()
            {
                if (!w.showGuides) return null;
                if (guideDrag)
                {
                    string a = guideVertical ? "x" : "y";
                    if (guideKill)
                        return guideNew ? "Drag onto the canvas to place this guide"
                                        : $"Release to DELETE guide {a}={guideOrigin}";
                    return $"Guide {a}={guideValue}" + (guideWasAxis ? " (mirror axis)" : "");
                }
                if (rulerHover != 0)
                    return rulerHover == 1
                        ? "Ruler — drag DOWN out of it for a horizontal guide"
                        : "Ruler — drag RIGHT out of it for a vertical guide";
                if (guideHover)
                {
                    var gs = w.ActiveGuides;
                    bool axis = guideHoverVertical ? gs.hasAxisX && gs.axisX == guideHoverValue
                                                   : gs.hasAxisY && gs.axisY == guideHoverValue;
                    string a = guideHoverVertical ? "x" : "y";
                    return axis
                        ? $"Guide {a}={guideHoverValue} (MIRROR AXIS) — drag to move, click to unset, drop on the ruler to delete"
                        : $"Guide {a}={guideHoverValue} — drag to move, click to make it the mirror axis, drop on the ruler to delete";
                }
                return w.MirrorSummary();
            }

            // ── drawing ────────────────────────────────────────────────────────
            /// Built once, in front of the Painter2D overlay: the ruler NUMBERS. Painter2D draws no text, and
            /// the ghost's pooled-Image lesson applies here for the same reason — a repaint must not allocate.
            internal void BuildRulerLayer()
            {
                rulerLayer = new VisualElement { pickingMode = PickingMode.Ignore };
                rulerLayer.style.position = Position.Absolute;
                rulerLayer.style.left = rulerLayer.style.top =
                    rulerLayer.style.right = rulerLayer.style.bottom = 0f;
                rulerLayer.style.overflow = Overflow.Hidden;
                Add(rulerLayer);
            }

            /// Tick spacings to choose from — the 1/2/5 progression every ruler in every tool uses, so the
            /// numbers stay round however far the canvas is zoomed.
            static readonly int[] TickSteps = { 1, 2, 5, 10, 20, 25, 50, 100, 200, 500, 1000, 2000, 5000 };

            static int TickStepFor(float stepPx, float minPx)
            {
                foreach (int s in TickSteps) if (s * stepPx >= minPx) return s;
                return TickSteps[TickSteps.Length - 1];
            }

            /// Position the ruler numbers. Pooled and hidden rather than removed, exactly like the ghost's
            /// art: this runs on every repaint, including every pointer move.
            void SyncRulerLabels()
            {
                if (rulerLayer == null) return;
                int used = 0;
                if (w.showGuides && w.level != null && viewRect.width > 0 && Step > 0.001f)
                {
                    var vp = Viewport();
                    int every = TickStepFor(Step, 46f);

                    for (int c = Mathf.CeilToInt(viewRect.xMin / (float)every) * every; c <= viewRect.xMax; c += every)
                    {
                        float px = GuideXPx(c);
                        if (px < vp.xMin - 1f || px > vp.xMax + 1f) continue;
                        var l = RulerLabel(used++);
                        l.text = c.ToString();
                        l.style.left = px - 18f;
                        l.style.top = 3f;
                        l.style.width = 36f;
                        l.style.unityTextAlign = TextAnchor.UpperCenter;
                    }
                    for (int r = Mathf.CeilToInt(viewRect.yMin / (float)every) * every; r <= viewRect.yMax; r += every)
                    {
                        float py = GuideYPx(r);
                        if (py < vp.yMin - 1f || py > vp.yMax + 1f) continue;
                        var l = RulerLabel(used++);
                        l.text = r.ToString();
                        l.style.left = 0f;
                        l.style.top = py - 7f;
                        l.style.width = RulerPx - 4f;
                        l.style.unityTextAlign = TextAnchor.UpperRight;
                    }
                }
                for (int i = used; i < rulerPool.Count; i++)
                    if (rulerPool[i].style.display != DisplayStyle.None)
                        rulerPool[i].style.display = DisplayStyle.None;
            }

            Label RulerLabel(int i)
            {
                while (rulerPool.Count <= i)
                {
                    var made = new Label { pickingMode = PickingMode.Ignore };
                    made.style.position = Position.Absolute;
                    made.style.fontSize = 9f;
                    made.style.color = RulerInk;
                    rulerLayer.Add(made);
                    rulerPool.Add(made);
                }
                rulerPool[i].style.display = DisplayStyle.Flex;
                return rulerPool[i];
            }

            /// Guides, then the ruler strips — drawn LAST in the overlay so a guide line can never bleed into
            /// the chrome, and so the strips cover a ghost tile that spills past the level's edge.
            internal void PaintGuides(Painter2D p)
            {
                if (!w.showGuides || Step <= 0.001f) return;
                var vp = Viewport();
                var gs = w.ActiveGuides;

                if (gs != null)
                {
                    foreach (int g in gs.vertical)
                        DrawGuide(p, vp, true, g, gs.hasAxisX && gs.axisX == g,
                            guideHover && guideHoverVertical && guideHoverValue == g, false);
                    foreach (int g in gs.horizontal)
                        DrawGuide(p, vp, false, g, gs.hasAxisY && gs.axisY == g,
                            guideHover && !guideHoverVertical && guideHoverValue == g, false);
                }
                if (guideDrag && !(guideKill && guideNew))
                    DrawGuide(p, vp, guideVertical, guideValue, guideWasAxis, true, guideKill);

                PaintRulers(p, vp);
            }

            /// One guide. A plain guide is a single hairline; the MIRROR AXIS is a DOUBLED line — told apart
            /// by shape, not only by brightness, so it still reads for an author who cannot separate the two
            /// magentas. `hot` (hovered or in hand) widens and brightens it: a thing you can grab has to look
            /// grabbable before you press, not after.
            void DrawGuide(Painter2D p, Rect vp, bool vertical, int value, bool axis, bool hot, bool kill)
            {
                float pos = vertical ? GuideXPx(value) : GuideYPx(value);
                if (vertical) { if (pos < vp.xMin - 1f || pos > vp.xMax + 1f) return; }
                else { if (pos < vp.yMin - 1f || pos > vp.yMax + 1f) return; }

                p.strokeColor = kill ? GuideKillColor : hot ? GuideHotColor : GuideColor;
                p.lineWidth = hot ? 2.5f : 1f;
                if (axis)
                {
                    p.lineWidth = hot ? 2f : 1.25f;
                    Line(p, vp, vertical, pos - 2f);
                    Line(p, vp, vertical, pos + 2f);
                }
                else Line(p, vp, vertical, pos);
            }

            static void Line(Painter2D p, Rect vp, bool vertical, float pos)
            {
                p.BeginPath();
                if (vertical)
                {
                    p.MoveTo(new Vector2(pos, vp.yMin));
                    p.LineTo(new Vector2(pos, vp.yMax));
                }
                else
                {
                    p.MoveTo(new Vector2(vp.xMin, pos));
                    p.LineTo(new Vector2(vp.xMax, pos));
                }
                p.Stroke();
            }

            static void FillRect(Painter2D p, Rect r)
            {
                p.BeginPath();
                p.MoveTo(new Vector2(r.xMin, r.yMin));
                p.LineTo(new Vector2(r.xMax, r.yMin));
                p.LineTo(new Vector2(r.xMax, r.yMax));
                p.LineTo(new Vector2(r.xMin, r.yMax));
                p.ClosePath();
                p.Fill();
            }

            /// The two strips: an opaque band, a hairline against the canvas, ticks, and a magenta wash on
            /// whichever strip the pointer is in — a surface you can pull something out of should react to
            /// being touched, which is the only hint a first-time author gets before they read the status line.
            void PaintRulers(Painter2D p, Rect vp)
            {
                var full = contentRect;
                float g = RulerPx;

                p.fillColor = RulerBg;
                FillRect(p, new Rect(0f, 0f, full.width, g));
                FillRect(p, new Rect(0f, g, g, Mathf.Max(0f, full.height - g)));

                if (rulerHover == 1 || (guideDrag && !guideVertical && guideKill))
                {
                    p.fillColor = new Color(GuideColor.r, GuideColor.g, GuideColor.b, 0.16f);
                    FillRect(p, new Rect(g, 0f, Mathf.Max(0f, full.width - g), g));
                }
                if (rulerHover == 2 || (guideDrag && guideVertical && guideKill))
                {
                    p.fillColor = new Color(GuideColor.r, GuideColor.g, GuideColor.b, 0.16f);
                    FillRect(p, new Rect(0f, g, g, Mathf.Max(0f, full.height - g)));
                }

                p.strokeColor = new Color(1f, 1f, 1f, 0.14f);
                p.lineWidth = 1f;
                p.BeginPath();
                p.MoveTo(new Vector2(0f, g));
                p.LineTo(new Vector2(full.width, g));
                p.MoveTo(new Vector2(g, g));
                p.LineTo(new Vector2(g, full.height));
                p.Stroke();

                if (w.level == null || viewRect.width <= 0) return;

                int minor = TickStepFor(Step, 7f);
                int major = TickStepFor(Step, 46f);
                p.strokeColor = RulerInk;
                p.lineWidth = 1f;
                p.BeginPath();
                for (int c = Mathf.CeilToInt(viewRect.xMin / (float)minor) * minor; c <= viewRect.xMax; c += minor)
                {
                    float px = GuideXPx(c);
                    if (px < vp.xMin - 1f || px > vp.xMax + 1f) continue;
                    float len = c % major == 0 ? g : 4f;
                    p.MoveTo(new Vector2(px, g - len));
                    p.LineTo(new Vector2(px, g));
                }
                for (int r = Mathf.CeilToInt(viewRect.yMin / (float)minor) * minor; r <= viewRect.yMax; r += minor)
                {
                    float py = GuideYPx(r);
                    if (py < vp.yMin - 1f || py > vp.yMax + 1f) continue;
                    float len = r % major == 0 ? g : 4f;
                    p.MoveTo(new Vector2(g - len, py));
                    p.LineTo(new Vector2(g, py));
                }
                p.Stroke();
            }

            /// The ruler numbers ride along with every overlay repaint — see RepaintOverlay, which is the one
            /// door for "the preview changed" and must move every layer of it together.
            internal void SyncRulers() => SyncRulerLabels();

            /// Re-run the view maths without rebuilding any content — what toggling the ruler gutter needs.
            internal void RefitView()
            {
                ApplyView();
                RepaintOverlay();
            }
        }
    }
}
