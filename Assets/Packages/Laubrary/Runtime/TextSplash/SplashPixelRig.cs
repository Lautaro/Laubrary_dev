using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Collections;
using TMPro;
using Laubrary.SpriteFx;

namespace Laubrary.TextSplash
{
    /// <summary>
    /// Makes a splash come out in REAL pixels rather than a shader that fakes them.
    ///
    /// The splash Canvas is put into Screen-Space-Camera and pointed at a dedicated orthographic camera whose
    /// target is a <see cref="RenderTexture"/> of ceil(screen / pixelSize). Every glyph,
    /// every SDF edge, every gradient is therefore RASTERIZED at that low resolution — the buffer genuinely holds
    /// (screen ÷ pixelSize)² pixels and nothing downstream can recover detail that was never drawn. A second
    /// overlay Canvas then presents that buffer full-screen with point filtering, so each buffer pixel lands as an
    /// exact pixelSize × pixelSize block.
    ///
    /// The presenter is a plain uGUI RawImage on an overlay Canvas ON PURPOSE: overlay canvases are composited by
    /// the engine itself, outside any render pipeline, so this works unchanged on Built-in, URP and HDRP. The
    /// obvious alternatives do not — `OnRenderImage` is never called under an SRP, and a URP Renderer Feature blit
    /// would be URP-only and need project-asset configuration. (This project runs URP 17.3 on Unity 6000.3.)
    ///
    /// Four stages cannot happen on the GPU without shipping a shader — the SpriteFx stack, the crisping alpha
    /// threshold (<see cref="SplashPixelation.alphaCutoff"/>), the palette lock
    /// (<see cref="SplashPixelation.paletteLock"/>) and the palette crunch
    /// (<see cref="SplashPixelation.colorSteps"/>) — so the buffer is read back to the CPU and they all run there, in
    /// that order, in <see cref="ApplyCpuStage"/>. The read-back is engaged only when at least one of them is
    /// actually asked for (<see cref="NeedsCpuStage"/>); with all of them off the RenderTexture goes straight to the
    /// RawImage and no pixel ever leaves the GPU.
    ///
    /// TMP's SDF shaders blend `One OneMinusSrcAlpha`, so the buffer holds PREMULTIPLIED colour and presenting it
    /// through uGUI's default UI material would multiply by alpha a second time — see
    /// <see cref="ApplyPresentMaterial"/>, which is what <see cref="SplashPixelation.fixEdgeAlpha"/> switches.
    ///
    /// Everything this creates (camera, render texture, presenter, presentation material, read-back texture) is
    /// HideAndDontSave and is torn down in <see cref="OnDestroy"/>, which fires when the player destroys the host.
    ///
    /// It does NOT own the canvas's render MODE. <see cref="SplashPlayer"/> decides that once, when it builds the
    /// host: a spec with depth on already arrives here as a Screen-Space-Camera canvas. This rig then ADOPTS that
    /// canvas — it retargets it at the low-res camera rather than flipping the mode a second time — so the two
    /// features can never each configure and restore the same canvas behind the other's back. It parks the camera
    /// it took over ONLY when the player built that camera itself; a camera the player merely borrowed from the
    /// scene belongs to the game and is left untouched (see <see cref="SplashPlayer.OwnedCanvasCamera"/>).
    /// </summary>
    [AddComponentMenu("")]
    public sealed class SplashPixelRig : MonoBehaviour
    {
        // Parked far from the scene so that even if the layer scan has to fall back to a shared layer, no real
        // content can wander into the pixel camera's frustum.
        const float CameraPark = 10000f;

        // A 3D spin swings glyph vertices deep in Z — by as much as half the LINE's width, since a whole-line spin
        // rotates the text about its own centre and one canvas unit is one world unit here. So the canvas sits well
        // down the camera's axis with the clip range spread either side of it, and no part of a spinning line can
        // fall behind the near plane. Under ORTHOGRAPHIC projection the distance is free: it changes what gets
        // clipped, never how big anything draws.
        const float CanvasPlane = 3000f;

        const string PremulShaderName = "Hidden/Laubrary/TextSplash/PremultipliedUI";
        // Path INSIDE the package's own Resources folder, so the shader ships with Laubrary and survives a build
        // without the consumer project having to configure anything. See ResolvePremulShader.
        const string PremulResourceName = "SplashPremultipliedUI";

        /// <summary>Give a live splash its low-res rig. Called once, by the player, right after it has built the
        /// host GameObject and its Canvas. A no-op when the spec asks for nothing to happen — pixelation off, or a
        /// pixel size of 1 with neither a palette crunch nor a SpriteFx stack, which would render identically to no
        /// rig at all.</summary>
        public static void Attach(GameObject host, Canvas canvas, TextSplash spec)
        {
            if (host == null || canvas == null || spec == null) return;
            var px = spec.pixelation;
            if (px == null || !px.enabled) return;
            if (px.pixelSize <= 1 && !NeedsCpuStage(px)) return;

            var rig = host.GetComponent<SplashPixelRig>();
            if (rig == null) rig = host.AddComponent<SplashPixelRig>();
            rig.Init(canvas, spec);
        }

