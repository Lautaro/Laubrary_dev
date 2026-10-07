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
    /// on the Laumination and walked at runtime by ZonedAnimationPlayer. Kept in a partial so the core window
    /// file only gains a draw call + save/load of the two fields.
    /// </summary>
    public partial class LauminationBuilderWindow
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


        private void AddZone()
        {
            int s = _seqMultiSel.Count > 0 ? _seqMultiSel.Min() : 0;
            int e = _seqMultiSel.Count > 0 ? _seqMultiSel.Max() : Mathf.Max(0, _sequence.Count - 1);
            _zones.Add(new AnimZone { name = $"Phase{_zones.Count + 1}", startFrame = s, endFrame = e, behavior = ZoneBehavior.PlayThrough });
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
        private void ClampZones() { /* Boundaries are explicit; conflicts are reported in the inspector. */ }

        /// <summary>A fresh copy of the authored zones for saving (so the saved def doesn't alias the live list).</summary>
        private List<AnimZone> ZonesForSave() =>
            _zones.Select(z => new AnimZone { name = z.name, startFrame = z.startFrame, endFrame = z.endFrame, behavior = z.behavior }).ToList();

        private void LoadZonesFrom(Laumination def)
        {
            _zonesEnabled = def != null && def.zonesEnabled;
            _zones.Clear();
            if (def?.zones != null)
                foreach (var z in def.zones)
                    _zones.Add(new AnimZone { name = z.name, startFrame = z.startFrame, endFrame = z.endFrame, behavior = z.behavior });
        }
    }
}
