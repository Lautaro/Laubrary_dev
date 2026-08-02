using System;
using System.Collections.Generic;
using FOW;
using UnityEngine;

namespace Laubrary.Fov
{
    /// How what a revealer has SEEN persists on screen. Three switchable behaviours, A/B-testable
    /// mid-play by flipping the enum in the Inspector.
    public enum FovRevealMode
    {
        /// No persistence at all: only the live cone/aura shows anything (memory floor 0).
        LiveOnly,
        /// Analog persistence: everything seen decays to `memoryFloor` and stays there, dimmed.
        PartialMemory,
        /// Binary per-cell reveal on top of the analog memory: once a REVEALABLE cell has been lit to
        /// `revealThreshold`, it is stamped permanently fully bright. Everything else keeps following
        /// the analog memory.
        SnapFull,
    }

    /// The room's reveal policy, layered over the Pixel-Perfect Fog Of War asset. Owns three things:
    /// the memory-floor knob (`MaxFogRegrowAmount`, re-pushed live so the mode can flip mid-play), the
    /// per-cell first-crossing tracking over a game-supplied REVEALABLE set, and the SnapFull stamping
    /// — full-bright quads re-drawn into the fog memory RT every frame AFTER the asset's own blit,
    /// because the regrow pass would otherwise decay a one-shot stamp back to the floor within a frame.
    ///
    /// The revealable set is INPUT: the game supplies which cells count (walls, borders, props — the
    /// module has no idea what a level is) plus a cell→world-centre mapping and the cell size. The
    /// convenience Bind overload derives the classic rect-room set from bounds + a walkability
    /// predicate: the border ring + every non-walkable interior cell.
    ///
    /// Actor visibility is deliberately untouched by ALL of this: FovRoomWorld configures the fog world
    /// with HidersUseFogTexture = false, so hiders are gated by the revealer's live line-of-sight
    /// geometry and can never leak through memory floors or stamps, whatever these knobs are set to.
    [AddComponentMenu("")]
    public class FovReveal : MonoBehaviour
    {
        [Header("Mode")]
        [Tooltip("LiveOnly = no persistence. PartialMemory = seen ground dims to the memory floor. " +
                 "SnapFull = revealable cells snap permanently bright once lit past the threshold.")]
        public FovRevealMode mode = FovRevealMode.PartialMemory;

        [Tooltip("How much visibility explored ground keeps once out of sight (PartialMemory/SnapFull). " +
                 "LiveOnly forces 0.")]
        [Range(0f, 1f)] public float memoryFloor = 0.45f;

        [Tooltip("Visibility (0-1) a revealable cell must reach to count as revealed — the SnapFull " +
                 "stamp trigger and the reveal-pop trigger, in every mode.")]
        [Range(0f, 1f)] public float revealThreshold = 0.5f;

        [Header("Reveal pop")]
        [Tooltip("Small arcade flash the first time a revealable cell crosses the threshold. Independent " +
                 "of the mode.")]
        public bool revealPop = true;
        [Tooltip("How long the flash lives.")]
        [Min(0.05f)] public float popSeconds = 0.25f;
        [Tooltip("Peak opacity of the flash. Keep it a blink, not a flashbang.")]
        [Range(0f, 1f)] public float popIntensity = 0.85f;

        [Tooltip("Seconds between threshold polls over the revealable set. A room is typically ~100 " +
                 "revealable cells, so this is cheap.")]
        [Min(0.05f)] public float pollInterval = 0.15f;

        FogOfWarWorld world;
        Func<Vector2Int, Vector3> cellToWorld;
        Vector2 cellSize = Vector2.one;
        bool bound;
        readonly List<Vector2Int> revealables = new();
        readonly HashSet<Vector2Int> crossed = new();   // first-crossing flags, tracked in EVERY mode
        readonly List<Rect> stamps = new();             // fog-RT UV rects of crossed cells, for SnapFull
        float pollT;
        Material stampMat;
        static Sprite popSprite;
        FovRevealMode appliedMode = (FovRevealMode)(-1);
        float appliedFloor = -1f;