        // ── live state ─────────────────────────────────────────────────────────────────────────────────────────
        Canvas _canvas;
        TextSplash _spec;
        TMP_Text _text;

        Camera _cam;
        GameObject _camGO;
        RenderTexture _rt;

        GameObject _presenterGO;
        Canvas _presenterCanvas;
        RawImage _raw;
        RectTransform _rawRect;

        Material _premulMat;     // premultiplied presentation material — null until it is first wanted AND found
        bool _premulApplied;     // whether the RawImage is currently presenting through it

        Texture2D _cpu;          // read-back target — only allocated while a CPU stage actually needs one

        float _fxElapsed;        // SpriteFx timeline, independent of the splash's own schedule
        int _fxFrame;            // frame counter the stack's noise/dither modifiers advance on

        CanvasScaler _scaler;
        int _layer;
        int _lowW, _lowH, _pixelSize, _screenW, _screenH;

        // canvas state we borrowed, restored if the canvas somehow outlives us
        RenderMode _prevMode;
        Camera _prevWorldCamera;
        float _prevPlaneDistance, _prevScaleFactor, _prevScalerFactor;
        bool _drivesScalerFactor;
        bool _ready;

        // The camera the PLAYER put on an already-camera-space canvas (depth), parked while we render instead of it.
        Camera _adoptedCam;
        bool _adoptedCamWasEnabled;
        bool _ownsLayer;

        void Init(Canvas canvas, TextSplash spec)
        {
            if (_ready) return;
            _canvas = canvas; _spec = spec;

            // Saved either way, and correct either way: when the canvas was already camera-space this restores it to
            // the PLAYER's configuration rather than to an overlay it never was.
            _prevMode = canvas.renderMode;
            _prevWorldCamera = canvas.worldCamera;
            _prevPlaneDistance = canvas.planeDistance;
            _prevScaleFactor = canvas.scaleFactor;

            // ADOPT rather than re-configure: a canvas that is already in camera space was put there by the player
            // (TextSplash.NeedsCameraSpace). WHICH camera is driving it decides what adopting means, and asking the
            // player is the only way to tell the two apart:
            //   the player's OWN camera — park it and reuse the isolated layer it already culls to. Re-resolving
            //     either would leave the splash on a layer that camera no longer culls, and would make two cameras
            //     draw the same splash, one to the screen and one to the buffer.
            //   a camera BORROWED from the scene — leave it strictly alone (disabling it would blank the game) and
            //     take an isolated layer of our own. Moving the splash onto that layer is precisely what stops the
            //     scene's camera from drawing it to the screen alongside the presented buffer.
            var player = GetComponent<SplashPlayer>();
            var owned = player != null ? player.OwnedCanvasCamera : null;
            _adoptedCam = owned != null && canvas.renderMode == RenderMode.ScreenSpaceCamera
                          && canvas.worldCamera == owned ? owned : null;
            if (_adoptedCam != null)
            {
                _adoptedCamWasEnabled = _adoptedCam.enabled;
                _adoptedCam.enabled = false;
                _layer = gameObject.layer;
                _ownsLayer = false;
            }
            else
            {
                _layer = SplashPlayer.ClaimIsolatedLayer();
                _ownsLayer = true;
            }
            SplashPlayer.SetLayerRecursively(gameObject, _layer);

            _camGO = new GameObject("[TextSplash] Pixel Camera") { hideFlags = HideFlags.HideAndDontSave };
            _camGO.transform.position = new Vector3(0f, CameraPark, 0f);
            _cam = _camGO.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.cullingMask = 1 << _layer;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = CanvasPlane * 2f;
            _cam.depth = -100f;                 // render before anything that might want the result
            _cam.allowHDR = false;
            _cam.allowMSAA = false;             // MSAA would soften exactly the edges we want hard
            // Dynamic resolution renders the camera into a SMALLER slice of the target and stretches it back —
            // a resample of the very buffer whose whole point is that one texel is one authored pixel.
            _cam.allowDynamicResolution = false;
            _cam.useOcclusionCulling = false;

            // Mode only when nobody else already set it; the retarget happens in both cases.
            if (_adoptedCam == null) canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _cam;
            canvas.planeDistance = CanvasPlane;

            // A Screen-Space-Camera canvas measures itself in camera pixels, and the camera is now the LOW-RES
            // buffer — so without a compensating scale factor a 96pt line would come out pixelSize× too big. A
            // resolution-relative scaler (Scale With Screen Size / Constant Physical Size) already tracks that
            // automatically and is left alone; only the fixed-pixel case needs the 1/pixelSize correction.
            _scaler = canvas.GetComponent<CanvasScaler>();
            _drivesScalerFactor = _scaler != null && _scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize;
            if (_scaler != null) _prevScalerFactor = _scaler.scaleFactor;

            BuildPresenter();
            EnsureTargets();
            // The camera can be rendered before this component ever gets a LateUpdate (the caller may Show() from a
            // coroutine, or the component may be added too late in the frame to be ticked), and until these two have
            // run the camera is still at its default orthographic size and the buffer is presented with the wrong
            // blend — one frame of a wildly zoomed splash.
            ApplyCanvasScale();
            ApplyPresentMaterial();
            _ready = true;
        }

