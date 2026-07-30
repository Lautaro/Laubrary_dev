using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace Laubrary.TextSplash
{
    /// <summary>A live splash you can await or stop early. `Show()` returns one.</summary>
    public sealed class SplashHandle
    {
        public bool IsDone { get; internal set; }
        public bool IsCancelled { get; private set; }
        /// Fires once when the splash finishes (or is cancelled).
        public event System.Action Completed;
        /// Stop the splash on its next frame.
        public void Cancel() => IsCancelled = true;
        internal void Complete() { if (IsDone) return; IsDone = true; Completed?.Invoke(); }
    }

    /// <summary>Transform-level pose of the whole line at one instant — everything a caller pushes onto the text's
    /// transform. With per-letter animation ON the letters carry their motion inside the MESH instead, so every
    /// field comes back neutral and only <see cref="done"/> still means anything.</summary>
    public struct SplashPose
    {
        public Vector2 offset;   // offset from the rest anchor, in the caller's units
        public float alpha;      // 0..1
        public float scale;      // uniform scale multiplier
        public float spinDeg;    // degrees
        public SplashAxis spinAxis;
        public bool done;        // the play has finished
    }

    /// <summary>The runtime host for one <see cref="TextSplash"/>: a Canvas + a TMP line (plus a border twin and,
    /// when depth is on, a stack of layer copies behind it) driven through entrance → hold → exit, then
    /// self-destroyed. Spawned by <see cref="TextSplash.Show"/>; never added by hand.
    ///
    /// Everything that decides how a frame LOOKS lives in the pure statics below —
    /// <see cref="ApplyLook"/> (text/font/size/fill/border dilation/bevel), <see cref="EvaluateLine"/> (the
    /// whole-line pose) and <see cref="ApplyMesh"/> (per-letter motion, spatial fills, depth offset) — so the editor
    /// window's live preview drives the exact same math against a scrub time and can never drift from what actually
    /// plays. All timing comes from <see cref="SplashSchedule"/>; none of it is re-derived here — every window is
    /// read through its <c>InProgress</c> / <c>OutProgress</c> / <c>LetterLife</c>, which is what lets the entrance
    /// and the exit cascade in different orders without a single caller here needing to know it.
    ///
    /// This component also owns the CANVAS MODE. A screen-space-OVERLAY canvas discards z outright, so
    /// <see cref="SplashDepth"/> would look right in the editor preview (a real 3D scene) and be invisible in game;
    /// <see cref="TextSplash.NeedsCameraSpace"/> therefore moves the canvas to screen-space-CAMERA here, once, and
    /// <see cref="SplashPixelRig"/> adopts that canvas rather than flipping it a second time. The camera driving it
    /// is the SCENE'S OWN (see <see cref="SetupCameraSpace"/>) — a private one is a last resort, because under URP a
    /// second Base camera sharing the screen re-clears it instead of compositing over it.</summary>
    [AddComponentMenu("")]
    public sealed class SplashPlayer : MonoBehaviour
    {
        // A Min-Max scalar rolls once per play (or once per letter) off a key hashed from the play seed, the letter
        // and a SALT. The salts have to differ per scalar: sharing one would hand a random size and a random alpha
        // the identical roll every time, so they would move in lockstep instead of independently.
        const int SaltSize = 1, SaltAlpha = 2, SaltBorder = 3;

        // The FALLBACK camera (built only when the scene has none to borrow) is parked far from the scene so that
        // even if the layer scan has to fall back to a shared layer, no real content can wander into its frustum.
        const float CameraPark = 10000f;
        // …and the canvas sits well down that camera's axis, with the clip range spread either side of it, so no
        // part of a spinning line (which swings glyphs deep in z) can fall behind the near plane. Under ORTHOGRAPHIC
        // projection the distance is free: it changes what gets clipped, never how big anything draws.
        const float CanvasPlane = 3000f;

        const string BevelShaderName = "TextMeshPro/Distance Field";

        TextSplash _spec;
        TextMeshProUGUI _tmp;
        TMP_Text _border;
        GameObject _borderGO;
        readonly List<GameObject> _depthGO = new List<GameObject>();
        readonly List<TMP_Text> _depth = new List<TMP_Text>();
        string _text;
        float _hold, _elapsed;
        int _playSeed;
        SplashHandle _handle;

        // Camera-space rig (depth only) — all null/-1 while the canvas is still an overlay.
        Canvas _canvas;
        // The camera the canvas is currently driven by: the scene's own, or the owned fallback below. Remembered so
        // the player can tell "nobody has touched it" from "the pixel rig retargeted it" and stand down accordingly.
        Camera _canvasCam;
        // The fallback camera this player BUILT — null whenever the canvas is driven by one the scene already had.
        Camera _camera;
        GameObject _camGO;
        int _camLayer = -1;

        /// <summary>The camera this player created for its own canvas, or null when the canvas is driven by a camera
        /// it merely BORROWED from the scene. <see cref="SplashPixelRig"/> asks before adopting: parking a borrowed
        /// camera would blank the game, and its isolated layer is not ours to reuse.</summary>
        internal Camera OwnedCanvasCamera => _camera;

        public static SplashHandle Play(TextSplash spec, string overrideText, float? overrideHold)
        {
            var handle = new SplashHandle();
            if (spec == null) { handle.Complete(); return handle; }

            // ONE seed for the whole play. Every Min-Max roll and the Random letter order hash off it, so a play
            // looks identical from its first frame to its last instead of strobing a fresh random every frame.
            int playSeed = Random.Range(1, int.MaxValue);

            var go = new GameObject("[TextSplash] " + spec.name) { hideFlags = HideFlags.HideAndDontSave };
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;   // above gameplay UI

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            var rt = tmp.rectTransform;
            rt.anchorMin = rt.anchorMax = spec.anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(4000f, 800f);   // generous; the text overflows freely
            ApplyLook(spec, tmp, overrideText, 0f, playSeed, false);
            tmp.ForceMeshUpdate();                     // the first schedule needs a real character count

            var player = go.AddComponent<SplashPlayer>();
            player._spec = spec; player._tmp = tmp; player._text = overrideText; player._canvas = canvas;
            player._hold = overrideHold ?? spec.holdDuration;
            player._playSeed = playSeed;
            player._handle = handle;

            // Canvas MODE is decided HERE and nowhere else, so depth and pixelation cannot each flip it and fight.
            // The pixel rig, attached at the end of this method, finds the canvas already in camera space and
            // RETARGETS it at its own low-res camera instead of re-configuring it.
            if (spec.NeedsCameraSpace) player.SetupCameraSpace(canvas);

            // Both of these are copies of the face, so they inherit its layer and are carried by its transform.
            player._border = EnsureBorderTwin(spec, tmp, ref player._borderGO);
            EnsureDepthLayers(spec, tmp, player._depthGO, player._depth);

            TryAttachPixelation(go, canvas, spec);
            return handle;
        }

        void Update()
        {
            if (_spec == null || _tmp == null || _handle == null) { Finish(); return; }
            if (_handle.IsCancelled) { Finish(); return; }

            _elapsed += Time.unscaledDeltaTime;
            var canvasRT = _canvas != null ? _canvas.transform as RectTransform : null;
            Vector2 cs = canvasRT != null ? canvasRT.rect.size : new Vector2(Screen.width, Screen.height);
            MaintainCanvasCamera(cs);

            // Rebuilt every frame from the LIVE character count and the play seed: the count decides how far the
            // cascade spreads, and every window a letter is read through comes out of this one schedule.
            var sch = _spec.Schedule(_tmp.textInfo.characterCount, _hold, _playSeed);
            float lineLife = sch.LineLife(_elapsed);

            // Look first, then the pose, then the mesh — ApplyMesh regenerates the mesh, so anything that feeds
            // vertex colours has to be set before it runs.
            ApplyLook(_spec, _tmp, _text, lineLife, _playSeed, false);
            // "Just off screen" depends on how wide the text actually is, so the slide needs the laid-out mesh.
            Vector2 textHalf = TextHalfExtents(_tmp);
            var pose = EvaluateLine(_spec, sch, _elapsed, cs.x, cs.y, textHalf, _playSeed);
            _tmp.alpha = pose.alpha;
            ApplyMesh(_spec, _tmp, sch, _elapsed, cs.x, cs.y, _playSeed, false);

            if (_border != null)
            {
                ApplyLook(_spec, _border, _text, lineLife, _playSeed, true);
                _border.alpha = pose.alpha;
                ApplyMesh(_spec, _border, sch, _elapsed, cs.x, cs.y, _playSeed, true);
            }

            DriveDepth(sch, lineLife, pose, cs);

            // The twin and the layers are children of the face, so the line pose carries them along for free — and
            // that is also what makes a whole-line depth step follow the line's own tilt and spin.
            var rt = _tmp.rectTransform;
            rt.anchoredPosition = pose.offset;
            rt.localScale = Vector3.one * pose.scale;
            rt.localRotation = RestTilt(_spec) * Quaternion.AngleAxis(pose.spinDeg, AxisVec(pose.spinAxis));

            if (pose.done) Finish();
        }

        /// <summary>Drive the depth stack for this frame: every layer gets the SAME look and the SAME per-letter mesh
        /// animation the pass it mirrors gets — otherwise the copies would drift apart the moment a letter spun —
        /// plus its own step away from the viewer and its own share of the DEPTH FALLOFF: how far this layer is
        /// carried toward the SIDES' own fill (<see cref="SplashDepth.sideFill"/>, brightened and saturated by
        /// <see cref="SplashDepth.sideBrightness"/> / <see cref="SplashDepth.sideSaturation"/>). The rearmost layer
        /// goes <see cref="SplashDepth.darken"/> of the way there and every layer in front of it a proportional
        /// share, so the extrusion reads as a surface receding rather than as a stack of stickers.
        ///
        /// Where the step is applied depends on who owns the rotation. With per-letter animation OFF the whole line
        /// spins on the face's transform and every layer is a CHILD of it, so a plain local z carries the step
        /// through that rotation for free. With it ON each glyph spins inside the mesh, so the step has to travel
        /// with the glyph — <see cref="ApplyMesh"/> takes it as a local offset and applies it AFTER the spin.</summary>
        void DriveDepth(in SplashSchedule sch, float lineLife, in SplashPose pose, Vector2 cs)
        {
            int extra = _depth.Count;
            if (extra == 0 || _spec.depth == null) return;

            var d = _spec.depth;
            // The layers extrude whatever silhouette is OUTERMOST: the dilated border when there is one, the face
            // otherwise. Extruding the face behind a fat border would leave the border floating off the front of a
            // narrower block.
            bool borderPass = DrawsBorder(_spec);
            float total = Mathf.Max(0f, d.distance) * LayoutSize(_spec, lineLife, _playSeed);
            float step = total / extra;
            bool meshDepth = _spec.PerLetter;

            for (int i = 0; i < extra; i++)
            {
                var layer = _depth[i];
                if (layer == null) continue;
                float z = step * (i + 1);
                // Layer `i` here is layer i+1 of the extrusion (the FACE is layer 0), so this is darken · i/(n−1)
                // over the whole stack: 0 at the face, the full falloff at the rearmost copy. Flat sides skip the
                // ramp entirely — every layer takes the side colour outright, which is what a solid extruded block
                // looks like; the ramp reads more like a soft shadow behind the letters.
                float fall = d.flatSides ? 1f : Mathf.Clamp01(d.darken) * (i + 1) / extra;

                ApplyLook(_spec, layer, _text, lineLife, _playSeed, borderPass);
                layer.alpha = pose.alpha;
                ApplyMesh(_spec, layer, sch, _elapsed, cs.x, cs.y, _playSeed, borderPass,
                          meshDepth ? z : 0f, fall);
                layer.rectTransform.anchoredPosition3D = new Vector3(0f, 0f, meshDepth ? 0f : z);
            }
        }

        void Finish()
        {
            _handle?.Complete();
            if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
        }

        void OnDestroy()
        {
            // The depth camera is deliberately NOT a child of the canvas (a screen-space-camera canvas is driven by
            // its camera's transform, so parenting one under the other is circular), which means it has to go by hand.
            if (_camGO != null) { DestroyObj(_camGO); _camGO = null; _camera = null; }
            if (_camLayer >= 0) { ReleaseIsolatedLayer(_camLayer); _camLayer = -1; }
        }

        /// <summary>Move the splash canvas into CAMERA space. Depth needs this and nothing else does: a
        /// screen-space-overlay canvas ignores vertex z entirely, so a stack of layers stepped away from the viewer
        /// would collapse into one.
        ///
        /// The canvas is driven by a camera the SCENE ALREADY HAS — <see cref="Camera.main"/> first, otherwise the
        /// last-drawing enabled camera. It has to be an existing one: under URP a second BASE camera sharing the
        /// screen does not composite over the first, it re-clears it (an "Uninitialized" background), so the
        /// dedicated camera this used to build changed the game view's background colour the moment depth came on.
        /// One Base camera means one clear, and a screen-space-camera canvas hung in front of it composites
        /// correctly with no clear of its own.
        ///
        /// Two honest consequences of drawing through the scene's camera, neither of which applied to a private
        /// one: the splash is now inside that camera's frame, so scene geometry CLOSER than the canvas plane can
        /// occlude it (TMP's SDF shaders depth-test through `unity_GUIZTestMode`, which is only `Always` for an
        /// overlay canvas), and it is composited before that camera's post-processing rather than after it.
        ///
        /// Only when there is no camera to borrow does it build one — see <see cref="SetupOwnCamera"/>.</summary>
        void SetupCameraSpace(Canvas canvas)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            var cam = ResolveSceneCamera();
            if (cam != null) BorrowCamera(canvas, cam);
            else SetupOwnCamera(canvas);
        }

        /// <summary>Hang the canvas in front of a camera the scene already owns. Nothing about that camera is
        /// written to — not its clear flags, not its culling mask, not its clip planes — so a splash can never
        /// change how the game itself renders. The only thing that moves is the SPLASH: onto a layer that camera
        /// actually draws, and to a plane distance its clip range can hold (<see cref="PlaneDistanceFor"/>).</summary>
        void BorrowCamera(Canvas canvas, Camera cam)
        {
            _canvasCam = cam;
            canvas.worldCamera = cam;
            canvas.planeDistance = PlaneDistanceFor(cam, CanvasHeightUnits(canvas));
            EnsureVisibleLayer(cam);
        }

        /// <summary>Keep the canvas's camera valid for the whole play. A borrowed camera is not ours and can be
        /// destroyed, disabled or replaced under us (a cutscene camera taking over, a scene load, the tagged main
        /// camera changing), so it is re-resolved every frame rather than cached once — and the plane distance with
        /// it, since a window resize changes the canvas's own height.</summary>
        void MaintainCanvasCamera(Vector2 cs)
        {
            if (_canvas == null || _canvas.renderMode != RenderMode.ScreenSpaceCamera) return;
            // Stand down the moment somebody else owns the canvas's camera: SplashPixelRig retargets this very
            // canvas at its own low-res camera, and re-pointing it back at the scene's every frame would fight it.
            if (_canvas.worldCamera != _canvasCam) return;

            if (_camera != null)
            {
                // Our own camera exists to frame the canvas, so it follows the canvas's size — this is what keeps
                // one canvas unit one world unit as the window is resized.
                _camera.orthographicSize = Mathf.Max(1f, cs.y * 0.5f);
                return;
            }

            var cam = ResolveSceneCamera();
            if (cam == null)
            {
                // Every camera went away mid-play. Rather than render nothing, fall back to a private one — the
                // splash then behaves exactly as it did before, alone on its own layer.
                SetupOwnCamera(_canvas);
                return;
            }
            if (cam != _canvasCam) { _canvasCam = cam; _canvas.worldCamera = cam; EnsureVisibleLayer(cam); }
            _canvas.planeDistance = PlaneDistanceFor(cam, CanvasHeightUnits(_canvas));
        }

        /// <summary>The scene camera the splash should hang in front of: the tagged main camera, else the one that
        /// draws LAST (highest depth) among the enabled cameras that render to the screen — the frame the splash
        /// has to land on top of. The currently borrowed camera is preferred over a fresh scan while it is still
        /// alive, so the common per-frame path is one cached <see cref="Camera.main"/> lookup and no allocation.</summary>
        Camera ResolveSceneCamera()
        {
            var main = Camera.main;
            if (main != null && main.isActiveAndEnabled) return main;
            if (_canvasCam != null && _canvasCam.isActiveAndEnabled) return _canvasCam;

            Camera best = null;
            var cams = Camera.allCameras;                      // enabled cameras only
            for (int i = 0; i < cams.Length; i++)
            {
                var c = cams[i];
                if (c == null || !c.isActiveAndEnabled) continue;
                if (c.targetTexture != null) continue;         // draws into a buffer, not onto the screen
                if (best == null || c.depth > best.depth) best = c;
            }
            return best;
        }

        /// <summary>Where to hang the canvas plane in front of a BORROWED camera. Two things have to hold: the
        /// plane must sit inside the camera's clip range with room either side of it — the depth stack steps AWAY
        /// from the viewer and a 3D spin swings glyphs TOWARD them, by as much as half the line's width — and the
        /// canvas must not end up at an absurd world scale.
        ///
        /// PERSPECTIVE: a screen-space-camera canvas always fills the frustum, so the distance changes the canvas's
        /// world SCALE but never what is on screen — and because the glyphs' z swing scales with it, whether a spin
        /// clips is very nearly distance-independent. That frees the distance to be chosen for scale, so it is the
        /// one at which the canvas fills the frustum at exactly one world unit per canvas unit.
        ///
        /// ORTHOGRAPHIC: the scale is the camera's own `orthographicSize` and nothing here can change it (1:1 is
        /// simply not on offer through a borrowed ortho camera — the extrusion stays PROPORTIONAL, which is what
        /// actually matters, since the z steps live in the same canvas units as the glyphs). The distance changes
        /// nothing about size, so it is spent entirely on headroom: far enough out that a line spun about its own
        /// centre stays in front of the near plane.
        ///
        /// Both are then clamped well inside the clip range — never hugging the near plane, and never past the
        /// halfway mark, which leaves as much room again behind the plane for the extrusion as there is in front
        /// of it for the spin.</summary>
        static float PlaneDistanceFor(Camera cam, float canvasHeightUnits)
        {
            float near = Mathf.Max(0.01f, cam.nearClipPlane);
            float far = Mathf.Max(near * 8f, cam.farClipPlane);
            float lo = near * 4f;
            float hi = Mathf.Max(lo, far * 0.5f);

            if (cam.orthographic)
            {
                // Half a canvas-wide line, in the camera's own world units — the deepest a whole-line spin can
                // swing toward the viewer.
                float swing = Mathf.Max(1f, cam.orthographicSize) * Mathf.Max(1f, cam.aspect);
                return Mathf.Clamp(near + swing * 1.2f, lo, hi);
            }

            float tan = Mathf.Tan(Mathf.Clamp(cam.fieldOfView, 1f, 179f) * 0.5f * Mathf.Deg2Rad);
            float oneToOne = tan > 1e-4f ? canvasHeightUnits * 0.5f / tan : hi;
            return Mathf.Clamp(oneToOne, lo, hi);
        }

        /// The canvas's own height in CANVAS units — screen pixels divided by whatever is scaling it. Read off
        /// Screen rather than the RectTransform so it is valid before the first layout has run.
        static float CanvasHeightUnits(Canvas canvas)
            => Mathf.Max(1f, Screen.height) / Mathf.Max(0.0001f, canvas != null ? canvas.scaleFactor : 1f);

        /// <summary>Put the splash on a layer the borrowed camera actually renders. A canvas the camera culls is
        /// simply invisible, with nothing on screen to explain why — and the splash is created on the default
        /// layer, which a project is free to have masked out.</summary>
        void EnsureVisibleLayer(Camera cam)
        {
            int mask = cam.cullingMask;
            if (mask == 0 || (mask & (1 << gameObject.layer)) != 0) return;
            for (int i = 0; i < 32; i++)
                if ((mask & (1 << i)) != 0) { SetLayerRecursively(gameObject, i); return; }
        }

        /// <summary>Build a camera of the splash's OWN — the last resort, used only when the scene has none to
        /// borrow (and the behaviour every camera-space splash used to get).
        ///
        /// It is orthographic — which is why <see cref="SplashDepth.tilt"/> matters, since an orthographic view of
        /// an untilted extrusion is exactly edge-on — culls to a layer nothing else draws on, and clears only depth
        /// so it composites over whatever the game already rendered. That last part is the reason it is a fallback
        /// and no longer the default: under URP a second Base camera sharing the screen re-clears it rather than
        /// compositing over it, so this path can still change the game view's background.</summary>
        void SetupOwnCamera(Canvas canvas)
        {
            if (_camera != null) return;
            _camLayer = ClaimIsolatedLayer();
            SetLayerRecursively(gameObject, _camLayer);

            _camGO = new GameObject("[TextSplash] Depth Camera") { hideFlags = HideFlags.HideAndDontSave };
            _camGO.transform.position = new Vector3(0f, CameraPark, 0f);
            _camera = _camGO.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = Mathf.Max(1f, Screen.height * 0.5f);
            _camera.clearFlags = CameraClearFlags.Depth;
            _camera.cullingMask = 1 << _camLayer;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = CanvasPlane * 2f;
            _camera.depth = TopCameraDepth() + 10f;    // after every gameplay camera
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            _camera.useOcclusionCulling = false;

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _camera;
            canvas.planeDistance = CanvasPlane;
            _canvasCam = _camera;
        }

        static float TopCameraDepth()
        {
            float top = 0f;
            var cams = Camera.allCameras;
            for (int i = 0; i < cams.Length; i++) if (cams[i].depth > top) top = cams[i].depth;
            return top;
        }

        /// The one seam that reaches for the optional pixelation rig, so a project that doesn't ship
        /// <see cref="SplashPixelRig"/> has exactly one line to remove.
        static void TryAttachPixelation(GameObject go, Canvas canvas, TextSplash spec)
        {
            if (spec.pixelation != null && spec.pixelation.enabled) SplashPixelRig.Attach(go, canvas, spec);
        }

        // ── isolated layers (shared with the pixel rig) ───────────────────────────────

        // Layers currently held by a LIVE splash camera — a depth camera here, or a pixel rig's buffer camera. Two
        // of them must never share one: the cameras are parked at the same spot, so a shared layer would put both
        // splashes in both frames. ONE registry for both features, so they cannot hand out the same layer twice.
        static int _claimedLayers;

        /// <summary>Take a layer for a splash camera to cull to — one nothing else draws on. "Unnamed" is a weak
        /// signal for that by itself (a project can park objects on a layer by index without ever naming it), so two
        /// stronger ones come first: a layer another live splash already holds is skipped outright, and a layer no
        /// live camera even renders is preferred over one that is merely unnamed. If every user layer is named it
        /// falls back to UI; the camera being parked thousands of units from the origin is what makes even that
        /// survivable. Release it with <see cref="ReleaseIsolatedLayer"/>.</summary>
        internal static int ClaimIsolatedLayer()
        {
            int rendered = 0;
            var cams = Camera.allCameras;
            for (int i = 0; i < cams.Length; i++) rendered |= cams[i].cullingMask;

            int free = -1, unnamed = -1;
            for (int i = 31; i >= 8 && free < 0; i--)
            {
                int bit = 1 << i;
                if ((_claimedLayers & bit) != 0) continue;
                if (!string.IsNullOrEmpty(LayerMask.LayerToName(i))) continue;
                if ((rendered & bit) == 0) free = i;          // unnamed AND rendered by nobody — the one we want
                else if (unnamed < 0) unnamed = i;
            }

            int pick = free >= 0 ? free : (unnamed >= 0 ? unnamed : 5);
            _claimedLayers |= 1 << pick;
            return pick;
        }

        internal static void ReleaseIsolatedLayer(int layer)
        {
            if (layer >= 0 && layer < 32) _claimedLayers &= ~(1 << layer);
        }

        internal static void SetLayerRecursively(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            var t = go.transform;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursively(t.GetChild(i).gameObject, layer);
        }

        // ── shared with the editor preview ────────────────────────────────────────────

        /// <summary>Apply a spec's static LOOK — text, font, point size, face/border fill, opacity, the border pass's
        /// SDF dilation and the bevel — to any <see cref="TMP_Text"/> (the runtime's UGUI text OR the preview's 3D
        /// text). `text` null = the spec's own default. <paramref name="isBorderPass"/> renders this TMP as a
        /// DILATED pass, so it takes the padded font, paints <see cref="TextSplash.borderFill"/> on its FACE and
        /// carries the outline's `_FaceDilate`. <paramref name="playSeed"/> keys the Min-Max rolls so they stay put
        /// for the whole play. Motion, per-letter variation and spatial fills are <see cref="EvaluateLine"/>'s and
        /// <see cref="ApplyMesh"/>'s job.</summary>
        public static void ApplyLook(TextSplash s, TMP_Text tmp, string text, float lineLife, int playSeed,
            bool isBorderPass)
        {
            if (s == null || tmp == null) return;
            tmp.text = text ?? s.text;

            // The border pass runs on a font asset baked with FAR more atlas padding than a stock one: the dilation
            // can only push the silhouette as far as the padding it has room in, and a stock asset's ~4% of an em is
            // ten times short of this tool's range.
            var source = s.font != null ? s.font : TMP_Settings.defaultFontAsset;
            if (isBorderPass)
            {
                var padded = BorderFont(s, source);
                if (padded != null && tmp.font != padded) tmp.font = padded;
            }
            else if (s.font != null && tmp.font != s.font) tmp.font = s.font;

            // One shared point size lays the whole line out: a per-letter size would re-flow it (the letters would
            // crawl sideways as the curve moved), so ApplyMesh applies that variation as a per-quad scale instead.
            float pt = LayoutSize(s, lineLife, playSeed);
            if (pt > 0f) tmp.fontSize = pt;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            // The border is a whole second text, not TMP's own outline; letting both draw would double it.
            tmp.outlineWidth = 0f;

            // An over-life fill is read at the LINE's life here; a per-letter line re-samples it at each letter's own
            // life in ApplyMesh, and a cycling face re-samples it at the scrolled phase in ApplyCycle.
            ApplyFill(tmp, isBorderPass ? s.borderFill : s.fill, lineLife);

            if (isBorderPass) ApplyBorderDilation(s, tmp, lineLife, playSeed);
            ApplyBevel(s, tmp, isBorderPass);
            // LAST of the three, and it has to be: the two above key their ONE-TIME material setup off having been
            // the call that created the pass's material instance, so anything that can instance it ahead of them
            // silently steals that setup (the border's RATIOS_OFF above all, without which the width is capped).
            ApplySharpness(s, tmp);

            // A per-letter alpha is written straight into the vertex colours by ApplyMesh, so the shared TMP alpha
            // stays wide open and the two can never disagree about which one owns opacity.
            tmp.alpha = s.alpha == null || s.alpha.perLetter
                ? 1f
                : Mathf.Clamp01(s.alpha.Evaluate(lineLife, s.alpha.RollKey(playSeed, 0, SaltAlpha)));
        }

        /// <summary>The line's RESTING orientation. Only <see cref="SplashDepth.tilt"/> lives here, and it exists
        /// because an extrusion seen exactly head-on under an orthographic camera is edge-on and reads as nothing —
        /// a few degrees of tilt is what turns the stack of layers into a visible side.
        ///
        /// It composes OUTSIDE the spin (<c>tilt * spin</c>) so a transition's spin turns within the tilted frame
        /// rather than wrenching the line back to face the camera.</summary>
        public static Quaternion RestTilt(TextSplash s)
            => s != null && s.depth != null && s.depth.enabled
                ? Quaternion.Euler(s.depth.tilt.x, s.depth.tilt.y, 0f)
                : Quaternion.identity;

        /// <summary>The whole line's pose at time <paramref name="t"/> for a frame of size (w,h): the offset from
        /// its rest anchor, its opacity, a uniform scale, a spin, and whether the play is over. The entrance and the
        /// exit are composed every frame — the entrance eases its offset back to zero while the exit eases a second
        /// offset away from zero — so the two ends meet cleanly at rest instead of fighting over one blend.
        ///
        /// <paramref name="textHalf"/> is the text's own half width/height in the same units as (w,h): a slide
        /// measures "fully outside the viewport" against the text's real size, and this has no TMP to ask (see
        /// <see cref="TextHalfExtents"/>).
        ///
        /// With per-letter animation ON the letters carry all of this themselves (see <see cref="ApplyMesh"/>), so
        /// everything but <see cref="SplashPose.done"/> comes back neutral.</summary>
        public static SplashPose EvaluateLine(TextSplash s, in SplashSchedule sch, float t, float w, float h,
            Vector2 textHalf, int playSeed)
        {
            var pose = new SplashPose { alpha = 1f, scale = 1f, spinAxis = SplashAxis.Y };
            if (s == null) { pose.done = true; return pose; }

            pose.done = t >= sch.Total;
            if (s.PerLetter) return pose;

            var inT = s.inTransition; var outT = s.outTransition;
            // Letter 0 IS the whole line here: with per-letter off the schedule collapses to one letter and no
            // stagger at either end, so neither cascade's order can change what this reads.
            float ei = inT.Ease(sch.InProgress(0, t));
            float eo = outT.Ease(sch.OutProgress(0, t));

            // Unclamped lerps on purpose: Back / Elastic / a hand-drawn Custom curve deliberately push past 1, and
            // that overshoot IS the effect. Only the alpha is pulled back into range.
            Vector2 offIn = SplashGeometry.SlideExtreme(inT, w, h, s.anchor, textHalf);
            Vector2 offOut = SplashGeometry.SlideExtreme(outT, w, h, s.anchor, textHalf);
            pose.offset = Vector2.LerpUnclamped(offIn, Vector2.zero, ei)
                        + Vector2.LerpUnclamped(Vector2.zero, offOut, eo);

            // Opacity is the alpha scalar and nothing else — one knob owns it end to end.
            pose.alpha = s.alpha != null
                ? Mathf.Clamp01(s.alpha.Evaluate(sch.LineLife(t), s.alpha.RollKey(playSeed, 0, SaltAlpha)))
                : 1f;

            // The size scalar is already in the point size ApplyLook set, so it must NOT be multiplied in again here.
            pose.scale = Mathf.LerpUnclamped(inT.scale, 1f, ei) * Mathf.LerpUnclamped(1f, outT.scale, eo);

            pose.spinDeg = inT.spinDegrees * (1f - ei) + outT.spinDegrees * eo;
            // One axis has to win: the entrance owns the spin until it has fully landed, the exit owns it after.
            pose.spinAxis = ei < 1f ? inT.spinAxis : outT.spinAxis;
            return pose;
        }

        /// <summary>Rewrite <paramref name="tmp"/>'s MESH for this frame. Three jobs live here because all three are
        /// per-character vertex work:
        ///   (a) per-letter animation — each letter's own staggered slide, scale, spin and scalars, whenever
        ///       <see cref="TextSplash.PerLetter"/> is on (which any per-letter scalar switches on by itself);
        ///   (b) the COLOURS a shared <c>tmp.color</c> cannot carry — a SPATIAL fill (Linear / Radial), sampled per
        ///       vertex because its colour depends on WHERE that vertex sits, a per-letter OVER-LIFE fill, where
        ///       every letter is at a different point of the same ramp, and a depth layer's shading toward the
        ///       SIDES' own fill;
        ///   (c) a depth layer's step away from the viewer, applied AFTER each letter's own spin so the extrusion
        ///       turns with the letter instead of sliding against it.
        /// (w,h) are the viewport size in the TMP's LOCAL vertex units (canvas px for a UGUI text, world ÷ scale for
        /// a 3D one). <paramref name="layerDepth"/> is that step, in the same local units, and is 0 for the face and
        /// the border twin; <paramref name="layerTint"/> is this layer's share of the depth falloff, 0..1 of the way
        /// toward <see cref="SplashDepth.sideFill"/>. Call it every frame, after <see cref="ApplyLook"/>.</summary>
        public static void ApplyMesh(TextSplash s, TMP_Text tmp, in SplashSchedule sch, float t, float w, float h,
            int playSeed, bool isBorderPass, float layerDepth = 0f, float layerTint = 0f)
        {
            if (s == null || tmp == null) return;

            var fill = isBorderPass ? s.borderFill : s.fill;
            bool spatial = IsSpatial(fill);
            bool overLife = IsOverLife(fill);
            bool perLetter = s.PerLetter;

            // How far this depth layer is carried toward the SIDES. The sides are a FILL of their own — solid or a
            // gradient, spatial or over-life, read exactly the way the face's and the border's are — with their own
            // brightness and saturation on top, because a side is a different SURFACE from the face and almost never
            // wants to be a darker copy of it.
            var d = s.depth;
            var side = d != null ? d.sideFill : null;
            float fall = side != null ? Mathf.Clamp01(layerTint) : 0f;
            bool shading = fall > 0.001f;
            bool sideSpatial = shading && IsSpatial(side);
            bool sideFixed = sideSpatial && side.space == ZuiFill.FillSpace.Fixed;
            // An over-life side ramp is one flat colour at any instant, so it only needs a per-vertex sample when
            // each LETTER reads it at its own life.
            bool sidePerLetter = shading && perLetter && IsOverLife(side);

            // A whole-line cycle repaints the shared TMP colour, so it has to land before the mesh is rebuilt. The
            // border twin never cycles — it carries its own fill.
            if (!isBorderPass && s.cycleFill && !perLetter) ApplyCycle(s, tmp, sch, t);

            tmp.ForceMeshUpdate();

            bool moveVerts = perLetter || layerDepth != 0f;
            bool paintCols = spatial || perLetter || shading;
            // Nothing per-character to do: the line pose covers the motion and TMP's own colour covers the fill.
            if (!moveVerts && !paintCols) return;

            var info = tmp.textInfo;
            if (info == null || info.meshInfo == null) return;
            int n = info.characterCount;
            float lineLife = sch.LineLife(t);
            // The point size ApplyLook laid the line out at — the denominator that turns a per-letter size into a
            // quad ratio.
            float baseSize = Mathf.Max(0.0001f, LayoutSize(s, lineLife, playSeed));

            // The line's resting box: the slide extremes measure "fully off screen" against it, and a Fixed-space
            // fill spans it. Read here, straight after ForceMeshUpdate, so it is the LAYOUT — not the letters as
            // they are currently flying.
            LineBox(tmp, out Vector2 lineCentre, out Vector2 lineHalf);
            if (isBorderPass)
            {
                // A dilated pass carries EXTRA quad padding: TMP grows every glyph quad to make room for
                // `_FaceDilate`, so this pass's measured box is wider than the face's by exactly that much. Take it
                // back out, or the twin would start its slide further off-stage than the face and trail it the whole
                // way in — and a Fixed-space fill would span a bigger box here than on the face.
                float pad = DilationQuadPadTexels(tmp) * FirstVisibleScale(info, n);
                lineHalf.x = Mathf.Max(0f, lineHalf.x - pad);
                lineHalf.y = Mathf.Max(0f, lineHalf.y - pad);
            }

            var inT = s.inTransition; var outT = s.outTransition;
            Vector2 offIn = SplashGeometry.SlideExtreme(inT, w, h, s.anchor, lineHalf);
            Vector2 offOut = SplashGeometry.SlideExtreme(outT, w, h, s.anchor, lineHalf);
            bool fixedFill = spatial && fill.space == ZuiFill.FillSpace.Fixed;

            // A side fill that is neither spatial nor read per letter is ONE colour for the whole pass, so the
            // brightness/saturation pass over it runs once here instead of on every vertex.
            Color32 sideFlat = default;
            if (shading && !sideSpatial && !sidePerLetter) sideFlat = SideShade(d, FlatColor(side, lineLife));

            for (int i = 0; i < n; i++)
            {
                var ci = info.characterInfo[i];
                if (!ci.isVisible) continue;
                int mi = ci.materialReferenceIndex;
                int vi = ci.vertexIndex;
                var verts = info.meshInfo[mi].vertices;
                var cols = info.meshInfo[mi].colors32;

                // The RESTING quad, captured before this frame's motion is written back over it: the fill's UVs
                // measure the glyph as it was laid out, not as it is flying.
                Vector3 p0 = verts[vi], p1 = verts[vi + 1], p2 = verts[vi + 2], p3 = verts[vi + 3];
                Vector3 c = (p0 + p2) * 0.5f;                          // char centre = (bottom-left + top-right)/2

                // Every scalar picks its OWN clock: `LifeFor` hands a per-letter scalar this letter's lifetime and a
                // line-scoped one the whole line's, so a per-letter Size curve can ripple in the very same frame a
                // line-scoped Alpha curve fades the lot together.
                float letterLife = sch.LetterLife(i, t);
                Vector2 off = Vector2.zero;
                float scale = 1f, spin = 0f;
                Vector3 axis = Vector3.up;

                if (perLetter)
                {
                    // Every window comes from the schedule, which resolves this letter's TURN in each cascade for
                    // itself — its entrance rank under the entry order, its exit rank under the exit order, which
                    // need not agree (a letter can arrive first and still leave last). The index is never a delay.
                    float ei = inT.Ease(sch.InProgress(i, t));
                    float eo = outT.Ease(sch.OutProgress(i, t));
                    off = Vector2.LerpUnclamped(offIn, Vector2.zero, ei)
                        + Vector2.LerpUnclamped(Vector2.zero, offOut, eo);
                    scale = Mathf.LerpUnclamped(inT.scale, 1f, ei) * Mathf.LerpUnclamped(1f, outT.scale, eo);
                    spin = inT.spinDegrees * (1f - ei) + outT.spinDegrees * eo;
                    axis = AxisVec(ei < 1f ? inT.spinAxis : outT.spinAxis);
                }

                float a = s.alpha != null
                    ? Mathf.Clamp01(s.alpha.Evaluate(s.alpha.LifeFor(lineLife, letterLife),
                                                     s.alpha.RollKey(playSeed, i, SaltAlpha)))
                    : 1f;
                // A per-letter point size would re-flow the line, so it becomes a scale about the char centre. A
                // line-scoped size reads the same life and the same roll as the layout did, so this ratio is 1.
                if (s.size != null)
                    scale *= s.size.Evaluate(s.size.LifeFor(lineLife, letterLife),
                                             s.size.RollKey(playSeed, i, SaltSize)) / baseSize;

                if (moveVerts)
                {
                    Quaternion rot = Quaternion.AngleAxis(spin, axis);
                    Vector3 delta = new Vector3(off.x, off.y, 0f);
                    // The depth step rides INSIDE the rotation, so it follows the letter's own facing after the
                    // spin; the slide rides outside it, because a slide is screen motion and must not be spun.
                    Vector3 back = new Vector3(0f, 0f, layerDepth);
                    verts[vi]     = c + rot * ((p0 - c) * scale + back) + delta;
                    verts[vi + 1] = c + rot * ((p1 - c) * scale + back) + delta;
                    verts[vi + 2] = c + rot * ((p2 - c) * scale + back) + delta;
                    verts[vi + 3] = c + rot * ((p3 - c) * scale + back) + delta;
                }

                // ── colours ──
                // The pass paints itself first — its own fill, its own per-letter alpha — and the depth shading
                // then runs OVER the result, so a layer is the same picture as the face it mirrors, carried toward
                // the sides. Painting the sides instead of the fill would throw away the gradient the extrusion is
                // supposed to be an extrusion OF.
                if (spatial)
                {
                    // Stamped vs Fixed is the whole point of the space switch: Stamped normalizes each vertex
                    // against the LETTER'S OWN quad, so every letter wears one complete gradient (a Radial fill puts
                    // a little disc on each of them); Fixed normalizes against the WHOLE LINE's box, so one gradient
                    // spans all the text (a Radial fill puts one big disc behind the line and each letter shows the
                    // slice of it that it happens to cover).
                    //
                    // Only 4 samples exist per glyph — its corners — so the GPU interpolates the gradient bilinearly
                    // across each quad. On display text (big glyphs, smooth ramps) that reads as a real gradient; it
                    // is NOT a per-pixel one, and a ramp with a hard colour stop inside a single glyph will smear.
                    Vector2 origin = fixedFill ? lineCentre : (Vector2)c;
                    Vector2 half = fixedFill ? lineHalf : QuadHalf(p0, p2);
                    cols[vi]     = SampleFill(fill, lineLife, p0, origin, half, a);
                    cols[vi + 1] = SampleFill(fill, lineLife, p1, origin, half, a);
                    cols[vi + 2] = SampleFill(fill, lineLife, p2, origin, half, a);
                    cols[vi + 3] = SampleFill(fill, lineLife, p3, origin, half, a);
                }
                else if (perLetter)
                {
                    byte ab = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                    if (overLife)
                    {
                        // An over-life fill is one flat colour at any instant, so a per-letter line samples it at
                        // each letter's OWN life — which turns a staggered line into a travelling colour sweep for
                        // free, since every letter is at a different point of the same ramp.
                        Color32 c32 = fill.Evaluate(FillPhase(s, letterLife, t, i, isBorderPass), 0f, 0f);
                        c32.a = ab;
                        for (int k = 0; k < 4; k++) cols[vi + k] = c32;
                    }
                    else
                    {
                        for (int k = 0; k < 4; k++)
                        {
                            var cc = cols[vi + k];
                            cc.a = ab;
                            cols[vi + k] = cc;
                        }
                    }
                }
                // (a flat fill on a non-per-letter pass needs nothing here — TMP has already baked tmp.color in)

                if (shading)
                {
                    if (sideSpatial)
                    {
                        // The sides get the SAME spatial treatment the face does, and their own space switch:
                        // Stamped normalizes against this letter's own quad, so every letter wears one complete
                        // side gradient; Fixed normalizes against the whole line's box, so one gradient spans the
                        // extrusion. Both go through the one shared circular divisor, so a Radial side stays a
                        // circle instead of an ellipse stretched to the box.
                        Vector2 so = sideFixed ? lineCentre : (Vector2)c;
                        Vector2 sh = sideFixed ? lineHalf : QuadHalf(p0, p2);
                        cols[vi]     = Toward(cols[vi],     SideShade(d, SampleFill(side, lineLife, p0, so, sh, 1f)), fall);
                        cols[vi + 1] = Toward(cols[vi + 1], SideShade(d, SampleFill(side, lineLife, p1, so, sh, 1f)), fall);
                        cols[vi + 2] = Toward(cols[vi + 2], SideShade(d, SampleFill(side, lineLife, p2, so, sh, 1f)), fall);
                        cols[vi + 3] = Toward(cols[vi + 3], SideShade(d, SampleFill(side, lineLife, p3, so, sh, 1f)), fall);
                    }
                    else
                    {
                        // A per-letter over-life side ramp reads at THIS letter's life, so a staggered line's
                        // extrusion sweeps colour across itself the same way its face does.
                        Color32 sc = sidePerLetter
                            ? SideShade(d, side.Evaluate(Mathf.Clamp01(letterLife), 0f, 0f))
                            : sideFlat;
                        for (int k = 0; k < 4; k++) cols[vi + k] = Toward(cols[vi + k], sc, fall);
                    }
                }
            }

            tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32);
        }

        /// <summary>Create (or reuse) <paramref name="face"/>'s BORDER twin: a duplicate TMP drawn behind the face,
        /// filled with <see cref="TextSplash.borderFill"/> and dilated through the glyph's own distance field. Null
        /// when the spec draws no border (and any existing twin is destroyed). <paramref name="twinGO"/> is the
        /// caller's cached GameObject, so a per-frame call is free after the first.
        ///
        /// It is a real second text rather than TMP's built-in outline because that outline CLAMPS to 0..1
        /// (`TMP_Text.SetOutlineThickness`) and its colour is a single material uniform, so it could never carry a
        /// gradient. A duplicate's FACE can — and pushing that face outward with `_FaceDilate` (see
        /// <see cref="ApplyBorderDilation"/>) is a true equidistant offset, so counters keep their inner edge and
        /// thin strokes fatten evenly.</summary>
        public static TMP_Text EnsureBorderTwin(TextSplash s, TMP_Text face, ref GameObject twinGO)
        {
            if (s == null || face == null || !DrawsBorder(s))
            {
                if (twinGO != null) { DestroyObj(twinGO); twinGO = null; }
                return null;
            }
            // One below the splash canvas: under the face, still above everything the splash was raised over. A
            // small +z puts it behind in a 3D preview too, where sorting comes from real distance.
            return EnsureCopy(face, ref twinGO, "Border", -1, 0.01f);
        }

        /// <summary>Create (or reuse) the DEPTH stack: one copy of <paramref name="face"/> per extra layer, all of
        /// them behind the face and behind the border twin, deepest last. Returns how many exist. The lists are the
        /// caller's cache and are kept the same length as each other; extra copies are destroyed when the layer
        /// count drops or depth is switched off.
        ///
        /// There are `layers − 1` copies, not `layers`: the FACE is layer 0 of the extrusion.</summary>
        public static int EnsureDepthLayers(TextSplash s, TMP_Text face, List<GameObject> gos, List<TMP_Text> texts)
        {
            if (gos == null || texts == null) return 0;

            bool on = s != null && s.depth != null && s.depth.enabled && face != null;
            int want = on ? Mathf.Max(0, Mathf.Clamp(s.depth.layers, 2, 32) - 1) : 0;

            while (gos.Count > want)
            {
                int last = gos.Count - 1;
                DestroyObj(gos[last]);
                gos.RemoveAt(last);
                if (texts.Count > last) texts.RemoveAt(last);
            }
            while (gos.Count < want) { gos.Add(null); texts.Add(null); }
            while (texts.Count < gos.Count) texts.Add(null);

            for (int i = 0; i < want; i++)
            {
                var go = gos[i];
                // −2 and down: the border twin holds −1, so the whole stack sits behind it.
                texts[i] = EnsureCopy(face, ref go, "Depth " + (i + 1), -2 - i, 0f);
                gos[i] = go;
            }
            return want;
        }

        /// <summary>One COPY of the face: a second TMP of the same concrete component type, laid out identically and
        /// drawn BEHIND it. The border twin and every depth layer are one of these, so the parenting, rect and
        /// sorting rules live in exactly one place.
        ///
        /// <paramref name="sortingOffset"/> is how far below the splash canvas the copy draws. For UGUI that needs a
        /// nested Canvas with `overrideSorting`, because UGUI draws a child AFTER its parent and sorting is the ONLY
        /// way to push a child behind the graphic it belongs to — which also means one BATCH per copy: a depth stack
        /// of N layers over a bordered face costs N + 1 extra canvas batches. A 3D TMP needs none of it; there the
        /// small local z is enough, since the transparent queue sorts by real distance.</summary>
        static TMP_Text EnsureCopy(TMP_Text face, ref GameObject go, string name, int sortingOffset, float z)
        {
            if (face == null)
            {
                if (go != null) { DestroyObj(go); go = null; }
                return null;
            }

            var copy = go != null ? go.GetComponent<TMP_Text>() : null;
            if (copy == null)
            {
                if (go != null) DestroyObj(go);
                // Same layer as the face, so a camera-space splash culling to its own layer picks the copy up too.
                go = new GameObject(name) { hideFlags = face.gameObject.hideFlags, layer = face.gameObject.layer };
                // A CHILD of the face, so the whole-line pose (position, scale, tilt, spin) carries it along.
                go.transform.SetParent(face.transform, false);
                copy = go.AddComponent(face.GetType()) as TMP_Text;
                if (copy == null) { DestroyObj(go); go = null; return null; }
            }

            copy.alignment = face.alignment;
            copy.enableWordWrapping = false;
            copy.overflowMode = face.overflowMode;

            // Centred on the face's own rect and the same size, so both lay their glyphs out identically.
            var crt = copy.rectTransform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = face.rectTransform.rect.size;
            crt.localScale = Vector3.one;
            crt.localRotation = Quaternion.identity;
            crt.anchoredPosition3D = new Vector3(0f, 0f, z);
            crt.SetSiblingIndex(0);

            if (face is TextMeshProUGUI)
            {
                var sub = go.GetComponent<Canvas>();
                if (sub == null) sub = go.AddComponent<Canvas>();
                var root = face.canvas;
                sub.overrideSorting = true;
                sub.sortingLayerID = root != null ? root.sortingLayerID : 0;
                sub.sortingOrder = (root != null ? root.sortingOrder : 0) + sortingOffset;
            }
            return copy;
        }

        /// <summary>Half the width and height of the text as it is currently laid out, in the TMP's own LOCAL vertex
        /// units — walked from the visible characters' quads, which is the only measure that survives an overflowing
        /// text (the RectTransform is deliberately oversized, so it says nothing about the glyphs).
        ///
        /// This is what makes "fully outside the viewport" mean the same thing for a three-letter word and a long
        /// sentence: a slide has to push the text past the edge by its OWN half-extent, not by a constant. Needs a
        /// laid-out mesh — call it after a `ForceMeshUpdate`.</summary>
        public static Vector2 TextHalfExtents(TMP_Text tmp)
        {
            LineBox(tmp, out _, out Vector2 half);
            return half;
        }

        /// <summary>Whole-line colour cycle: re-sample the face's OVER-LIFE ramp at the scrolled phase (see
        /// <see cref="FillPhase"/>). One flat colour, because that is all an over-life fill ever is at one instant —
        /// <see cref="ApplyLook"/> already wrote the un-scrolled one, and this is the only place that holds the
        /// SECONDS clock the scroll runs on.
        ///
        /// Only an OVER-LIFE ramp has a phase to scroll. Solid has no ramp, a texture has no life axis, and a
        /// spatial fill (Linear / Radial) places its colour by POSITION, not by life — <see cref="ApplyMesh"/>
        /// paints those per vertex and would overwrite anything written here. A per-letter line never comes through
        /// here either: every letter cycles at its own phase, which is per-vertex work (again <see cref="ApplyMesh"/>).
        ///
        /// Face only — the border twin has its own fill. Call it BEFORE the mesh is rebuilt: TMP bakes
        /// <c>tmp.color</c> into the vertex colours as it generates them.</summary>
        public static void ApplyCycle(TextSplash s, TMP_Text tmp, in SplashSchedule sch, float t)
        {
            var fill = s != null ? s.fill : null;
            if (fill == null || tmp == null || !IsOverLife(fill)) return;
            var c = fill.Evaluate(FillPhase(s, sch.LineLife(t), t, 0, false), 0f, 0f);
            // Opacity belongs to the alpha scalar, which the caller has already pushed onto the TMP — assigning a
            // whole Color would otherwise hand it back to the gradient's own alpha key and undo the fade.
            c.a = tmp.color.a;
            tmp.enableVertexGradient = false;
            tmp.color = c;
        }

        // ── border: SDF dilation ──────────────────────────────────────────────────────

        /// <summary>The font asset the BORDER pass draws from: the spec's face font re-baked with enough atlas
        /// padding to hold the widest border the width dial can reach. A stock TMP asset carries about 5 texels of
        /// padding, which buys roughly 4% of an em of dilation — an order of magnitude short of this tool's range,
        /// and the reason the border used to have to be faked by scaling quads.</summary>
        static TMP_FontAsset BorderFont(TextSplash s, TMP_FontAsset source)
        {
            if (source == null) return null;
            // The padding is expressed against the atlas's own sampling size, because that is what a texel means.
            int padding = s.ResolveBorderPadding(source.faceInfo.pointSize);

            // A COMMITTED twin wins outright when it is padded enough. It is the only version that works in a player
            // build: rasterising needs a UnityEngine.Font, and TMP nulls that on a Static font asset, so a runtime
            // bake can only find the font through the AssetDatabase — i.e. in the editor.
            var pre = s.bakedBorderFont;
            if (pre != null && pre.atlasPadding >= padding) return pre;

            var baked = SplashFontBaker.GetPadded(source, padding, s.borderAtlasSize);
            // GetPadded hands the FACE straight back when it could not bake — exactly the build case above. An
            // under-padded committed twin is still far better than the face's ~4%-of-em hairline ceiling.
            if (pre != null && (baked == null || baked == source) && pre.atlasPadding > source.atlasPadding)
                return pre;
            return baked;
        }

        /// <summary>Push the border pass's silhouette OUTWARD by the authored width, by moving the SDF's own
        /// threshold rather than the geometry.
        ///
        /// The math, straight out of `TMP_SDF.shader`: the vertex stage builds
        /// <c>weight = (lerp(_WeightNormal,_WeightBold,bold)/4 + _FaceDilate)·_ScaleRatioA·0.5</c> and
        /// <c>bias = (0.5 − weight) + 0.5/scale</c>, and the fragment covers everything with
        /// <c>sd = (bias − c)·scale ≤ 0</c> — so the 50%-coverage contour is exactly <c>c = 0.5 − weight</c>, with
        /// the ±0.5/scale term being nothing but the one-pixel antialias band. One unit of the atlas value <c>c</c>
        /// spans <c>_GradientScale</c> texels (that is what `_GradientScale`, set to atlasPadding + 1, means), so
        /// lowering the threshold by <c>weight</c> moves the silhouette OUT by <c>weight · _GradientScale</c> texels
        /// — i.e. <c>0.5 · _FaceDilate · _GradientScale</c> texels once the ratio is pinned at 1. Dividing by the
        /// atlas's sampling point size turns texels into ems:
        /// <c>_FaceDilate = 2 · width · samplingPointSize / _GradientScale</c>, minus the font's own normal weight.
        ///
        /// Because it offsets the DISTANCE FIELD, it is a true equidistant offset: counters close correctly, thin
        /// strokes fatten by the same amount as thick ones, and nothing depends on a glyph's aspect or on how far a
        /// stroke sits from the glyph's centroid — all three of which a uniform quad scale gets wrong.
        ///
        /// Two material settings have to be forced or TMP takes the width back:
        ///   `RATIOS_OFF` + `_ScaleRatioA = 1` — `ShaderUtilities.UpdateShaderRatios` divides the ratio down as soon
        ///     as dilate + outline + weight passes 1, which would cap the border well below the padding it was baked
        ///     for; and TMP recomputes that ratio behind our back whenever it re-measures padding.
        ///   `_WeightBold = 0` — that same recompute takes `Max(_WeightNormal, _WeightBold)/4` into its divisor, and
        ///     a stock asset ships _WeightBold at 0.5, so ~12% of the budget would be gone before the border starts.
        ///
        /// Its one honest limit: `_FaceDilate` is a per-MATERIAL float, so it cannot differ from letter to letter —
        /// see <see cref="WarnPerLetterBorder"/>.</summary>
        static void ApplyBorderDilation(TextSplash s, TMP_Text tmp, float lineLife, int playSeed)
        {
            var mat = PassMaterial(tmp, out bool fresh);
            if (mat == null || !mat.HasProperty(ShaderUtilities.ID_FaceDilate)
                            || !mat.HasProperty(ShaderUtilities.ID_GradientScale)) return;

            if (fresh)
            {
                // RATIOS_OFF is a MATERIAL-only keyword — no TMP shader declares it; it exists purely so
                // `UpdateShaderRatios` can be told to stand down (TMP's own shader GUI toggles it the same way). It
                // is set once, when the instance is made, rather than re-checked per frame: an undeclared keyword's
                // enabled state is not reliably readable back, so a per-frame guard could re-enable it for ever.
                mat.EnableKeyword(ShaderUtilities.Keyword_Ratios);
                mat.SetFloat(ShaderUtilities.ID_WeightBold, 0f);
                // TMP's own outline would draw a second, thinner ring on top of the dilated face and eat into the
                // same padding budget.
                mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0f);
                mat.SetFloat(ShaderUtilities.ID_OutlineSoftness, 0f);
            }
            // TMP writes this back whenever it re-measures padding, so it is re-pinned every frame. With the keyword
            // set that recompute already yields 1; this is what makes the border survive if the keyword ever doesn't
            // take.
            if (mat.GetFloat(ShaderUtilities.ID_ScaleRatio_A) != 1f)
                mat.SetFloat(ShaderUtilities.ID_ScaleRatio_A, 1f);

            if (s.borderWidth != null && s.borderWidth.perLetter) WarnPerLetterBorder(s);

            float gs = mat.GetFloat(ShaderUtilities.ID_GradientScale);
            float pt = tmp.font != null ? tmp.font.faceInfo.pointSize : 0f;
            if (gs <= 0f || pt <= 0f) return;

            float bw = s.borderWidth != null
                ? Mathf.Max(0f, s.borderWidth.Evaluate(lineLife, s.borderWidth.RollKey(playSeed, 0, SaltBorder)))
                : 0f;

            // How far the BAKED padding can actually push the silhouette. Past that the shader clamps and the border
            // simply stops widening, which looks like a broken dial unless it is said out loud.
            float ceiling = SplashFontBaker.MaxOutwardEm(tmp.font);
            if (ceiling > 0f && bw > ceiling) { WarnBorderClamped(s, bw, ceiling); bw = ceiling; }

            float weightNormal = mat.GetFloat(ShaderUtilities.ID_WeightNormal);
            float dilate = Mathf.Clamp01(2f * bw * pt / gs - weightNormal * 0.25f);

            if (Mathf.Abs(mat.GetFloat(ShaderUtilities.ID_FaceDilate) - dilate) > 1e-4f)
            {
                mat.SetFloat(ShaderUtilities.ID_FaceDilate, dilate);
                // TMP sizes every glyph QUAD from the material's dilation, so without this the dilated silhouette is
                // clipped by the quad it lives in. It re-reads the material (which allocates a keyword array), so it
                // runs only on a real change: a static border width pays it once, an animated one per frame.
                tmp.UpdateMeshPadding();
            }
        }

        /// <summary>The extra atlas texels TMP grows every glyph quad by to make room for this pass's dilation —
        /// `ShaderUtilities.GetPadding` clamps `_FaceDilate · _ScaleRatioA` to 1 and multiplies by `_GradientScale`,
        /// so with the ratio pinned at 1 that is the whole story. Read back off the material instead of recomputed,
        /// so it can never disagree with what was actually written.</summary>
        static float DilationQuadPadTexels(TMP_Text tmp)
        {
            var mat = tmp != null ? tmp.fontSharedMaterial : null;
            if (mat == null || !mat.HasProperty(ShaderUtilities.ID_FaceDilate)
                            || !mat.HasProperty(ShaderUtilities.ID_GradientScale)) return 0f;
            return Mathf.Clamp01(mat.GetFloat(ShaderUtilities.ID_FaceDilate))
                 * mat.GetFloat(ShaderUtilities.ID_GradientScale);
        }

        /// Local units per atlas texel for the laid-out line — TMP records it per character as `scale`, which is the
        /// very factor it multiplied the material padding by when it built the quads.
        static float FirstVisibleScale(TMP_TextInfo info, int n)
        {
            for (int i = 0; i < n; i++)
                if (info.characterInfo[i].isVisible) return info.characterInfo[i].scale;
            return 0f;
        }

        // Warned-about assets, so a per-frame preview repaint cannot turn one honest limitation into a console flood.
        static readonly HashSet<int> _warnedPerLetter = new HashSet<int>();
        static readonly HashSet<int> _warnedClamped = new HashSet<int>();

        /// <summary>Say ONCE, per asset, that a per-letter border width is not being honoured. The dilation lives in
        /// `_FaceDilate`, one float for the whole draw, and there is no per-vertex channel to carry a different width
        /// for each letter — so the width is read LINE-WIDE.
        ///
        /// The obvious fallback (grow each glyph QUAD instead, per letter) is deliberately NOT offered: that is the
        /// technique this pass replaced, and it is wrong in three separate ways — a uniform scale leaves counters
        /// with no inner edge at all, makes thickness proportional to distance from the glyph's centroid, and scales
        /// with the quad's aspect. Silently swapping back to it for one dial would make a per-letter line look like a
        /// different, broken tool.</summary>
        static void WarnPerLetterBorder(TextSplash s)
        {
            if (s == null || !_warnedPerLetter.Add(s.GetInstanceID())) return;
            Debug.LogWarning($"[TextSplash] \"{s.name}\": Border Width is scoped PER LETTER, but the border is drawn " +
                "by dilating the glyph's distance field and that dilation is a single material float — it cannot " +
                "differ from letter to letter. The width is being driven LINE-WIDE: every letter wears the same " +
                "border. Turn the per-letter scope off on Border Width to make that explicit.", s);
        }

        static void WarnBorderClamped(TextSplash s, float asked, float ceiling)
        {
            if (s == null || !_warnedClamped.Add(s.GetInstanceID())) return;
            Debug.LogWarning($"[TextSplash] \"{s.name}\": a border of {asked:0.###} em is wider than the baked font " +
                $"atlas can carry ({ceiling:0.###} em), so it is capped there. Raise Border Atlas Padding (or leave " +
                "it at 0 to have it derived from the width dial's own ceiling); a bigger padding costs atlas area, " +
                "so raise Border Atlas Size with it.", s);
        }

        // ── bevel ─────────────────────────────────────────────────────────────────────

        static readonly int ID_BevelOffset = Shader.PropertyToID("_BevelOffset");
        static readonly int ID_BevelWidth = Shader.PropertyToID("_BevelWidth");
        static readonly int ID_BevelRoundness = Shader.PropertyToID("_BevelRoundness");
        static readonly int ID_BevelClamp = Shader.PropertyToID("_BevelClamp");
        static readonly int ID_SpecularColor = Shader.PropertyToID("_SpecularColor");
        static readonly int ID_SpecularPower = Shader.PropertyToID("_SpecularPower");
        static readonly int ID_Diffuse = Shader.PropertyToID("_Diffuse");
        static readonly int ID_Ambient = Shader.PropertyToID("_Ambient");

        /// <summary>Turn TMP's per-pixel SDF bevel on (or off) for this pass.
        ///
        /// The shipped font material points at `TMP_SDF-Mobile`, and EVERY Mobile variant strips shading outright —
        /// no `_Bevel`, no `BEVEL_ON`, no specular — so this has to swap the pass's own material instance onto
        /// `TextMeshPro/Distance Field` and enable the keyword before any of the knobs mean anything.
        ///
        /// FACE ONLY, on purpose. Lighting the border twin as well would put a second, independently-lit ridge
        /// around the first: the two silhouettes are offset by the border width, so their highlights never line up
        /// and the pair reads as a double emboss rather than one solid letter. The twin also already carries an
        /// extreme `_FaceDilate`, and the bevel measures its ramp from `_OutlineWidth + _BevelWidth` against the
        /// dilated contour — matching the face's relief on it would need a second calibration with no dial to author
        /// it from.
        ///
        /// The light angle is fixed in GLYPH space, so an X/Y spin foreshortens the letter while its shading stays
        /// put — the price of an effect that lives inside the distance field instead of in screen space, and the
        /// same reason it survives a per-letter spin at all.</summary>
        static void ApplyBevel(TextSplash s, TMP_Text tmp, bool isBorderPass)
        {
            var b = s.bevel;
            bool want = b != null && b.enabled && !isBorderPass;

            // `fontMaterial` re-derives TMP's padding and dirties the vertices on every call, so the common path
            // must not reach for an instance at all — only inspect the shared material and leave. And it un-sets the
            // keyword ONLY on an instance this pass owns: a project's own font material may legitimately carry a
            // bevel for the rest of its text, and a splash has no business switching that off for everyone.
            if (!want)
            {
                var shared = tmp.fontSharedMaterial;
                var stock = tmp.font != null ? tmp.font.material : null;
                if (shared != null && stock != null && shared != stock
                    && shared.IsKeywordEnabled(ShaderUtilities.Keyword_Bevel))
                    shared.DisableKeyword(ShaderUtilities.Keyword_Bevel);
                return;
            }

            var shader = ResolveBevelShader();
            if (shader == null) return;
            var mat = PassMaterial(tmp, out _);
            if (mat == null) return;

            if (mat.shader != shader) mat.shader = shader;
            if (!mat.IsKeywordEnabled(ShaderUtilities.Keyword_Bevel))
                mat.EnableKeyword(ShaderUtilities.Keyword_Bevel);

            mat.SetFloat(ShaderUtilities.ID_BevelAmount, b.amount);
            mat.SetFloat(ID_BevelOffset, b.offset);
            mat.SetFloat(ID_BevelWidth, b.width);
            mat.SetFloat(ID_BevelRoundness, b.roundness);
            mat.SetFloat(ID_BevelClamp, b.clamp);
            // The shader takes RADIANS (its own range is 0..2π); the dial is authored in degrees.
            mat.SetFloat(ShaderUtilities.ID_LightAngle, b.lightAngle * Mathf.Deg2Rad);
            mat.SetColor(ID_SpecularColor, b.specularColor);
            mat.SetFloat(ID_SpecularPower, b.specularPower);
            mat.SetFloat(ID_Diffuse, b.diffuse);
            mat.SetFloat(ID_Ambient, b.ambient);
        }

        // ── SDF edge sharpness ────────────────────────────────────────────────────────

        /// <summary>Tighten the SDF's own antialias band while the splash is being PIXELATED.
        ///
        /// TMP's fragment stage covers `sd = (bias − c)·scale ≤ 0`, and the vertex stage builds that scale as
        /// `… · _GradientScale · (_Sharpness + 1)` — so the band the edge fades across is inversely proportional to
        /// `_Sharpness + 1`, and pushing the dial to its maximum of 1 halves it. At native resolution that band is a
        /// fraction of a screen pixel and nobody notices; through a low-res buffer it is a fraction of a CHUNKY
        /// pixel, which is a whole visible pixel of mush around every letter — the softness
        /// <see cref="SplashPixelation.alphaCutoff"/> exists to clean up. Halving it before it is ever rasterized
        /// means the CPU threshold has less to decide about, and the pixels it does decide are less ambiguous.
        ///
        /// Deliberately NOT exposed as a dial: it is not a look, it is a correction that is right whenever the
        /// buffer is low-res and wrong otherwise (a sharpened edge at native resolution just aliases).</summary>
        static void ApplySharpness(TextSplash s, TMP_Text tmp)
        {
            bool want = s.pixelation != null && s.pixelation.enabled;
            var shared = tmp.fontSharedMaterial;
            if (shared == null || !shared.HasProperty(ShaderUtilities.ID_Sharpness)) return;

            if (!want)
            {
                // Reset only on an instance this pass owns — writing to the font asset's own material would
                // sharpen every other text in the project that uses that font.
                var stock = tmp.font != null ? tmp.font.material : null;
                if (stock == null || shared == stock) return;
                if (!Mathf.Approximately(shared.GetFloat(ShaderUtilities.ID_Sharpness), 0f))
                    shared.SetFloat(ShaderUtilities.ID_Sharpness, 0f);
                return;
            }

            var mat = PassMaterial(tmp, out _);
            if (mat != null && !Mathf.Approximately(mat.GetFloat(ShaderUtilities.ID_Sharpness), 1f))
                mat.SetFloat(ShaderUtilities.ID_Sharpness, 1f);
        }

        static Shader _bevelShader;
        static bool _bevelResolved;

        /// <summary>Find the one TMP shader that can actually shade, once per domain.
        ///
        /// This is the honest weak point of the whole feature IN A BUILD. `TextMeshPro/Distance Field` sits in no
        /// Resources folder and — unless a project happens to use it — is referenced by no material, so a player
        /// build strips it and `Shader.Find` comes back null. Worse, `BEVEL_ON` is a `shader_feature`, which means
        /// even a surviving shader ships WITHOUT that variant unless some included MATERIAL has the keyword enabled:
        /// adding the shader to Always Included Shaders is not enough on its own.</summary>
        static Shader ResolveBevelShader()
        {
            if (_bevelResolved) return _bevelShader;
            _bevelResolved = true;
            // Resolved through the PRESET MATERIAL that ships in the package's own Resources folder, not a bare
            // Shader.Find: an included material with BEVEL_ON enabled is the only thing that keeps that
            // shader_feature variant alive in a build. SplashBevelMaterial warns precisely on each failure mode
            // (shader unreachable vs. preset missing but Shader.Find working — the second being "fine in the
            // editor, flat in a build").
            _bevelShader = SplashBevelMaterial.GetShader();
            return _bevelShader;
        }

        // ── helpers ───────────────────────────────────────────────────────────────────

        /// <summary>The material this pass is allowed to WRITE to: an instance, never the font asset's own shared
        /// material, which every other text using that font would inherit the change through.
        ///
        /// It goes through `tmp.fontMaterial` only when no instance exists yet. That getter re-derives TMP's mesh
        /// padding, marks the vertices dirty and allocates on EVERY call, so touching it per frame would rebuild the
        /// mesh twice a frame for nothing. A TMP whose shared material is no longer its font asset's own material is
        /// already carrying an instance.</summary>
        static Material PassMaterial(TMP_Text tmp, out bool created)
        {
            created = false;
            if (tmp == null) return null;
            var shared = tmp.fontSharedMaterial;
            if (shared == null) return null;
            var stock = tmp.font != null ? tmp.font.material : null;
            if (stock != null && shared != stock) return shared;
            created = true;
            return tmp.fontMaterial;
        }

        /// <summary>The point size the LINE is laid out at — and the denominator a per-letter size variation is
        /// expressed against. A line-scoped size just reads its own value at this life (so the ratio in
        /// <see cref="ApplyMesh"/> comes out 1 and the point size does all the work).
        ///
        /// A per-letter size instead freezes the layout at the LARGEST size the scalar can reach. It has to freeze,
        /// or the whole line would re-flow every frame as the curve moved; and it has to be the largest, so each
        /// letter is only ever a shrink inside the slot the layout already reserved for it — a smaller base would
        /// let letters swell into their neighbours, and a base that curved down to zero would divide by nothing.</summary>
        static float LayoutSize(TextSplash s, float lineLife, int playSeed)
        {
            var sz = s.size;
            var v = sz != null ? sz.value : null;
            if (v == null) return 0f;
            if (!sz.perLetter) return sz.Evaluate(lineLife, sz.RollKey(playSeed, 0, SaltSize));

            if (v.mode == ZUIValue.Mode.MinMax) return Mathf.Max(v.min, v.max) * v.Multiplier();
            if (v.mode != ZUIValue.Mode.Curve) return sz.Evaluate(0f, 0);
            // Curve: sample the whole 0..1 life for its peak. 13 samples of a hand-drawn envelope is free, and it
            // beats trusting the authored yMax, which is only the editor's plot ceiling and is usually far above
            // where the curve actually goes.
            float peak = 0f;
            for (int k = 0; k <= 12; k++) peak = Mathf.Max(peak, sz.Evaluate(k / 12f, 0));
            return peak;
        }

        /// <summary>One vertex's colour from a SPATIAL fill. The point is normalized into the −1..1 (u,v) ZuiFill
        /// expects using ONE divisor for BOTH axes — the box's larger half-extent — so a Radial fill stays a true
        /// CIRCLE. Dividing x and y by their own half-extents would squash it into an ellipse stretched to fit the
        /// box, which is exactly what "radial" must not look like.</summary>
        static Color32 SampleFill(ZuiFill fill, float life, Vector3 p, Vector2 origin, Vector2 half, float alpha)
        {
            float d = Mathf.Max(0.0001f, Mathf.Max(half.x, half.y));
            Color c = fill.Evaluate(life, (p.x - origin.x) / d, (p.y - origin.y) / d);
            c.a *= alpha;   // the fill's own alpha survives, scaled by the splash's opacity
            return c;
        }

        /// <summary>Carry one vertex's colour toward the sides' colour by <paramref name="k"/> — the depth falloff.
        /// RGB only: the alpha carries both the letter's opacity and the SDF's antialiased coverage, neither of
        /// which a depth layer is allowed to touch (and neither of which the side FILL's own alpha may overwrite,
        /// which is why the side colour's alpha is dropped here rather than blended).</summary>
        static Color32 Toward(Color32 c, Color32 side, float k)
        {
            if (k <= 0.001f) return c;
            return new Color32(
                ToByte(Mathf.Lerp(c.r, side.r, k)),
                ToByte(Mathf.Lerp(c.g, side.g, k)),
                ToByte(Mathf.Lerp(c.b, side.b, k)),
                c.a);
        }

        /// <summary>The sides' own BRIGHTNESS and SATURATION, applied on top of whatever
        /// <see cref="SplashDepth.sideFill"/> sampled. Two dials rather than a second colour picker because that is
        /// what an extrusion's sides actually want: the same hue as the fill, dimmer and greyer for a side falling
        /// into shadow, or hotter and more saturated for one catching the light.
        ///
        /// Saturation is a LUMINANCE-weighted lerp, not an HSV round trip. HSV's "value" is max(r,g,b), so
        /// desaturating through it collapses a deep blue and a bright yellow of the same V to the SAME grey — the
        /// sides would stop tracking how bright the colour actually looked, which is the entire job here. Above 1 it
        /// keeps going (the lerp is unclamped in effect, since it pushes past the endpoints) and the byte conversion
        /// is what finally clamps. Alpha is untouched.
        ///
        /// It works in the same sRGB byte space every other colour in this file does, so it composes exactly with
        /// the fills either side of it.</summary>
        static Color32 SideShade(SplashDepth d, Color32 c)
        {
            float sat = d != null ? Mathf.Max(0f, d.sideSaturation) : 1f;
            float bri = d != null ? Mathf.Max(0f, d.sideBrightness) : 1f;
            float r = c.r, g = c.g, b = c.b;
            if (!Mathf.Approximately(sat, 1f))
            {
                float luma = 0.299f * r + 0.587f * g + 0.114f * b;
                r = luma + (r - luma) * sat;
                g = luma + (g - luma) * sat;
                b = luma + (b - luma) * sat;
            }
            return new Color32(ToByte(r * bri), ToByte(g * bri), ToByte(b * bri), c.a);
        }

        /// The ONE colour a non-spatial fill has at this life — an over-life ramp sampled there, anything else its
        /// own colour (a texture has no glyph-space equivalent, so it falls back to its tint, exactly as
        /// <see cref="ApplyFill"/> does).
        static Color FlatColor(ZuiFill f, float life)
            => f == null ? Color.white : (IsOverLife(f) ? f.Evaluate(Mathf.Clamp01(life), 0f, 0f) : f.color);

        static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v), 0, 255);

        /// The half width/height of one glyph quad, from its bottom-left and top-right corners.
        static Vector2 QuadHalf(Vector3 bl, Vector3 tr)
            => new Vector2(Mathf.Abs(tr.x - bl.x) * 0.5f, Mathf.Abs(tr.y - bl.y) * 0.5f);

        /// <summary>The bounding box of every VISIBLE glyph in the laid-out mesh: its centre and its half extents,
        /// in local vertex units. Empty (both zero) when nothing is visible yet.</summary>
        static void LineBox(TMP_Text tmp, out Vector2 centre, out Vector2 half)
        {
            centre = Vector2.zero; half = Vector2.zero;
            var info = tmp != null ? tmp.textInfo : null;
            if (info == null || info.meshInfo == null || info.characterInfo == null) return;

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            int n = info.characterCount;
            for (int i = 0; i < n; i++)
            {
                var ci = info.characterInfo[i];
                if (!ci.isVisible) continue;
                int mi = ci.materialReferenceIndex;
                if (mi < 0 || mi >= info.meshInfo.Length) continue;
                var verts = info.meshInfo[mi].vertices;
                int vi = ci.vertexIndex;
                if (verts == null || vi + 3 >= verts.Length) continue;
                for (int k = 0; k < 4; k++)
                {
                    var p = verts[vi + k];
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.y < minY) minY = p.y;
                    if (p.y > maxY) maxY = p.y;
                }
            }
            if (minX > maxX) return;   // no visible glyph
            centre = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            half = new Vector2((maxX - minX) * 0.5f, (maxY - minY) * 0.5f);
        }

        /// Linear and Radial are SPATIAL: their colour depends on WHERE a vertex sits, which TMP's single
        /// top→bottom vertex gradient cannot express — they have to be sampled per vertex.
        static bool IsSpatial(ZuiFill f) => f != null && f.texture == ZuiFill.TextureKind.None
                                         && (f.mode == ZuiFill.Mode.Linear || f.mode == ZuiFill.Mode.Radial);

        /// An over-life ramp: the one fill whose colour is driven by a 0..1 clock, so it is FLAT in space and moves
        /// in time — one sample paints the whole thing, and which clock feeds it is the only question.
        static bool IsOverLife(ZuiFill f)
            => f != null && f.texture == ZuiFill.TextureKind.None && f.mode == ZuiFill.Mode.OverLife;

        /// <summary>Paint a ZuiFill onto a TMP's face, sampled at a 0..1 <paramref name="life"/>. An OVER-LIFE ramp
        /// is ONE FLAT colour at any instant — the colour the ramp has reached at that life — because over-life means
        /// the colour changes over the LIFE, not down the glyph; animating it is simply calling this again next frame.
        /// Solid and a texture are flat too (a texture has no glyph-space equivalent, so it falls back to its tint).
        /// A spatial fill (Linear / Radial) is left white for <see cref="ApplyMesh"/>'s per-vertex sampling.</summary>
        static void ApplyFill(TMP_Text tmp, ZuiFill fill, float life)
        {
            // TMP's vertex gradient is never used by a splash: all it can do is run one colour into another DOWN the
            // glyph, which is not what any of these fills mean — a spatial fill needs a real per-vertex sample, and
            // the other three are flat.
            tmp.enableVertexGradient = false;
            // White for a spatial fill, so the per-vertex colours ApplyMesh writes are not tinted a second time.
            if (IsSpatial(fill)) tmp.color = Color.white;
            else if (IsOverLife(fill)) tmp.color = fill.Evaluate(Mathf.Clamp01(life), 0f, 0f);
            else tmp.color = fill != null ? fill.color : Color.white;
        }

        /// <summary>The 0..1 point an OVER-LIFE ramp is sampled at. The base IS the life — that is what "over life"
        /// means — and the colour cycle SCROLLS that point rather than replacing it, so an authored ramp still reads
        /// over the life while the cycle travels through it (and a cycle left at speed 0 changes nothing).
        ///
        /// The per-letter offset is the letter's INDEX, not its rank in the cascade: the cycle travels across the line
        /// in READING order — where a letter SITS — while a rank is about WHEN it moves. The border twin never cycles;
        /// <see cref="TextSplash.cycleFill"/> is the FACE's knob and the twin carries its own fill.</summary>
        static float FillPhase(TextSplash s, float life, float t, int letterIndex, bool isBorderPass)
        {
            if (s == null || isBorderPass || !s.cycleFill) return Mathf.Clamp01(life);
            return Frac(life + t * s.cycleSpeed - letterIndex * s.cyclePerLetter);
        }

        /// A border is skipped only when it is statically switched off — an animated width may be 0 at this instant
        /// and fat at the next, so the twin has to exist for the whole play.
        static bool DrawsBorder(TextSplash s)
        {
            var v = s.borderWidth != null ? s.borderWidth.value : null;
            if (v == null) return false;
            return !(v.mode == ZUIValue.Mode.Static && v.staticValue <= 0f);
        }

        static void DestroyObj(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        static Vector3 AxisVec(SplashAxis a) => a switch
        {
            SplashAxis.X => Vector3.right,
            SplashAxis.Z => Vector3.forward,
            _ => Vector3.up,
        };

        static float Frac(float x) => x - Mathf.Floor(x);
    }
}
