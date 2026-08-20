// PyrePlusPlayback3DPreview — PROOF OF CONCEPT. Drives a PreviewRenderUtility scene that instantiates the
// Playback3D layer's assigned prefab, applies Speed/Scale/Tint to it, and renders either the graded live 3D frame
// or a point-filtered pixelated downsample of it. This is the ONE thing this new ShapeForm is actually about —
// see PyrePlusWindow.BuildPlaybackBox for the dials and PyrePlusWindow.Preview.cs's DrawPreview for where this
// class's output replaces the normal composited-canvas blit when the selected layer is Playback3D.
//
// Deliberately NOT the same render path as PyrePlusRenderer (rule #7, "one shared core", does not apply here —
// there is no deterministic closed-form/replay-harness core for a live Unity ParticleSystem sim; PyrePlusRenderer
// itself documents this shape as an editor-preview-only stub with no runtime bake yet).
//
// ── Matching the pack (2026-08-20) ──────────────────────────────────────────────────────────────────────────
// The first version of this preview rendered the prefab raw: LDR buffer, grey background, no grade, and it
// overwrote every ParticleSystem's startColor with the Tint dial. Against a real VFX pack that looks nothing like
// the pack's own demo scene, which is what the user reported. Verified against Vefects Fire VFX (URP): its fire
// materials use emissive intensities of 33 and 123 and its demo scene runs a Global Volume with Bloom (threshold
// 1, intensity 1, scatter 0.7) + ACES Tonemapping over an HDR camera on a dark background. A PreviewRenderUtility
// scene gets none of that, and a global Volume can't be added to one without leaking into every other editor
// camera — so the grade is reproduced explicitly here (HDR render target + PyrePlusPlayback3DPost blit chain).
// The reference rig this was matched against is the saved scene Assets/Dev/PyrePlusPlayback3D/Playback3DVfxTest.unity.
using UnityEditor;
using UnityEngine;

namespace Laubrary.PyrePlus.Editor
{
    public class PyrePlusPlayback3DPreview : System.IDisposable
    {
        PreviewRenderUtility util;
        GameObject instance;
        GameObject instancedFrom;   // the prefab `instance` was built from, so a prefab swap rebuilds it
        ParticleSystem[] roots;     // ONLY the top-most ParticleSystems — see ApplyDialsAndSimulate for why
        ParticleSystem[] allSystems;
        ParticleSystem.Particle[] particleBuf = new ParticleSystem.Particle[512];

        // Per-prefab camera framing, solved once from the effect's own simulated bounds (see SolveFraming).
        Vector3 fitCentre = new Vector3(0f, 1f, 0f);
        float fitRadius = 2.4f;     // world half-extent the framing fits; Zoom divides the resulting distance
        bool fitSolved;
        // The dial values the cached fit was solved against. Scale changes the effect's size and Loop duration
        // changes WHICH moment of the effect gets measured, so a fit solved at one value is wrong at another —
        // without this, dragging Scale from 1 to 5 burst the effect straight out of a frame solved for 1x.
        float fitSolvedScale, fitSolvedLoop;

        // HDR render target for the live 3D frame. HDR is not optional: the pack thresholds bloom at 1 and its
        // particles are authored well above 1, so an LDR (clamped) buffer has nothing left above the threshold and
        // produces no glow at all — the flat, clipped look the old preview had.
        RenderTexture sceneRT;
        int sceneW = -1, sceneH = -1;

        // Graded output (what the window actually draws in live mode) and the pixel-grid downsample.
        RenderTexture gradedRT;
        RenderTexture pixelRT;
        int pixelRTW = -1, pixelRTH = -1;

        Material postMat;
        bool postShaderMissing;     // cached so a missing shader does not re-run Shader.Find on every repaint
        const int PassPrefilter = 0, PassDown = 1, PassUp = 2, PassComposite = 3, PassQuantise = 4;
        const int BloomIterations = 5;
        static readonly int IdBloomTex = Shader.PropertyToID("_BloomTex");
        static readonly int IdFilter = Shader.PropertyToID("_Filter");
        static readonly int IdParams = Shader.PropertyToID("_Params");
        static readonly int IdTint = Shader.PropertyToID("_Tint");
        static readonly int IdSample = Shader.PropertyToID("_Sample");

