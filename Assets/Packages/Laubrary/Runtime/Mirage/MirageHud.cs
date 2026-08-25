#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using ZuiRuntime;

namespace Laubrary.Mirage
{
    /// <summary>
    /// Play-mode-only, Game-View-rendered authoring HUD — the ONLY place previewables get placed/dragged,
    /// deliberately, because only the real preview <see cref="previewCamera"/> shows the true pixel-perfect
    /// result (Scene View uses a different, unrelated editor camera). Whole file is Editor-only: Mirage
    /// never ships, so this is free to call AssetDatabase (via MirageAssetPicker) straight from OnGUI.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Laubrary/Mirage/Mirage HUD")]
    public class MirageHud : MonoBehaviour
    {
        public MirageRig rig;
        public Camera previewCamera;

        Object _armedContent;
        string _draggingId;

        string _flashEntryId;
        double _flashUntil;

        void OnEnable() => MirageFlashSignal.Requested += HandleFlashRequested;
        void OnDisable() => MirageFlashSignal.Requested -= HandleFlashRequested;

        // MirageHud is a scene object, not something ZoeSpawner/MirageRig re-creates every load — it's easy
        // to lose (confirmed real: it was accidentally deleted from MirageStage.unity during an unrelated
        // editing pass this session, silently breaking Ping's flash with no error, just nothing happening).
        // Called from Ping so a missing/misconfigured one self-heals on demand instead of staying broken
        // until someone notices and manually re-adds it. Re-wires rig/previewCamera even on an EXISTING
        // MirageHud, so one left pointing at a stale/destroyed rig also recovers.
        public static MirageHud EnsureExists(MirageRig rig)
        {
            if (rig == null) return null;
            var hud = Object.FindFirstObjectByType<MirageHud>();
            if (hud == null)
            {
                var go = new GameObject("MirageHud");
                Undo.RegisterCreatedObjectUndo(go, "Create Mirage HUD");
                hud = go.AddComponent<MirageHud>();
            }
            hud.rig = rig;
            hud.previewCamera = rig.previewCamera;
            EditorUtility.SetDirty(hud);
            return hud;
        }

        void HandleFlashRequested(string entryId)
        {
            _flashEntryId = entryId;
            _flashUntil = EditorApplication.timeSinceStartup + 1.5;
        }

        void OnGUI()
        {
            DrawPanel();
            DrawManualControls();
            HandleInput();
            DrawFlash();
        }

        static readonly string[] Compass16 =
        {
            "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
            "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"
        };