        /// <summary>The overlay canvas the buffer is presented on, and the RawImage that presents it.
        ///
        /// Two things here are load-bearing for "one buffer texel = one whole block of screen pixels", and both are
        /// about keeping every number in the chain a WHOLE number of screen pixels:
        ///   scaleFactor 1, with no CanvasScaler — a canvas unit has to BE a screen pixel. Under any other factor an
        ///     integer block size resolves to a fractional number of device pixels and every block boundary falls
        ///     inside a screen pixel, which is one authored pixel showing two colours.
        ///   the RawImage pinned to the canvas's bottom-left CORNER, pivot and all — the buffer is
        ///     ceil(screen / pixelSize) so it overhangs, and a corner-anchored rect spans [0, low*pixelSize] with no
        ///     division by two anywhere. Centring it and offsetting by half the overhang arrives at the same place,
        ///     but only because two halves cancel; a corner leaves nothing to cancel.
        /// uGUI's own `pixelPerfect` is deliberately NOT used to do this: it rounds in CANVAS-local space, whose
        /// origin is the screen's centre, so on a screen with an odd dimension it rounds a whole-pixel corner onto a
        /// half-pixel one — the exact defect being avoided.</summary>
        void BuildPresenter()
        {
            _presenterGO = new GameObject("[TextSplash] Pixel Presenter") { hideFlags = HideFlags.HideAndDontSave };
            _presenterCanvas = _presenterGO.AddComponent<Canvas>();
            _presenterCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _presenterCanvas.sortingOrder = _canvas.sortingOrder;
            _presenterCanvas.scaleFactor = 1f;
            _presenterCanvas.pixelPerfect = false;

            var imgGO = new GameObject("Buffer");
            imgGO.transform.SetParent(_presenterGO.transform, false);
            _raw = imgGO.AddComponent<RawImage>();
            _raw.raycastTarget = false;
            _raw.uvRect = new Rect(0f, 0f, 1f, 1f);
            _rawRect = _raw.rectTransform;
            _rawRect.anchorMin = _rawRect.anchorMax = _rawRect.pivot = Vector2.zero;
            _rawRect.anchoredPosition = Vector2.zero;
            // The SIZE is EnsureTargets' job — it depends on the buffer it sizes.
        }

        void LateUpdate()
        {
            // After every Update, so the player has already written this frame's pose and mesh.
            if (!_ready || _canvas == null || _spec == null) return;

            if (_text == null)
            {
                _text = GetComponentInChildren<TMP_Text>(true);
                // The border twin and any depth layers are built after Init, so the layer has to be re-stamped once
                // they exist — they inherit the face's layer, but a copy created before this ran would miss it.
                if (_text != null) SplashPlayer.SetLayerRecursively(gameObject, _layer);
            }

            EnsureTargets();
            ApplyPresentMaterial();
            ApplyCanvasScale();
            Snap();
            RunCpuStage();
        }

        // ── buffers ────────────────────────────────────────────────────────────────────────────────────────────
        void EnsureTargets()
        {
            int ps = Mathf.Clamp(_spec.pixelation.pixelSize, 1, 32);
            int sw = Mathf.Max(1, Screen.width), sh = Mathf.Max(1, Screen.height);
            int lw = Mathf.Max(1, Mathf.CeilToInt(sw / (float)ps));
            int lh = Mathf.Max(1, Mathf.CeilToInt(sh / (float)ps));

            if (_rt == null || lw != _lowW || lh != _lowH || ps != _pixelSize || sw != _screenW || sh != _screenH)
            {
                // Unhook before releasing: destroying a texture that is still a camera's live target is exactly the
                // case Unity warns about, and the camera is about to be handed the new one anyway.
                if (_cam != null) _cam.targetTexture = null;
                ReleaseRenderTexture();
                _lowW = lw; _lowH = lh; _pixelSize = ps; _screenW = sw; _screenH = sh;

                _rt = new RenderTexture(lw, lh, 24, RenderTextureFormat.ARGB32)
                {
                    name = "[TextSplash] Pixel Buffer",
                    filterMode = FilterMode.Point,       // the whole point — nearest-neighbour on the way back up
                    wrapMode = TextureWrapMode.Clamp,
                    antiAliasing = 1,
                    useMipMap = false,
                    autoGenerateMips = false,
                    hideFlags = HideFlags.HideAndDontSave
                };
                _rt.Create();
                _cam.targetTexture = _rt;

                // Present at an EXACT integer multiple so every buffer pixel is the same size on screen. Corner-
                // anchored (see BuildPresenter), so this ONE integer is the whole presentation: the rect spans
                // [0, low*pixelSize] from the screen's bottom-left, and the round-up's overhang — at most
                // (pixelSize − 1) px — falls off the right/top edge instead of being halved onto both.
                _rawRect.anchoredPosition = Vector2.zero;
                _rawRect.sizeDelta = new Vector2(lw * ps, lh * ps);

                if (_cpu != null) { SafeDestroy(_cpu); _cpu = null; }
            }

            // Re-asserted rather than set once: a canvas unit that stops being a screen pixel turns the integer
            // size above into a fractional one, and nothing else in the rig would notice.
            if (_presenterCanvas != null && !Mathf.Approximately(_presenterCanvas.scaleFactor, 1f))
                _presenterCanvas.scaleFactor = 1f;

            EnsureCpuTarget();
        }