        void EnsureUtil()
        {
            if (util != null) return;
            util = new PreviewRenderUtility();
            util.camera.nearClipPlane = 0.05f;
            util.camera.farClipPlane = 200f;
            // Black, not the PreviewRenderUtility default grey: every one of these packs is additive fire/smoke
            // authored against a dark scene. On grey, the black smoke reads as a dirty blob and the fire loses all
            // of its glow — the single biggest visual mismatch in the old preview.
            util.camera.clearFlags = CameraClearFlags.SolidColor;
            // Alpha 0, so the graded result composites over the PyrePlus backdrop like every other shape rather
            // than covering it with an opaque black rectangle. RGB still black: additive fire needs a dark base.
            util.camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            util.camera.allowHDR = true;
            util.camera.allowMSAA = false;
            util.lights[0].intensity = 1.1f;
            util.lights[0].transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            util.lights[1].intensity = 0.4f;
        }

        Material PostMat
        {
            get
            {
                if (postMat == null)
                {
                    if (postShaderMissing) return null;
                    var sh = Shader.Find("Hidden/Laubrary/PyrePlus/Playback3DPost");
                    if (sh == null) { postShaderMissing = true; return null; }   // caller falls back to the ungraded frame
                    postMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                }
                return postMat;
            }
        }

        // (Re)builds the instantiated prefab when the assigned prefab changes (or first use). Destroys the
        // previous instance first — PreviewRenderUtility's own scene, never the real one, so DestroyImmediate is
        // safe/appropriate here (no Undo needed: pure preview state, per the authoring guide's rule #5 exception).
        void EnsureInstance(GameObject prefab)
        {
            if (instance != null && instancedFrom == prefab) return;
            if (instance != null) { Object.DestroyImmediate(instance); instance = null; roots = null; allSystems = null; }
            instancedFrom = prefab;
            fitSolved = false;
            if (prefab == null) return;

            EnsureUtil();
            // Object.Instantiate, NOT PrefabUtility.InstantiatePrefab: the latter drops the object into the user's
            // ACTIVE SCENE first (dirtying it, and stranding the VFX in their hierarchy if anything throws before
            // AddSingleGO moves it out). The preview needs no prefab connection.
            instance = Object.Instantiate(prefab);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.position = Vector3.zero;
            util.AddSingleGO(instance);

            // Only the TOP-MOST ParticleSystems are kept. Simulate(t, withChildren:true) already drives the whole
            // sub-tree, so calling it on every descendant as well re-ran each child a second time from t=0 with a
            // different parent state — visibly wrong timing on multi-system packs (every Vefects hero prefab has
            // 5–11 systems). Also silences an AudioSource riding along on the hero prefabs.
            var all = instance.GetComponentsInChildren<ParticleSystem>(true);
            var list = new System.Collections.Generic.List<ParticleSystem>();
            foreach (var ps in all)
            {
                var p = ps.transform.parent;
                bool nested = false;
                while (p != null) { if (p.GetComponent<ParticleSystem>() != null) { nested = true; break; } p = p.parent; }
                if (!nested) list.Add(ps);
            }
            roots = list.ToArray();
            allSystems = all;
            foreach (var a in instance.GetComponentsInChildren<AudioSource>(true)) a.enabled = false;
        }

        // Applies Speed/Scale and simulates the whole effect from t=0 up to `seconds`. Tint is deliberately NOT
        // applied here: the old version assigned it straight into every main-module startColor, which REPLACED the
        // pack's authored per-particle colours (the Vefects systems use random-between-two-colours gradients) with
        // one flat colour and was a large part of why the preview did not look like the pack. Tint is now a
        // multiply in the composite pass, so white leaves the prefab's own colours completely untouched.
        void ApplyDialsAndSimulate(PyrePlusLayer layer, float seconds)
        {
            if (instance == null) return;
            instance.transform.localScale = Vector3.one * Mathf.Max(0.001f, layer.playbackScale);
            for (int i = 0; i < roots.Length; i++)
            {
                var main = roots[i].main;
                main.simulationSpeed = Mathf.Max(0.001f, layer.playbackSpeed);
            }
            // restart:true replays deterministically from t=0 each time — Simulate has no incremental "advance
            // from where we left off" that's safe to call from arbitrary editor repaints, so a full re-simulate
            // to the target time is the correct (if not the cheapest) way to land on an exact scrub position.
            for (int i = 0; i < roots.Length; i++)
                roots[i].Simulate(Mathf.Max(0f, seconds), true, true, false);
        }

