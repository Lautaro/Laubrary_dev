using UnityEngine;

namespace Laubrary.Shaper
{
    /// <summary>
    /// T-0115 -- the canvas/grid numbers this task's cost model is budgeted against, measured from the
    /// reference tool's actual source rather than assumed (design B9's own correction of an earlier wrong
    /// "16 to 256 square, default 64" claim). Laubrary's own Shaper (this asmdef) has no document/window/
    /// canvas-grid layer yet as of T-0115 -- Wave 2/3 built the compile/evaluate backbone only
    /// (<see cref="ShaperCompiler"/>/<see cref="ShaperEvaluator"/>), not an authored document asset or an
    /// editor window with a resolution field. These constants are therefore the DESIGN TARGET a future
    /// document/window layer must honour, ported verbatim from the actual reference implementation this
    /// rebuild replaces:
    ///
    /// <list type="bullet">
    /// <item><b>Schema bounds</b> -- <c>D:\CODEZ\AgentHQ\3D Shaper\project_document.py:79</c>:
    /// <c>"resolution": (32.0, 256.0)</c>, and <c>:152</c>: <c>bounded_integer(source.get("resolution"), 96, 32, 256)</c>
    /// -- a saved document's resolution is bounded 32-256 and defaults to 96. Verified by reading both lines
    /// directly (not copied from a prior report).</item>
    /// <item><b>Renderer clamp</b> -- <c>D:\CODEZ\AgentHQ\3D Shaper\public\index.html:1372</c>:
    /// <c>const gridW = clampTo(Math.round(Number(source.resolution) || 96), 16, 256);</c> -- the renderer
    /// itself clamps 16-256, but 16 is unreachable through a saved document since the schema floor is 32.</item>
    /// <item><b>Non-square grid</b> -- <c>:1373</c>: <c>let gridH = Math.max(8, Math.round(gridW * (source.height /
    /// Math.max(1, source.width))));</c> -- grid HEIGHT is derived from the canvas aspect ratio, never a
    /// separate resolution dial and never forced square.</item>
    /// <item><b>Cell cap</b> -- <c>:982</c>: <c>MAX_GRID_CELLS = 68000</c>, enforced at <c>:1374</c>:
    /// <c>if (gridW*gridH>MAX_GRID_CELLS) gridH=Math.max(8,Math.floor(MAX_GRID_CELLS/gridW));</c>.</item>
    /// <item><b>Default canvas</b> -- <c>project_document.py</c>'s <c>normalise_canvas</c>:
    /// <c>bounded_integer(source.get("width"), 960, 1, 8192)</c> / height default 640 -- so the default document
    /// is 960x640 at resolution 96, which <see cref="ComputeGrid"/> below resolves to exactly 96x64 = 6,144
    /// cells, matching the design doc's own worked example.</item>
    /// </list>
    /// </summary>
    public static class ShaperCacheBudget
    {
        public const int SchemaResolutionMin = 32;
        public const int SchemaResolutionMax = 256;
        public const int SchemaResolutionDefault = 96;

        public const int RendererClampMin = 16;
        public const int RendererClampMax = 256;

        public const int MaxGridCells = 68000;

        public const int DefaultCanvasWidth = 960;
        public const int DefaultCanvasHeight = 640;

        /// <summary>The typical-case cell count this task's cost model budgets against: the default 960x640
        /// canvas at the default resolution 96, which <see cref="ComputeGrid"/> resolves to 96x64.</summary>
        public const int TypicalCells = 96 * 64;   // = 6144, asserted equal to ComputeGrid's own result by a test

        /// <summary>
        /// A faithful port of <c>renderModelGrid</c>'s own grid-sizing arithmetic
        /// (<c>public/index.html:1372-1374</c>), so a caller can compute the SAME (gridW, gridH, cells) the
        /// reference renderer would for any (resolution, canvasWidth, canvasHeight) -- used by this task's
        /// budget tests rather than a second, hand-derived formula that could silently drift from the real one.
        /// </summary>
        public static void ComputeGrid(int resolution, int canvasWidth, int canvasHeight, out int gridW, out int gridH, out int cells)
        {
            gridW = Mathf.Clamp(Mathf.RoundToInt(resolution <= 0 ? RendererClampMax : resolution), RendererClampMin, RendererClampMax);
            float aspect = canvasHeight / Mathf.Max(1f, canvasWidth);
            // long arithmetic here (not int, not float-only): an extreme aspect ratio (the schema allows
            // width/height down to 1) can make gridW*gridH overflow a 32-bit int BEFORE the cap below has a
            // chance to shrink it -- a real bug this task's own audit caught (CT0 first ran with plain int
            // multiplication and silently produced a negative cell count instead of tripping the cap).
            long gridHLong = System.Math.Max(8L, (long)Mathf.Round(gridW * aspect));
            long cellsLong = (long)gridW * gridHLong;
            if (cellsLong > MaxGridCells)
                gridHLong = System.Math.Max(8L, MaxGridCells / gridW);
            gridH = (int)System.Math.Min(gridHLong, int.MaxValue);
            cells = (int)System.Math.Min((long)gridW * gridH, int.MaxValue);
        }

        /// <summary>The absolute worst case under the SCHEMA's own bounds (not the renderer's wider clamp,
        /// since 16 is unreachable through a saved document): resolution 256 on a canvas whose aspect makes
        /// height also want to be large, capped at <see cref="MaxGridCells"/> regardless.</summary>
        public static void WorstCaseGrid(out int gridW, out int gridH, out int cells)
            => ComputeGrid(SchemaResolutionMax, 1, 100000, out gridW, out gridH, out cells);   // extreme aspect forces the 68,000-cell cap to bind
    }
}
