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
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.BackSplash.Editor
{
    public static class BackSplashZui
    {
        /// A framed backdrop panel over a caller-owned BackSplashSettings.
        ///
        /// `onChanged` fires after any edit — dirty the owning asset and repaint there. `onStructureChanged`
        /// fires when a change adds or removes controls (picking or clearing the image), so a retained-mode
        /// host can rebuild; it falls back to `onChanged` when not supplied.
        ///
        /// `domainHalfWidth/Height` bound the position pad in the CALLER's own unit space (see the file
        /// comment). Defaults to BackSplash.MaxImageOffset, which is the pixel-space answer Pyre wants.
        /// `icon` (optional, a ZUI icon name) draws a header glyph beside the title — off by default, so
        /// existing hosts (Mirage) render exactly as before.
        public static VisualElement Build(BackSplashSettings settings, string title, string tooltip,
            Action onChanged, Action onStructureChanged = null,
            float domainHalfWidth = BackSplash.MaxImageOffset,
            float domainHalfHeight = BackSplash.MaxImageOffset,
            string icon = null)
        {
            var box = string.IsNullOrEmpty(icon) ? Z.Box(title, tooltip) : Z.Box(title, tooltip, icon);
            if (settings == null) return box;

            onStructureChanged ??= onChanged;
            void Changed() => onChanged?.Invoke();
            void Restructured() => onStructureChanged?.Invoke();

            var recall = Z.Button("Recall…",
                "Copy colour/image/position/zoom/tint FROM an existing preset — a one-time copy, not a live link.", null);
            recall.clicked += () =>
            {
                var wb = recall.worldBound;
                // Recall lands on a LATER event than the click that opened the popup, so the host cannot
                // catch it with a change-check around this call — it has to be told.
                BackSplashGUI.ShowRecall(new Rect(wb.x, wb.y, wb.width, wb.height), settings, Restructured);
            };

            var save = Z.Button("Save…",
                "Write this copy's current values TO a preset you pick (overwriting it) or a new one you name.", null);
            save.clicked += () =>
            {
                var wb = save.worldBound;
                BackSplashGUI.ShowSave(new Rect(wb.x, wb.y, wb.width, wb.height), settings);
            };

            box.Add(Z.Row(recall, save));

            box.Add(Z.Row(
                Z.Field("Colour", "Solid background fill behind the image.",
                    Z.Color(settings.cameraColor, "Solid background fill behind the image.",
                        v => { settings.cameraColor = v; Changed(); }, 90f)),
                Z.Field("Image", "The backdrop image sprite.",
                    Z.Object<Sprite>(settings.image, "The backdrop image sprite.",
                        v => { settings.image = v; Restructured(); }, 120f))));

            // Position/zoom/tint describe an image; with no image they'd be dials over nothing.
            if (settings.image != null)
            {
                var domain = new Rect(-domainHalfWidth, -domainHalfHeight, domainHalfWidth * 2f, domainHalfHeight * 2f);
                box.Add(Z.Row(
                    Z.Pad(settings.imagePos, domain,
                        "Drag to offset the backdrop image.",
                        // ClampImagePos is the much larger hard backstop; the domain above is always tighter,
                        // so this only matters if a caller passes a domain wider than the backstop.
                        v => { settings.imagePos = BackSplash.ClampImagePos(v); Changed(); }, 68f),
                    Z.Column(
                        // Label-inside MicroSlider (no Z.Field wrap — it draws its own "Zoom" caption + value).
                        // Same 0.1..16 range and 5-decimal rounding as the native Slider it replaced.
                        Z.MicroSlider("Zoom", settings.imageZoom, 0.1f, 16f,
                            "How much of the backdrop image fills the viewport.",
                            v => { settings.imageZoom = v; Changed(); }, 150f, showValue: true),
                        Z.Field("Tint", "Multiplies the image's own colours.",
                            Z.Color(settings.imageTint, "Multiplies the image's own colours.",
                                v => { settings.imageTint = v; Changed(); }, 90f)))));
            }
            return box;
        }
    }
}
