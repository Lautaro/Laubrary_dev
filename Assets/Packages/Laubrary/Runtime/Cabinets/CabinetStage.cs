using UnityEngine;

namespace Laubrary.Cabinets
{
    /// Puts a scene INSIDE a cabinet. Drop one on the camera, pick a Cabinet, and the framing is correct —
    /// in the Game view, in edit mode, before anyone presses play.
    ///
    /// [ExecuteAlways] is the whole point rather than a convenience. A presentation contract you can only
    /// see by entering play mode is one you will author blind, and "it looked right in the Scene view" is
    /// how a level ends up built for the wrong screen. Authoring and running must show the same picture.
    ///
    /// This component is also what <see cref="Cabinet.Current"/> finds, so putting one in a scene is what
    /// tells every OTHER tool which cabinet this scene is in. That is the "tools use it automatically"
    /// mechanism: one component, and nothing else has to be told anything.
    [ExecuteAlways]
    [AddComponentMenu("Laubrary/Cabinet Stage")]
    [DisallowMultipleComponent]
    public class CabinetStage : MonoBehaviour
    {
        [Tooltip("The presentation contract this scene runs in. Empty means the scene keeps whatever camera " +
                 "settings it already had — the stage never forces a default on you.")]
        public Cabinet cabinet;

        [Tooltip("Camera to drive. Left empty, it uses the Camera on this same object, then the main camera.")]
        public Camera target;

        [Tooltip("Re-apply every frame. Only needed when something else fights the camera for control of " +
                 "its size; otherwise it just costs frames for no benefit.")]
        public bool continuous;

        CabinetLook appliedLook;
        static bool warnedNoPixelPerfect;

        public Camera ResolveCamera()
        {
            if (target != null) return target;
            var own = GetComponent<Camera>();
            return own != null ? own : Camera.main;
        }

        void OnEnable() { Cabinet.Invalidate(); Apply(); }
        void OnDisable() { Cabinet.Invalidate(); ReleaseLook(); }
        void OnValidate() { Cabinet.Invalidate(); Apply(); }
        void Update() { if (continuous) Apply(); }

        /// Push the cabinet's geometry onto the camera. Safe to call repeatedly — it is idempotent, which is
        /// what lets OnValidate use it while the user drags a field.
        public void Apply()
        {
            if (cabinet == null) return;
            var cam = ResolveCamera();
            if (cam == null) return;

            if (cabinet.IsFlat)
            {
                cam.orthographic = true;
                cam.orthographicSize = cabinet.OrthographicSize;
            }
            else
            {
                cam.orthographic = false;
                cam.fieldOfView = cabinet.fieldOfView;
            }

            ApplyWindowFit(cam);

            if (!ReferenceEquals(appliedLook, cabinet.look))
            {
                if (appliedLook != null) appliedLook.Remove(cam);
                appliedLook = cabinet.look;
            }
            if (appliedLook != null) appliedLook.Apply(cam);
        }

        void ReleaseLook()
        {
            if (appliedLook == null) return;
            var cam = ResolveCamera();
            if (cam != null) appliedLook.Remove(cam);
            appliedLook = null;
        }

        /// Window fit is the one part that cannot be done with Camera alone: honouring an integer upscale
        /// needs a render target sized to the virtual canvas. URP ships exactly that as PixelPerfectCamera,
        /// so we drive it WHEN PRESENT — by reflection, deliberately, so that Laubrary keeps zero dependency
        /// on any render pipeline. A package that hard-references URP cannot be used by a project that is
        /// not on URP, and this feature is not worth that price.
        ///
        /// Without it, the orthographic size above still gives correct FRAMING (the right amount of world is
        /// visible); what you lose is the guarantee that pixels land on whole device pixels. That degradation
        /// is stated out loud once rather than silently pretended away.
        void ApplyWindowFit(Camera cam)
        {
            var ppcType = System.Type.GetType("UnityEngine.Rendering.Universal.PixelPerfectCamera, Unity.RenderPipelines.Universal.Runtime")
                       ?? System.Type.GetType("UnityEngine.U2D.PixelPerfectCamera, Unity.2D.PixelPerfect");
            if (ppcType == null) return;

            var ppc = cam.GetComponent(ppcType);
            if (ppc == null)
            {
                if (cabinet.windowFit != Cabinet.WindowFit.ExpandView && !warnedNoPixelPerfect)
                {
                    warnedNoPixelPerfect = true;
                    Debug.LogWarning(
                        $"[Cabinet] '{cabinet.name}' asks for {cabinet.windowFit}, which needs a " +
                        $"PixelPerfectCamera component on '{cam.name}' to enforce. Framing is still correct " +
                        $"({cabinet.WorldUnitsVisible.x:0.##} x {cabinet.WorldUnitsVisible.y:0.##} world units), " +
                        $"but pixels may land on fractional device pixels and shimmer when things move.", cam);
                }
                return;
            }

            Set(ppc, ppcType, "assetsPPU", cabinet.referencePpu);
            Set(ppc, ppcType, "refResolutionX", cabinet.virtualResolution.x);
            Set(ppc, ppcType, "refResolutionY", cabinet.virtualResolution.y);

            switch (cabinet.windowFit)
            {
                case Cabinet.WindowFit.IntegerOnly:
                    Set(ppc, ppcType, "upscaleRT", true);
                    Set(ppc, ppcType, "cropFrameX", true);
                    Set(ppc, ppcType, "cropFrameY", true);
                    Set(ppc, ppcType, "stretchFill", false);
                    break;
                case Cabinet.WindowFit.Letterbox:
                    Set(ppc, ppcType, "upscaleRT", false);
                    Set(ppc, ppcType, "cropFrameX", true);
                    Set(ppc, ppcType, "cropFrameY", true);
                    Set(ppc, ppcType, "stretchFill", false);
                    break;
                case Cabinet.WindowFit.ExpandView:
                    Set(ppc, ppcType, "upscaleRT", false);
                    Set(ppc, ppcType, "cropFrameX", false);
                    Set(ppc, ppcType, "cropFrameY", false);
                    Set(ppc, ppcType, "stretchFill", false);
                    break;
                case Cabinet.WindowFit.Stretch:
                    Set(ppc, ppcType, "cropFrameX", true);
                    Set(ppc, ppcType, "cropFrameY", true);
                    Set(ppc, ppcType, "stretchFill", true);
                    break;
            }

            Set(ppc, ppcType, "pixelSnapping", cabinet.pixelSnap);
        }

        /// Property-or-field, quietly. PixelPerfectCamera has moved members between properties and fields
        /// across Unity versions; a miss here degrades one setting rather than throwing.
        static void Set(object target, System.Type t, string member, object value)
        {
            var p = t.GetProperty(member);
            if (p != null && p.CanWrite) { p.SetValue(target, value); return; }
            var f = t.GetField(member);
            if (f != null) f.SetValue(target, value);
        }
    }
}
