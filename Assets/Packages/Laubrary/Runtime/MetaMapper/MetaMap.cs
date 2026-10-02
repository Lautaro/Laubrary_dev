using System.Collections.Generic;
using Laubrary.PreviewKit;
using UnityEngine;

namespace Laubrary.MetaMapper
{
    /// <summary>What kind of visual a <see cref="MetaSubjectRef"/> points at. The KIND is stored separately
    /// from the object because MetaMapper deliberately references no other assembly (not Cartographer, not
    /// Launimator, not ZUI) — it cannot name Tileset or LevelAsset as types, so it names them as intent and
    /// holds the asset itself as a plain UnityEngine.Object.</summary>
    public enum MetaSubjectKind
    {
        /// <summary>Nothing picked yet. An embedded MetaMapData never needs a subject at all — its owner IS its
        /// subject; this only matters for the standalone <see cref="MetaMap"/> asset.</summary>
        None = 0,

        /// <summary>subject = a Sprite. The case that forced the standalone asset to exist: a bare Sprite has
        /// nowhere to embed a map.</summary>
        Sprite = 1,

        /// <summary>subject = a Cartographer Tileset, key = the clump's STABLE OPAQUE ID. Clumps are
        /// serializable classes, not Objects, so no hard reference to one can exist — but the key is an id, not
        /// a name: two clumps may legitimately share a display name, and keying by name meant opening one
        /// edited the other's metadata. EDITOR CONVENIENCE ONLY: runtime always goes subject → map (the clump
        /// holds its own embedded map), never map → subject.</summary>
        Clump = 2,

        /// <summary>subject = a Cartographer LevelAsset. Whole-level regions.</summary>
        Level = 3,

        /// <summary>subject = anything else a future provider knows how to draw; key disambiguates within it.
        /// The escape hatch that keeps this enum from becoming a registry of every tool in the package.</summary>
        Other = 99,
    }

    /// <summary>Which visual a standalone <see cref="MetaMap"/> explains — enough for the generic window to draw
    /// the right art, verify refSize and render a thumbnail.
    ///
    /// Held as (kind + Object + key) rather than the design's typed { Sprite } / { Tileset, clumpName } /
    /// { LevelAsset } union, because those are CARTOGRAPHER types and this module must reference nothing — that
    /// zero-reference property is what lets every tool reference MetaMapper without a cycle. The Object
    /// reference is a real hard link (survives renames and moves); the kind says how to interpret it; the key
    /// names a part INSIDE it for subjects that are not Objects in their own right.</summary>
    [System.Serializable]
    public class MetaSubjectRef
    {
        [Tooltip("How to interpret `subject`. Set this FIRST — it is what tells a provider whether the object " +
                 "dropped below is a Sprite, a Tileset (with `key` naming a clump) or a LevelAsset.")]
        public MetaSubjectKind kind = MetaSubjectKind.None;

        [Tooltip("The asset itself. Typed as a plain Object on purpose: this module names no other assembly's " +
                 "types, so `kind` carries the type information a picker or provider needs.")]
        public Object subject;

        [Tooltip("The part WITHIN `subject`, when the subject is not an Object of its own — a clump's stable " +
                 "opaque id. \"\" when `subject` IS the whole subject. OPAQUE: never show it to a user and " +
                 "never parse it; the human-readable name of what is open comes from the host's own " +
                 "MetaSubjectVisual.label.")]
        public string key = "";

        /// <summary>The subject as a Sprite when that is what it is — the one type this module can name, since
        /// Sprite is UnityEngine's own. Null otherwise.</summary>
        public Sprite AsSprite => kind == MetaSubjectKind.Sprite ? subject as Sprite : null;

        /// <summary>True when this points at something. A map with no subject is still perfectly valid data —
        /// it just cannot be DRAWN until someone repicks.</summary>
        public bool IsSet => kind != MetaSubjectKind.None && subject != null;

        public MetaSubjectRef Clone() => new MetaSubjectRef { kind = kind, subject = subject, key = key };

        public override string ToString()
        {
            if (!IsSet) return "(no subject)";
            return string.IsNullOrEmpty(key) ? $"{kind}: {subject.name}" : $"{kind}: {subject.name} / {key}";
        }
    }

