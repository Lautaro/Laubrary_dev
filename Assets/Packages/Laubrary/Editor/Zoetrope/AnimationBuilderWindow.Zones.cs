using System.Collections.Generic;
using System.Linq;
using Laubrary.Zoetrope;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// Authoring side of the zoned-animation feature (ANIMATION_CONTROLLER.md). Adds a "Zones" meta-track under
    /// the sequence: mark contiguous frame ranges (Start/Air/Fall/Land…) and set each PlayThrough or Hold. Stored
    /// on the AnimationDef and walked at runtime by ZonedAnimationPlayer. Kept in a partial so the core window
    /// file only gains a draw call + save/load of the two fields.
    /// </summary>
    public partial class AnimationBuilderWindow
    {
        private bool _zonesEnabled;
        private readonly List<AnimZone> _zones = new List<AnimZone>();

        private static readonly Color[] ZonePalette =
        {
            new Color(0.30f, 0.80f, 1.00f), new Color(1.00f, 0.70f, 0.25f),
            new Color(0.55f, 1.00f, 0.45f), new Color(1.00f, 0.45f, 0.70f),
            new Color(0.80f, 0.55f, 1.00f), new Color(1.00f, 0.95f, 0.40f),
        };
        private static Color ZoneColor(int i) => ZonePalette[((i % ZonePalette.Length) + ZonePalette.Length) % ZonePalette.Length];

        private void DrawZoneTrack()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _zonesEnabled = GUILayout.Toggle(_zonesEnabled, new GUIContent("Zones",
                    "Mark phased ranges (Start/Air/Fall/Land) on this strip for the runtime ZonedAnimationPlayer."),
                    "Button", GUILayout.Width(70));
                if (_zonesEnabled)
                    GUILayout.Label("phased strip — mark ranges, set PlayThrough / Loop", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
            }
            if (!_zonesEnabled) return;

            ClampZones();
            DrawZoneBar();

            for (int zi = 0; zi < _zones.Count; zi++)
            {
                var z = _zones[zi];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.DrawRect(GUILayoutUtility.GetRect(12, 16, GUILayout.Width(12), GUILayout.Height(16)), ZoneColor(zi));
                    string zn = EditorGUILayout.TextField(z.name, GUILayout.Width(90));
                    if (zn != z.name) { RecordUndo("Rename zone"); z.name = zn; }
                    var zb = (ZoneBehavior)EditorGUILayout.EnumPopup(z.behavior, GUILayout.Width(100));
                    if (zb != z.behavior) { RecordUndo("Zone behavior"); z.behavior = zb; }
                    GUILayout.Label("frames", EditorStyles.miniLabel, GUILayout.Width(44));
                    int zs = EditorGUILayout.IntField(z.startFrame, GUILayout.Width(38));
                    if (zs != z.startFrame) { RecordUndo("Zone range"); z.startFrame = zs; }
                    GUILayout.Label("–", GUILayout.Width(8));
                    int ze = EditorGUILayout.IntField(z.endFrame, GUILayout.Width(38));
                    if (ze != z.endFrame) { RecordUndo("Zone range"); z.endFrame = ze; }
                    using (new EditorGUI.DisabledScope(_seqMultiSel.Count == 0))
                        if (GUILayout.Button(new GUIContent("Set = selection", "Set this zone's range to the currently selected sequence frames."), GUILayout.Width(108)))
                        { RecordUndo("Zone range"); z.startFrame = _seqMultiSel.Min(); z.endFrame = _seqMultiSel.Max(); }
                    if (GUILayout.Button(new GUIContent("✕", "Remove this zone."), GUILayout.Width(22)))
                    { RecordUndo("Remove zone"); _zones.RemoveAt(zi); GUIUtility.ExitGUI(); }
                    GUILayout.FlexibleSpace();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ Zone", GUILayout.Width(70))) { RecordUndo("Add zone"); AddZone(); }
                EditorGUILayout.LabelField("PlayThrough = play once → next zone.   Loop = loop here until the game Advances.",
                    EditorStyles.miniLabel);
            }
        }

        private void DrawZoneBar()
        {
            int n = Mathf.Max(1, _sequence.Count);
            Rect bar = GUILayoutUtility.GetRect(10, 26, GUILayout.ExpandWidth(true), GUILayout.Height(26));
            EditorGUI.DrawRect(bar, new Color(0.10f, 0.10f, 0.10f));
            for (int zi = 0; zi < _zones.Count; zi++)
            {
                var z = _zones[zi];
                Color col = ZoneColor(zi);
                float x0 = bar.x + (z.startFrame / (float)n) * bar.width;
                float x1 = bar.x + ((z.endFrame + 1) / (float)n) * bar.width;
                var r = new Rect(x0, bar.y, Mathf.Max(2f, x1 - x0), bar.height);
                EditorGUI.DrawRect(r, new Color(col.r, col.g, col.b, z.behavior == ZoneBehavior.Loop ? 0.55f : 0.28f));
                DrawRectOutline(r, col, 1f);
                GUI.Label(new Rect(r.x + 3, r.y + 4, Mathf.Max(0, r.width - 4), 18),
                    $"{z.name} {(z.behavior == ZoneBehavior.Loop ? "⟳" : "→")}", EditorStyles.whiteMiniLabel);
            }
            if (_sequence.Count > 0)
            {
                float px = bar.x + ((_animFrame + 0.5f) / n) * bar.width;
                EditorGUI.DrawRect(new Rect(px - 1, bar.y, 2, bar.height), new Color(0.2f, 1f, 0.5f, 0.95f));
            }
        }

        private void AddZone()
        {
            int s = _seqMultiSel.Count > 0 ? _seqMultiSel.Min() : 0;
            int e = _seqMultiSel.Count > 0 ? _seqMultiSel.Max() : Mathf.Max(0, _sequence.Count - 1);
            _zones.Add(new AnimZone { name = $"Zone{_zones.Count + 1}", startFrame = s, endFrame = e, behavior = ZoneBehavior.PlayThrough });
        }

        /// <summary>If zones are on and frame <paramref name="frameIndex"/> falls in a zone, output that zone's
        /// colour. Used to tint each sequence thumbnail's border by zone.</summary>
        private bool TryFrameZone(int frameIndex, out Color col)
        {
            col = Color.white;
            if (!_zonesEnabled) return false;
            for (int zi = 0; zi < _zones.Count; zi++)
                if (_zones[zi].Contains(frameIndex)) { col = ZoneColor(zi); return true; }
            return false;
        }

        /// <summary>Keep zone ranges valid against the current sequence length.</summary>
        private void ClampZones()
        {
            int last = Mathf.Max(0, _sequence.Count - 1);
            foreach (var z in _zones)
            {
                z.startFrame = Mathf.Clamp(z.startFrame, 0, last);
                z.endFrame = Mathf.Clamp(z.endFrame, z.startFrame, last);
            }
        }

        /// <summary>A fresh copy of the authored zones for saving (so the saved def doesn't alias the live list).</summary>
        private List<AnimZone> ZonesForSave() =>
            _zones.Select(z => new AnimZone { name = z.name, startFrame = z.startFrame, endFrame = z.endFrame, behavior = z.behavior }).ToList();

        private void LoadZonesFrom(AnimationDef def)
        {
            _zonesEnabled = def != null && def.zonesEnabled;
            _zones.Clear();
            if (def?.zones != null)
                foreach (var z in def.zones)
                    _zones.Add(new AnimZone { name = z.name, startFrame = z.startFrame, endFrame = z.endFrame, behavior = z.behavior });
        }
    }
}
