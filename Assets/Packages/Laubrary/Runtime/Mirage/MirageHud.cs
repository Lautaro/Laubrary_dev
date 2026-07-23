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
            HandleInput();
            DrawFlash();
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
