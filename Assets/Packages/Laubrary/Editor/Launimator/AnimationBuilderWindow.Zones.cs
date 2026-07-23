using System.Collections.Generic;
using System.Linq;
using Laubrary.Launimator;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Launimator.Editor
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

        private void BuildZoneTrack(VisualElement root)
        {
            var head = WrapRow(Z.Toggle("Zones",
                "Mark phased ranges (Start/Air/Fall/Land) on this strip for the runtime ZonedAnimationPlayer.",
                _zonesEnabled, v => { _zonesEnabled = v; Refresh(); }));
            if (_zonesEnabled)
                head.Add(Z.Text("phased strip — mark ranges, set PlayThrough / Loop", ZuiText.Subtle,
                    "What the zone track below is for."));
            root.Add(head);
            if (!_zonesEnabled) return;

            ClampZones();

            _zoneBarIM = new IMGUIContainer(DrawZoneBarGUI)
            {
                tooltip = "Every zone drawn across the sequence, with the playhead — a read-only overview."
            };
            _zoneBarIM.style.height = 26f;
            _zoneBarIM.style.flexShrink = 0f;
            root.Add(_zoneBarIM);

            for (int zi = 0; zi < _zones.Count; zi++)
            {
                int index = zi;
                var z = _zones[zi];
                var row = WrapRow();

                var swatch = new VisualElement { tooltip = "This zone's colour on the bar and on each frame's border." };
                swatch.style.width = 12f;
                swatch.style.height = 16f;
                swatch.style.flexShrink = 0f;
                swatch.style.backgroundColor = ZoneColor(zi);
                row.Add(swatch);

                row.Add(Z.TextInput(z.name, "This zone's name — what the runtime player advances between.",
                    v => { RecordUndo("Rename zone"); z.name = v; Dirty(); }, 90f));
                row.Add(Z.EnumDropdown(z.behavior,
                    "PlayThrough plays once and moves on; Loop holds here until the game advances.",
                    v => { RecordUndo("Zone behavior"); z.behavior = v; Dirty(); }, 110f));
                row.Add(Z.Field("frames", "First frame of this zone (0-based).",
                    Z.Int(z.startFrame, "First frame of this zone (0-based).",
                        v => { RecordUndo("Zone range"); z.startFrame = v; ClampZones(); Dirty(); }, 44f)));
                row.Add(Z.Text("–", ZuiText.Small, "to").W(8f));
                row.Add(Z.Int(z.endFrame, "Last frame of this zone (0-based, inclusive).",
                    v => { RecordUndo("Zone range"); z.endFrame = v; ClampZones(); Dirty(); }, 44f));

                var setSel = Z.Button("Set = selection", "Set this zone's range to the currently selected sequence frames.",
                    () =>
                    {
                        RecordUndo("Zone range");
                        z.startFrame = _seqMultiSel.Min(); z.endFrame = _seqMultiSel.Max();
                        Refresh();
                    }).W(108f);
                setSel.SetEnabled(_seqMultiSel.Count > 0);
                row.Add(setSel);
                row.Add(Z.Button("X", "Remove this zone.",
                    () => { RecordUndo("Remove zone"); _zones.RemoveAt(index); Refresh(); }).W(22f));
                root.Add(row);
            }

            root.Add(WrapRow(
                Z.Button("+ Zone", "Add a zone covering the current selection (or the whole sequence).",
                    () => { RecordUndo("Add zone"); AddZone(); Refresh(); }).W(70f),
                Z.Text("PlayThrough = play once → next zone.   Loop = loop here until the game Advances.",
                    ZuiText.Subtle, "What each zone behaviour means at runtime.")));
        }

        /// The zone bar — an IMGUI island: a proportional coloured bar with labels and the playhead.
        private void DrawZoneBarGUI()
        {
            if (_zoneBarIM == null) return;
            int n = Mathf.Max(1, _sequence.Count);
            Rect bar = new Rect(0f, 0f, _zoneBarIM.layout.width, _zoneBarIM.layout.height);
            if (!(bar.width > 10f)) return;
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