        void EnsureCpuTarget()
        {
            if (!NeedsCpuStage(_spec.pixelation))
            {
                if (_cpu != null) { SafeDestroy(_cpu); _cpu = null; }
                if (_raw.texture != _rt) _raw.texture = _rt;
                return;
            }

            if (_cpu == null || _cpu.width != _lowW || _cpu.height != _lowH)
            {
                if (_cpu != null) SafeDestroy(_cpu);
                _cpu = new Texture2D(_lowW, _lowH, TextureFormat.RGBA32, false, false)
                {
                    name = "[TextSplash] Pixel Buffer (CPU)",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
            if (_raw.texture != _cpu) _raw.texture = _cpu;
        }

        void ApplyCanvasScale()
        {
            float want = 1f / Mathf.Max(1, _pixelSize);
            if (_scaler == null)
            {
                if (!Mathf.Approximately(_canvas.scaleFactor, want)) _canvas.scaleFactor = want;
            }
            else if (_drivesScalerFactor)
            {
                float target = _prevScalerFactor * want;
                if (!Mathf.Approximately(_scaler.scaleFactor, target)) _scaler.scaleFactor = target;
            }

            // Keep one canvas unit ≈ one world unit so the text never renders at an extreme world scale.
            float sf = Mathf.Max(0.0001f, _canvas.scaleFactor);
            float orthoSize = (_lowH / sf) * 0.5f;
            if (!Mathf.Approximately(_cam.orthographicSize, orthoSize)) _cam.orthographicSize = orthoSize;
        }

        // ── presentation blend ─────────────────────────────────────────────────────────────────────────────────
        /// <summary>Choose the material the RawImage presents the buffer with, honouring
        /// <see cref="SplashPixelation.fixEdgeAlpha"/> — re-checked every frame so toggling it live actually takes.
        ///
        /// TMP's SDF shaders blend `One OneMinusSrcAlpha`, so the buffer holds PREMULTIPLIED colour. uGUI's default
        /// UI material blends `SrcAlpha OneMinusSrcAlpha` and multiplies by alpha a SECOND time, so an antialiased
        /// edge pixel composites as C·a² instead of C·a and the rim reads darker than it should. Interior pixels
        /// (a = 1) are exact either way, which is why only the rim was ever wrong.</summary>
        void ApplyPresentMaterial()
        {
            if (_raw == null || _spec == null || _spec.pixelation == null) return;

            bool want = _spec.pixelation.fixEdgeAlpha;
            if (want && _premulMat == null)
            {
                var shader = ResolvePremulShader();
                if (shader != null)
                    _premulMat = new Material(shader)
                    {
                        name = "[TextSplash] Premultiplied Present",
                        hideFlags = HideFlags.HideAndDontSave
                    };
            }
            // A missing shader silently becomes "don't fix it" rather than "present nothing".
            want &= _premulMat != null;

            if (want == _premulApplied) return;
            _premulApplied = want;
            _raw.material = want ? _premulMat : null;   // null = uGUI's own default UI material
        }

        static Shader _premulShader;
        static bool _premulResolved;

        /// <summary>Look the presentation shader up ONCE per domain.
        ///
        /// It loads from the package's OWN `Resources` folder rather than via `Shader.Find`, and that is the whole
        /// point: a shader that sits in no Resources folder, is in no project's Always Included list and is
        /// referenced by no material gets STRIPPED from a player build, so `Shader.Find` would return null and the
        /// fix would quietly switch itself off in builds while working perfectly in the editor. Shipping it inside
        /// the package means it travels with Laubrary and needs no per-project setup — which matters, because this
        /// package is copied into consumer projects that will never know to configure it.
        ///
        /// `Shader.Find` is still tried as a fallback (it covers a project that DID add it to Always Included, or a
        /// renamed/relocated copy). A miss is cached and warned about exactly once however many splashes play.</summary>
        static Shader ResolvePremulShader()
        {
            if (_premulResolved) return _premulShader;
            _premulResolved = true;
            _premulShader = Resources.Load<Shader>(PremulResourceName) ?? Shader.Find(PremulShaderName);
            if (_premulShader == null)
                Debug.LogWarning($"[TextSplash] Shader \"{PremulShaderName}\" was not found, so the pixelated " +
                    "splash is presented with the default UI blend and its antialiased edge will read slightly " +
                    "dark. It should ship at Runtime/TextSplash/Resources/" + PremulResourceName + ".shader — if " +
                    "that file was moved or removed, restore it or add the shader to Project Settings > Graphics > " +
                    "Always Included Shaders.");
            return _premulShader;
        }

        // ── pixel-grid snapping ────────────────────────────────────────────────────────────────────────────────
        /// One buffer pixel is `1 / canvas.scaleFactor` canvas units, so rounding to that step is rounding to whole
        /// low-res pixels. Whole-line mode only has to move the text's rect; per-letter mode moves glyph VERTICES,
        /// so those get snapped as well — each glyph by a single shared delta, never corner by corner, or the quad
        /// would breathe by a pixel as it travels.
        void Snap()
        {
            if (!_spec.pixelation.snapMotion || _text == null) return;
            float step = 1f / Mathf.Max(0.0001f, _canvas.scaleFactor);
            if (step <= 1.0001f) return;

            var rt = _text.rectTransform;
            Vector2 p = rt.anchoredPosition;
            Vector2 snapped = new Vector2(Mathf.Round(p.x / step) * step, Mathf.Round(p.y / step) * step);
            if (snapped != p) rt.anchoredPosition = snapped;

            if (!_spec.PerLetter) return;

            var info = _text.textInfo;
            if (info == null || info.characterCount == 0 || info.meshInfo == null) return;
            bool touched = false;
            for (int i = 0; i < info.characterCount; i++)
            {
                var ci = info.characterInfo[i];
                if (!ci.isVisible) continue;
                int mi = ci.materialReferenceIndex;
                if (mi < 0 || mi >= info.meshInfo.Length) continue;
                var verts = info.meshInfo[mi].vertices;
                int vi = ci.vertexIndex;
                if (verts == null || vi < 0 || vi + 3 >= verts.Length) continue;

                Vector3 bl = verts[vi];
                float dx = Mathf.Round(bl.x / step) * step - bl.x;
                float dy = Mathf.Round(bl.y / step) * step - bl.y;
                if (dx == 0f && dy == 0f) continue;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 v = verts[vi + k];
                    v.x += dx; v.y += dy;
                    verts[vi + k] = v;
                }
                touched = true;
            }
            if (touched) _text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
        }

        // ── CPU stage ──────────────────────────────────────────────────────────────────────────────────────────
        /// <summary>Whether ANY CPU stage is asked for: a SpriteFx stack, the alpha threshold that crisps the edges,
        /// the palette lock, or the palette crunch. One predicate because five separate places have to agree about
        /// it — the attach check, the read-back TEXTURE, the read-back itself, the stage, and the editor preview,
        /// which is why this is public — and a setting that engaged the stage but not the read-back would silently do
        /// nothing at all. (`alphaCutoff` was once missing from the preview's own copy of this test, and the dial
        /// therefore did nothing there while working perfectly at runtime.)</summary>
        public static bool NeedsCpuStage(SplashPixelation px)
            => px != null && (px.colorSteps > 0 || px.spriteFx != null || px.alphaCutoff > 0f || px.paletteLock);

        /// Pull the low-res buffer back to the CPU and run the stage over it. This is the ONLY path in the rig that
        /// costs a GPU→CPU sync, which is why it is gated at all and why the buffer being low-res is what makes it
        /// affordable (a 480×270 buffer is 129,600 pixels — 1/16th of 1080p's 2,073,600).
        /// It reads LAST frame's buffer: the pixel camera renders after LateUpdate, so the read-back sees the
        /// previous render. One frame of lag on a splash is not perceptible, and it avoids forcing a mid-frame
        /// Camera.Render() (which SRPs treat as a special case).
        void RunCpuStage()
        {
            if (!NeedsCpuStage(_spec.pixelation) || _cpu == null || _rt == null) return;

            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _cpu.ReadPixels(new Rect(0f, 0f, _lowW, _lowH), 0, 0, false);
            RenderTexture.active = prev;

            _fxElapsed += Time.unscaledDeltaTime;
            ApplyCpuStage(_spec, _cpu, _lowW, _lowH, _fxElapsed, _fxFrame++);
        }

        /// <summary>The whole CPU half — SpriteFx stack, then the alpha threshold, then the palette lock or the
        /// palette crunch — over a read-back buffer, as a PURE STATIC so the editor preview runs the identical code
        /// instead of a lookalike that drifts. `cpu` is mutated in place and `Apply`d; `fxTime` is seconds since the
        /// effect started.
        ///
        /// THE ORDER IS THE DESIGN, and every boundary is load-bearing:
        ///   after SpriteFx — a LayerDissolve or an AlphaMask writes exactly the partly transparent pixels the
        ///     threshold exists to remove, so crisping first would let the mush straight back in. Running last of
        ///     the two also means everything downstream sees fully opaque pixels, where premultiplied and straight
        ///     colour coincide.
        ///   the palette lock AFTER the threshold — the threshold is what un-premultiplies a promoted edge pixel, and
        ///     what DELETES every pixel too faint to promote. Snapping first would spend the search on pixels about
        ///     to be thrown away, and would spend it badly: recovering the straight colour of a 10%-coverage pixel
        ///     multiplies its rounding error by ten, so what got matched against the palette would be mostly noise.
        ///     Crisping first hands the snap a buffer of alpha-255 pixels, where straight and premultiplied colour
        ///     are the same number and the match is exact. (At a cutoff of 0 the snap still runs — it takes the
        ///     coverage off and puts it back itself — it is simply working on a softer buffer, and the stored
        ///     premultiplied value of a partly transparent pixel is then its palette colour SCALED by its coverage,
        ///     which is what the presentation blend expects.)
        ///   the palette lock INSTEAD OF the crunch — both answer "which colours may this splash use", and the lock
        ///     answers it with the author's own fills instead of an even numeric grid those fills do not sit on. So
        ///     the crunch does not get to run after the lock and push every snapped pixel back off the palette; it
        ///     stays the fallback for a splash with no palette to lock to at all. Neither touches alpha, so neither
        ///     can undo the threshold.
        ///
        /// It forces the BURST path rather than honouring <c>SpriteFxSettings.UseBurstJobs</c>: that project default
        /// is justified for a 64×64 sprite frame (4,096 pixels), where scheduling a job costs more than the loop. A
        /// splash buffer is 8–30× larger, so inline would be the wrong call every time — not a preference to inherit.</summary>
        public static void ApplyCpuStage(TextSplash spec, Texture2D cpu, int w, int h, float fxTime, int frame)
        {
            var p = spec != null ? spec.pixelation : null;
            if (p == null || cpu == null || !NeedsCpuStage(p)) return;

            // GetRawTextureData gives a VIEW straight onto the texture's own memory, so every pass below mutates in
            // place — no managed copy in or out.
            NativeArray<Color32> px = cpu.GetRawTextureData<Color32>();
            if (p.spriteFx != null) RunSpriteFx(p.spriteFx, px, w, h, fxTime, frame);
            if (p.alphaCutoff > 0f) Crisp(px, p.alphaCutoff);

            int palette = p.paletteLock ? EnsurePalette(spec) : 0;
            if (palette > 0) SnapToPalette(px, palette);
            else if (p.colorSteps > 0) Quantize(px, p.colorSteps);
            cpu.Apply(false, false);
        }

        static void RunSpriteFx(SpriteFxSpec fx, NativeArray<Color32> px, int w, int h, float fxTime, int frame)
        {
            float life = fx.SampleEnvelope(Mathf.Clamp01(fxTime / Mathf.Max(0.001f, fx.duration)));
            var eval = SpriteFxStack.LifeEval(life, fx.seed);

            SpriteFxStack.Resolve(fx.modifiers, eval, Allocator.TempJob, out var ops, out var luts);
            try
            {
                if (ops.Length > 0)
                    SpriteFxStack.Schedule(px, ops, luts, w, h, frame, life, fx.seed).Complete();
            }
            finally
            {
                if (ops.IsCreated) ops.Dispose();
                if (luts.IsCreated) luts.Dispose();
            }
        }

        /// <summary>Make every pixel either fully ON or fully OFF — REAL pixel art, where a pixel is a decision and
        /// never a blend.
        ///
        /// What it is fixing: TMP's SDF antialiases every glyph edge, and that soft band is sized in BUFFER pixels.
        /// At native resolution it is a fraction of a screen pixel and reads as a clean edge; through a low-res
        /// buffer the very same band is a fraction of a CHUNKY pixel, i.e. a whole visible block of half-transparent
        /// mush around every letter. No amount of point filtering on the way back up can recover from that, because
        /// the softness is in the data, not in the sampling.
        ///
        /// PREMULTIPLIED ALPHA is why this cannot just write `a = 255`. TMP's shaders blend `One OneMinusSrcAlpha`
        /// into a transparent-black-cleared buffer, so what is stored is C·a: an edge pixel at 40% coverage holds
        /// 40% of its colour, not its colour. Promoting that pixel's alpha to 255 as-is would keep the darkened RGB
        /// and leave a black rim exactly where the mush used to be. So a promoted pixel is UN-PREMULTIPLIED first —
        /// its RGB divided by the coverage that is about to be thrown away — which recovers the glyph's true colour;
        /// re-premultiplying by the new alpha of 1 is then the identity, so the buffer's premultiplied contract
        /// still holds and the presentation blend (fixEdgeAlpha) keeps working unchanged. A killed pixel is written
        /// as transparent BLACK rather than just alpha 0, since a premultiplied buffer has no other zero.</summary>
        static void Crisp(NativeArray<Color32> px, float cutoff)
        {
            // At least 1: a cutoff above 0 must never keep a pixel with no coverage at all.
            int t = Mathf.Clamp(Mathf.RoundToInt(cutoff * 255f), 1, 255);
            for (int i = 0; i < px.Length; i++)
            {
                Color32 c = px[i];
                if (c.a < t) { px[i] = new Color32(0, 0, 0, 0); continue; }
                if (c.a < 255)
                {
                    float inv = 255f / c.a;
                    c.r = ToByte(c.r * inv);
                    c.g = ToByte(c.g * inv);
                    c.b = ToByte(c.b * inv);
                    c.a = 255;
                }
                px[i] = c;
            }
        }

        static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v), 0, 255);

