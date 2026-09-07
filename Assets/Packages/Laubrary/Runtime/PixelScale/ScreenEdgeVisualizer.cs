using UnityEngine;

namespace Laubrary.PixelScale
{
    /// <summary>
    /// Draws where the game screen's edges fall, in world units, derived entirely from
    /// <see cref="PixelScaleProjectSettings"/> — no numbers of its own beyond an on/off toggle and the outer
    /// display resolution. Two rectangles, both centred on this GameObject's transform (typically the camera):
    ///
    /// - The CORE rectangle: the project's target resolution (default 320x200) at its pixels-per-unit.
    /// - The OUTER rectangle: the extra area a larger display (<see cref="outerResolution"/>, default a
    ///   1920x1080 Steam Deck-class screen) reveals around that core, via the project's own integer-upscale
    ///   rule (<see cref="PixelScale.IntegerUpscale"/>) — the same rule a real pixel-perfect camera setup
    ///   uses, so this stays consistent with what actually renders rather than a naive PPU scaling.
    ///
    /// Generic and reusable — not ProtoGuy-specific. Originally scoped as a Life Manager task (T-0007,
    /// "screen-border visualiser ... for Mirage, Cartographer and other tools"); this is that helper.
    ///
    /// Draws via Gizmos in the editor (Scene view, always available) and via runtime <see cref="LineRenderer"/>s
    /// while playing (visible in the Game view too), mirroring the pattern already used by
    /// <c>Laubrary.Demos.ProtoGuyDemo.FireDirectionVisualizer</c> for persistent debug lines.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScreenEdgeVisualizer : MonoBehaviour
    {
        [Tooltip("Turn the whole visualiser off without removing the component.")]
        public bool enableVisualizer = true;

        [Tooltip("The larger display the core canvas is shown within (e.g. a Steam Deck). Everything else is derived from PixelScaleProjectSettings.")]
        public Vector2Int outerResolution = new Vector2Int(1920, 1080);

        [Tooltip("Colour of the core (target resolution) rectangle.")]
        public Color coreColor = Color.yellow;

        [Tooltip("Colour of the outer (larger display) rectangle.")]
        public Color outerColor = new Color(1f, 0.5f, 0f);

        [Tooltip("Line width in world units for the runtime (Play-mode) lines. Not used by the editor Gizmos drawing.")]
        [Min(0.001f)] public float lineWidth = 0.03f;

        [Tooltip("Distance in front of this transform (along its forward axis) the runtime lines are drawn at. Needed because a rectangle drawn exactly at the camera's own position sits behind its near clip plane and never renders; irrelevant for an orthographic camera's apparent size.")]
        public float lineDistance = 1f;

        Transform _container;
        LineRenderer _coreLine, _outerLine;
        Material _lineMaterial;

        void OnDisable() => DestroyRuntimeLines();

        void LateUpdate()
        {
            if (!Application.isPlaying) return;

            if (!enableVisualizer)
            {
                DestroyRuntimeLines();
                return;
            }

            if (_container == null) BuildRuntimeLines();
            var (core, outer) = ComputeSizes();
            SetRect(_coreLine, core);
            SetRect(_outerLine, outer);
        }

        (Vector2 core, Vector2 outer) ComputeSizes()
        {
            var settings = PixelScaleProjectSettings.Instance;
            var ppu = Mathf.Max(1, settings.pixelsPerUnit);
            var core = new Vector2(settings.targetResolution.x / (float)ppu, settings.targetResolution.y / (float)ppu);

            var upscale = PixelScale.IntegerUpscale(Mathf.Max(1, outerResolution.y), settings);
            var outer = new Vector2(outerResolution.x / (float)upscale / ppu, outerResolution.y / (float)upscale / ppu);
            return (core, outer);
        }

        void BuildRuntimeLines()
        {
            _container = new GameObject("~ScreenEdgeVisualizer").transform;
            _container.SetParent(transform, false);
            _coreLine = MakeLine("CoreRect", coreColor);
            _outerLine = MakeLine("OuterRect", outerColor);
        }

        void DestroyRuntimeLines()
        {
            if (_container == null) return;
            Destroy(_container.gameObject);
            _container = null;
            _coreLine = null;
            _outerLine = null;
        }

        LineRenderer MakeLine(string goName, Color color)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(_container, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.positionCount = 4;
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.numCornerVertices = 2;
            lr.material = LineMaterial();
            lr.startColor = color;
            lr.endColor = color;
            return lr;
        }

        Material LineMaterial()
        {
            if (_lineMaterial == null) _lineMaterial = new Material(Shader.Find("Sprites/Default"));
            return _lineMaterial;
        }

        void SetRect(LineRenderer lr, Vector2 size)
        {
            if (lr == null) return;
            Vector3 c = transform.position + transform.forward * lineDistance;
            float hx = size.x * 0.5f, hy = size.y * 0.5f;
            lr.SetPosition(0, c + new Vector3(-hx, -hy, 0f));
            lr.SetPosition(1, c + new Vector3(hx, -hy, 0f));
            lr.SetPosition(2, c + new Vector3(hx, hy, 0f));
            lr.SetPosition(3, c + new Vector3(-hx, hy, 0f));
        }

        void OnDrawGizmos()
        {
            if (!enableVisualizer) return;
            var (core, outer) = ComputeSizes();
            Gizmos.color = outerColor;
            Gizmos.DrawWireCube(transform.position, new Vector3(outer.x, outer.y, 0f));
            Gizmos.color = coreColor;
            Gizmos.DrawWireCube(transform.position, new Vector3(core.x, core.y, 0f));
        }
    }
}
