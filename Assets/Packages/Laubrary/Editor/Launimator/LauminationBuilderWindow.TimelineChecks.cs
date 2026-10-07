using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Launimator.Editor
{
    public partial class LauminationBuilderWindow
    {
        /// <summary>Exercises typed clipboard and frame attachment without opening a window, saving assets,
        /// or disturbing the user's open document. Throws on failure, restores the shared clipboard.</summary>
        public static string[] VerifyTimelineDocumentContracts()
        {
            var passed = new List<string>();
            void Check(bool condition, string name)
            { if (!condition) throw new InvalidOperationException(name); passed.Add(name); }
            var originalClipboard = _timelineClipboard;
            var window = CreateInstance<LauminationBuilderWindow>();
            try
            {
                window._animPlaying = false;
                window._sequence.Add(new CellRef(0, 0) { pct = 100 });
                window._sequence.Add(new CellRef(0, 1) { pct = -100 });
                window._sequence.Add(new CellRef(0, 2));
                var sourceRegion = new Region { label = "selection check", sourceTextureGuid = "" };
                sourceRegion.cells.AddRange(new[] { new Rect(0, 0, 1, 1), new Rect(1, 0, 1, 1), new Rect(2, 0, 1, 1) });
                sourceRegion.SyncPivots(Vector2.zero); window._regions.Add(sourceRegion);
                window.SelectSingle(0, 0); window.SeqSelectSingle(2); window.SelectTimelineSource();
                Check(window._selRegion == 0 && window._selCell == 2 && window._multiSel.Count == 1,
                    "Timeline frame selection replaces an earlier source selection for registration and transforms");
                window._metaLayers.Add(new MetaLayer { id = "typed", mode = MetaLayerMode.Vector });
                window._activeLayer = 0; window.SyncMetaFrames();
                window._metaLayers[0].vectorFrames[0] = new VectorMetaFrame { authored = true, origin = new Vector2(.2f, .3f), direction = Vector2.right, length = 7 };
                window._metaLayers[0].frames[0] = new MetaFrame { w = 1, h = 1, cells = new byte[] { 8 }, param = "payload" };
                window._events.Add(new FrameEvent { frame = 0, name = "hit", zoundName = "clang", hasPosition = true, position = new Vector2Int(2, 3) });
                window._zones.Add(new AnimZone { name = "phase", startFrame = 0, endFrame = 1, behavior = ZoneBehavior.Loop });
                window._zonesEnabled = true;

                window._animateTool = AnimateTool.Layers; window.SeqSelectSingle(0); window.CopyTimelineContent();
                window.SeqSelectSingle(1); window.PasteTimelineContent();
                var vector = window._metaLayers[0].vectorFrames[1];
                Check(vector.authored && vector.length == 7 && vector.origin == new Vector2(.2f, .3f), "Vector clipboard preserves authored origin, direction and length");
                window.PerformUndo();
                Check(!window._metaLayers[0].vectorFrames[1].authored, "One Undo removes the pasted vector");
                window.PerformRedo();
                vector = window._metaLayers[0].vectorFrames[1];
                Check(vector.authored && vector.length == 7, "Redo restores the complete pasted vector");
                window._metaLayers[0].vectorFrames[0].length = 20;
                Check(vector.length == 7, "Pasted vectors do not alias their source");
                window._metaLayers[0].mode = MetaLayerMode.Point;
                Check(!window.CanPasteTimeline(), "Vector clipboard cannot paste into a point layer");
                window.SeqSelectSingle(0); window.CopyTimelineContent(); window.SeqSelectSingle(2); window.PasteTimelineContent();
                var mask = window._metaLayers[0].frames[2];
                Check(mask.cells[0] == 8 && mask.param == "payload", "Point clipboard preserves cell values and parameter");
                window._metaLayers[0].frames[0].cells[0] = 1;
                Check(mask.cells[0] == 8, "Pasted mask cells do not alias their source");
                window._metaLayers[0].mode = MetaLayerMode.Shape;
                Check(!window.CanPasteTimeline(), "Point clipboard cannot silently become a shape");

                window._animateTool = AnimateTool.Events; window.SeqSelectSingle(0); window.CopyTimelineContent(); window.SeqSelectSingle(2); window.PasteTimelineContent();
                var ev = window._events.Find(e => e.frame == 2);
                Check(ev != null && ev.name == "hit" && ev.zoundName == "clang" && ev.hasPosition && ev.position == new Vector2Int(2, 3), "Event clipboard preserves sound and positional payload");
                window._animateTool = AnimateTool.Phases; window.SeqSelectSingle(0); window.CopyTimelineContent(); window.SeqSelectSingle(2); window.PasteTimelineContent();
                Check(window._zones[1].startFrame == 2 && window._zones[1].endFrame == 3 && window.GetPhaseConflicts().Count > 0,
                    "Pasted phase keeps its explicit length and diagnoses overrun instead of clipping");

                window.MoveSequenceFrame(0, 2);
                Check(window._sequence[2].pct == 100 && window._metaLayers[0].frames[2].param == "payload" && window._events.Exists(e => e.frame == 2 && e.zoundName == "clang"),
                    "Frame reorder keeps timing, spatial and complete event data attached");
                Check(window._zones[0].startFrame == 0 && window._zones[0].endFrame == 1, "Frame reorder leaves explicit phase boundaries unchanged");
                return passed.ToArray();
            }
            finally
            {
                _timelineClipboard = originalClipboard;
                DestroyImmediate(window);
            }
        }
    }
}