    /// <summary>
    /// The STANDALONE map asset: one <see cref="MetaMapData"/> plus the subject it explains. Exists precisely
    /// because some subjects — a bare Sprite, a Clump inside a Tileset — cannot own an embedded map. Anything
    /// that CAN own one should embed <see cref="MetaMapData"/> directly instead and skip this wrapper; the
    /// queries, spaces and bindings are identical either way.
    ///
    /// IVisualPreview, as the design sketched (added in Phase B). The one reference this assembly carries is
    /// <c>Laubrary.PreviewKit</c>, whose OWN asmdef references nothing — so the zero-cycle property that lets
    /// every tool reference MetaMapper is provably intact. It has to live on THIS class: an interface cannot
    /// be bolted onto a type from a bridge assembly.
    ///
    /// The thumbnail wants "the subject with its markers over it", which needs the editor-side subject
    /// provider registry — so the editor installs itself through <see cref="ThumbnailProvider"/> and this
    /// class falls back to a marker-only rendering when nobody has (a runtime build, or a subject no provider
    /// can resolve).
    /// </summary>
    [CreateAssetMenu(menuName = "Laubrary/MetaMapper/Meta Map", fileName = "MetaMap")]
    public class MetaMap : ScriptableObject, IVisualPreview
    {
        [Tooltip("Which visual this map explains. Editor-facing: it lets the window draw the right art and " +
                 "check for drift. Runtime never walks map → subject; it always goes subject → map.")]
        public MetaSubjectRef subject = new MetaSubjectRef();

        [Tooltip("The model. Identical to an embedded map in every way — this asset is only a home for it.")]
        public MetaMapData data = new MetaMapData();

        /// <summary>Never null: an asset whose data was cleared still answers queries (with nothing) rather
        /// than throwing at whatever gameplay code asked.</summary>
        public MetaMapData Data => data ?? (data = new MetaMapData());

        // ── identity passthroughs ────────────────────────────────────────────────────
        // The first real consumer asks IDENTITY questions ("is this clump a loot shelf?"), and it holds a
        // MetaMap reference, not a MetaMapData. These three save every such caller a `.data.` hop; everything
        // else is deliberately reached through Data so this wrapper never becomes a second API to maintain.

        public IEnumerable<string> LayerIds => Data.LayerIds;
        public bool HasLayer(string layerId) => Data.HasLayer(layerId);
        public bool HasLayer(string layerId, LayerKind kind) => Data.HasLayer(layerId, kind);
        public bool HasContent(string layerId, int frame = 0) => Data.HasContent(layerId, frame);

        /// <summary>Heal on load rather than trust the file (the RegionSlicer discipline). Cheap; runs once per
        /// asset load.</summary>
        void OnEnable() => Data.Normalize();

        // ── IVisualPreview ───────────────────────────────────────────────────────────

        /// <summary>Installed by the EDITOR (MetaMapperEditorLink) so a thumbnail can show the real subject
        /// with the markers over it — the composite needs the subject-provider registry, which is editor-only
        /// by nature. Returns a FRESH texture the caller owns, or null for "I could not resolve the subject",
        /// which falls the preview back to <see cref="RenderMarkersOnly"/>.
        ///
        /// A static delegate rather than an interface because this class must stay referenceable from every
        /// tool: naming an editor type here would invert the dependency the whole module is built around.</summary>
        public static System.Func<MetaMap, Texture2D> ThumbnailProvider;

        /// <summary>Subject with the markers over it where the editor can resolve one; a marker-only card
        /// otherwise. Caller owns and destroys the result.</summary>
        public Texture2D RenderPreviewTexture()
        {
            var fn = ThumbnailProvider;
            if (fn != null)
            {
                var composed = fn(this);
                if (composed != null) return composed;
            }
            return RenderMarkersOnly();
        }

        /// <summary>False on purpose. A map's frames are the SUBJECT's frames, so animating a thumbnail would
        /// mean re-compositing through the editor's provider once per tick — real cost for a browser cell,
        /// and the interface explicitly allows a representative still.</summary>
        public bool CanAnimatePreview => false;

        public float PreviewFps => 0f;

        public void UpdateAnimatedPreview(Texture2D tex, double time) { }

        const int PreviewSize = 96;