        /// Raised the FIRST time a revealable cell crosses `revealThreshold`, in every mode — the
        /// documented hook for fog-aware systems (discovery-driven pathfinding, MarkDiscovered
        /// patterns, completion scoring). The reveal pop is itself just the first subscriber, so
        /// external listeners see exactly the crossings the pop celebrates. Fired from the threshold
        /// poll, so latency is up to `pollInterval` after the cell actually lit.
        public event Action<Vector2Int> CellFirstRevealed;

        /// The per-cell revealed flags — the natural data source for a reveal-percentage mechanic.
        public IReadOnlyCollection<Vector2Int> Crossed => crossed;
        public int RevealableCount => revealables.Count;
        public int RevealedCount => crossed.Count;
        /// Completion 0-1: revealed members / total revealable members. 0 when nothing is bound.
        public float Completion => revealables.Count == 0 ? 0f : (float)crossed.Count / revealables.Count;

        /// Bind to a fog world with an explicit revealable set. `cellToWorld` maps a cell to its world
        /// CENTRE; `cellSize` is the cell's world size (for the stamp rect and the pop's scale).
        public void Bind(FogOfWarWorld fogWorld, IEnumerable<Vector2Int> revealableCells,
                         Func<Vector2Int, Vector3> cellToWorld, Vector2 cellSize)
        {
            world = fogWorld;
            this.cellToWorld = cellToWorld;
            this.cellSize = cellSize;
            revealables.Clear();
            crossed.Clear();
            stamps.Clear();
            bound = world != null && cellToWorld != null;
            if (!bound) return;

            var seen = new HashSet<Vector2Int>();
            foreach (var cell in revealableCells) seen.Add(cell);
            revealables.AddRange(seen);

            // Give the async readback a moment to produce its first frame — polling before it has data
            // falls back to a per-cell ReadPixels, which would hitch once for the whole set.
            pollT = 0.75f;
        }

        /// Convenience for the classic rect room: revealables = the bounds' border ring + every interior
        /// cell the predicate calls non-walkable (walls/blockers). Bounds are cell coordinates,
        /// xMax/yMax exclusive.
        public void Bind(FogOfWarWorld fogWorld, RectInt cellBounds, Func<Vector2Int, bool> walkable,
                         Func<Vector2Int, Vector3> cellToWorld, Vector2 cellSize)
        {
            var seen = new HashSet<Vector2Int>();
            for (int x = cellBounds.xMin; x < cellBounds.xMax; x++)
            {
                seen.Add(new Vector2Int(x, cellBounds.yMin));
                seen.Add(new Vector2Int(x, cellBounds.yMax - 1));
            }
            for (int y = cellBounds.yMin; y < cellBounds.yMax; y++)
            {
                seen.Add(new Vector2Int(cellBounds.xMin, y));
                seen.Add(new Vector2Int(cellBounds.xMax - 1, y));
            }
            if (walkable != null)
                for (int x = cellBounds.xMin; x < cellBounds.xMax; x++)
                    for (int y = cellBounds.yMin; y < cellBounds.yMax; y++)
                    {
                        var cell = new Vector2Int(x, y);
                        if (!walkable(cell)) seen.Add(cell);
                    }
            Bind(fogWorld, seen, cellToWorld, cellSize);
        }

        void Update()
        {
            if (world == null || !bound) return;

            ApplyMode();

            pollT -= Time.deltaTime;
            if (pollT > 0f) return;
            pollT = pollInterval;

            foreach (var cell in revealables)
            {
                if (crossed.Contains(cell)) continue;
                var centre = cellToWorld(cell);
                // SampleFogTextureColorAtPoint returns VISIBILITY (1 = lit): the RT stores fog amount and
                // the sampler inverts it — see FOW_RT.shader's outSample = 1 - revealerSample.
                if (FogOfWarWorld.SampleFogTextureColorAtPoint(centre) < revealThreshold) continue;

                crossed.Add(cell);
                stamps.Add(CellUvRect(cell));
                CellFirstRevealed?.Invoke(cell);
            }
        }

