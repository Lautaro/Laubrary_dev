using System.Collections.Generic;
using System.IO;
using Laubrary.AssetKit.Editor;
using Laubrary.PreviewKit;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.MetaMapper.Editor
{
    /// <summary>
    /// METAMAPPER — the generic authoring surface for spatial metadata: typed marks (Points, Directions,
    /// painted Masks) in named layers on a visual subject.
    ///
    /// TWO ENTRY PATHS, ONE CONTRACT (design §7):
    ///   • hosts PUSH — <see cref="Open(MetaSubjectVisual, MetaEditSession)"/>. The host composes a picture,
    ///     describes it honestly (origin, pixels-per-unit, space) and hands over the data to edit IN PLACE.
    ///   • the window PULLS — <see cref="OpenFor(MetaMap)"/> for a standalone <see cref="MetaMap"/> asset,
    ///     resolving its <see cref="MetaSubjectRef"/> through <see cref="MetaSubjectProviders"/>, which other
    ///     editors populate. THIS WINDOW NEVER REFERENCES CARTOGRAPHER (or Launimator, or any adopter).
    ///
    /// THE SHORTEST USEFUL PATH, and the reason this exists: create a map, add a layer, name it, save. A
    /// layer that merely EXISTS — zero marks on it — is already the whole answer to "is this clump a loot
    /// shelf?" (<c>HasLayer("LootShelf")</c>). Everything else on this window is for when the answer needs
    /// geometry too.
    /// </summary>
    public partial class MetaMapperWindow : ZuiWindow
    {
        [MenuItem("Laubrary/MetaMapper")]
        static void OpenMenu() => GetWindow<MetaMapperWindow>("MetaMapper");

        /// <summary>
        /// THE PUSH ENTRY POINT. A host composes its subject's picture, describes it with a
        /// <see cref="MetaSubjectVisual"/>, and hands the data over in a <see cref="MetaEditSession"/>.
        ///
        /// Worked call site — the Tileset Builder's "Edit meta map…" button for a clump (the ellipsis is
        /// deliberate: the control opens further UI, so it is named for the destination, not an action):
        /// <code>
        /// Z.Button("Edit meta map…", "Open this clump's spatial metadata in MetaMapper.", () =>
        /// {
        ///     if (clump.map == null)
        ///     {
        ///         var made = AssetLibrary&lt;MetaMap&gt;.Create(clump.displayName + " Map",
        ///                                                  TilesetForge.TilesFolder(set));
        ///         made.subject = new MetaSubjectRef
        ///             { kind = MetaSubjectKind.Clump, subject = set, key = clump.displayName };
        ///         made.Data.space = MapSpace.GridCells;          // set from the SUBJECT KIND, never by hand
        ///         made.Data.refSize = new Vector2Int(clump.Bounds.width, clump.Bounds.height);
        ///         EditorUtility.SetDirty(made);
        ///         Undo.RecordObject(set, "Attach meta map");     // record BEFORE the mutation
        ///         clump.map = made;
        ///         EditorUtility.SetDirty(set);
        ///     }
        ///     var bounds = clump.Bounds;                       // may start at NEGATIVE cell offsets
        ///     MetaMapperWindow.Open(
        ///         new MetaSubjectVisual
        ///         {
        ///             frames        = new[] { ComposeClumpTexture(set, clump) },
        ///             ownsFrames    = true,
        ///             fps           = 0f,
        ///             originPx      = new Vector2(-bounds.xMin * tileSize, -bounds.yMin * tileSize),
        ///             pixelsPerUnit = tileSize,
        ///             space         = MapSpace.GridCells,
        ///             refSize       = new Vector2Int(bounds.width, bounds.height),
        ///             label         = clump.displayName,
        ///         },
        ///         new MetaEditSession
        ///         {
        ///             data       = clump.map.Data,
        ///             undoTarget = clump.map,
        ///             onChanged  = () =&gt; { RefreshClumpBox(); Repaint(); },
        ///         });
        /// });
        /// </code>
        /// </summary>
        public static MetaMapperWindow Open(MetaSubjectVisual visual, MetaEditSession session)
        {
            var w = GetWindow<MetaMapperWindow>("MetaMapper");
            w.Adopt(visual, session);
            return w;
        }

        /// <summary>
        /// THE SUBJECT ENTRY POINT — and the one hosts with EMBEDDED data should use.
        ///
        /// The host names its subject as a serializable (kind + Object + key) and the window resolves BOTH
        /// halves through <see cref="MetaSubjectProviders"/>: the picture, and a session over the data. That
        /// indirection is the whole reason an embedded edit survives a domain reload — a pushed session is a
        /// live object that dies with the domain, but the ref is just data and can be re-resolved.
        ///
        /// <c>onChanged</c> is a live delegate and therefore does NOT survive a reload; editing continues,
        /// only the host's live repaint stops until it pushes again.
        /// </summary>
        public static MetaMapperWindow OpenSubject(MetaSubjectRef subject, System.Action onChanged = null)
        {
            var w = GetWindow<MetaMapperWindow>("MetaMapper");
            w.AdoptSubject(subject, onChanged);
            return w;
        }

        /// <summary>THE PULL ENTRY POINT: edit a standalone map asset, resolving its subject through the
        /// provider registry. Registered with <c>LauAssetEditors.RegisterOpen</c>, so every LauAsset field's
        /// ✎ button and a double-click in the Project window land here.</summary>
        public static MetaMapperWindow OpenFor(MetaMap map)
        {
            var w = GetWindow<MetaMapperWindow>("MetaMapper");
            w.SetMap(map);
            return w;
        }

        // ── state ───────────────────────────────────────────────────────────────────
        // Everything here is VIEW state or an asset reference — never map data. The data lives on the
        // session's undoTarget and is edited in place.

        [SerializeField] MetaMap map;                 // standalone path; survives a domain reload
        [SerializeField] MetaSubjectRef pushed;       // embedded path; the ONLY thing that survives a reload
        [SerializeField] int activeLayerIndex;
        [SerializeField] int frame;
        [SerializeField] bool browsing;
        [SerializeField] bool showGrid = true;
        [SerializeField] bool showAllLayers = true;
        [SerializeField] bool snapQuarter = true;
        [SerializeField] int brushSize = 1;
        [SerializeField] int brushValue = 5;
        [SerializeField] string newLayerId = "LootShelf";
        [SerializeField] int newLayerKind;
        [SerializeField] List<string> hiddenLayerIds = new List<string>();

        // View position survives a rebuild: switching frame or layer must not throw the canvas back to fit.
        [SerializeField] internal float StageZoom = 1f;
        [SerializeField] internal Vector2 StagePan;

        MetaSubjectVisual visual;
        MetaEditSession session;
        string subjectProblem;

        MetaStage stage;
        VisualElement toolRowHost;
        Label statusLine;
        Label breadcrumb;
        VisualElement layersBody;
        VisualElement driftRow;
        Label driftLabel;
        Button driftButton;
        Button reanchorButton;
        Button keepCoordsButton;
        List<MetaMap> browseCache;
        readonly Dictionary<MetaMap, Texture2D> browseThumbs = new Dictionary<MetaMap, Texture2D>();

        // ── what the stage reads ────────────────────────────────────────────────────

        internal MetaSubjectVisual Visual => visual;
        internal MetaEditSession Session => session;
        internal MetaMapData Data => session?.data;
        internal MapSpace Space => Data != null ? Data.space : MapSpace.SpritePixels;
        internal int Frame => Data != null ? Mathf.Clamp(frame, 0, Mathf.Max(0, Data.frameCount - 1)) : 0;
        internal bool ShowGrid => showGrid;
        internal bool ShowAllLayers => showAllLayers;
        internal bool SnapQuarter => snapQuarter;
        internal int BrushSize => brushSize;
        internal int BrushValue => brushValue;

        internal MetaMapLayer ActiveLayer
        {
            get
            {
                var d = Data;
                if (d?.layers == null || d.layers.Count == 0) return null;
                return d.LayerAt(Mathf.Clamp(activeLayerIndex, 0, d.layers.Count - 1));
            }
        }

        /// <summary>The native resolution a Mask paints at: the map's own recorded footprint, falling back to
        /// what the subject says it is. Keeping these equal is what makes <c>MaskCellToMapScale</c> come out
        /// 1:1, so a mask cell IS a map unit and <c>floor(mapPos)</c> IS its cell.</summary>
        internal Vector2Int MaskSize
        {
            get
            {
                var d = Data;
                if (d != null && d.refSize.x > 0 && d.refSize.y > 0) return d.refSize;
                return visual != null ? visual.refSize : Vector2Int.zero;
            }
        }

        internal bool IsLayerVisible(MetaMapLayer L)
            => L != null && !hiddenLayerIds.Contains(L.id ?? "");

        /// <summary>The entry the active layer authors into on the current frame. Padding a short PerFrame
        /// track is a real (if tiny) data change, so it goes through the session like everything else.</summary>
        internal MetaEntry ActiveEntry(bool createIfMissing)
        {
            var L = ActiveLayer;
            var d = Data;
            if (L == null || d == null) return null;

            if (L.binding == FrameBinding.Uniform)
            {
                if (L.uniform == null && createIfMissing) L.uniform = new MetaEntry();
                return L.uniform;
            }
            if (createIfMissing && (L.track == null || L.track.Count < d.frameCount))
                session.Edit("Prepare frame track", () => L.EnsureTrack(d.frameCount));
            return L.EntryAt(Frame);
        }

        // ── opening ─────────────────────────────────────────────────────────────────

        /// <summary>Open on an embedded subject named by a serializable ref — see <see cref="OpenSubject"/>.</summary>
        internal void AdoptSubject(MetaSubjectRef subject, System.Action onChanged)
        {
            pushed = subject?.Clone();
            var s = MetaSubjectProviders.ResolveSession(subject);
            if (s == null || !s.IsValid)
            {
                Debug.LogError($"[MetaMapper] Nobody can open '{subject}' for editing — the tool that owns " +
                               "that subject kind did not register a session provider.");
                pushed = null;
                Rebuild();
                return;
            }
            if (onChanged != null) s.onChanged = onChanged;
            // No art (a clump whose tiles lost their sprites) must never block authoring IDENTITY, so fall
            // back to a blank canvas of the right size and anchor rather than to nothing at all.
            var v = MetaSubjectProviders.Resolve(subject)
                    ?? MetaSubjectVisual.Blank(s.data.space, FallbackSize(s.data), s.data.footprintMin,
                                               subject?.subject != null ? subject.subject.name : "");
            Adopt(v, s, keepPushed: true);
        }

        internal void Adopt(MetaSubjectVisual v, MetaEditSession s) => Adopt(v, s, keepPushed: false);

        void Adopt(MetaSubjectVisual v, MetaEditSession s, bool keepPushed)
        {
            DisposeVisual();
            session = s;
            visual = v;
            map = s?.undoTarget as MetaMap;
            if (!keepPushed) pushed = null;
            browsing = false;
            activeLayerIndex = 0;
            frame = 0;
            subjectProblem = null;

            if (v != null && s?.data != null && !v.Validate(s.data, out subjectProblem))
            {
                // Loud, not silent: a mis-described picture would put every mark in the wrong place, and the
                // author would only find out when the game ran.
                Debug.LogError("[MetaMapper] Refusing to edit — " + subjectProblem);
                visual = null;
                if (v.ownsFrames) v.Dispose();
            }
            else SyncToSubject();

            Rebuild();
        }

        internal void SetMap(MetaMap m)
        {
            DisposeVisual();
            map = m;
            pushed = null;          // a standalone asset and a pushed subject are two different edits
            subjectProblem = null;
            activeLayerIndex = 0;
            frame = 0;

            if (m == null) { session = null; browsing = true; Rebuild(); return; }

            session = MetaEditSession.ForAsset(m, () => stage?.Refresh());
            var resolved = MetaSubjectProviders.Resolve(m.subject);
            if (resolved != null && !resolved.Validate(m.Data, out subjectProblem))
            {
                Debug.LogError("[MetaMapper] Refusing to draw '" + m.name + "' — " + subjectProblem);
                resolved.Dispose();
                resolved = null;
            }
            // No subject, or nobody registered a provider for its kind: a BLANK canvas of the map's own
            // recorded size. Authoring identity (a layer that merely exists) must never wait on art.
            visual = resolved ?? MetaSubjectVisual.Blank(m.Data.space, FallbackSize(m.Data), m.Data.footprintMin, m.name);
            SyncToSubject();
            browsing = false;
            Rebuild();
        }

        static Vector2Int FallbackSize(MetaMapData d)
            => (d != null && d.refSize.x > 0 && d.refSize.y > 0) ? d.refSize : new Vector2Int(16, 16);

        /// <summary>Adopt what the subject knows and the map does not. Deliberately one-way and never
        /// shrinking: frameCount only GROWS (the owner's sequence got longer), refSize is only adopted when
        /// the map never recorded one. Shrinking either would silently orphan authored frames, and
        /// <see cref="MetaMapData.SetFrameCount"/> already refuses to trim or to touch any binding.</summary>
        void SyncToSubject()
        {
            var d = Data;
            if (visual == null || d == null) return;
            // A placeholder canvas was invented FROM the map; letting it teach the map back would bake a
            // made-up 16×16 footprint into an asset whose real subject just is not loaded yet.
            if (visual.isPlaceholder) return;
            bool needRef = (d.refSize.x <= 0 || d.refSize.y <= 0) && visual.refSize.x > 0 && visual.refSize.y > 0;
            bool needFrames = visual.FrameCount > d.frameCount;
            // The footprint anchor restates where the subject's picture sits in map space, which only the
            // subject knows — safe to adopt while the two merely disagree about bookkeeping.
            //
            // NOT safe when the host has REPORTED DRIFT: then the disagreement is the subject having changed
            // shape under authored content, and adopting the new anchor with the marks left behind is the
            // silent mangling this whole path exists to stop. Leave the map exactly as authored; the drift bar
            // asks the author which way it should go. (This was the bug: the anchor moved, the marks did not,
            // no event, no undo entry, no notice.)
            bool needMin = d.footprintMin != visual.MapMin && visual.drift == null;
            if (!needRef && !needFrames && !needMin) return;
            session.Edit("Sync meta map to subject", () =>
            {
                if (needRef) d.refSize = visual.refSize;
                if (needMin) d.footprintMin = visual.MapMin;
                if (needFrames) d.SetFrameCount(visual.FrameCount);
            });
        }

        void DisposeVisual()
        {
            visual?.Dispose();
            visual = null;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            DisposeVisual();
            stage?.Dispose();
            ClearBrowseThumbs();
        }

        void OnFocus()
        {
            // A domain reload drops the transient session and the composed picture. The two serialized
            // anchors — the map asset, or the pushed subject ref — are what let the edit come back instead
            // of dumping the author into an empty browser.
            if (session == null)
            {
                if (map != null) SetMap(map);
                else if (pushed != null && pushed.IsSet) AdoptSubject(pushed, null);
                return;
            }
            // The session survived, but the DATA underneath it may not have: another window can replace the
            // owner's embedded model wholesale while this one is open (the Tileset Builder's "Clear" hands the
            // clump a brand-new MetaMapData). Nothing rebuilds this window when that happens, so without this
            // check the author comes back to a canvas that still shows their marks and paints into an object
            // no longer attached to anything. Re-ask the owner on the way in — the same hazard, and the same
            // cure, as the undo re-point in BuildUI.
            if (Repoint()) Rebuild();
        }

        /// Ask the owner for the CURRENT model and re-point the session at it if it has been replaced.
        /// Returns whether anything moved. Both hazards it guards are the same shape: something outside this
        /// window swapped the instance (undo deserializing a fresh one; a host assigning a new one).
        bool Repoint()
        {
            if (session == null) return false;
            if (map != null && !ReferenceEquals(session.data, map.data)) { session.data = map.Data; return true; }
            if (pushed != null && pushed.IsSet)
            {
                var fresh = MetaSubjectProviders.ResolveSession(pushed);
                if (fresh != null && fresh.IsValid && !ReferenceEquals(fresh.data, session.data))
                { session.data = fresh.data; return true; }
            }
            return false;
        }

        // ── window body ─────────────────────────────────────────────────────────────

        protected override void OnBeforeRebuild()
        {
            stage?.Dispose();
            stage = null; toolRowHost = null;
            statusLine = null; breadcrumb = null; layersBody = null;
            driftRow = null; driftLabel = null; driftButton = null;
            reanchorButton = null; keepCoordsButton = null;
        }

        /// Re-resolve whatever is open from its serialized anchor. The composed picture, its size and its DRIFT
        /// report are all snapshots of the subject taken at open time, so anything that changes the subject —
        /// or the map's agreement with it — has to come back through here rather than patch the stale visual.
        internal void ReopenSubject()
        {
            if (pushed != null && pushed.IsSet) AdoptSubject(pushed, session?.onChanged);
            else if (map != null) SetMap(map);
        }

        protected override void BuildUI(VisualElement root)
        {
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;

            // Post-reload recovery, before anything reads `session`: the rebuild that follows a domain reload
            // runs BEFORE OnFocus, so without this the window would paint its empty browser once and the
            // author would see their work vanish for a frame (or for good, if they clicked something).
            if (session == null && map != null) SetSessionFromMapQuietly();
            else if (session == null && pushed != null && pushed.IsSet) SetSessionFromSubjectQuietly();

            // Undo restores a [Serializable] field by DESERIALIZING a fresh instance onto the asset, so a
            // session still holding the pre-undo MetaMapData would quietly go on editing a detached object.
            // ZuiWindow rebuilds on every undo/redo, which makes this the exact moment to re-point it — and
            // the embedded case has the same hazard, so both live in Repoint().
            Repoint();

            root.Add(BuildToolbar());

            if (session == null || browsing) { root.Add(BuildBrowser()); UpdateBreadcrumb(); return; }

            var left = new VisualElement();
            left.style.minWidth = 240f;
            left.style.minHeight = 0f;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            left.Add(scroll);
            BuildLeftPane(scroll.contentContainer);

            var right = new VisualElement();
            right.style.flexGrow = 1f;
            right.style.minWidth = 200f;
            right.style.minHeight = 0f;
            BuildRightPane(right);

            root.Add(Z.Split("metamapper", 300f, left, right));
            UpdateBreadcrumb();
            UpdateStatus();
        }

        /// The embedded twin of <see cref="SetSessionFromMapQuietly"/>: re-resolve a pushed subject's data and
        /// picture from the serialized ref, without recursing into Rebuild().
        void SetSessionFromSubjectQuietly()
        {
            subjectProblem = null;
            var s = MetaSubjectProviders.ResolveSession(pushed);
            if (s == null || !s.IsValid) { pushed = null; return; }
            session = s;
            var resolved = MetaSubjectProviders.Resolve(pushed);
            if (resolved != null && !resolved.Validate(s.data, out subjectProblem)) { resolved.Dispose(); resolved = null; }
            visual = resolved ?? MetaSubjectVisual.Blank(s.data.space, FallbackSize(s.data), s.data.footprintMin,
                                                         pushed.subject != null ? pushed.subject.name : "");
        }

        /// A rebuild after a domain reload: rebuild the session without recursing into Rebuild().
        void SetSessionFromMapQuietly()
        {
            subjectProblem = null;
            session = MetaEditSession.ForAsset(map, () => stage?.Refresh());
            var resolved = MetaSubjectProviders.Resolve(map.subject);
            if (resolved != null && !resolved.Validate(map.Data, out subjectProblem)) { resolved.Dispose(); resolved = null; }
            visual = resolved ?? MetaSubjectVisual.Blank(map.Data.space, FallbackSize(map.Data), map.Data.footprintMin, map.name);
        }

        VisualElement BuildToolbar()
        {
            breadcrumb = (Label)Z.Text("", ZuiText.Subtle,
                "What is open: the subject, its coordinate space and its size in map units.");
            breadcrumb.style.whiteSpace = WhiteSpace.NoWrap;
            breadcrumb.style.overflow = Overflow.Hidden;
            breadcrumb.style.flexShrink = 1f;
            breadcrumb.style.minWidth = 0f;
            breadcrumb.style.unityTextAlign = TextAnchor.MiddleRight;

            var save = Z.Button("Save", "Write every pending change to disk (edits are already applied and " +
                                        "undoable; this just flushes them to the asset file that owns them).",
                () => { if (session?.undoTarget != null) EditorUtility.SetDirty(session.undoTarget); AssetDatabase.SaveAssets(); });

            // EMBEDDED subject open? Then there is no MetaMap asset involved at all, and an asset field
            // reading "None (Meta Map)" beside the clump the author is plainly editing is a lie that reads as
            // "nothing is loaded". Say what IS open instead. (Caught by eye in the handover walk, not by any
            // probe — the tree was correct and the sentence was wrong.)
            VisualElement row;
            if (pushed != null && pushed.IsSet)
            {
                // THE HOST'S OWN LABEL, never the subject ref's ToString(). The ref's key is opaque identity
                // (it is a GUID for a clump), so printing it would show the author a hex string where they
                // expect the name of the thing they are editing. `visual.label` is what the host called it.
                var what = Z.Text("Editing " + SubjectTitle(), ZuiText.Body,
                    "This metadata is stored INSIDE that asset — there is no separate file. Close this window " +
                    "whenever you like; nothing needs saving beyond the owning asset.");
                what.style.flexShrink = 1f;
                what.style.minWidth = 0f;
                row = Z.Row(
                    what,
                    save,
                    Z.Button("MetaMap assets…", "Leave this clump and browse the standalone MetaMap assets — " +
                                                "those exist only for subjects that cannot hold their own " +
                                                "metadata, such as a bare Sprite.",
                        () => { browsing = true; RefreshBrowse(); SetMap(null); }),
                    Z.Flexible(),
                    breadcrumb);
            }
            else
            {
                row = Z.Row(
                    Z.Object<MetaMap>(map, "The MetaMap asset open here. A map explains ONE visual subject; " +
                                           "assign one, or make a new one. A Cartographer clump does NOT need " +
                                           "one — its metadata lives on the clump.", SetMap, 180f),
                    Z.Button("New MetaMap…", "Create a new MetaMap asset on disk (you pick where) and open it here.",
                        CreateNewMap),
                    Z.Button(browsing ? "Close browser" : "Browse",
                        "Show every MetaMap asset in the project as a thumbnail grid.",
                        () => { browsing = !browsing; if (browsing) RefreshBrowse(); Rebuild(); }),
                    save,
                    Z.Flexible(),
                    breadcrumb);
            }

            // Stable workspace: ONE non-wrapping row of fixed height. A toolbar that wraps shoves the canvas
            // down mid-gesture, and the user's target jumps out from under the cursor.
            row.style.flexWrap = Wrap.NoWrap;
            row.style.flexShrink = 0f;
            row.style.minHeight = 24f;
            return row;
        }

        /// What to CALL what is open, in the author's terms. The host names its own subject
        /// (<see cref="MetaSubjectVisual.label"/> — "Shelf A"); the owning asset qualifies it when there is one
        /// ("Canari Tileset / Shelf A"). Nothing here reads the subject ref's kind or key: the key is opaque
        /// identity, not a name, and the kind is a word the core should not have to know.
        internal string SubjectTitle()
        {
            string own = visual != null && !string.IsNullOrEmpty(visual.label) ? visual.label : null;
            string host = pushed?.subject != null ? pushed.subject.name : null;
            if (own != null && host != null && own != host) return host + " / " + own;
            return own ?? host ?? "(no subject)";
        }

        void UpdateBreadcrumb()
        {
            if (breadcrumb == null) return;
            var d = Data;
            if (d == null) { breadcrumb.text = "Nothing open"; return; }
            string subj = visual != null && !string.IsNullOrEmpty(visual.label) ? visual.label : "(no subject)";
            breadcrumb.text = $"{subj}  ·  {d.space}  ·  {d.refSize.x}×{d.refSize.y}" +
                              (d.frameCount > 1 ? $"  ·  {d.frameCount} frames" : "");
        }

        // ── the right pane: tool settings above, stage, frame strip, status ─────────

        void BuildRightPane(VisualElement right)
        {
            if (!string.IsNullOrEmpty(subjectProblem))
            {
                right.Add(Z.Help(subjectProblem, HelpBoxMessageType.Error));
                right.Add(Z.Text("Layers on the left still work — a layer's EXISTENCE is queryable metadata " +
                                 "and does not depend on the picture.", ZuiText.Subtle));
                return;
            }

            // The tool row lives in its own host so the ACTIVE LAYER can change without rebuilding — and
            // therefore without destroying — the stage the user is working on.
            toolRowHost = new VisualElement();
            toolRowHost.style.flexShrink = 0f;
            right.Add(toolRowHost);
            RebuildToolRow();

            stage = new MetaStage(this);
            right.Add(stage);

            var strip = BuildFrameStrip();
            if (strip != null) right.Add(strip);

            statusLine = (Label)Z.Text("", ZuiText.Subtle,
                "Where the pointer is in map space, what the active layer holds, and the current zoom.");
            statusLine.style.height = 18f;
            statusLine.style.flexShrink = 0f;
            statusLine.style.whiteSpace = WhiteSpace.NoWrap;
            statusLine.style.overflow = Overflow.Hidden;
            right.Add(statusLine);

            stage.Refresh();
        }

        static T Vis<T>(T ve, bool visible) where T : VisualElement
        {
            // visibility, NOT display: Hidden keeps its layout space, so the row's geometry never changes as
            // the active layer's kind changes and the canvas below never moves.
            ve.style.visibility = visible ? Visibility.Visible : Visibility.Hidden;
            return ve;
        }

        internal void RebuildToolRow()
        {
            if (toolRowHost == null) return;
            toolRowHost.Clear();
            toolRowHost.Add(BuildToolRow());
        }

        VisualElement BuildToolRow()
        {
            var L = ActiveLayer;
            bool mask = L != null && L.kind == LayerKind.Mask;
            bool marks = L != null && L.kind != LayerKind.Mask;
            bool cells = Space == MapSpace.GridCells;

            var snap = Vis(Z.Toggle("Snap ¼", "Snap placement to quarter-cells — everyday tidiness. Hold Alt " +
                                              "while placing for a free, fully fractional position.",
                snapQuarter, v => { snapQuarter = v; stage?.Refresh(); }), marks && cells);
            snap.style.width = 74f;

            var brush = Vis(Z.MicroSlider("Brush", brushSize, 1f, 8f,
                "Paint brush width, in mask cells.", v => { brushSize = Mathf.RoundToInt(v); stage?.Refresh(); },
                118f, true, 1f, 0), mask);

            var value = Vis(Z.MicroSlider("Value", brushValue, 1f, 10f,
                "The 0–10 intensity painted cells get. The queries read >0 as \"inside\"; the value ramps the " +
                "layer colour's BRIGHTNESS so painted regions read apart.",
                v => { brushValue = Mathf.RoundToInt(v); stage?.Refresh(); }, 118f, true, 5f, 0), mask);

            var single = Vis(Z.Toggle("1 cell", "Single-cell paint: each dab clears the previous one instead of " +
                                                "accumulating a blob. Stored on the layer as `pointHint` — an " +
                                                "authoring hint, never a data rule.",
                L != null && L.pointHint,
                v => { if (L != null) session.Edit("Toggle single-cell paint", () => L.pointHint = v); AfterEdit(); }), mask);
            single.style.width = 62f;

            var grid = Z.Toggle("Grid", "Draw the map's unit grid and its origin cross over the subject.",
                showGrid, v => { showGrid = v; stage?.Refresh(); });
            grid.style.width = 52f;

            var all = Z.Toggle("All layers", "Draw every visible layer, not just the active one.",
                showAllLayers, v => { showAllLayers = v; stage?.Refresh(); });
            all.style.width = 82f;

            var kind = (Label)Z.Text(L == null ? "no layer" : L.kind.ToString(), ZuiText.Subtle,
                "What the active layer stores — it decides what a click on the canvas does.");
            kind.style.width = 74f;
            kind.style.whiteSpace = WhiteSpace.NoWrap;
            kind.style.overflow = Overflow.Hidden;

            var row = Z.Row(kind, snap, brush, value, single, grid, all, Z.Flexible());
            row.style.flexWrap = Wrap.NoWrap;
            row.style.flexShrink = 0f;
            row.style.minHeight = 22f;
            return row;
        }

        VisualElement BuildFrameStrip()
        {
            var d = Data;
            // HIDDEN at one frame, by decree: the static case must not LOOK like an animation tool.
            if (d == null || d.frameCount <= 1) return null;

            var strip = new ScrollView(ScrollViewMode.Horizontal);
            strip.style.flexShrink = 0f;
            strip.style.maxHeight = 30f;
            strip.contentContainer.style.flexDirection = FlexDirection.Row;
            for (int i = 0; i < d.frameCount; i++)
            {
                int idx = i;
                var b = Z.Button((i + 1).ToString(),
                    $"Scrub to frame {i + 1}. PerFrame layers show that frame's own entry; Uniform layers " +
                    "show the same content on every frame.",
                    () => { frame = idx; Rebuild(); });
                b.style.width = 26f;
                if (i == Frame) b.AddToClassList("zui-radio__on");
                strip.contentContainer.Add(b);
            }
            return strip;
        }

        internal void UpdateStatus()
        {
            if (statusLine == null) return;
            if (TryShowFlash()) return;
            var d = Data;
            if (d == null) { statusLine.text = ""; return; }

            string where = "";
            if (stage != null && stage.TryGetHover(out var m))
                where = Space == MapSpace.GridCells
                    ? $"cell ({m.x:0.00}, {m.y:0.00})"
                    : $"px ({m.x:0.0}, {m.y:0.0})";

            var L = ActiveLayer;
            string what;
            if (L == null) what = "no active layer";
            else
            {
                var e = d.EntryAt(L, Frame);
                what = L.kind == LayerKind.Mask
                    ? $"'{L.id}' · {(e != null && e.MaskUsable ? e.maskW + "×" + e.maskH : "unpainted")}"
                    : $"'{L.id}' · {(e?.marks != null ? e.marks.Count : 0)} mark(s)";
                if (L.binding == FrameBinding.PerFrame) what += $" · frame {Frame + 1}/{d.frameCount}";
            }

            string zoom = stage != null ? $"{stage.DisplayScale * 100f:0}%" : "";
            statusLine.text = string.Join("   ·   ",
                new List<string>(new[] { where, what, zoom }).FindAll(s => !string.IsNullOrEmpty(s)));
        }

        /// <summary>After any committed data edit: the canvas and the layer panel both re-read the model.
        /// Deliberately NOT a whole-window Rebuild — that would destroy the stage (and its view position)
        /// under the user's cursor.</summary>
        internal void AfterEdit()
        {
            RebuildLayers();
            stage?.Refresh();
            UpdateBreadcrumb();
            UpdateStatus();
            RefreshDriftRow();
        }

        // ── creation + browsing ─────────────────────────────────────────────────────

        void CreateNewMap()
        {
            string folder = DefaultFolder();
            string path = EditorUtility.SaveFilePanelInProject(
                "New MetaMap", "MetaMap", "asset",
                "A MetaMap explains one visual subject: what it IS (named layers) and where things happen on " +
                "it (marks). Where should it live?", folder);
            if (string.IsNullOrEmpty(path)) return;

            var created = ScriptableObject.CreateInstance<MetaMap>();
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssets();
            Undo.RegisterCreatedObjectUndo(created, "Create MetaMap");
            browsing = false;
            SetMap(created);
            EditorGUIUtility.PingObject(created);
        }

        string DefaultFolder()
        {
            string cur = map != null ? AssetDatabase.GetAssetPath(map) : null;
            if (!string.IsNullOrEmpty(cur)) return Path.GetDirectoryName(cur).Replace('\\', '/');
            return "Assets";
        }

        void RefreshBrowse()
        {
            ClearBrowseThumbs();
            browseCache = AssetLibrary<MetaMap>.Enumerate();
        }

        void ClearBrowseThumbs()
        {
            foreach (var t in browseThumbs.Values) if (t != null) DestroyImmediate(t);
            browseThumbs.Clear();
        }

        VisualElement BuildBrowser()
        {
            if (browseCache == null) RefreshBrowse();

            var col = new VisualElement();
            col.style.flexGrow = 1f;
            col.style.minHeight = 0f;

            col.Add(Z.Row(
                Z.Text($"MetaMap library ({browseCache.Count})", ZuiText.Section,
                    "Every MetaMap asset in the project. Click one to open it."),
                Z.Flexible(),
                Z.Button("Refresh", "Re-scan the project.", () => { RefreshBrowse(); Rebuild(); })));

            // THE EMPTY STATE. With zero maps in the project the grid below is blank, and a blank grid teaches
            // nothing — it reads as a broken window (it did: "I have MetaMapper open but I can't find any
            // MetaMap"). An empty state is the one place instructional text belongs, because it is not chrome
            // the user re-reads: it exists only while there is nothing else to show, and the FIRST created map
            // deletes it. It names BOTH ways in, because the useful one is not the one on this window.
            if (browseCache.Count == 0)
            {
                var empty = new VisualElement();
                empty.style.marginTop = 6f;
                empty.style.marginBottom = 6f;
                empty.Add(Z.Text("No MetaMap assets — and most subjects never need one.", ZuiText.Body,
                    "A MetaMap asset exists ONLY for a subject that cannot hold its own metadata."));
                empty.Add(Z.Text("A Cartographer clump keeps its metadata inside itself: select the clump in " +
                                 "the Tileset Builder and press \"Edit meta…\". Nothing to create, nothing to " +
                                 "find here.", ZuiText.Subtle,
                    "Embedded metadata (design §3): the data lives on the thing it describes, so it can never " +
                    "be lost, mis-assigned or pointed at the wrong clump."));
                // NO second "New MetaMap…" here. The toolbar above already carries it, three centimetres away
                // and visible in this very view, and two buttons that do the identical thing on one screen is
                // a question ("what's the difference?") rather than an affordance.
                empty.Add(Z.Text("An asset is only for a bare Sprite, which has nowhere to embed one.",
                    ZuiText.Subtle,
                    "A Sprite cannot own a MetaMapData field, which is the whole reason this asset type " +
                    "exists. \"New MetaMap…\" in the toolbar above creates one."));
                col.Add(empty);
            }

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            scroll.Add(grid);
            foreach (var item in browseCache) if (item != null) grid.Add(BrowseCell(item));
            col.Add(scroll);
            return col;
        }

        VisualElement BrowseCell(MetaMap item)
        {
            var cell = new VisualElement();
            cell.AddToClassList("zui-cell");
            cell.style.width = 104f;
            if (ReferenceEquals(map, item)) cell.AddToClassList("zui-cell--selected");
            var layers = string.Join(", ", item.LayerIds);
            cell.tooltip = string.IsNullOrEmpty(layers)
                ? $"{item.name} — no layers yet. Click to open."
                : $"{item.name} — declares: {layers}. Click to open.";

            var thumbBox = new VisualElement();
            thumbBox.AddToClassList("zui-cell__thumb");
            thumbBox.style.width = 92f;
            thumbBox.style.height = 92f;
            var tex = Thumb(item);
            if (tex != null)
            {
                var img = new Image { image = tex, scaleMode = ScaleMode.ScaleToFit };
                img.style.width = 86f;
                img.style.height = 86f;
                thumbBox.Add(img);
            }
            cell.Add(thumbBox);

            var name = new Label(item.name);
            name.AddToClassList("zui-cell__name");
            name.style.maxWidth = 100f;
            cell.Add(name);

            cell.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                browsing = false;
                SetMap(item);
                e.StopPropagation();
            });
            return cell;
        }

        Texture2D Thumb(MetaMap item)
        {
            if (browseThumbs.TryGetValue(item, out var t) && t != null) return t;
            var tex = ((IVisualPreview)item).RenderPreviewTexture();
            if (tex != null) browseThumbs[item] = tex;
            return tex;
        }
    }
}