        // ── palette lock ───────────────────────────────────────────────────────────────────────────────────────
        // What this fixes, and why the crisping threshold above could never fix it: the threshold decides whether a
        // pixel EXISTS, and it works perfectly — a splash with a cutoff has no partly transparent pixel left at all.
        // But a low-res pixel that straddles the white face and the dark border still RASTERIZED to a blend of the
        // two, and that blend is fully opaque, so it survives the threshold untouched. A two-colour splash therefore
        // reaches the screen as dozens of shades of grey: not soft edges, but the INSIDE of every letter painted in
        // colours the author never picked. `colorSteps` flattens them onto an even numeric grid, which is why it
        // helped and yet read as banding — the grid's levels are not the author's colours.
        //
        // The palette itself comes from the asset (TextSplash.BuildPalette): the face, border and — with depth on —
        // side fills, gradients sampled across their ramp. Consequences worth knowing, both direct results of the
        // lock having the LAST word on colour: a bevel's per-pixel lighting and depth's falloff/brightness grading
        // produce colours that are in no fill, so they are snapped onto the fills too and read flatter. That is what
        // a locked palette means in real pixel art — shading has to BE a palette entry — and the escape is the dial.

        // The palette is CACHED. A rebuild costs one BuildPalette into a scratch list plus a 2–20 entry comparison
        // per call, which is nothing beside a 130,000-pixel loop, and the per-entry luma/chroma decomposition that
        // loop actually reads is recomputed only when the colours themselves move. Comparing CONTENTS rather than
        // trusting a spec reference is what lets an editor preview and a live splash alternate through this static
        // without either poisoning the other's cache.
        static readonly List<Color32> _palScratch = new List<Color32>(48);
        static Color32[] _palColors = new Color32[0];
        static float[] _palY = new float[0], _palCr = new float[0], _palCb = new float[0];
        static int _palCount;

