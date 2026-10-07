using System.Collections.Generic;
using System.Linq;
using Laubrary.Launimator;
using UnityEngine;

namespace Laubrary.Launimator.Editor
{
    /// <summary>
    /// Phase data shared by the inspector and frame timeline. Each named range plays through or holds;
    /// runtime storage remains AnimZone so the editor and game use the same phase behavior.
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
