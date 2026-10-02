using UnityEngine;
using Laubrary.Larder;

/// Demo-only: click a Ware to shoot it. Left-mouse fires a ray into the 2D scene; whatever ShelfWare is under the
/// cursor takes a Hit at that world point, so products crack apart stage by stage and burst in their own colours.
/// Not shipped with the package.
public class LarderDemoClickShooter : MonoBehaviour
{
    public Camera cam;

    void Awake()
    {
        if (cam == null) cam = Camera.main;
    }

    void Update()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        var c = cam != null ? cam : Camera.main;
        if (c == null) return;

        Vector3 wp = c.ScreenToWorldPoint(Input.mousePosition);
        wp.z = 0f;

        var hit = Physics2D.OverlapPoint(wp);
        if (hit == null) return;

        var ware = hit.GetComponentInParent<ShelfWare>();
        if (ware != null) ware.Hit(wp);
    }
}