        /// The number of palette entries now cached (0 = this splash has no palette to lock to).
        static int EnsurePalette(TextSplash spec)
        {
            if (spec == null) return 0;
            spec.BuildPalette(_palScratch);
            int n = _palScratch.Count;
            if (n == 0) { _palCount = 0; return 0; }
            if (n == _palCount && PaletteMatches(n)) return n;

            if (_palColors.Length < n)
            {
                _palColors = new Color32[n];
                _palY = new float[n]; _palCr = new float[n]; _palCb = new float[n];
            }
            for (int i = 0; i < n; i++)
            {
                Color32 c = _palScratch[i];
                _palColors[i] = c;
                Decompose(c.r, c.g, c.b, out _palY[i], out _palCr[i], out _palCb[i]);
            }
            _palCount = n;
            return n;
        }

        static bool PaletteMatches(int n)
        {
            for (int i = 0; i < n; i++)
            {
                Color32 a = _palColors[i], b = _palScratch[i];
                if (a.r != b.r || a.g != b.g || a.b != b.b) return false;
            }
            return true;
        }

        // A LUMA/CHROMA split, rather than RGB Euclidean distance, is what "nearest colour" means below — because
        // plain RGB cannot tell a brightness error from a hue error. Mid-grey sits 220 units from white and 221 from
        // saturated green, so which of the two a grey pixel snaps to is decided by ROUNDING; weighting the channels
        // 0.30/0.59/0.11 inside one Euclidean distance barely moves that (16,129 against 16,233 — still half a
        // percent apart). Separating the two axes and charging a hue error DOUBLE puts the same pair 5.6× apart and
        // white wins outright, which is the answer a human gives. It is also the right bias for this job: at one
        // pixel per letter-stroke a wrong hue reads as a different MATERIAL, while a wrong brightness only reads as
        // shading.
        const float LumaR = 0.30f, LumaG = 0.59f, LumaB = 0.11f;
        const float ChromaWeight = 2f;