        void Awake() => CellFirstRevealed += PopOnFirstRevealed;

        /// The built-in reveal pop, wired as an ordinary subscriber to CellFirstRevealed — the event is
        /// the one source of "a cell just crossed", internal and external alike.
        void PopOnFirstRevealed(Vector2Int cell)
        {
            if (revealPop && cellToWorld != null) Pop(cellToWorld(cell));
        }

        /// The memory floor is read by the asset ONCE per material push, so a live mode flip must both set
        /// the field and re-push the texture-material properties.
        void ApplyMode()
        {
            float floor = mode == FovRevealMode.LiveOnly ? 0f : memoryFloor;
            if (appliedMode == mode && Mathf.Approximately(appliedFloor, floor)) return;
            appliedMode = mode;
            appliedFloor = floor;
            world.MaxFogRegrowAmount = floor;
            world.UpdateFowTextureMaterialProperties();
        }

        /// SnapFull stamping. LateUpdate on purpose: FogOfWarWorld runs at execution order -100 and blits
        /// the memory RT in ITS LateUpdate, so ours lands after — the stamp survives into rendering, and
        /// re-stamping every frame is what makes it permanent against the regrow decay.
        void LateUpdate()
        {
            if (mode != FovRevealMode.SnapFull || world == null || stamps.Count == 0) return;
            var rt = world.GetFOWRT();
            if (rt == null) return;

            if (stampMat == null)
            {
                stampMat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                stampMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                stampMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                stampMat.SetInt("_ZWrite", 0);
            }

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.PushMatrix();
            GL.LoadOrtho();
            stampMat.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(new Color(0f, 0f, 0f, 1f));   // the RT's red channel is FOG amount — 0 = fully seen
            foreach (var r in stamps)
            {
                GL.Vertex3(r.xMin, r.yMin, 0f);
                GL.Vertex3(r.xMax, r.yMin, 0f);
                GL.Vertex3(r.xMax, r.yMax, 0f);
                GL.Vertex3(r.xMin, r.yMax, 0f);
            }
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        Rect CellUvRect(Vector2Int cell)
        {
            var centre = cellToWorld(cell);
            var half = (Vector3)(cellSize * 0.5f);
            var a = FogOfWarWorld.GetFowTextureUVFromWorldPosition(centre - half);
            var b = FogOfWarWorld.GetFowTextureUVFromWorldPosition(centre + half);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        void Pop(Vector3 at)
        {
            if (popSprite == null)
            {
                var tex = Texture2D.whiteTexture;   // ppu = width → the sprite is exactly 1 world unit
                popSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
            }

            var go = new GameObject("RevealPop");
            go.transform.position = at;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = popSprite;
            sr.color = new Color(1f, 1f, 1f, popIntensity);
            sr.sortingOrder = 60;   // above a level stack and its actor band
            go.AddComponent<FovRevealPopFx>().Init(popSeconds, cellSize.x);
        }

        void OnDestroy() { if (stampMat != null) Destroy(stampMat); }
    }

    /// The flash itself: a white cell-sized quad that punches slightly past the cell and fades out.
    /// Code-generated, no art assets, destroys itself.
    [AddComponentMenu("")]
    public class FovRevealPopFx : MonoBehaviour
    {
        float life = 0.25f, size = 1f, startAlpha = 1f, t;
        SpriteRenderer sr;

        public void Init(float seconds, float cellSize)
        {
            life = Mathf.Max(0.05f, seconds);
            size = cellSize;
            sr = GetComponent<SpriteRenderer>();
            startAlpha = sr != null ? sr.color.a : 1f;
        }

        void Update()
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / life);
            float ease = 1f - (1f - k) * (1f - k);                       // fast out, settling
            float s = size * Mathf.Lerp(0.55f, 1.15f, ease);             // slight scale punch past the cell
            transform.localScale = new Vector3(s, s, 1f);
            if (sr != null)
            {
                var c = sr.color;
                c.a = startAlpha * (1f - k);
                sr.color = c;
            }
            if (t >= life) Destroy(gameObject);
        }
    }
}
