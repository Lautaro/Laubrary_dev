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
        enum Kind { Slice, Cut, Bullet, Shotgun, RemoveHead, Shotgun2 }
        static readonly string[] KindNames = { "Slice (cuts through)", "Cut (knife)", "Bullet", "Shotgun", "Remove head", "Shotgun 2 (pellets fly)" };

        [Tooltip("The spawner that owns the imps.")]
        public GoreLabDemoSpawner spawner;

        [Tooltip("A click that is not a drag becomes a swipe this long (world units) for slice and cut, and an aim line this long for shots.")]
        public float clickLength = 2.4f;

        // ---- shotgun cone: a placeable object. Drag empty space to make one, drag its body to move it, drag its tip to aim it and set the power, Space or Fire to shoot.
        enum ConeDrag { None, Create, Move, Aim }
        bool _coneExists;
        Vector2 _muzzle, _tip = new Vector2(3f, 0f);
        float _coneLen = 3f;
        int _pellets = 30;
        ConeDrag _coneDrag;
        Vector2 _coneGrab;
        Rect _sliderRect;
        Mesh _coneMesh;
        MeshRenderer _coneRenderer;
        LineRenderer _coneOutline, _muzzleRing, _tipRing;
        const float RefLength = 3f, MinLength = 0.5f, MaxLength = 12f, HandleRadius = 0.4f;
        const int ArcPoints = 14;

        GoreLabShotgun _shotgun2, _coneGun, _bulletGun;
        int _blastSeed;
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
            BuildCone();
            _shotgun2 = gameObject.AddComponent<GoreLabShotgun>();
            _shotgun2.Init(spawner != null ? spawner.rig : null);
            _coneGun = new GameObject("Cone gun").AddComponent<GoreLabShotgun>();
            _coneGun.transform.SetParent(transform);
            _coneGun.Init(spawner != null ? spawner.rig : null);
            _bulletGun = new GameObject("Bullet gun").AddComponent<GoreLabShotgun>();
            _bulletGun.transform.SetParent(transform);
            _bulletGun.Init(spawner != null ? spawner.rig : null);
            var rigForRecipes = spawner != null ? spawner.rig : null;
            if (rigForRecipes != null && rigForRecipes.recipes != null)
                foreach (var r in rigForRecipes.recipes) if (r is ShotgunRecipe sg) _pellets = Mathf.Max(1, sg.pellets);
        }

        LineRenderer MakeLine(string name, int points, Color c, float width)
        {
            var lr = new GameObject(name).AddComponent<LineRenderer>();
            lr.transform.SetParent(transform);
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = lr.endColor = c;
            lr.startWidth = lr.endWidth = width;
            lr.positionCount = points;
            lr.loop = true;
            lr.sortingOrder = 1001;
            lr.useWorldSpace = true;
            lr.enabled = false;
            return lr;
        }

        void BuildCone()
        {
            var go = new GameObject("Shotgun cone");
            go.transform.SetParent(transform);
            _coneMesh = new Mesh { name = "ConeMesh" };
            go.AddComponent<MeshFilter>().sharedMesh = _coneMesh;
            _coneRenderer = go.AddComponent<MeshRenderer>();
            _coneRenderer.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.55f, 0.1f, 0.3f) };
            _coneRenderer.sortingOrder = 900;
            _coneRenderer.enabled = false;
            _coneOutline = MakeLine("Cone outline", ArcPoints + 2, new Color(1f, 0.75f, 0.3f, 0.95f), 0.05f);
            _muzzleRing = MakeLine("Cone muzzle handle", 20, new Color(1f, 1f, 1f, 0.9f), 0.05f);
            _tipRing = MakeLine("Cone tip handle", 20, new Color(1f, 0.85f, 0.3f, 0.95f), 0.05f);
        }

        float ConeHalfAngleRad()
        {
            double deg = 12;
            var rig = spawner != null ? spawner.rig : null;
            if (rig != null && rig.recipes != null) foreach (var r in rig.recipes) if (r is ShotgunRecipe sg) deg = sg.coneDeg;
            return (float)deg * Mathf.Deg2Rad;
        }

        static void Ring(LineRenderer lr, Vector2 c)
        {
            for (int i = 0; i < lr.positionCount; i++)
            {
                float a = i / (float)lr.positionCount * Mathf.PI * 2f;
                lr.SetPosition(i, new Vector3(c.x + Mathf.Cos(a) * HandleRadius, c.y + Mathf.Sin(a) * HandleRadius, 0f));
            }
        }

        void RefreshCone()
        {
            bool show = _kind == Kind.Shotgun && _coneExists;
            _coneRenderer.enabled = _coneOutline.enabled = _muzzleRing.enabled = _tipRing.enabled = show;
            if (!show) return;
            Vector2 dir = (_tip - _muzzle).normalized;
            float baseA = Mathf.Atan2(dir.y, dir.x), half = ConeHalfAngleRad();
            var verts = new Vector3[ArcPoints + 2];
            var tris = new int[ArcPoints * 3];
            verts[0] = _muzzle;
            for (int i = 0; i <= ArcPoints; i++)
            {
                float a = baseA - half + 2f * half * i / ArcPoints;
                verts[i + 1] = new Vector3(_muzzle.x + Mathf.Cos(a) * _coneLen, _muzzle.y + Mathf.Sin(a) * _coneLen, 0f);
            }
            for (int i = 0; i < ArcPoints; i++) { tris[i * 3] = 0; tris[i * 3 + 1] = i + 1; tris[i * 3 + 2] = i + 2; }
            _coneMesh.Clear();
            _coneMesh.vertices = verts;
            _coneMesh.triangles = tris;
            for (int i = 0; i < verts.Length; i++) _coneOutline.SetPosition(i, verts[i]);
            Ring(_muzzleRing, _muzzle);
            Ring(_tipRing, _tip);
        }

        bool InsideCone(Vector2 p)
        {
            Vector2 v = p - _muzzle;
            if (v.magnitude <= HandleRadius * 1.2f) return true;
            if (v.magnitude > _coneLen) return false;
            return Vector2.Angle(v, _tip - _muzzle) <= ConeHalfAngleRad() * Mathf.Rad2Deg;
        }

        void SetTip(Vector2 world)
        {
            Vector2 v = world - _muzzle;
            if (v.sqrMagnitude < 1e-6f) v = Vector2.right * MinLength;
            _coneLen = Mathf.Clamp(v.magnitude, MinLength, MaxLength);
            _tip = _muzzle + v.normalized * _coneLen;
        }

        void ConeInput(Mouse mouse, Vector2 screen, Vector2 world)
        {
            if (mouse.leftButton.wasPressedThisFrame && !OverPanel(screen))
            {
                if (_coneExists && Vector2.Distance(world, _tip) <= HandleRadius * 1.5f) _coneDrag = ConeDrag.Aim;
                else if (_coneExists && InsideCone(world)) { _coneDrag = ConeDrag.Move; _coneGrab = world - _muzzle; }
                else { _coneExists = true; _muzzle = world; _coneLen = MinLength; _tip = world + Vector2.right * MinLength; _coneDrag = ConeDrag.Create; }
            }
            if ((_coneDrag == ConeDrag.Create || _coneDrag == ConeDrag.Aim) && mouse.leftButton.isPressed) SetTip(world);
            else if (_coneDrag == ConeDrag.Move && mouse.leftButton.isPressed)
            {
                Vector2 delta = world - _coneGrab - _muzzle;
                _muzzle += delta; _tip += delta;
            }
            if (mouse.leftButton.wasReleasedThisFrame) _coneDrag = ConeDrag.None;
            var kb = Keyboard.current;
            if (kb != null && kb.spaceKey.wasPressedThisFrame) FireCone();
        }

        /// <summary>A real attack: the cone's fan of pellets flies as projectiles, and every pellet wounds the imp it reaches along its own line.</summary>
        void FireCone()
        {
            if (!_coneExists) { _last = "Drag on the ground to make a cone first."; return; }
            _coneGun.pellets = _pellets;
            _coneGun.coneDeg = ConeHalfAngleRad() * Mathf.Rad2Deg;
            _coneGun.energy = _shotgun2.energy * _coneLen / RefLength;
            int n = _coneGun.Fire(_muzzle, _tip, Random.Range(1, 1000000));
            _last = "Fired " + n + " pellets.";
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || _cam == null) return;
            Vector2 screen = mouse.position.ReadValue();
            Vector2 world = _cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_cam.transform.position.z));

            if (_kind == Kind.Shotgun)
            {
                ConeInput(mouse, screen, world);
                RefreshCone();
                return;
            }
            RefreshCone();
            if (mouse.leftButton.wasPressedThisFrame && !OverPanel(screen)) { _dragging = true; _down = world; _blastSeed = Random.Range(1, 1000000); }
            if (_dragging)
            {
                _line.enabled = true;
                _line.SetPosition(0, _down);
                _line.SetPosition(1, world);
                if (_kind == Kind.Shotgun2) { ShotLine(_down, world, out var m, out var t); _shotgun2.ShowPreview(m, t, _blastSeed); }
            }
            if (mouse.leftButton.wasReleasedThisFrame && _dragging)
            {
                _dragging = false;
                _line.enabled = false;
                _shotgun2.HidePreview();
                Strike(_down, world);
            }
        }

        // The GUI panel works in top-left screen coordinates, the mouse in bottom-left.
        bool OverPanel(Vector2 screen)
        {
            var p = new Vector2(screen.x, Screen.height - screen.y);
            return _panelRect.Contains(p) || (_kind == Kind.Shotgun && _coneExists && _sliderRect.Contains(p));
        }

        int CountAlive()
        {
            int n = 0;
            foreach (var b in spawner.bodies) if (b != null) n++;
            return n;
        }

        // A click (no drag) aims from the lower left toward the clicked point.
        void ShotLine(Vector2 a, Vector2 b, out Vector2 muzzle, out Vector2 target)
        {
            bool click = (b - a).magnitude < 0.3f;
            muzzle = click ? b - new Vector2(1f, 0.6f).normalized * clickLength : a;
            target = b;
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
            if (_kind == Kind.Shotgun2)
            {
                // Pellets fly on their own and wound whatever they reach, so there is no target to pick here.
                int n = _shotgun2.Fire(a, b, _blastSeed);
                _last = "Fired " + n + " pellets.";
                return;
            }
            int seed = Random.Range(1, 1000000);
            if (_kind == Kind.Bullet)
            {
                // a real projectile; where it lands the bullet recipe digs its shallow hole
                _bulletGun.FireOne(a, b, MakeRecipe(), seed, 45f);
                _last = "Bullet fired.";
                return;
            }
            // slice, cut and remove head are melee swings: a hitbox travels the swipe line and damages what it touches
            GoreLabSwing.Begin(a, b, MakeRecipe(), seed);
            _last = KindNames[(int)_kind] + ": swing started.";
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
            Rect rc = Zui.Panel(ZuiAnchor.TopLeft, 200f, _kind == Kind.Shotgun2 ? 560f : 400f, PanelBg);
            float pad = UIScale.S(10f);
            _panelRect = new Rect(rc.x - pad, rc.y - pad, rc.width + pad * 2f, rc.height + pad * 2f);
            var s = new ZuiStack(rc);
            s.Header("GoreLab demo");
            s.Label("Damage type", 13f);
            for (int i = 0; i < KindNames.Length; i++)
                if (s.Toggle(KindNames[i], (int)_kind == i, 14f) && (int)_kind != i) _kind = (Kind)i;
            if (_kind == Kind.Shotgun)
            {
                if (s.Button("Fire cone (Space)", 15f)) FireCone();
            }
            if (_kind == Kind.Shotgun2)
            {
                _shotgun2.pellets = Mathf.RoundToInt(s.Slider("Pellets", _shotgun2.pellets, 1, 60, 13f, "0"));
                _shotgun2.coneDeg = s.Slider("Cone", _shotgun2.coneDeg, 0f, 45f, 13f, "0.#");
                _shotgun2.energy = s.Slider("Power", _shotgun2.energy, 0.3f, 30f, 13f, "0.#");
                _shotgun2.speed = s.Slider("Speed", _shotgun2.speed, 5f, 100f, 13f, "0");
            }
            s.Space();
            s.Label("Imps: " + CountAlive(), 13f);
            if (s.Button("+  Add imp", 15f)) { spawner.Add(); }
            if (s.Button("-  Remove oldest", 15f)) { spawner.RemoveOldest(); }
            s.Space(4f);
            if (s.Button("Reset wounds", 15f)) { spawner.ResetAll(); _last = "Wounds cleared."; }
            s.Space(4f);
            s.Label("Every attack is real: swings sweep your line, bullets and pellets fly and hit the first imp in their way.", 11f, new Color(0.75f, 0.78f, 0.85f));
            if (_kind == Kind.Shotgun2 && _shotgun2.Summary.Length > 0) s.Label(_shotgun2.Summary, 11f, new Color(0.55f, 1f, 0.6f));
            if (_last.Length > 0) s.Label(_last, 11f, new Color(1f, 0.82f, 0.4f));

            // the mini slider for the pellet count sits next to the cone's muzzle
            if (_kind == Kind.Shotgun && _coneExists && _cam != null)
            {
                Vector3 sp = _cam.WorldToScreenPoint(_muzzle);
                _sliderRect = new Rect(sp.x - 60f, Screen.height - sp.y + 22f, 130f, 34f);
                GUI.Box(_sliderRect, GUIContent.none);
                GUI.Label(new Rect(_sliderRect.x + 4f, _sliderRect.y, 124f, 16f), "Pellets " + _pellets + "  Power " + (_coneLen / RefLength).ToString("0.0") + "x");
                _pellets = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(_sliderRect.x + 6f, _sliderRect.y + 20f, 118f, 12f), _pellets, 3f, 100f));
            }
        }
    }
}
