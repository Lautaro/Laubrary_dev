using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Laubrary.Zoetrope;
using Laubrary.Caching;
using Laubrary.Pyre;
using PyreAsset = Laubrary.Pyre.Pyre;   // the class is shadowed by the namespace inside a Laubrary.* namespace
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Laubrary.Mirage
{
    /// <summary>
    /// The one component in the hierarchy. Owns NO UI — it only realizes a <see cref="MirageView"/> into
    /// live GameObjects and exposes Add/Remove/Reposition as the seam <c>MirageWindow</c>/<c>MirageHud</c>
    /// call into. Every write goes straight to the live <see cref="view"/> asset (same persistence model as
    /// Pyre's Pyre — no clone, no discard on Stop). Runs in Edit mode too (<see cref="ExecuteAlways"/>)
    /// so previewables show up and can be repositioned without pressing Play — realized GameObjects are
    /// <see cref="HideFlags.DontSave"/> so they never leak into the saved scene.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Laubrary/Mirage/Mirage Rig")]
    public class MirageRig : MonoBehaviour
    {
        [FormerlySerializedAs("setup")] public MirageView view;

        [Tooltip("The real preview Camera (same one MirageHud uses) — gets clearFlags=SolidColor + the " +
                 "active view's BackSplash.cameraColor applied every tick. Optional: leave null to not touch " +
                 "any camera (e.g. a MirageRig used purely for edit-mode composition, no dedicated camera yet).")]
        public Camera previewCamera;

        const string ActiveViewGuidKey = "Laubrary.Mirage.ActiveViewGuid";

        readonly Dictionary<string, GameObject> _live = new Dictionary<string, GameObject>();
        readonly HashSet<string> _pendingZoeScale = new HashSet<string>();
        readonly HashSet<string> _pendingRealize = new HashSet<string>();
        GameObject _backdrop;

        const float SelfCaptureInterval = 2f;
        float _selfCaptureClock;

        void OnEnable()
        {
            // A previewed asset was edited → rebuild the live entries showing it (task #7). Mirrors MirageSubject's
            // proven Zoe path but for the OTHER content types (a Pyre's bare BlastPlayer fetches its frames once and
            // never re-fetches, so without this a Pyre edit only shows up on the next Play/Stop or view switch).
            AssetCacheInvalidation.Invalidated += HandleAssetInvalidated;
#if UNITY_EDITOR
            MirageActiveView.Changed += HandleActiveViewChanged;
            if (view == null)
            {
                var guid = EditorPrefs.GetString(ActiveViewGuidKey, "");
                if (!string.IsNullOrEmpty(guid))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(path)) view = AssetDatabase.LoadAssetAtPath<MirageView>(path);
                }
            }
#endif
            if (view == null) return;
            foreach (var entry in view.previewables) Realize(entry);
        }

        void OnDisable()
        {
            AssetCacheInvalidation.Invalidated -= HandleAssetInvalidated;
#if UNITY_EDITOR
            MirageActiveView.Changed -= HandleActiveViewChanged;
#endif
            ClearLive();
        }

        // Only SETS pending ids — the destroy + re-realize is deferred to Update(). This callback fires reentrant to
        // Unity's ObjectChangeEvents dispatch, where synchronous scene mutation (DestroyImmediate / AddComponent)
        // trips the "Access version should be odd" assertion — the exact trap MirageSubject.HandleAssetInvalidated
        // documents. Zoe entries are skipped: their own MirageSubject already destroy+respawns on invalidation, so
        // rebuilding them here too would double-spawn.
        void HandleAssetInvalidated(Object asset)
        {
            if (view == null || asset == null) return;
            foreach (var entry in view.previewables)
                if (entry != null && entry.content == asset && !(entry.content is Zoe))
                    _pendingRealize.Add(entry.id);
        }

        void Update()
        {
            SyncBackSplash();
            if (view == null) { if (_live.Count > 0) ClearLive(); return; }

#if UNITY_EDITOR
            // Refresh the CURRENTLY active view's own thumbnail periodically, in EDIT MODE TOO (not just
            // Play) — CaptureNow below is a synchronous camera render, not a coroutine, so there's no
            // Application.isPlaying gate needed: coroutines never advance in Edit mode at all (no Player
            // Loop running to drive them), which is exactly why this used to be Play-mode-only and a view
            // composed purely in Edit mode (never pressed Play) never got a thumbnail. Throttled (not every
            // frame) since SetThumbnail does a real AssetDatabase.SaveAssets each time.
            _selfCaptureClock += Time.unscaledDeltaTime;
            if (_selfCaptureClock >= SelfCaptureInterval)
            {
                _selfCaptureClock = 0f;
                CaptureNow(view);
            }
#endif

            // Rebuild any entry whose content asset was just edited (deferred from HandleAssetInvalidated, task #7):
            // destroy the stale live GameObject and re-realize it, so e.g. a Pyre blast reflects the edit INSTANTLY
            // (a fresh BlastPlayer.Play() re-fetches from the now-invalidated cache) instead of only on the next
            // Play/Stop or view switch. Done before the membership reconcile below so the fresh GO is already in
            // _live and isn't seen as "new" and realized twice.
            if (_pendingRealize.Count > 0)
            {
                foreach (var id in _pendingRealize)
                {
                    if (_live.TryGetValue(id, out var stale)) { DestroyGO(stale); _live.Remove(id); _pendingZoeScale.Remove(id); }
                    var entry = FindEntry(id);
                    if (entry != null) Realize(entry);
                }
                _pendingRealize.Clear();
            }

            // Live children are a pure reflection of view.previewables — any writer (MirageWindow's
            // Add/Remove/Position, MirageHud's click/drag, Undo, a script) just edits the data; this
            // reconciles MEMBERSHIP every tick instead of every caller remembering to also push a live
            // update. Real bug this fixes: MirageWindow.AddEntry/RemoveEntry only ever mutated view.
            // previewables directly (unlike MirageRig's own same-named methods, which also touch _live) —
            // so removing/adding via the window did nothing live until a Play/Stop cycle forced a full
            // OnDisable/OnEnable rebuild. Realize anything new; destroy anything no longer in the list.
            _seenIds.Clear();
            foreach (var entry in view.previewables)
            {
                _seenIds.Add(entry.id);
                if (!_live.ContainsKey(entry.id)) Realize(entry);
            }
            _staleIds.Clear();
            foreach (var id in _live.Keys) if (!_seenIds.Contains(id)) _staleIds.Add(id);
            foreach (var id in _staleIds)
            {
                DestroyGO(_live[id]);
                _live.Remove(id);
                _pendingZoeScale.Remove(id);
            }

            // Live GameObjects are a pure view of entry.position too — reconciles the transform every tick.
            foreach (var entry in view.previewables)
            {
                if (!_live.TryGetValue(entry.id, out var go) || go == null) continue;
                var want = new Vector3(entry.position.x, entry.position.y, 0f);
                if (go.transform.position != want) go.transform.position = want;
            }

            if (_pendingZoeScale.Count == 0) return;
            // A Zoe's sprite/PPU isn't known until its own MirageSubject.Start() has run — poll once until ready.
            _resolved.Clear();
            foreach (var id in _pendingZoeScale)
            {
                if (!_live.TryGetValue(id, out var go) || go == null) { _resolved.Add(id); continue; }
                var ps = go.GetComponent<MirageSubject>();
                if (ps == null || ps.Spawned == null) continue;
                var sr = ps.Spawned.GetComponentInChildren<SpriteRenderer>();
                if (sr == null || sr.sprite == null) continue;

                var entry = FindEntry(id);
                if (entry != null) ApplyScale(go, sr.sprite.pixelsPerUnit, entry.scale);
                _resolved.Add(id);
            }
            foreach (var id in _resolved) _pendingZoeScale.Remove(id);
        }
        readonly List<string> _resolved = new List<string>();
        readonly HashSet<string> _seenIds = new HashSet<string>();
        readonly List<string> _staleIds = new List<string>();

        PreviewableEntry FindEntry(string id)
        {
            if (view == null) return null;
            foreach (var e in view.previewables) if (e.id == id) return e;
            return null;
        }

        /// The live realized GameObject for an entry, if this rig currently has one — used by MirageWindow's
        /// Ping button to locate a previewable that might not be visibly rendering anything (e.g. a Zoe whose
        /// idle pose is a blank first frame).
        public GameObject GetLive(string id) => _live.TryGetValue(id, out var go) ? go : null;

        // Applies the active view's BackSplash to the real preview camera (solid colour) and a dedicated
        // backdrop SpriteRenderer, every tick — same "data is truth, live objects just reflect it" approach
        // as the position-sync above, so editing a BackSplash's fields (in Pyre's window, BackSplash's own
        // window, or a future Mirage picker) shows up immediately with no extra change-detection wiring.
        void SyncBackSplash()
        {
            var bs = view != null ? view.backSplash : null;

            if (previewCamera != null && bs != null)
            {
                previewCamera.clearFlags = CameraClearFlags.SolidColor;
                previewCamera.backgroundColor = bs.cameraColor;
            }

            if (bs == null || bs.image == null)
            {
                if (_backdrop != null) { DestroyGO(_backdrop); _backdrop = null; }
                return;
            }

            if (_backdrop == null)
            {
                _backdrop = new GameObject("~BackSplash") { hideFlags = HideFlags.DontSave };
                _backdrop.transform.SetParent(transform, false);
                _backdrop.AddComponent<SpriteRenderer>().sortingOrder = -1000;   // behind everything, including entry backgrounds (-100)
            }
            var sr = _backdrop.GetComponent<SpriteRenderer>();
            sr.sprite = bs.image;
            sr.color = bs.imageTint;
            // imagePos is an OFFSET FROM THE CAMERA'S OWN VIEW CENTER, not an absolute world position — matches
            // Pyre's own IMGUI preview, which adds imagePos to an already-centered rect (see PyreWindow's
            // DrawPreview). Previously this assigned imagePos directly as the absolute position, so (0,0) only
            // looked centered if the camera itself happened to sit at world (0,0) — this scene's camera is at
            // (0, 2, -10), so (0,0) actually landed 2 units below true center, a real "dot in the middle of the
            // pad doesn't center the image" bug.
            Vector3 camCenter = previewCamera != null ? previewCamera.transform.position : Vector3.zero;
            _backdrop.transform.position = new Vector3(camCenter.x + bs.imagePos.x, camCenter.y + bs.imagePos.y, 0f);
            float displayPpu = view != null ? view.displayPixelsPerUnit : 64f;
            float srcPpu = bs.image.pixelsPerUnit;
            float ppuScale = srcPpu > 0f ? displayPpu / srcPpu : 1f;
            _backdrop.transform.localScale = Vector3.one * (ppuScale * Mathf.Max(0.01f, bs.imageZoom));
        }

        void ApplyScale(GameObject go, float sourcePixelsPerUnit, float authorScale)
        {
            float displayPpu = view != null ? view.displayPixelsPerUnit : 64f;
            float ppuScale = sourcePixelsPerUnit > 0f ? displayPpu / sourcePixelsPerUnit : 1f;
            go.transform.localScale = Vector3.one * (ppuScale * Mathf.Max(0.01f, authorScale));
        }

        void Realize(PreviewableEntry entry)
        {
            if (entry == null || entry.content == null || _live.ContainsKey(entry.id)) return;

            var go = new GameObject(entry.content.name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(entry.position.x, entry.position.y, 0f);
            _live[entry.id] = go;

            switch (entry.content)
            {
                case Zoe zoe:
                {
                    var ps = go.AddComponent<MirageSubject>();
                    ps.zoe = zoe;
                    ps.weapon = entry.weapon;
                    ps.choreography = entry.choreography;
                    ps.clips = entry.clips;
                    ps.previewAimDirection = entry.previewAimDirection;
                    ps.previewPose = entry.previewPose;
                    ps.targetPractice = entry.targetPractice;
                    ps.respawnDelay = entry.respawnDelay;
                    _pendingZoeScale.Add(entry.id);   // PPU not known until ps.Spawned exists — resolved in Update()
                    break;
                }
                case PyreAsset blast:
                {
                    // BlastPlayer.Awake() runs SYNCHRONOUSLY during AddComponent, before `spec` is assigned
                    // below — it sees spec==null and never builds frames or starts playing (the same
                    // AddComponent-timing trap WeaponMuzzleCue.Configure() exists to avoid). Kick Play()
                    // explicitly once spec is actually set instead of relying on Awake's playOnAwake path.
                    var bp = go.AddComponent<BlastPlayer>();
                    bp.spec = blast;
                    bp.loop = true;
                    bp.Play();
                    ApplyScale(go, blast.pixelsPerUnit, entry.scale);
                    break;
                }
                case Sprite sprite:
                {
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = sprite;
                    sr.sortingOrder = -100;   // background — draw behind everything else
                    ApplyScale(go, sprite.pixelsPerUnit, entry.scale);
                    break;
                }
                default:
                    Debug.LogWarning($"[Mirage] Unsupported previewable content type: {entry.content.GetType().Name}");
                    break;
            }
        }

        static void DestroyGO(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        void ClearLive()
        {
            foreach (var go in _live.Values) DestroyGO(go);
            _live.Clear();
            _pendingZoeScale.Clear();
            if (_backdrop != null) { DestroyGO(_backdrop); _backdrop = null; }
        }

        /// Adds a new entry, persists it straight to <see cref="view"/>, and realizes it live immediately.
        public PreviewableEntry AddEntry(UnityEngine.Object content, Vector2 position)
        {
            if (view == null || content == null) return null;
            var entry = view.AddEntry(content, position);
            MarkDirty();
            Realize(entry);
            return entry;
        }

        public void RemoveEntry(string id)
        {
            if (view == null) return;
            view.previewables.RemoveAll(e => e.id == id);
            if (_live.TryGetValue(id, out var go)) DestroyGO(go);
            _live.Remove(id);
            _pendingZoeScale.Remove(id);
            MarkDirty();
        }

        public void Reposition(string id, Vector2 position)
        {
            var entry = FindEntry(id);
            if (entry == null) return;
            entry.position = position;
            if (_live.TryGetValue(id, out var go) && go != null)
                go.transform.position = new Vector3(position.x, position.y, 0f);
            MarkDirty();
        }

        void MarkDirty()
        {
#if UNITY_EDITOR
            if (view != null) EditorUtility.SetDirty(view);
#endif
        }

#if UNITY_EDITOR
        /// Fires whenever the Mirage window's browser selects a different MirageView — hot-swaps this rig
        /// onto it instantly instead of only picking it up on the next OnEnable. Snapshots the OUTGOING
        /// view first (see CaptureNow) so its thumbnail stays current, in Edit mode or Play mode alike.
        void HandleActiveViewChanged(MirageView newView)
        {
            if (newView == view) return;
            if (view != null) CaptureNow(view);
            SwapTo(newView);
        }

        const int ThumbnailSize = 256;

        /// Synchronous camera-render capture — works identically in Edit mode and Play mode, unlike the
        /// coroutine-based ScreenCapture.CaptureScreenshotAsTexture() approach this replaced: a coroutine
        /// never advances in Edit mode at all (no Player Loop running to drive its yields), and
        /// ScreenCapture itself needs a real composited screen frame, which effectively only exists reliably
        /// during Play. Renders previewCamera into a temporary RenderTexture and reads it back directly —
        /// the same technique Unity's own AssetPreview generation uses, and it's why this needs no
        /// WaitForEndOfFrame/StartCoroutine at all: Camera.Render() renders and returns immediately.
        void CaptureNow(MirageView target)
        {
            if (target == null || previewCamera == null) return;
            var rt = RenderTexture.GetTemporary(ThumbnailSize, ThumbnailSize, 24, RenderTextureFormat.ARGB32);
            var prevTarget = previewCamera.targetTexture;
            var prevActive = RenderTexture.active;
            previewCamera.targetTexture = rt;
            previewCamera.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(ThumbnailSize, ThumbnailSize, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, ThumbnailSize, ThumbnailSize), 0, 0);
            tex.Apply();
            previewCamera.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
            target.SetThumbnail(tex);
        }

        void SwapTo(MirageView newView)
        {
            ClearLive();
            view = newView;
            if (view != null) foreach (var entry in view.previewables) Realize(entry);
        }
#endif
    }
}