        /// Split a colour into LUMA (how bright) and CHROMA (which hue — what is left of the red and blue channels
        /// once that luma is removed; green's own residue is implied, since the three weigh out to zero).
        static void Decompose(float r, float g, float b, out float y, out float cr, out float cb)
        {
            y = LumaR * r + LumaG * g + LumaB * b;
            cr = r - y;
            cb = b - y;
        }

        /// <summary>Snap every visible pixel's colour to the nearest palette entry.
        ///
        /// PREMULTIPLIED ALPHA is the trap here, exactly as it is in <see cref="Crisp"/>: the buffer holds C·a while
        /// the palette is authored in STRAIGHT colour, so a pixel at 40% coverage holds 40% of its colour and would
        /// match against a palette of full-strength ones as if it were four tenths as bright — every result wrong in
        /// proportion to its own alpha. So the coverage comes off before the comparison and goes back on after it,
        /// and alpha is never touched, which is what keeps the threshold's work (and the presentation blend) intact.
        /// After <see cref="Crisp"/> every surviving pixel is alpha 255 and both conversions are the identity, which
        /// is why this sits where it does.</summary>
        static void SnapToPalette(NativeArray<Color32> px, int n)
        {
            // A one-entry memo of the previous pixel's answer, held in the loop so there is no cache to invalidate.
            // Flat runs are the NORM in a low-res buffer — a letter's interior is one colour for tens of pixels at a
            // time, and the loop walks the buffer row by row — so this turns most of the work into a four-byte
            // comparison instead of an n-entry search. It keys on the full colour INCLUDING alpha (the coverage is
            // part of the answer) and returns what a full search would have returned, so it can never differ.
            byte inR = 0, inG = 0, inB = 0, inA = 0, outR = 0, outG = 0, outB = 0;
            bool memo = false;

            for (int i = 0; i < px.Length; i++)
            {
                Color32 c = px[i];
                if (c.a == 0) continue;              // a killed pixel has no colour to snap

                if (memo && c.r == inR && c.g == inG && c.b == inB && c.a == inA)
                {
                    c.r = outR; c.g = outG; c.b = outB;
                    px[i] = c;
                    continue;
                }
                inR = c.r; inG = c.g; inB = c.b; inA = c.a;

                float sr = c.r, sg = c.g, sb = c.b;
                if (c.a < 255)
                {
                    // Recover the straight colour. Clamped because a brightened buffer (a SpriteFx Brightness, an
                    // additive blend) can hold a channel above its own coverage, and that would divide past 255.
                    float inv = 255f / c.a;
                    sr = Mathf.Min(255f, sr * inv);
                    sg = Mathf.Min(255f, sg * inv);
                    sb = Mathf.Min(255f, sb * inv);
                }

                Decompose(sr, sg, sb, out float y, out float cr, out float cb);
                int best = 0;
                float bestD = float.MaxValue;
                for (int k = 0; k < n; k++)
                {
                    float dy = y - _palY[k], dr = cr - _palCr[k], db = cb - _palCb[k];
                    float d = dy * dy + ChromaWeight * (dr * dr + db * db);
                    if (d < bestD) { bestD = d; best = k; }
                }

                Color32 t = _palColors[best];
                if (c.a < 255)
                {
                    float f = c.a / 255f;
                    outR = ToByte(t.r * f); outG = ToByte(t.g * f); outB = ToByte(t.b * f);
                }
                else { outR = t.r; outG = t.g; outB = t.b; }
                memo = true;

                c.r = outR; c.g = outG; c.b = outB;
                px[i] = c;
            }
        }