        /// <summary>Hand controls for the live previewables that have something a hand can do — a Zoe's facing
        /// dial, walk and fire; a Chunks burst's replay.
        ///
        /// Every control here is generated from what the thing can actually do, never assumed: a Zoe's controls
        /// come from <see cref="MirageSubject.Capabilities"/> (no weapon → no Fire toggle, one facing → no
        /// direction control) and a burst's come from <see cref="MirageChunkBurst"/>. That is what makes this
        /// work for an ENEMY as readily as for the player — the panel asks the content what it can do rather
        /// than knowing anything about who is supposed to drive it.
        ///
        /// Nothing is drawn at all when there is nothing to drive, so a Mirage view assembled to look at a
        /// backdrop is untouched by this.</summary>
        void DrawManualControls()
        {
            // Walk the RIG's children rather than FindObjectsByType: the rig marks its live preview objects
            // HideFlags.DontSave (they're transient scaffolding, not scene content), and FindObjectsByType
            // EXCLUDES DontSave objects — so the find-based lookup returned nothing while the subjects sat
            // right there in the hierarchy, and the panel silently never appeared.
            if (rig == null) return;
            var driven = new System.Collections.Generic.List<MirageSubject>();
            foreach (var s in rig.GetComponentsInChildren<MirageSubject>(true))
            {
                if (s == null || !s.manualControls || !s.enabled) continue;
                // Self-heal after a domain reload wiped the runtime-only Manual/Capabilities.
                s.EnsureManualControls();
                if (s.Manual != null && s.Capabilities.Zoe != null) driven.Add(s);
            }

            // Chunk bursts are collected the same way and for the same DontSave reason — but they are NOT
            // opt-in the way a Zoe's manual controls are, and that difference is the point. A Zoe keeps playing
            // by itself, so hand controls are a convenience its entry can decline. A burst fires once and is
            // gone: without a way to fire it again the entry is a preview you can watch exactly once, and the
            // UI guide is explicit that a step the user has to be TOLD about is a missing feature, not a
            // workflow. So the replay control is always there for a burst, never something another editor has
            // to remember to switch on.
            var bursts = new System.Collections.Generic.List<MirageChunkBurst>();
            foreach (var b in rig.GetComponentsInChildren<MirageChunkBurst>(true))
                if (b != null && b.enabled && b.spec != null) bursts.Add(b);

            if (driven.Count == 0 && bursts.Count == 0) return;

            // Size the panel to what will actually be drawn, so it never reserves space for controls a
            // character hasn't earned (ui-layout-rules: an empty slot is pure cost).
            float h = 8f;
            foreach (var s in driven)
            {
                var c = s.Capabilities;
                h += 22f;                                   // header
                if (c.CanAim) h += 46f;                     // facing slider + compass label
                if (c.CanMove) h += 26f + 22f;              // walk toggle + lock-to-aim toggle
                if (c.CanMove && !s.Manual.lockMoveToAim) h += 24f;
                if (c.CanFire) h += 26f;
                h += 8f;
            }
            foreach (var b in bursts)
            {
                h += 22f;                                   // header
                if (!b.CanFire) h += 20f;                   // the one honest line instead of dead controls
                else
                {
                    h += 26f + 22f;                         // Replay button + Auto-replay toggle
                    if (b.autoRepeat) h += 24f;             // interval slider
                }
                h += 8f;
            }

            var content = Zui.Panel(ZuiAnchor.BottomLeft, 250f, h, new Color(0f, 0f, 0f, 0.68f));
            var stack = new ZuiStack(content, 4f);

            foreach (var s in driven)
            {
                var caps = s.Capabilities;
                var m = s.Manual;
                stack.Label(caps.Zoe != null ? caps.Zoe.displayName : s.name, bold: true);

                if (caps.CanAim)
                {
                    // A free 0..360 slider snapped to the directions the ART actually has — asking for a pose
                    // between two authored frames is not a thing the character can do, so the control doesn't
                    // let you express it.
                    float raw = stack.Slider("Facing", m.aimAngleDeg, 0f, 359.9f);
                    m.aimAngleDeg = SnapToNearest(raw, caps.AimAngles);
                    stack.Label("   " + CompassLabel(m.aimAngleDeg) + "  (" + m.aimAngleDeg.ToString("0.#") + "°)");
                }

                if (caps.CanMove)
                {
                    m.walking = stack.Toggle(m.walking ? "Walking" : "Idle", m.walking);
                    m.lockMoveToAim = stack.Toggle("Move = facing", m.lockMoveToAim);
                    if (!m.lockMoveToAim)
                    {
                        // Unlocked is how backpedalling and strafing get previewed: travel one way, face another.
                        float rawMove = stack.Slider("Travel", m.moveAngleDeg, 0f, 359.9f);
                        m.moveAngleDeg = SnapToNearest(rawMove, caps.AimAngles);
                    }
                }

                if (caps.CanFire) m.firing = stack.Toggle(m.firing ? "Firing" : "Hold fire", m.firing);

                stack.Space(4f);
            }

            foreach (var b in bursts)
            {
                stack.Label(b.spec.name, bold: true);

                if (!b.CanFire)
                {
                    // Honest rather than a dead button: nothing a burst spawns can move outside Play mode
                    // (Chunk and ChunkModuleRunner both drive off Update/coroutines, neither is ExecuteAlways),
                    // so there is no burst to replay yet. Say that, the same way DrawPanel already says it for
                    // placement, instead of offering a control that would visibly do nothing.
                    stack.Label("Enter Play Mode to fire this burst.");
                }
                else
                {
                    // Label = action: this fires the burst on press, it does not open anything.
                    if (stack.Button("Replay burst")) b.Fire();

                    // Off by default — see MirageChunkBurst on why an auto-detonating explosion is the thing
                    // that makes this preview unreadable. The interval only appears once it can matter, the
                    // same conditional-row shape the Travel slider above already uses.
                    b.autoRepeat = stack.Toggle(b.autoRepeat ? "Auto-replaying" : "Manual replay", b.autoRepeat);
                    if (b.autoRepeat)
                        b.repeatInterval = stack.Slider("Every", b.repeatInterval, 0.25f, 10f);
                }

                stack.Space(4f);
            }
        }

        /// Nearest authored direction to a requested angle, wrapping across 0/360. Returns the request
        /// unchanged when the character declares no specific directions.
        static float SnapToNearest(float deg, System.Collections.Generic.IReadOnlyList<float> options)
        {
            if (options == null || options.Count == 0) return deg;
            float best = options[0], bestDelta = float.MaxValue;
            for (int i = 0; i < options.Count; i++)
            {
                float d = Mathf.Abs(Mathf.DeltaAngle(deg, options[i]));
                if (d < bestDelta) { bestDelta = d; best = options[i]; }
            }
            return best;
        }

        /// Compass name for an angle when it lands on a 16-point direction, else the bare angle — so a set
        /// authored at some other count still reads sensibly instead of being mislabelled.
        static string CompassLabel(float deg)
        {
            float step = 360f / 16f;
            float snapped = Mathf.Repeat(deg, 360f) / step;
            int idx = Mathf.RoundToInt(snapped);
            if (Mathf.Abs(snapped - idx) > 0.01f) return deg.ToString("0.#") + "°";
            return Compass16[((idx % 16) + 16) % 16];
        }

