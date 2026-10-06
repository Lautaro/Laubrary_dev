using System;
using System.Linq;
using Laubrary.ZTracker.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor
{
    public sealed partial class ZTrackerModelWindow
    {
        /// <summary>Run after layout on a populated, attached Pattern workspace. Counts complete sequencer tracks inside the actual viewport, including every displayed field.</summary>
        public string CheckGridDensity(int minimumTracks)
        {
            if(pane!=0||grid?.panel==null||Data==null)throw new InvalidOperationException("Open an attached Pattern workspace before checking density.");
            var viewport=grid.contentViewport.worldBound;
            if(viewport.width<=0||viewport.height<=0||float.IsNaN(viewport.width))throw new InvalidOperationException("Wait for UI Toolkit layout before checking density.");
            if(grid.scrollOffset.x>1)throw new InvalidOperationException("Density must be checked at the left edge, without horizontal scrolling.");
            int complete=0;
            for(int i=0;i<Data.tracks.Count;i++)
            {
                var t=Data.tracks[i];if(t.kind!=TrackKind.Sequencer||t.visibleNoteColumns<1||t.visibleEffectColumns<1)continue;
                var header=gridCanvas.Q<Label>("pattern-track-header-"+i);if(header==null)continue;
                var bounds=header.worldBound;
                if(bounds.width>0&&bounds.xMin>=viewport.xMin-.5f&&bounds.xMax<=viewport.xMax+.5f)complete++;
            }
            var first=labels.Values.FirstOrDefault();
            if(first==null||first.resolvedStyle.fontSize<11)throw new InvalidOperationException("Density cannot be achieved by making the text unreadably small.");
            if(complete<minimumTracks)throw new InvalidOperationException($"Pattern density: {complete} complete tracks, expected at least {minimumTracks}; viewport {viewport.width:0.##} × {viewport.height:0.##}.");
            return $"PASS density: window {position.width:0} px, viewport {viewport.width:0.##} px, {complete} complete tracks (minimum {minimumTracks}), font {first.resolvedStyle.fontSize:0} px.";
        }
        /// <summary>Acceptance targets: four tracks at 1100 pixels, two at 760. Invoke once after layout at each window size.</summary>
        public string CheckGridDensity()=>CheckGridDensity(position.width>=1000?4:2);
    }
}