        // Frames the camera on the effect's ACTUAL extents instead of a hardcoded position. The old fixed camera
        // was tuned by hand and still clipped the tall _Smoke variants (whose smoke column rises many metres above
        // the pivot) while leaving the small ones tiny. Bounds are taken from the particle renderers at a
        // mid-loop simulation — where an effect is at its largest — and then cached, so the framing does not
        // jitter as the user scrubs.
        // Frames the camera on the effect's ACTUAL extents instead of a hardcoded position. The old fixed camera
        // was tuned by hand: it clipped the tall _Smoke variants and left the small ones tiny.
        //
        // Two things make a naive fit wrong on real packs, both verified against the Vefects hero prefabs:
        //   * ParticleSystemRenderer.bounds comes back wildly oversized (distortion/culling systems report boxes
        //     many times the visible effect), so real particle POSITIONS are used instead.
        //   * Even then, a max-extent fit frames the whole smoke column and stray sparks, shrinking the actual
        //     fire to a dot in the middle. So the radius is a PERCENTILE of the particle spread, not the maximum —
        //     the dense body of the effect fills the frame and the sparse tail is allowed to run off the top,
        //     which is exactly how the pack's own demo scene reads.
        // Solved once per prefab (at three mid-loop sample times, where an effect is at its largest) and cached,
        // so the framing does not jitter as the user scrubs. The Zoom dial scales the result.
        const float FitPercentile = 0.72f;

        void SolveFraming(PyrePlusLayer layer)
        {
            if (instance == null || allSystems == null || allSystems.Length == 0) return;
            if (fitSolved
                && Mathf.Approximately(fitSolvedScale, layer.playbackScale)
                && Mathf.Approximately(fitSolvedLoop, layer.playbackLoopDuration)) return;
            fitSolvedScale = layer.playbackScale;
            fitSolvedLoop = layer.playbackLoopDuration;

            float loop = Mathf.Max(0.01f, layer.playbackLoopDuration);
            var pts = new System.Collections.Generic.List<Vector4>();   // xyz = world position, w = particle radius
            foreach (float t in new[] { loop * 0.35f, loop * 0.6f, loop * 0.85f })
            {
                ApplyDialsAndSimulate(layer, t);
                foreach (var ps in allSystems)
                {
                    if (ps == null) continue;
                    int count = ps.particleCount;
                    if (count <= 0) continue;
                    if (particleBuf.Length < count) particleBuf = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(count)];
                    count = ps.GetParticles(particleBuf);
                    bool worldSpace = ps.main.simulationSpace == ParticleSystemSimulationSpace.World;
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 wp = worldSpace ? particleBuf[i].position
                                                : ps.transform.TransformPoint(particleBuf[i].position);
                        pts.Add(new Vector4(wp.x, wp.y, wp.z, particleBuf[i].GetCurrentSize(ps) * 0.5f));
                    }
                }
            }

            if (pts.Count == 0)
            {
                fitCentre = new Vector3(0f, 1f, 0f);
                fitRadius = 2.4f;
                fitSolved = true;
                return;
            }

            // Centre on the MEDIAN particle, not the mean — a handful of far-flung sparks would drag a mean.
            var xs = new float[pts.Count]; var ys = new float[pts.Count];
            for (int i = 0; i < pts.Count; i++) { xs[i] = pts[i].x; ys[i] = pts[i].y; }
            System.Array.Sort(xs); System.Array.Sort(ys);
            fitCentre = new Vector3(xs[xs.Length / 2], ys[ys.Length / 2], 0f);