        // Unity's native Ping/Selection only flashes the Project window / Hierarchy row — useless while
        // you're actually looking at the Game View composing a shot. This draws a pulsing outline directly
        // around the entry's LIVE rendered sprite bounds, in screen space, so "where did that thing go" is
        // answerable without looking away from the view you're actually authoring.
        void DrawFlash()
        {
            if (_flashEntryId == null || rig == null || previewCamera == null) return;
            double now = EditorApplication.timeSinceStartup;
            if (now >= _flashUntil) { _flashEntryId = null; return; }
            if (Event.current.type != EventType.Repaint) return;

            var live = rig.GetLive(_flashEntryId);
            var sr = live != null ? live.GetComponentInChildren<SpriteRenderer>() : null;
            if (sr == null) return;

            var b = sr.bounds;
            Span4(b, out var c0, out var c1, out var c2, out var c3);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var c in new[] { c0, c1, c2, c3 })
            {
                var sp = previewCamera.WorldToScreenPoint(c);
                float gx = sp.x, gy = Screen.height - sp.y;   // flip to OnGUI's top-down convention
                minX = Mathf.Min(minX, gx); maxX = Mathf.Max(maxX, gx);
                minY = Mathf.Min(minY, gy); maxY = Mathf.Max(maxY, gy);
            }
            var rect = Rect.MinMaxRect(minX - 4f, minY - 4f, maxX + 4f, maxY + 4f);

            float pulse = 0.4f + 0.6f * Mathf.Abs(Mathf.Sin((float)now * Mathf.PI * 3f));
            var col = new Color(1f, 0.85f, 0.1f, pulse);
            const float thick = 2f;
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thick), col);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thick, rect.width, thick), col);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thick, rect.height), col);
            EditorGUI.DrawRect(new Rect(rect.xMax - thick, rect.y, thick, rect.height), col);
        }

        static void Span4(Bounds b, out Vector3 c0, out Vector3 c1, out Vector3 c2, out Vector3 c3)
        {
            c0 = new Vector3(b.min.x, b.min.y, 0f);
            c1 = new Vector3(b.max.x, b.min.y, 0f);
            c2 = new Vector3(b.max.x, b.max.y, 0f);
            c3 = new Vector3(b.min.x, b.max.y, 0f);
        }

        void DrawPanel()
        {
            var content = Zui.Panel(ZuiAnchor.TopLeft, 240f, 90f, new Color(0f, 0f, 0f, 0.6f));
            var s = new ZuiStack(content);
            s.Label("Mirage", bold: true);

            if (rig == null || previewCamera == null)
            {
                s.Label("Rig/Camera not wired.");
                return;
            }

            // Placement/drag stays Play-mode-only — deliberately, per the class doc: only the real preview
            // camera shows the true pixel-perfect result. Everything else here (panel label, flash) now runs
            // via [ExecuteAlways] so Ping's flash works in Edit mode too.
            if (!Application.isPlaying)
            {
                s.Label("Enter Play Mode to place previewables here.");
                return;
            }

            if (_armedContent != null)
            {
                s.Label($"Placing {_armedContent.name} — click in view");
                if (s.Button("Cancel")) _armedContent = null;
            }
            else if (s.Button("Add Previewable"))
            {
                MirageAssetPicker.BuildMenu(picked => _armedContent = picked).ShowAsContext();
            }
        }

        void HandleInput()
        {
            if (rig == null || previewCamera == null || !Application.isPlaying) return;
            var e = Event.current;

            if (_armedContent != null)
            {
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    rig.AddEntry(_armedContent, ScreenToWorld(e.mousePosition));
                    _armedContent = null;
                    e.Use();
                }
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                var hit = FindNearestEntry(ScreenToWorld(e.mousePosition));
                if (hit != null) { _draggingId = hit.id; e.Use(); }
            }
            else if (e.type == EventType.MouseDrag && _draggingId != null)
            {
                rig.Reposition(_draggingId, ScreenToWorld(e.mousePosition));
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _draggingId != null)
            {
                _draggingId = null;
                e.Use();
            }
        }

        /// OnGUI's mouse Y is top-down; Camera pixel coords are bottom-up — flip before converting. The
        /// orthographic camera ignores the Z distance for X/Y (no perspective divide), so any in-range value
        /// works; using the camera's own distance to the Z=0 plane keeps the resulting Z sane regardless.
        Vector2 ScreenToWorld(Vector2 guiMousePos)
        {
            float flippedY = Screen.height - guiMousePos.y;
            float dist = -previewCamera.transform.position.z;
            var world = previewCamera.ScreenToWorldPoint(new Vector3(guiMousePos.x, flippedY, dist));
            return new Vector2(world.x, world.y);
        }

        PreviewableEntry FindNearestEntry(Vector2 world)
        {
            if (rig.view == null) return null;
            PreviewableEntry best = null;
            float bestDist = 1f;   // click must land within 1 world unit of an entry to grab it
            foreach (var entry in rig.view.previewables)
            {
                float d = Vector2.Distance(entry.position, world);
                if (d < bestDist) { bestDist = d; best = entry; }
            }
            return best;
        }
    }
}
#endif
