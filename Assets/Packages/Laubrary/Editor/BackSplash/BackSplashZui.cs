// BackSplashZui — the ONE backdrop editor, built from Zui (UI Toolkit) controls.
//
// This replaced two parallel implementations of the same panel: BackSplashGUI.DrawInline (IMGUI, hosted
// by Mirage inside an IMGUIContainer) and a hand-built Zui copy inlined in PyreWindow. Two editors over
// one data type is exactly what "solve a UI problem once in ZUI and every tool benefits" exists to stop —
// and they had already drifted (only one of them clamped the position, only one offered Save).
//
// The one thing that genuinely differs per host is the position pad's DOMAIN, because imagePos is not in
// the same unit space everywhere: Pyre adds it straight to a screen-space Rect in its IMGUI preview, so it
// is PIXELS; Mirage feeds it to a real Transform.position read by an orthographic camera, so it is WORLD
// UNITS. Passing +-40 pixels to Mirage put the image five to eight times outside its camera's frustum
// before the pad was touched. So the caller supplies the domain; the control never guesses it.
using System;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.BackSplash.Editor
{
    public static class BackSplashZui
    {
        /// A framed backdrop panel over a caller-owned BackSplashSettings.
        ///
        /// `owner` is the UnityEngine.Object that ACTUALLY holds `settings` (a spec/view ScriptableObject
        /// asset, or null for a window-scoped, non-persisted copy like TextSplash's audition backdrop). When
        /// supplied, every edit here — including Recall — is wrapped in `Undo.RecordObject(owner, ...)` +
        /// `EditorUtility.SetDirty(owner)` BY THIS CONTROL, not by the caller. This used to be the caller's
        /// job, and two of the three hosts (Pyre, Pyre) never did it: every OTHER dial in those windows
        /// records undo, but the backdrop didn't, so a later Ctrl+Z elsewhere in the same session snapshotted
        /// and silently reverted the backdrop right along with whatever the user actually meant to undo —
        /// which reads as "the backsplash doesn't stick." Centralizing it here means every consumer gets the
        /// fix for free and a future one can't reintroduce the gap by forgetting the boilerplate.
        ///
        /// `onChanged` fires after any edit, owner-dirtying already done — repaint the preview there.
        /// `onStructureChanged` fires when a change adds or removes controls (picking or clearing the
        /// image), so a retained-mode host can rebuild; it falls back to `onChanged` when not supplied.
        ///
        /// `domainHalfWidth/Height` bound the position pad in the CALLER's own unit space (see the file
        /// comment). Defaults to BackSplash.MaxImageOffset, which is the pixel-space answer Pyre wants.
        /// `icon` (optional, a ZUI icon name) draws a header glyph beside the title — off by default, so
        /// existing hosts (Mirage) render exactly as before.
        public static VisualElement Build(BackSplashSettings settings, string title, string tooltip,
            Action onChanged, Action onStructureChanged = null,
            float domainHalfWidth = BackSplash.MaxImageOffset,
            float domainHalfHeight = BackSplash.MaxImageOffset,
            string icon = null, UnityEngine.Object owner = null)
        {
            var box = string.IsNullOrEmpty(icon) ? Z.Box(title, tooltip) : Z.Box(title, tooltip, icon);
            if (settings == null) return box;

            onStructureChanged ??= onChanged;
            // Undo recorded BEFORE `apply` mutates `settings` — RecordObject's snapshot has to be the
            // PRE-edit state, or undoing this very edit would restore the state it just landed.
            void Edit(Action apply, Action after)
            {
                if (owner != null) Undo.RecordObject(owner, $"Edit {title}");
                apply();
                if (owner != null) EditorUtility.SetDirty(owner);
                after?.Invoke();
            }
            void Changed(Action apply) => Edit(apply, onChanged);
            void Restructured(Action apply) => Edit(apply, onStructureChanged);

            var recall = Z.Button("Recall…",
                "Copy colour/image/position/zoom/tint FROM an existing preset — a one-time copy, not a live link.", null);
            recall.clicked += () =>
            {
                var wb = recall.worldBound;
                // Recall lands on a LATER event than the click that opened the popup, so the host cannot
                // catch it with a change-check around this call — it has to be told. The undo snapshot has
                // to be taken inside that same later callback, immediately before CopyFrom, so ShowRecall
                // takes `owner` itself rather than this method pre-recording too early.
                BackSplashGUI.ShowRecall(new Rect(wb.x, wb.y, wb.width, wb.height), settings,
                    onStructureChanged, owner, title);
            };

            var save = Z.Button("Save…",
                "Write this copy's current values TO a preset you pick (overwriting it) or a new one you name.", null);
            save.clicked += () =>
            {
                var wb = save.worldBound;
                BackSplashGUI.ShowSave(new Rect(wb.x, wb.y, wb.width, wb.height), settings);
            };

            box.Add(Z.Row(recall, save));

            // T-0312 — this pair overhangs its box by 18.7px whenever the host's settings pane sits at the
            // 320px minimum a Z.Split allows (measured in Shaper's right pane: Colour 137.3 + Image 167.1 +
            // the row's 6.7px gap = 311.1 inside a 292.0 content box), which clipped the right edge of the
            // sprite picker. Both fields carry explicit widths and `.zui-field` is flex-shrink:0, so nothing
            // in the row can give: the honest answer is to let the row fold. Wrapping costs nothing at any
            // width that already fits — flex-wrap only engages on overflow — and this is a static settings
            // row, not a contextual toolbar, so folding it moves no workspace under the pointer.
            var colourImageRow = Z.Row(
                Z.Field("Colour", "Solid background fill behind the image.",
                    Z.Color(settings.cameraColor, "Solid background fill behind the image.",
                        v => Changed(() => settings.cameraColor = v), 90f)),
                Z.Field("Image", "The backdrop image sprite.",
                    Z.Object<Sprite>(settings.image, "The backdrop image sprite.",
                        v => Restructured(() =>
                        {
                            settings.image = v;
                            // A fresh image starts at the default view — an old zoom/offset tuned for the
                            // PREVIOUS image's aspect ratio just as often shows nothing recognisable at all.
                            if (v != null) { settings.imageZoom = 1f; settings.imagePos = Vector2.zero; }
                        }), 120f)));
            colourImageRow.style.flexWrap = Wrap.Wrap;
            box.Add(colourImageRow);

            // Position/zoom/tint describe an image; with no image they'd be dials over nothing.
            if (settings.image != null)
            {
                var domain = new Rect(-domainHalfWidth, -domainHalfHeight, domainHalfWidth * 2f, domainHalfHeight * 2f);
                box.Add(Z.Row(
                    Z.Pad(settings.imagePos, domain,
                        "Drag to offset the backdrop image.",
                        // ClampImagePos is the much larger hard backstop; the domain above is always tighter,
                        // so this only matters if a caller passes a domain wider than the backstop.
                        v => Changed(() => settings.imagePos = BackSplash.ClampImagePos(v)), 68f),
                    Z.Column(
                        // Label-inside MicroSlider (no Z.Field wrap — it draws its own "Zoom" caption + value).
                        // Same 0.1..16 range and 5-decimal rounding as the native Slider it replaced.
                        Z.MicroSlider("Zoom", settings.imageZoom, 0.1f, 16f,
                            "How much of the backdrop image fills the viewport.",
                            v => Changed(() => settings.imageZoom = v), 150f, showValue: true),
                        Z.Field("Tint", "Multiplies the image's own colours.",
                            Z.Color(settings.imageTint, "Multiplies the image's own colours.",
                                v => Changed(() => settings.imageTint = v), 90f)))));
            }
            return box;
        }
    }
}