        /// <summary>The no-subject fallback: the map's own geometry on a dark card, plus a colour bar per
        /// layer along the bottom. The bars matter more than they look — the FIRST real use of this model is
        /// a layer with NOTHING authored on it ("this clump is a loot shelf"), and a thumbnail that drew only
        /// marks would render that as an empty square.</summary>
        Texture2D RenderMarkersOnly()
        {
            var tex = new Texture2D(PreviewSize, PreviewSize, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[PreviewSize * PreviewSize];
            var bg = new Color32(30, 30, 34, 255);
            for (int i = 0; i < px.Length; i++) px[i] = bg;

            var d = Data;
            Rect extent = ExtentOf(d);
            float sx = extent.width > 0f ? (PreviewSize - 8f) / extent.width : 1f;
            float sy = extent.height > 0f ? (PreviewSize - 16f) / extent.height : 1f;
            float s = Mathf.Min(sx, sy);

            if (d.layers != null)
                for (int li = 0; li < d.layers.Count; li++)
                {
                    var L = d.layers[li];
                    if (L == null) continue;
                    var e = L.EntryAt(0);
                    if (e == null) continue;

                    if (L.kind == LayerKind.Mask && e.MaskUsable)
                    {
                        for (int y = 0; y < e.maskH; y++)
                            for (int x = 0; x < e.maskW; x++)
                            {
                                int v = e.MaskGet(x, y);
                                if (v <= 0) continue;
                                // Through the model's own cell→map conversion, so the mask's footprint anchor
                                // is honoured here exactly as the queries honour it.
                                var m = d.MaskCellCenterToMap(e, new Vector2Int(x, y));
                                Plot(px, m, extent, s, MetaPalette.CellColor(L.color, v), 1);
                            }
                    }
                    if (e.marks != null)
                        for (int i = 0; i < e.marks.Count; i++)
                            if (e.marks[i] != null) Plot(px, e.marks[i].pos, extent, s, L.color, 2);
                }

            // The identity strip: one bar per layer, so "declares a LootShelf layer" is visible even with
            // nothing authored on it.
            int n = d.LayerCount;
            if (n > 0)
            {
                int barW = Mathf.Max(1, (PreviewSize - 4) / n);
                for (int li = 0; li < n; li++)
                {
                    var L = d.LayerAt(li);
                    if (L == null) continue;
                    var c = (Color32)L.color;
                    for (int y = 2; y < 8; y++)
                        for (int x = 2 + li * barW; x < Mathf.Min(PreviewSize - 2, 2 + (li + 1) * barW - 1); x++)
                            px[y * PreviewSize + x] = c;
                }
            }

            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        /// <summary>The map-space box the preview fits into: the authored footprint when there is one, else
        /// whatever the marks and masks actually span, else a unit square (never a zero-size divide).</summary>
        Rect ExtentOf(MetaMapData d)
        {
            if (d.refSize.x > 0 && d.refSize.y > 0) return d.Footprint;

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            if (d.layers != null)
                foreach (var L in d.layers)
                {
                    var e = L?.EntryAt(0);
                    if (e?.marks == null) continue;
                    foreach (var m in e.marks)
                    {
                        if (m == null) continue;
                        minX = Mathf.Min(minX, m.pos.x); maxX = Mathf.Max(maxX, m.pos.x);
                        minY = Mathf.Min(minY, m.pos.y); maxY = Mathf.Max(maxY, m.pos.y);
                    }
                }
            if (maxX < minX) return new Rect(0f, 0f, 1f, 1f);
            return Rect.MinMaxRect(minX - 1f, minY - 1f, maxX + 1f, maxY + 1f);
        }

        static void Plot(Color32[] px, Vector2 mapPos, Rect extent, float scale, Color color, int radius)
        {
            // +y up in map space, +y down in a texture row index — the flip belongs here, not in the data.
            int cx = 4 + Mathf.RoundToInt((mapPos.x - extent.xMin) * scale);
            int cy = 10 + Mathf.RoundToInt((mapPos.y - extent.yMin) * scale);
            var c = (Color32)color;
            for (int y = cy - radius; y <= cy + radius; y++)
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    if (x < 0 || y < 0 || x >= PreviewSize || y >= PreviewSize) continue;
                    px[y * PreviewSize + x] = c;
                }
        }
    }
}
