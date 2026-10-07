using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Laubrary.Launimator.Editor
{
    public partial class LauminationBuilderWindow
    {
        /// <summary>One mapping drives every payload: each NEW slot names the OLD frame it receives.
        /// Repeated source indices duplicate content; omitted source indices delete it. Phase boundaries
        /// are deliberately outside this operation: the author chooses their numeric endpoints.</summary>
        private void RemapSequence(IReadOnlyList<int> newIndexToOldIndex, string undoLabel)
        {
            if (newIndexToOldIndex == null) throw new ArgumentNullException(nameof(newIndexToOldIndex));
            if (newIndexToOldIndex.Any(i => i < 0 || i >= _sequence.Count))
                throw new ArgumentOutOfRangeException(nameof(newIndexToOldIndex), "Every new slot must refer to an existing frame.");
            RecordUndo(undoLabel);
            var original = _sequence.ToArray();
            _sequence.Clear();
            foreach (int oldIndex in newIndexToOldIndex) _sequence.Add(original[oldIndex]);
            RemapFramePayloads(_metaLayers, _events, newIndexToOldIndex);
            _animPlaying = false; _inDivider = false; _showingFrameZero = false;
            _animFrame = Mathf.Clamp(_animFrame, 0, Mathf.Max(0, _sequence.Count - 1));
            _previewHash = -1; ClearMaskCache();
            var conflicts = GetPhaseConflicts();
            if (conflicts.Count > 0) _status = "Frames changed; phase boundaries were kept. " + conflicts[0];
        }

        internal static void RemapFramePayloads(IList<MetaLayer> layers, List<FrameEvent> events, IReadOnlyList<int> mapping)
        {
            foreach (var layer in layers)
            {
                var masks = layer.frames ?? new List<MetaFrame>();
                var vectors = layer.vectorFrames ?? new List<VectorMetaFrame>();
                layer.frames = mapping.Select(i => i < masks.Count ? masks[i]?.Clone() ?? new MetaFrame() : new MetaFrame()).ToList();
                layer.vectorFrames = mapping.Select(i => i < vectors.Count ? vectors[i]?.Clone() ?? new VectorMetaFrame() : new VectorMetaFrame()).ToList();
            }
            var originalEvents = CloneEvents(events);
            events.Clear();
            for (int newIndex = 0; newIndex < mapping.Count; newIndex++)
                foreach (var item in originalEvents)
                    if (item.frame == mapping[newIndex]) events.Add(CloneEvent(item, newIndex));
        }

        private void MoveSequenceFrame(int from, int to)
        {
            if (from < 0 || from >= _sequence.Count || to < 0 || to >= _sequence.Count || from == to) return;
            var mapping = Enumerable.Range(0, _sequence.Count).ToList();
            mapping.RemoveAt(from); mapping.Insert(to, from);
            RemapSequence(mapping, "Reorder frame");
            SeqSelectSingle(to);
        }
    }
}
