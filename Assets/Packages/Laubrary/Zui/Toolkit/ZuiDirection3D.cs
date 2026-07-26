// ZuiDirection3D — a compact, reusable "3D direction / orientation" control.
//
// Across the tools the same shape kept recurring: a direction on a SPHERE — a yaw (azimuth) + a pitch
// (elevation), sometimes with a distance — authored as two (or three) separate 1D sliders, or as a flat 2D
// pad with no sense of the sphere it lives on. PyrePlus's Gem/solid KEY LIGHT is the prime example
// (gemLightYaw / gemLightPitch / gemLightDistance). This packages that primitive once, in ZUI, so every tool
// gets the same control: a small DRAGGABLE LIT SPHERE that shows the direction as real 3D shading (the lit
// hotspot IS the readout), numeric fallback fields for precise entry, an optional distance, and a larger
// preview that opens on hover (a non-modal Z.Popover) or pins open.
//
// It edits the SAME plain float fields the old sliders did (yaw/pitch/distance) and fires OnChanged(y,p,d)
// once per edit — the caller wraps that in its Undo/Dirty helper. No render math lives here: the sphere is a
// cheap reference GIZMO (a Blinn-Phong point light on a unit sphere, matching the renderer's light-position
// convention so it reads faithfully), not the tool's actual output.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiDirection3D : VisualElement
    {
        public class Options
        {
            public float yawMin = -180f, yawMax = 180f;     // azimuth range (left/right)
            public float pitchMin = -85f, pitchMax = 85f;   // elevation range (below/above the horizon)
            public bool showDistance = false;               // expose the 3rd axis (light distance, in radii)
            public float distanceMin = 1.5f, distanceMax = 8f;
            public float gizmoSize = 60f;                   // the inline draggable sphere, px
            public bool showNumericFields = true;           // the Yaw/Pitch/(Dist) fallback column
            public bool enableHoverPreview = true;          // open a larger preview while the pointer is over the control
            public float previewSize = 140f;                // the hover/pinned preview sphere, px
            public Color sphereColor = new Color(0.70f, 0.77f, 0.90f);   // the reference sphere's base colour
        }

        readonly Options _opt;
        readonly string _tooltip;

        float _yaw, _pitch, _distance;

        VisualElement _gizmo;
        Texture2D _gizmoTex;
        FloatField _yawField, _pitchField, _distField;
        ZuiToggleButton _pin;
        bool _pinned;
        bool _dragging;
        bool _hovering;

        ZuiPopover _preview;
        VisualElement _previewSphere;
        Texture2D _previewTex;
        Label _previewVals;

        /// Fired once per edit (drag step or numeric commit) with the current (yaw, pitch, distance). Distance
        /// is passed even when showDistance is false (it stays at the constructed value) so the callback shape
        /// is stable; ignore it there.
        public event Action<float, float, float> OnChanged;

        public float Yaw => _yaw;
        public float Pitch => _pitch;
        public float Distance => _distance;

        public ZuiDirection3D(float yaw, float pitch, float distance, Options options, string tooltip)
        {
            _opt = options ?? new Options();
            _tooltip = tooltip;
            _yaw = Mathf.Clamp(yaw, _opt.yawMin, _opt.yawMax);
            _pitch = Mathf.Clamp(pitch, _opt.pitchMin, _opt.pitchMax);
            _distance = Mathf.Clamp(distance, _opt.distanceMin, _opt.distanceMax);

            AddToClassList("zui-dir3d");
            this.tooltip = tooltip;
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.FlexStart;

            BuildGizmo();
            if (_opt.showNumericFields) Add(BuildNumericColumn());

            RegisterCallback<DetachFromPanelEvent>(_ => Cleanup());

            if (_opt.enableHoverPreview)
            {
                RegisterCallback<PointerEnterEvent>(_ => { _hovering = true; OpenPreview(); });
                RegisterCallback<PointerLeaveEvent>(_ => { _hovering = false; if (!_pinned && !_dragging) ClosePreview(); });
            }

            RenderGizmo();
        }

        // ── the draggable lit-sphere gizmo ────────────────────────────────────────────
        void BuildGizmo()
        {
            _gizmo = new VisualElement { tooltip = _tooltip };
            _gizmo.AddToClassList("zui-dir3d__gizmo");
            _gizmo.style.width = _opt.gizmoSize;
            _gizmo.style.height = _opt.gizmoSize;

            int px = Mathf.Max(16, Mathf.RoundToInt(_opt.gizmoSize));
            _gizmoTex = MakeTex(px);
            _gizmo.style.backgroundImage = new StyleBackground(_gizmoTex);

            // Absolute linear drag (the ZuiPad idiom, proven and predictable): x over the box → yaw across its
            // range, y over the box → pitch (up = higher). The sphere re-shades live, so the lit hotspot and the
            // baked light marker orbit the ball as you drag — the 3D readout the flat pad never gave.
            _gizmo.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                _dragging = true;
                _gizmo.CapturePointer(e.pointerId);
                SetAnglesFromLocal(e.localPosition);
                e.StopPropagation();
            });
            _gizmo.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_gizmo.HasPointerCapture(e.pointerId)) return;
                SetAnglesFromLocal(e.localPosition);
                e.StopPropagation();
            });
            _gizmo.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!_gizmo.HasPointerCapture(e.pointerId)) return;
                _gizmo.ReleasePointer(e.pointerId);
                _dragging = false;
                if (!_pinned && !_hovering) ClosePreview();
            });
            Add(_gizmo);
        }

        void SetAnglesFromLocal(Vector3 local)
        {
            var r = _gizmo.contentRect;
            if (!(r.width > 0f) || !(r.height > 0f)) return;
            float tx = Mathf.Clamp01(local.x / r.width);
            float ty = Mathf.Clamp01(local.y / r.height);
            _yaw = (float)Math.Round(Mathf.Lerp(_opt.yawMin, _opt.yawMax, tx), 3);
            _pitch = (float)Math.Round(Mathf.Lerp(_opt.pitchMax, _opt.pitchMin, ty), 3);   // top = highest pitch
            _yawField?.SetValueWithoutNotify(_yaw);
            _pitchField?.SetValueWithoutNotify(_pitch);
            RenderGizmo();
            RenderPreview();
            OnChanged?.Invoke(_yaw, _pitch, _distance);
        }

        // ── numeric fallback column (Yaw / Pitch / Distance) + pin toggle ─────────────
        VisualElement BuildNumericColumn()
        {
            var col = new VisualElement();
            col.AddToClassList("zui-dir3d__nums");
            col.style.marginLeft = 8f;

            _yawField = Z.Float(_yaw, "Yaw (azimuth) in degrees — which side the direction comes FROM (left/right). "
                + "Drag the sphere or type a value.", v =>
            {
                _yaw = Mathf.Clamp(v, _opt.yawMin, _opt.yawMax);
                RenderGizmo(); RenderPreview();
                OnChanged?.Invoke(_yaw, _pitch, _distance);
            }, 54f);
            col.Add(Z.Field("Yaw", "Yaw (azimuth), degrees — left/right around the sphere.", _yawField));

            _pitchField = Z.Float(_pitch, "Pitch (elevation) in degrees — the direction's height (negative = from "
                + "below/behind). Drag the sphere or type a value.", v =>
            {
                _pitch = Mathf.Clamp(v, _opt.pitchMin, _opt.pitchMax);
                RenderGizmo(); RenderPreview();
                OnChanged?.Invoke(_yaw, _pitch, _distance);
            }, 54f);
            col.Add(Z.Field("Pitch", "Pitch (elevation), degrees — up/down.", _pitchField));

            if (_opt.showDistance)
            {
                _distField = Z.Float(_distance, "Distance from the surface, as a multiple of its radius — the "
                    + "direction's 3rd axis. Closer = a tighter, brighter hotspot; farther = flatter, more even.",
                    v =>
                {
                    _distance = Mathf.Clamp(v, _opt.distanceMin, _opt.distanceMax);
                    _distField.SetValueWithoutNotify(_distance);
                    RenderGizmo(); RenderPreview();
                    OnChanged?.Invoke(_yaw, _pitch, _distance);
                }, 54f);
                col.Add(Z.Field("Dist", "Distance, in radii — the 3rd axis.", _distField));
            }

            if (_opt.enableHoverPreview)
            {
                _pin = Z.ToggleButton("Pin preview", "Keep the larger 3D preview open (it otherwise shows only "
                    + "while the pointer is over this control).", _pinned, on =>
                {
                    _pinned = on;
                    if (_pinned) OpenPreview();
                    else if (!_hovering) ClosePreview();
                });
                _pin.style.marginTop = 3f;
                col.Add(_pin);
            }
            return col;
        }

        // ── the hover / pinned preview (a non-modal Z.Popover of a larger lit sphere) ──
        void OpenPreview()
        {
            if (!_opt.enableHoverPreview || (_preview != null && _preview.IsOpen)) return;
            _preview = Z.Popover(_gizmo, panel =>
            {
                panel.Add(Z.Text("Direction preview", ZuiText.Small,
                    "A reference lit sphere showing the direction this control sets — the lit hotspot is where it "
                    + "points from. Cheap gizmo, not the tool's actual output."));

                _previewSphere = new VisualElement();
                _previewSphere.AddToClassList("zui-dir3d__gizmo");
                _previewSphere.style.width = _opt.previewSize;
                _previewSphere.style.height = _opt.previewSize;
                int pp = Mathf.Max(32, Mathf.RoundToInt(_opt.previewSize));
                _previewTex = MakeTex(pp);
                _previewSphere.style.backgroundImage = new StyleBackground(_previewTex);
                panel.Add(_previewSphere);

                _previewVals = Z.Text(ValsText(), ZuiText.Subtle, "The current values.");
                panel.Add(_previewVals);
            }, new ZuiPopover.Options
            {
                modal = false,                 // float above the tool without stealing its input
                dismissOnOutsideClick = false,
                dismissOnEsc = false,
                minWidth = _opt.previewSize + 8f,
                onClosed = () => { DestroyTex(ref _previewTex); _previewSphere = null; _previewVals = null; }
            });
            RenderPreview();
        }

        void ClosePreview() => _preview?.Close();

        void RenderPreview()
        {
            if (_previewSphere == null || _previewTex == null) return;
            RenderSphere(_previewTex, _yaw, _pitch, _distance, _opt);
            _previewSphere.MarkDirtyRepaint();
            if (_previewVals != null) _previewVals.text = ValsText();
        }

        string ValsText() => _opt.showDistance
            ? $"yaw {_yaw:0.#}°   pitch {_pitch:0.#}°   dist {_distance:0.##}"
            : $"yaw {_yaw:0.#}°   pitch {_pitch:0.#}°";

        void RenderGizmo()
        {
            if (_gizmoTex == null) return;
            RenderSphere(_gizmoTex, _yaw, _pitch, _distance, _opt);
            _gizmo?.MarkDirtyRepaint();
        }

        // ── the shared cheap sphere shader (Blinn-Phong point light on a unit sphere) ──
        // The light direction / position convention matches PyrePlusRenderer's Gem/solid light EXACTLY:
        //   Ldir = (cos p · sin y, sin p, cos p · cos y),  lightPos = Ldir · distance,  range = distance + 1.2
        // so the preview reads faithfully against the tool's real lighting. This is a GIZMO, not that renderer.
        static void RenderSphere(Texture2D tex, float yaw, float pitch, float distance, Options opt)
        {
            int size = tex.width;
            float ly = yaw * Mathf.Deg2Rad, lp = pitch * Mathf.Deg2Rad;
            float cp = Mathf.Cos(lp), sp = Mathf.Sin(lp);
            Vector3 ldir = new Vector3(cp * Mathf.Sin(ly), sp, cp * Mathf.Cos(ly));   // unit
            Vector3 lightPos = ldir * distance;
            float range = distance + 1.2f, range2 = range * range;
            Vector3 view = new Vector3(0f, 0f, 1f);
            Color baseCol = opt.sphereColor;

            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size * 2f - 1f;         // row 0 = bottom → v = -1 (down); top → +1 (up)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float rr = u * u + v * v;
                    int idx = y * size + x;
                    if (rr > 1f) { px[idx] = new Color32(0, 0, 0, 0); continue; }   // outside the sphere: clear

                    float sz = Mathf.Sqrt(Mathf.Max(0f, 1f - rr));
                    Vector3 N = new Vector3(u, v, sz);          // surface normal on the unit sphere (radius 1)
                    Vector3 toL = lightPos - N;
                    float dist = toL.magnitude;
                    Vector3 L = toL / Mathf.Max(1e-4f, dist);
                    float atten = 1f / (1f + dist * dist / range2);
                    float ndl = Mathf.Max(0f, Vector3.Dot(N, L));
                    float diff = ndl * atten;
                    Vector3 H = (L + view).normalized;
                    float spec = ndl > 0f ? Mathf.Pow(Mathf.Max(0f, Vector3.Dot(N, H)), 40f) * atten : 0f;

                    float shade = 0.14f + 1.0f * diff;
                    float rimDark = Mathf.SmoothStep(1f, 0.86f, rr);   // subtle terminator at the silhouette
                    float rr2 = baseCol.r * shade * rimDark + spec * 0.9f;
                    float gg2 = baseCol.g * shade * rimDark + spec * 0.9f;
                    float bb2 = baseCol.b * shade * rimDark + spec * 0.95f;
                    px[idx] = new Color32(
                        (byte)(Mathf.Clamp01(rr2) * 255f),
                        (byte)(Mathf.Clamp01(gg2) * 255f),
                        (byte)(Mathf.Clamp01(bb2) * 255f), 255);
                }
            }

            // A small marker at the orthographic projection of the light direction — FILLED bright when it faces
            // us (z ≥ 0), a hollow dim ring when it is BEHIND the sphere (z < 0), so the back hemisphere reads
            // unambiguously (the shading alone goes flat there). Baked into the same texture, so it can never
            // disagree with the shading.
            {
                float mx = (ldir.x * 0.92f + 1f) * 0.5f * size;
                float my = (ldir.y * 0.92f + 1f) * 0.5f * size;
                bool front = ldir.z >= 0f;
                float rad = Mathf.Max(1.6f, size * 0.045f);
                Color32 hot = front ? new Color32(255, 245, 200, 255) : new Color32(150, 160, 180, 255);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float d = Mathf.Sqrt((x + 0.5f - mx) * (x + 0.5f - mx) + (y + 0.5f - my) * (y + 0.5f - my));
                        int idx = y * size + x;
                        if (px[idx].a == 0) continue;                    // never paint the marker off the sphere
                        if (front) { if (d <= rad) px[idx] = hot; }
                        else if (d <= rad && d >= rad - 1.6f) px[idx] = hot;   // hollow ring for a behind light
                    }
            }

            tex.SetPixels32(px);
            tex.Apply(false);
        }

        static Texture2D MakeTex(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            return t;
        }

        static void DestroyTex(ref Texture2D t)
        {
            if (t != null) { UnityEngine.Object.DestroyImmediate(t); t = null; }
        }

        void Cleanup()
        {
            _preview?.Close();
            _preview = null;
            DestroyTex(ref _gizmoTex);
            DestroyTex(ref _previewTex);
        }
    }
}