        // The quantization LUT is shared and rebuilt only when the step count changes — it is a pure function of
        // `steps`, so a static cache is correct and keeps the per-frame path allocation-free.
        static readonly byte[] _lut = new byte[256];
        static int _lutSteps = -1;

        /// Flatten each colour channel onto `steps` evenly spaced levels. Alpha is left alone: the pixel grid
        /// already supplies the chunkiness, and quantizing coverage would eat the glyph's shape as well as its
        /// palette.
        static void Quantize(NativeArray<Color32> px, int steps)
        {
            int n = Mathf.Clamp(steps, 2, 32);
            if (_lutSteps != n)
            {
                float d = n - 1;
                for (int i = 0; i < 256; i++)
                    _lut[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Round(i / 255f * d) / d * 255f), 0, 255);
                _lutSteps = n;
            }

            for (int i = 0; i < px.Length; i++)
            {
                Color32 c = px[i];
                c.r = _lut[c.r]; c.g = _lut[c.g]; c.b = _lut[c.b];
                px[i] = c;
            }
        }

        // ── teardown ───────────────────────────────────────────────────────────────────────────────────────────
        void OnDestroy()
        {
            // Only a layer this rig actually claimed: an adopted canvas is on the PLAYER's layer, and the player
            // releases that one itself.
            if (_ownsLayer) SplashPlayer.ReleaseIsolatedLayer(_layer);
            if (_adoptedCam != null) { _adoptedCam.enabled = _adoptedCamWasEnabled; _adoptedCam = null; }

            if (_canvas != null)
            {
                _canvas.renderMode = _prevMode;
                _canvas.worldCamera = _prevWorldCamera;
                _canvas.planeDistance = _prevPlaneDistance;
                _canvas.scaleFactor = _prevScaleFactor;
                if (_scaler != null && _drivesScalerFactor) _scaler.scaleFactor = _prevScalerFactor;
            }

            if (_cam != null) _cam.targetTexture = null;
            ReleaseRenderTexture();
            if (_cpu != null) { SafeDestroy(_cpu); _cpu = null; }
            if (_camGO != null) { SafeDestroy(_camGO); _camGO = null; _cam = null; }
            if (_presenterGO != null)
            {
                SafeDestroy(_presenterGO);
                _presenterGO = null; _presenterCanvas = null; _raw = null; _rawRect = null;
            }
            // A material assigned to a Graphic is not destroyed with it, so this one has to go by hand.
            if (_premulMat != null) { SafeDestroy(_premulMat); _premulMat = null; _premulApplied = false; }
        }

        void ReleaseRenderTexture()
        {
            if (_rt == null) return;
            if (RenderTexture.active == _rt) RenderTexture.active = null;
            _rt.Release();
            SafeDestroy(_rt);
            _rt = null;
        }

        // ── helpers ────────────────────────────────────────────────────────────────────────────────────────────
        // The pixel camera must see the splash and NOTHING else, which means a layer nothing else draws on — and no
        // two live splash cameras may share one, since they are all parked at the same spot. That registry lives on
        // SplashPlayer (SplashPlayer.ClaimIsolatedLayer / ReleaseIsolatedLayer) because the player hands out layers
        // for its own depth camera too, and one registry is the only way the two can't collide.

        static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
