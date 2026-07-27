// ZuiSwatchBinding.cs
// Drives a target's colour from a ZuiSwatchRef, live — in the editor AND at runtime. Watches the
// swatch's palette Version and re-pushes only when it changes (or the resolved colour changes), so a
// swatch edit repaints every bound target with no per-user wiring and no per-frame cost beyond a cheap
// resolve + compare. This is the general "swatches update at runtime" mechanism; it needs NO shader
// (the shader path is only for the gradient/ramp/cycling case).

using UnityEngine;

[ExecuteAlways]
[AddComponentMenu("Laubrary/ZUI/Swatch Binding")]
public class ZuiSwatchBinding : MonoBehaviour
{
    public enum TargetKind
    {
        SpriteRenderer,          // pushes to SpriteRenderer.color
        RendererMaterialColor,   // pushes to a Renderer's material colour via a MaterialPropertyBlock (no instancing)
    }

    [Tooltip("Where to push the resolved colour.")]
    public TargetKind target = TargetKind.SpriteRenderer;

    [Tooltip("The swatch (or inline colour) that drives the target.")]
    public ZuiSwatchRef swatch = new ZuiSwatchRef(Color.white);

    [Tooltip("Shader colour property to set when Target = Renderer material colour. URP: _BaseColor; built-in: _Color.")]
    public string materialProperty = "_BaseColor";

    SpriteRenderer _sr;
    Renderer _rend;
    MaterialPropertyBlock _mpb;
    int _propId = -1;
    int _lastVersion = int.MinValue;
    Color _lastColor;
    bool _force = true;

    void OnEnable()   { _force = true; }
    void OnValidate() { _force = true; _propId = -1; _sr = null; _rend = null; }

    void LateUpdate() => Apply();

    void Apply()
    {
        int v = swatch.palette != null ? swatch.palette.Version : -1;
        Color c = swatch.Resolve();
        if (!_force && v == _lastVersion && c == _lastColor) return;
        _force = false; _lastVersion = v; _lastColor = c;

        switch (target)
        {
            case TargetKind.SpriteRenderer:
                if (_sr == null) _sr = GetComponent<SpriteRenderer>();
                if (_sr != null) _sr.color = c;
                break;

            case TargetKind.RendererMaterialColor:
                if (_rend == null) _rend = GetComponent<Renderer>();
                if (_rend == null) break;
                if (_mpb == null) _mpb = new MaterialPropertyBlock();
                if (_propId < 0)
                    _propId = Shader.PropertyToID(string.IsNullOrEmpty(materialProperty) ? "_BaseColor" : materialProperty);
                _rend.GetPropertyBlock(_mpb);
                _mpb.SetColor(_propId, c);
                _rend.SetPropertyBlock(_mpb);
                break;
        }
    }
}