            var spread = new float[pts.Count];
            for (int i = 0; i < pts.Count; i++)
                spread[i] = Mathf.Max(Mathf.Abs(pts[i].x - fitCentre.x), Mathf.Abs(pts[i].y - fitCentre.y)) + pts[i].w;
            System.Array.Sort(spread);
            float radius = spread[Mathf.Clamp(Mathf.RoundToInt((spread.Length - 1) * FitPercentile), 0, spread.Length - 1)];
            radius = Mathf.Max(0.2f, radius * 1.15f);

            fitRadius = radius;
            fitSolved = true;
        }

        static RenderTexture Alloc(int w, int h, RenderTextureFormat fmt, FilterMode fm, int depthBits = 0)
        {
            var rt = new RenderTexture(Mathf.Max(1, w), Mathf.Max(1, h), depthBits, fmt)
            { filterMode = fm, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            rt.Create();
            return rt;
        }

        // Bloom + exposure + tint + ACES, matched to the pack's own Volume profile. Source and destination are
        // both HDR/graded RTs owned by this class.
        void Grade(RenderTexture src, RenderTexture dst, PyrePlusLayer layer)
        {
            var mat = PostMat;
            if (mat == null) { Graphics.Blit(src, dst); return; }

            const float threshold = 1.0f, knee = 0.6f;
            mat.SetVector(IdFilter, new Vector4(threshold, threshold * knee, 1f / (4f * threshold * knee + 0.0001f), 0f));
            mat.SetVector(IdParams, new Vector4(Mathf.Max(0f, layer.playbackGlow), 1f, 0f, 0f));
            mat.SetVector(IdTint, layer.playbackTint);
            mat.SetFloat(IdSample, 1f);

            int w = src.width / 2, h = src.height / 2;
            var chain = new RenderTexture[BloomIterations];
            int levels = 0;
            RenderTexture cur = null;
            for (int i = 0; i < BloomIterations && w >= 4 && h >= 4; i++)
            {
                var rt = RenderTexture.GetTemporary(w, h, 0, src.format);
                rt.filterMode = FilterMode.Bilinear;
                if (i == 0) Graphics.Blit(src, rt, mat, PassPrefilter);
                else Graphics.Blit(cur, rt, mat, PassDown);
                chain[levels++] = rt;
                cur = rt;
                w /= 2; h /= 2;
            }

            // Walk back up, adding each level we captured on the way down — this is the "scatter" that gives a
            // wide soft halo rather than one tight ring.
            for (int i = levels - 2; i >= 0; i--)
            {
                var target = RenderTexture.GetTemporary(chain[i].width, chain[i].height, 0, src.format);
                target.filterMode = FilterMode.Bilinear;
                mat.SetTexture(IdBloomTex, chain[i]);
                Graphics.Blit(cur, target, mat, PassUp);
                cur = target;
                RenderTexture.ReleaseTemporary(chain[i]);
                chain[i] = target;
            }

            mat.SetTexture(IdBloomTex, cur != null ? (Texture)cur : Texture2D.blackTexture);
            Graphics.Blit(src, dst, mat, PassComposite);

            // Unbind before releasing: postMat is persistent, so leaving _BloomTex pointing at a pooled RT means
            // the material would hold a texture the pool destroys a few frames later.
            mat.SetTexture(IdBloomTex, Texture2D.blackTexture);
            for (int i = 0; i < levels; i++)
                if (chain[i] != null) { chain[i].filterMode = FilterMode.Point; RenderTexture.ReleaseTemporary(chain[i]); }
        }

        // Renders one frame at the given scrub position (seconds into the loop) and returns the resulting
        // texture — either the graded 3D render, or (pixelated == true) a point-filtered downsample of it at the
        // requested grid resolution. Caller does NOT own/destroy the returned texture.
        //
        // ASPECT: the pixel grid is sized to the SAME aspect as the render, not forced square. Forcing a square
        // grid was the "squashed into one line" bug the user reported — a wide preview island was being squeezed
        // into a square grid and then drawn back out with ScaleToFit, compressing the whole effect horizontally.
        public Texture Render(PyrePlusLayer layer, Rect viewSize, bool pixelated, int pixelGrid)
        {
            EnsureInstance(layer.playbackPrefab);
            if (instance == null) return null;

            EnsureUtil();
            int w = Mathf.Clamp(Mathf.RoundToInt(viewSize.width), 8, 2048);
            int h = Mathf.Clamp(Mathf.RoundToInt(viewSize.height), 8, 2048);

            SolveFraming(layer);
            util.cameraFieldOfView = 40f;
            // Zoom is applied here, not baked into the solve, so dragging it re-frames without re-simulating.
            // The fit has to satisfy BOTH axes: the 40 degree field of view is VERTICAL, so on a preview island
            // narrower than it is tall the horizontal half-angle is the tighter constraint and a vertical-only fit
            // crops the effect off at the sides (verified on a 200x480 island). Take whichever distance is larger.
            float aspect = h > 0 ? w / (float)h : 1f;
            float vHalf = 20f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * Mathf.Max(0.01f, aspect));
            float dist = fitRadius / Mathf.Tan(Mathf.Min(vHalf, hHalf)) / Mathf.Max(0.05f, layer.playbackZoom);
            util.camera.transform.position = fitCentre + new Vector3(0f, 0f, -dist);
            util.camera.transform.LookAt(fitCentre);

            ApplyDialsAndSimulate(layer, layer.playbackScrub01 * Mathf.Max(0.01f, layer.playbackLoopDuration));

            var prevActive = RenderTexture.active;
            // BeginPreview/EndPreview must wrap the render even though we do NOT use the texture EndPreview
            // returns: it is what binds the preview scene to the camera and enables the preview lights. Rendering
            // util.camera directly without it produced a completely black frame (verified). The camera's target is
            // then swapped to our own HDR buffer, because BeginPreview's own buffer is LDR and the pack's particles
            // are authored far above 1 — clamping them there leaves nothing for bloom to find.
            util.BeginPreview(new Rect(0, 0, w, h), GUIStyle.none);
            var beginTarget = util.camera.targetTexture;
            // try/finally, NOT a bare sequence: if the render throws (a VFX prefab with a broken or
            // mismatched-pipeline material is the realistic trigger) the camera would keep our RT as its target,
            // EndPreview would never run, and PreviewRenderUtility would stay "begun" — after which every later
            // repaint draws into a stale active RT and the rest of the window silently stops rendering. One bad
            // prefab must not be able to poison the window permanently.
            try
            {
                // The HDR buffer MUST match BeginPreview's own target size, not the requested point size: on a
                // high-DPI editor BeginPreview allocates at pixelsPerPoint scale and leaves the camera's viewport set
                // for THAT size, so a smaller substitute target gets rendered into as a clipped sub-rect (verified —
                // it showed as a hard-edged box cropping the effect).
                int rw = beginTarget != null ? beginTarget.width : w;
                int rh = beginTarget != null ? beginTarget.height : h;
                if (sceneRT == null || sceneW != rw || sceneH != rh)
                {
                    if (sceneRT != null) { sceneRT.Release(); Object.DestroyImmediate(sceneRT); }
                    if (gradedRT != null) { gradedRT.Release(); Object.DestroyImmediate(gradedRT); }
                    // 24-bit depth: a camera rendering into a depth-less RT under URP produces nothing at all.
                    sceneRT = Alloc(rw, rh, RenderTextureFormat.ARGBHalf, FilterMode.Bilinear, 24);
                    gradedRT = Alloc(rw, rh, RenderTextureFormat.ARGB32, FilterMode.Bilinear);
                    sceneW = rw; sceneH = rh;
                }
                util.camera.targetTexture = sceneRT;
                util.camera.Render();
            }
            finally
            {
                util.camera.targetTexture = beginTarget;
                util.EndPreview();
                RenderTexture.active = prevActive;
            }

            if (sceneRT == null || gradedRT == null) return null;
            Grade(sceneRT, gradedRT, layer);

            if (!pixelated)
            {
                // Graphics.Blit leaves its destination bound as RenderTexture.active, and this whole method runs
                // inside an IMGUI Repaint callback (PyrePlusWindow.Preview.cs's DrawPlayback3DPreview) — under URP,
                // leaving the wrong RT active here corrupts every GUILayout call drawn AFTER the preview in the
                // same pass (confirmed live: the transport row / backdrop controls silently stopped rendering).
                RenderTexture.active = prevActive;
                return gradedRT;
            }

            // Pixel grid, aspect-preserved: the LONG edge gets `pixelGrid` cells, the short edge gets
            // proportionally fewer, so a square effect stays square.
            int grid = Mathf.Clamp(pixelGrid, 8, 128);
            int pw, ph;
            if (w >= h) { pw = grid; ph = Mathf.Max(4, Mathf.RoundToInt(grid * (h / (float)w))); }
            else { ph = grid; pw = Mathf.Max(4, Mathf.RoundToInt(grid * (w / (float)h))); }

            if (pixelRT == null || pixelRTW != pw || pixelRTH != ph)
            {
                if (pixelRT != null) { pixelRT.Release(); Object.DestroyImmediate(pixelRT); }
                pixelRT = Alloc(pw, ph, RenderTextureFormat.ARGB32, FilterMode.Point);
                pixelRTW = pw; pixelRTH = ph;
            }

            // PROGRESSIVE downsample, halving at a time, instead of one big Blit straight to the grid. A single
            // bilinear Blit from ~936x576 down to 48x30 only reads a 2x2 neighbourhood per output cell out of the
            // ~19x19 it covers, so most of the frame — every spark, every flame lick — is simply never sampled and
            // the result is a soft, aliased blob that flickers as you scrub. Halving repeatedly averages the whole
            // block, which is what makes each pixel cell honestly represent what is under it.
            RenderTexture src = gradedRT;
            gradedRT.filterMode = FilterMode.Bilinear;
            RenderTexture step = null;
            int cw = gradedRT.width, ch = gradedRT.height;
            while (cw / 2 >= pw && ch / 2 >= ph && cw > 2 && ch > 2)
            {
                cw /= 2; ch /= 2;
                var next = RenderTexture.GetTemporary(cw, ch, 0, RenderTextureFormat.ARGB32);
                next.filterMode = FilterMode.Bilinear;
                Graphics.Blit(src, next);
                // filterMode restored to GetTemporary's default before release, so the next caller that pulls this
                // same-sized RT out of the pool does not silently inherit Bilinear.
                if (step != null) { step.filterMode = FilterMode.Point; RenderTexture.ReleaseTemporary(step); }
                step = next; src = next;
            }

            var quant = PostMat;
            if (quant != null)
            {
                quant.SetVector(IdParams, new Vector4(Mathf.Max(0f, layer.playbackGlow), 1f,
                                                      layer.playbackPixelLevels >= 2 ? layer.playbackPixelLevels : 0f, 0f));
                Graphics.Blit(src, pixelRT, quant, PassQuantise);
            }
            else Graphics.Blit(src, pixelRT);

            if (step != null) { step.filterMode = FilterMode.Point; RenderTexture.ReleaseTemporary(step); }
            // MUST restore: Graphics.Blit leaves its destination bound as RenderTexture.active and this runs
            // inside an IMGUI Repaint (see the live-mode branch above for the failure it caused).
            RenderTexture.active = prevActive;
            return pixelRT;
        }

        // The pixel grid the last Render() actually produced. The requested grid size is only the LONG edge; the
        // short edge follows the preview's aspect, so the window's status label has to read these rather than print
        // grid x grid, which would claim a square buffer that does not exist.
        public int LastPixelWidth => pixelRTW;
        public int LastPixelHeight => pixelRTH;

        public void Dispose()
        {
            if (instance != null) { Object.DestroyImmediate(instance); instance = null; }
            if (sceneRT != null) { sceneRT.Release(); Object.DestroyImmediate(sceneRT); sceneRT = null; }
            if (gradedRT != null) { gradedRT.Release(); Object.DestroyImmediate(gradedRT); gradedRT = null; }
            if (pixelRT != null) { pixelRT.Release(); Object.DestroyImmediate(pixelRT); pixelRT = null; }
            if (postMat != null) { Object.DestroyImmediate(postMat); postMat = null; }
            if (util != null) { util.Cleanup(); util = null; }
        }
    }
}
