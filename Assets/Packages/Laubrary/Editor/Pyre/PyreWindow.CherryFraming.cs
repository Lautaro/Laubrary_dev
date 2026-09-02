// PyreWindow.CherryFraming — Pyre's half of the cherry panel: the source-frame thumbnail cache, and the
// host adapter that lets the shared PyreCherryPanel draw over this spec.
//
// The panel itself (source grid, slot grid, selection, drag-reorder, right-click slot editor, keyboard
// shortcuts) moved to PyreCherryPanel.cs so Shaper can draw the SAME panel rather than a hand-kept partial
// copy of it — see that file's header for why, and for the two load-bearing gesture rules it carries. This
// window's rendered UI and its Undo behaviour are unchanged: the adapter below supplies exactly the things
// the panel used to reach into this window for.
//
// Every edit the panel makes is a SEQUENCE edit (which frames play, for how long) — never a render input —
// so they all go through DirtyRepaintOnly and leave the frame cache alone.
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Pyre.Editor
{
    public partial class PyreWindow
    {
        // ── cherry source-grid thumbnail cache — independent of the preview's frame cache (its textures are
        // bound to UITK tiles as backgroundImage, so they must outlive every preview refill). Rebuilt only
        // when frameCount/canvasSize actually change.
        Texture2D[] cherryStripCache;
        int cherryStripCacheCanvas = -1;

        void DestroyCherryStripCache()
        {
            if (cherryStripCache == null) return;
            for (int i = 0; i < cherryStripCache.Length; i++)
                if (cherryStripCache[i] != null) DestroyImmediate(cherryStripCache[i]);
            cherryStripCache = null;
            cherryStripCacheCanvas = -1;
        }

        void EnsureCherryStripCache(Pyre s)
        {
            int n = Mathf.Max(1, s.frameCount);
            bool structural = cherryStripCache == null || cherryStripCache.Length != n || cherryStripCacheCanvas != s.canvasSize;
            if (!structural) return;
            DestroyCherryStripCache();
            cherryStripCache = new Texture2D[n];
            for (int i = 0; i < n; i++) cherryStripCache[i] = PyreRenderer.RenderFrameTexture(s, i);
            cherryStripCacheCanvas = s.canvasSize;
        }

        // ── panel scaffolding ───────────────────────────────────────────────────────────────────────────
        PyreCherryPanel cherryPanel;

        void BuildCherryPanel(VisualElement root, Pyre s)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            cherryPanel = new PyreCherryPanel(new CherryHost(this));
            scroll.Add(cherryPanel.Root);
            root.Add(scroll);
            cherryPanel.Rebuild();
        }

        /// <summary>
        /// What <see cref="PyreCherryPanel"/> needs from this window. Everything here used to be an inline
        /// reach into <c>spec</c> from the panel code; nothing about it is new behaviour. The chrome is the
        /// default one, which IS Pyre's own wording and saved-view keys.
        /// </summary>
        sealed class CherryHost : IPyreCherryHost
        {
            readonly PyreWindow w;
            readonly PyreCherryChrome chrome = new PyreCherryChrome();

            public CherryHost(PyreWindow window) { w = window; }

            Pyre S => w.spec;

            public PyreCherryChrome Chrome => chrome;

            public int CherryFrameCount => S != null ? S.frameCount : 0;

            public bool CherryEnabled => S != null && S.cherryEnabled;
            public void SetCherryEnabled(bool value) => w.DirtyRepaintOnly(() => S.cherryEnabled = value);

            public float CherryTileSize => S != null ? S.previewCherryStripSize : 96f;
            public void SetCherryTileSize(float px) =>
                w.DirtyRepaintOnly(() => S.previewCherryStripSize =
                    Mathf.Clamp(px, PyreCherryPanel.MinTileSize, PyreCherryPanel.MaxTileSize));

            List<CherryFrame> Slots
            {
                get
                {
                    if (S == null) return null;
                    return S.cherryFrames ??= new List<CherryFrame>();
                }
            }

            public int CherrySlotCount => Slots?.Count ?? 0;

            public PyreCherrySlotView ReadCherrySlot(int index)
            {
                var f = Slots[index];
                return new PyreCherrySlotView
                {
                    sourceIndex = f.sourceIndex,
                    lengthMultiplier = f.lengthMultiplier,
                    minLengthMultiplier = f.minLengthMultiplier,
                    maxLengthMultiplier = f.maxLengthMultiplier,
                    useMinMaxLength = f.useMinMaxLength,
                    multiFrame = f.multiFrame,
                };
            }

            public void WriteCherrySlot(int index, PyreCherrySlotView v)
            {
                var f = Slots[index];
                f.sourceIndex = v.sourceIndex;
                f.lengthMultiplier = v.lengthMultiplier;
                f.minLengthMultiplier = v.minLengthMultiplier;
                f.maxLengthMultiplier = v.maxLengthMultiplier;
                f.useMinMaxLength = v.useMinMaxLength;
                f.multiFrame = v.multiFrame;
            }

            public void AppendCherrySlot(int sourceIndex) =>
                Slots.Add(new CherryFrame { sourceIndex = sourceIndex });

            public void DuplicateCherrySlots(List<int> indices, int insertAt)
            {
                var copies = new List<CherryFrame>();
                foreach (var idx in indices)
                {
                    var src = Slots[idx];
                    copies.Add(new CherryFrame
                    {
                        sourceIndex = src.sourceIndex,
                        lengthMultiplier = src.lengthMultiplier,
                        minLengthMultiplier = src.minLengthMultiplier,
                        maxLengthMultiplier = src.maxLengthMultiplier,
                        useMinMaxLength = src.useMinMaxLength,
                        multiFrame = src.multiFrame,
                        multiFrameSources = new List<int>(src.multiFrameSources ?? new List<int>()),
                        multiFrameRandomSeed = src.multiFrameRandomSeed,
                    });
                }
                for (int k = 0; k < copies.Count; k++) Slots.Insert(insertAt + k, copies[k]);
            }

            public void RemoveCherrySlotAt(int index) => Slots.RemoveAt(index);

            public int MoveCherryBlock(List<int> block, int targetIndex) =>
                ZuiThumbGrid.MoveBlock(Slots, block, targetIndex);

            public Texture2D CherrySourceThumb(int frameIndex)
            {
                if (S == null) return null;
                w.EnsureCherryStripCache(S);
                if (w.cherryStripCache == null || frameIndex < 0 || frameIndex >= w.cherryStripCache.Length) return null;
                return w.cherryStripCache[frameIndex];
            }

            public void CherryEdit(System.Action apply) => w.DirtyRepaintOnly(apply);

            public void ResetCherryPlayback() => w.ResetCherryPlayback();

            // Nothing in Pyre's window is derived from the slot count, and Pyre draws no rows of its own in
            // this panel — its loop delay lives in the transport row, not here.
            public void CherrySlotCountChanged() { }
            public void DecorateCherrySlotHeader(int slotIndex, VisualElement header) { }
            public void BuildExtraCherryPopoverRows(int slotIndex, bool multi, VisualElement panel) { }
            public void BuildExtraCherrySlotBoxRows(VisualElement slotBox) { }
            public void BuildExtraCherrySectionRows(VisualElement section) { }
        }
    }
}
