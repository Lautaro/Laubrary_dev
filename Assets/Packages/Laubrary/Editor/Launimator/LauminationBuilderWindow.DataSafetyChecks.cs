using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Laubrary.Launimator.Editor
{
    public partial class LauminationBuilderWindow
    {
        // Called directly by the editor verification harness. No test-runner window, asset creation,
        // disk writes or modal dialogs: every test uses an unshown, temporary working document.
        internal static string RunDataSafetyChecks()
        {
            var passed = new List<string>();
            var window = CreateInstance<LauminationBuilderWindow>();
            try
            {
                void Check(bool condition, string message)
                {
                    if (!condition) throw new InvalidOperationException("Builder data safety: " + message);
                }
                MetaLayer Layer() => new MetaLayer
                {
                    id = "aim", mode = MetaLayerMode.Vector, vectorAllowLength = true,
                    vectorSnapAngle = true, vectorSnapDivisions = 24,
                    frames = new List<MetaFrame>
                    {
                        new MetaFrame { param = "first", w = 1, h = 1, cells = new byte[] { 3 } },
                        new MetaFrame { param = "second", w = 1, h = 1, cells = new byte[] { 9 } }
                    },
                    vectorFrames = new List<VectorMetaFrame>
                    {
                        new VectorMetaFrame { authored = true, origin = new Vector2(.1f, .2f), direction = Vector2.left, length = 4f },
                        new VectorMetaFrame { authored = true, origin = new Vector2(.8f, .9f), direction = Vector2.right, length = 7f }
                    }
                };
                void Seed()
                {
                    window._regions.Clear();
                    window._regions.Add(new Region
                    {
                        label = "test", sourceTextureGuid = "test-source",
                        cells = new List<Rect> { new Rect(0, 0, 4, 4), new Rect(4, 0, 4, 4) },
                        pivots = new List<Vector2> { Vector2.zero, Vector2.one },
                        transforms = new List<CellTransform> { CellTransform.Identity, CellTransform.Identity }
                    });
                    window._sequence.Clear();
                    window._sequence.Add(new CellRef(0, 0) { pct = 100f });
                    window._sequence.Add(new CellRef(0, 1) { pct = -50f });
                    window._metaLayers = new List<MetaLayer> { Layer() };
                    window._metaEnabled = true;
                    window._events = new List<FrameEvent>
                    {
                        new FrameEvent { frame = 0, name = "first", zoundName = "sound-a", hasPosition = true, position = new Vector2Int(3, 4) },
                        new FrameEvent { frame = 1, name = "second", zoundName = "sound-b", hasPosition = true, position = new Vector2Int(8, 9) }
                    };
                    window._zones.Clear();
                    window._zones.Add(new AnimZone { name = "hold", startFrame = 0, endFrame = 1, behavior = ZoneBehavior.Loop });
                    window._zonesEnabled = true;
                    window.BeginCleanDocument();
                }

                Seed();
                var clone = CloneLayers(window._metaLayers)[0];
                Check(clone.mode == MetaLayerMode.Vector && clone.vectorAllowLength && clone.vectorSnapAngle && clone.vectorSnapDivisions == 24,
                    "layer copies must preserve mode and authoring configuration");
                clone.frames[0].cells[0] = 0; clone.vectorFrames[0].length = 99;
                Check(window._metaLayers[0].frames[0].cells[0] == 3 && window._metaLayers[0].vectorFrames[0].length == 4,
                    "layer copies must own their mask arrays and vector objects");
                var copiedEvent = CloneEvents(window._events)[0]; copiedEvent.name = "changed";
                Check(window._events[0].name == "first" && copiedEvent.zoundName == "sound-a" && copiedEvent.position == new Vector2Int(3, 4),
                    "event copies must preserve positional sound data without sharing objects");
                passed.Add("deep copies preserve vectors, masks, configuration and all event data");

                var persistedCopy = LauminaryRepo.CopyAnimation(new Laumination
                {
                    recipe = window._sequence.Select(window.DocumentFrame).ToList(),
                    events = window._events, metaLayers = window._metaLayers
                });
                Check(persistedCopy.events[0].zoundName == "sound-a" && persistedCopy.events[0].hasPosition
                    && persistedCopy.events[0].position == new Vector2Int(3, 4), "the orphan/version persistence copy must retain sound and position");
                persistedCopy.events[0].name = "copy edit";
                Check(window._events[0].name == "first", "persisted copies must not alias working events");
                passed.Add("orphan/version persistence copies retain sound and positional events");

                window.RemapSequence(new[] { 1, 0 }, "test reverse");
                Check(window._sequence[0].cell == 1 && window._sequence[0].pct == -50f,
                    "reverse must carry image identity and duration together");
                Check(window._metaLayers[0].frames[0].param == "second" && window._metaLayers[0].vectorFrames[0].length == 7f,
                    "reverse must carry both spatial payload formats");
                Check(window._events[0].name == "second" && window._events[0].frame == 0 && window._events[0].zoundName == "sound-b",
                    "reverse must attach events to their original image");
                Check(window._zones[0].startFrame == 0 && window._zones[0].endFrame == 1,
                    "reordering must leave explicit phase endpoints unchanged");
                passed.Add("reverse keeps every frame payload attached and phase boundaries explicit");

                Seed(); window.RemapSequence(new[] { 0, 1, 1 }, "test duplicate");
                window._metaLayers[0].frames[2].cells[0] = 1;
                window._metaLayers[0].vectorFrames[2].length = 42;
                window._events.Single(e => e.frame == 2).position = Vector2Int.zero;
                Check(window._metaLayers[0].frames[1].cells[0] == 9 && window._metaLayers[0].vectorFrames[1].length == 7,
                    "duplicated spatial payloads must be independently editable");
                Check(window._events.Single(e => e.frame == 1).position == new Vector2Int(8, 9),
                    "duplicated events must be independently editable");
                passed.Add("duplicates own independent masks, vectors and event objects");

                Seed(); window.RemapSequence(new[] { 1 }, "test delete");
                Check(window._events.Count == 1 && window._events[0].name == "second" && window._events[0].frame == 0,
                    "delete must remove only deleted-frame events and reindex survivors");
                Check(window._zones[0].endFrame == 1 && window.HasInvalidPhaseRanges && window.GetPhaseConflicts().Count > 0,
                    "delete must report the now-invalid phase range without silently shortening it");
                passed.Add("delete remaps survivors and reports out-of-range phase boundaries");

                Seed(); window.MoveSequenceFrame(0, 1);
                Check(window._sequence[1].cell == 0 && window._events.Single(e => e.frame == 1).name == "first",
                    "drag reordering must use the same payload operation");
                bool rejected = false;
                try { window.RemapSequence(new[] { 9 }, "invalid"); } catch (ArgumentOutOfRangeException) { rejected = true; }
                Check(rejected && window._sequence.Count == 2, "invalid mappings must fail before changing any state");
                passed.Add("drag reorder shares the payload operation; malformed mappings are rejected atomically");

                Seed();
                Check(!window.IsDocumentDirty, "new baseline must be clean");
                window.RecordUndo("test full state");
                window._metaLayers[0].vectorSnapDivisions = 8;
                window._events[0].position = Vector2Int.zero;
                window._bgKeyEnabled = true; window._bgKey = new Color32(30, 40, 50, 255);
                window._cols += 2;
                Check(window.IsDocumentDirty, "saved-content changes must be dirty");
                window.PerformUndo();
                Check(window._metaLayers[0].vectorSnapDivisions == 24 && window._events[0].position == new Vector2Int(3, 4)
                    && !window._bgKeyEnabled && window._cols == 3, "undo must restore every authored data family");
                Check(!window.IsDocumentDirty, "undo to saved content must clear dirty state");
                window.PerformRedo();
                Check(window.IsDocumentDirty && window._metaLayers[0].vectorSnapDivisions == 8 && window._cols == 5,
                    "redo must restore the complete authored edit");
                window.BeginCleanDocument();
                Check(window._undo.Count == 0 && window._redo.Count == 0 && !window.IsDocumentDirty,
                    "a new document must not inherit another document's history");
                passed.Add("undo/redo restore all data; saved baselines and document histories remain independent");

                Seed();
                window._animFps += 3f;
                Check(window.IsAnimationDirty && !window.IsSlicingDirty, "animation timing must not dirty slicing");
                window.MarkAnimationSaved();
                Check(!window.IsDocumentDirty, "saving animation must update only its own baseline");
                window._cols += 1;
                Check(!window.IsAnimationDirty && window.IsSlicingDirty, "grid edits must not dirty the animation");
                window.MarkSlicingSaved();
                Check(!window.IsDocumentDirty, "saving slicing must update only its own baseline");
                passed.Add("animation and slicing have independent dirty and save baselines");

                Seed();
                var transformed = CellTransform.Identity;
                transformed.flipX = true; transformed.angle = 33; transformed.scaleY = 2.5f;
                window._regions[0].transforms[0] = transformed;
                window._frameZero = new CellRef(0, 0) { pct = 400 };
                string beforeRecipe = window.AnimationStateFingerprint();
                var sliced = Newtonsoft.Json.JsonConvert.DeserializeObject<RegionSlicerPersistence.StateDto>(
                    Newtonsoft.Json.JsonConvert.SerializeObject(window.BuildState()));
                Check(sliced.regions[0].sourceTextureGuid == "test-source" && sliced.regions[0].transforms[0].angle == 33
                    && sliced.regions[0].transforms[0].scaleY == 2.5f, "slicing must round-trip source identity and transform");
                window.ApplyState(sliced);
                Check(window.AnimationStateFingerprint() == beforeRecipe, "reloading slices must preserve animation, lead-in and every payload");
                Check(window._regions[0].transforms[0].flipX, "reloading slices must restore the transform");
                passed.Add("slicing round-trip preserves source identities/transforms without replacing animation data");

                Seed();
                string savedAnimation = window.AnimationStateFingerprint();
                int savedColumns = window._cols;
                var savedPivot = window._regions[0].pivots[0];
                window.RecordUndo("edit before discard");
                window._regions[0].pivots[0] = new Vector2(-.5f, 1.5f);
                window._cols += 9;
                window._events[0].name = "unsaved";
                window._metaLayers[0].vectorFrames[0].length = 99;
                window._detectedCells.Add(new Rect(9, 9, 2, 2));
                window.DiscardDocumentChanges();
                Check(window._cols == savedColumns && window._regions[0].pivots[0] == savedPivot,
                    "Discard must restore source settings and pivots before New reuses the palette");
                Check(window.AnimationStateFingerprint() == savedAnimation && !window.IsDocumentDirty && window._undo.Count == 0,
                    "Discard must restore saved animation data and clear its abandoned history");
                Check(window._detectedCells.Count == 0, "Discard must clear unaccepted cuts from the old working state");
                passed.Add("Discard restores saved source and animation content before palette reuse");

                Seed();
                var outsidePivot = new Vector2(-2f, 3f);
                var outsideFrame = new FrameRef { sourceTextureGuid = "second-source", cell = new Rect(0, 0, 8, 8), pivot = outsidePivot, transform = CellTransform.Identity };
                var outsideCell = window.FindOrCreateCellForFrame(outsideFrame);
                Check(window._regions[outsideCell.region].pivots[outsideCell.cell] == outsidePivot,
                    "new recipe cells must preserve arbitrary finite pivots beyond the sprite bounds");
                window._sequence.Add(outsideCell);
                outsideFrame.pivot = new Vector2(-3f, 4f);
                var secondCell = window.FindOrCreateCellForFrame(outsideFrame);
                Check(window._regions[secondCell.region].pivots[secondCell.cell] == outsideFrame.pivot && secondCell.cell != outsideCell.cell,
                    "same-image recipe variants must preserve distinct out-of-bounds pivots");
                passed.Add("recipe loading preserves arbitrary finite pivots and distinct registration variants");

                int saves = 0;
                Check(!ResolveDocumentTransition(0, () => { saves++; return false; }) && saves == 1,
                    "failed save must cancel navigation");
                Check(ResolveDocumentTransition(0, () => true), "successful save must permit navigation");
                Check(!ResolveDocumentTransition(1, () => throw new Exception("cancel must not save")), "Cancel must stay on the document");
                Check(ResolveDocumentTransition(2, () => throw new Exception("discard must not save")), "Discard must navigate without saving");
                passed.Add("Save/Discard/Cancel never navigate after a failed save and never autosave");
                return string.Join("\n", passed.Select((text, i) => $"PASS {i + 1}: {text}"));
            }
            finally { DestroyImmediate(window); }
        }
    }
}
