using UnityEngine;
using UnityEngine.InputSystem;
using Laubrary.GoreLab;
using ZuiRuntime;

namespace Laubrary.GoreLabDemo
{
    /// <summary>
    /// Mouse and panel for the gore demo. Press, drag and release across an imp to wound it: slices and cuts follow the line you draw, shots treat it as
    /// the aim line. A plain click also works. Every marked body part can be hit; the mouse never chooses a part.
    /// </summary>
    public sealed class GoreLabDemoController : MonoBehaviour
    {
        enum Kind { Slice, Cut, Bullet, Shotgun, RemoveHead }
        static readonly string[] KindNames = { "Slice (cuts through)", "Cut (knife)", "Bullet", "Shotgun", "Remove head" };

        [Tooltip("The spawner that owns the imps.")]
        public GoreLabDemoSpawner spawner;

        [Tooltip("A click that is not a drag becomes a swipe this long (world units) for slice and cut, and an aim line this long for shots.")]
        public float clickLength = 2.4f;

        Kind _kind = Kind.Bullet;
        bool _straightOn = true;
        bool _dragging;
        Vector2 _down;
        Camera _cam;
        LineRenderer _line;
        Rect _panelRect;
        string _last = "";
        static readonly Color PanelBg = new Color(0.06f, 0.06f, 0.09f, 0.93f);

        void Start()
        {
            _cam = Camera.main;
            _line = new GameObject("Drag line").AddComponent<LineRenderer>();
            _line.transform.SetParent(transform);
            _line.material = new Material(Shader.Find("Sprites/Default"));
            _line.startColor = _line.endColor = new Color(1f, 1f, 1f, 0.85f);
            _line.startWidth = _line.endWidth = 0.06f;
            _line.positionCount = 2;
            _line.sortingOrder = 1000;
            _line.enabled = false;
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || _cam == null) return;
            Vector2 screen = mouse.position.ReadValue();
            Vector2 world = _cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_cam.transform.position.z));

            if (mouse.leftButton.wasPressedThisFrame && !OverPanel(screen)) { _dragging = true; _down = world; }
            if (_dragging)
            {
                _line.enabled = true;
                _line.SetPosition(0, _down);
                _line.SetPosition(1, world);
            }
            if (mouse.leftButton.wasReleasedThisFrame && _dragging)
            {
                _dragging = false;
                _line.enabled = false;
                Strike(_down, world);
            }
        }

        // The GUI panel works in top-left screen coordinates, the mouse in bottom-left.
        bool OverPanel(Vector2 screen) { return _panelRect.Contains(new Vector2(screen.x, Screen.height - screen.y)); }

        int CountAlive()
        {
            int n = 0;
            foreach (var b in spawner.bodies) if (b != null) n++;
            return n;
        }

        void Strike(Vector2 a, Vector2 b)
        {
            if ((b - a).magnitude < 0.3f)
            {
                // a click: slices and cuts get a short level swipe through the point, shots an aim line coming from the lower left
                bool swipe = _kind == Kind.Slice || _kind == Kind.Cut;
                Vector2 dir = swipe ? new Vector2(1f, 0.15f).normalized : new Vector2(1f, 0.6f).normalized;
                Vector2 at = b;
                a = swipe ? at - dir * clickLength * 0.5f : at - dir * clickLength;
                b = swipe ? at + dir * clickLength * 0.5f : at;
            }
            GoreBody target = FindImp(a, b);
            if (target == null) { _last = "Nothing under the line."; return; }
            bool hit = target.ApplyWound(MakeRecipe(), a, b);
            _last = hit ? target.name + ": " + KindNames[(int)_kind] : target.name + ": nothing visible changed (try another spot)";
        }

        // The rig's own tuned recipe of the chosen type (a copy, so the asset is never touched); a fresh default if the rig has none.
        IWoundRecipe MakeRecipe()
        {
            System.Type want;
            switch (_kind)
            {
                case Kind.Slice: want = typeof(SliceRecipe); break;
                case Kind.Cut: want = typeof(CutRecipe); break;
                case Kind.Shotgun: want = typeof(ShotgunRecipe); break;
                case Kind.RemoveHead: want = typeof(RemoveHeadRecipe); break;
                default: want = typeof(BulletRecipe); break;
            }
            IWoundRecipe made = null;
            var rig = spawner != null ? spawner.rig : null;
            if (rig != null && rig.recipes != null)
                foreach (var r in rig.recipes)
                    if (r != null && r.GetType() == want)
                    {
                        made = (IWoundRecipe)System.Activator.CreateInstance(want);
                        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(r), made);
                        break;
                    }
            if (made == null) made = (IWoundRecipe)System.Activator.CreateInstance(want);
            if (made is ShotgunRecipe sg) sg.straightOn = _straightOn;
            return made;
        }

        // The imp whose picture the line touches; the end of the line wins, so a shot lands where it was aimed.
        GoreBody FindImp(Vector2 a, Vector2 b)
        {
            for (int s = 10; s >= 0; s--)
            {
                Vector2 p = Vector2.Lerp(a, b, s / 10f);
                foreach (var body in spawner.bodies)
                {
                    if (body == null) continue;
                    var sr = body.GetComponentInChildren<SpriteRenderer>();
                    if (sr == null) continue;
                    var bounds = sr.bounds;
                    if (bounds.Contains(new Vector3(p.x, p.y, bounds.center.z))) return body;
                }
            }
            return null;
        }

        void OnGUI()
        {
            Rect rc = Zui.Panel(ZuiAnchor.TopLeft, 200f, 365f, PanelBg);
            float pad = UIScale.S(10f);
            _panelRect = new Rect(rc.x - pad, rc.y - pad, rc.width + pad * 2f, rc.height + pad * 2f);
            var s = new ZuiStack(rc);
            s.Header("GoreLab demo");
            s.Label("Damage type", 13f);
            for (int i = 0; i < KindNames.Length; i++)
                if (s.Toggle(KindNames[i], (int)_kind == i, 14f) && (int)_kind != i) _kind = (Kind)i;
            if (_kind == Kind.Shotgun) _straightOn = s.Toggle("Straight on", _straightOn, 14f);
            s.Space();
            s.Label("Imps: " + CountAlive(), 13f);
            if (s.Button("+  Add imp", 15f)) { spawner.Add(); }
            if (s.Button("-  Remove oldest", 15f)) { spawner.RemoveOldest(); }
            s.Space(4f);
            if (s.Button("Reset wounds", 15f)) { spawner.ResetAll(); _last = "Wounds cleared."; }
            s.Space(4f);
            s.Label("Drag across an imp. Slice and cut follow your line; shots use it as the aim.", 11f, new Color(0.75f, 0.78f, 0.85f));
            if (_last.Length > 0) s.Label(_last, 11f, new Color(1f, 0.82f, 0.4f));
        }
    }
}
