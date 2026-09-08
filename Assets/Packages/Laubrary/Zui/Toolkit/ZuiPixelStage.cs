// ZuiPixelStage — what the number in a preview's "Zoom" box MEANS, decided once for every Laubrary tool.
//
// Before this file each pixel preview answered a different question with the same widget. Pyre's zoom 1 was
// one canvas pixel per GUI point; Shaper's zoom 1 was "however big ScaleToFit happened to make it in the
// pane you left the window at", so the same number meant a different size after every window resize; Chunks
// had no zoom at all and always blew its subject up to the largest whole number that fitted; Launimator's
// two zoom fields started at a fit value and at a hardcoded 6. Four tools, four meanings, one label.
//
// The one meaning, stated as the user's own sentence: AT ZOOM 1 A SIMULATED PIXEL IS THE SIZE IT WILL BE IN
// THE GAME. The game's own scale is a whole number of screen pixels per game pixel — the project renders a
// 320x200 world onto a 1280x800 Steam Deck, so one game pixel is a 4x4 block of screen pixels — and that
// number, not 1, is what zoom 1 draws. Zoom N is N times it. Nothing else is multiplied in: no automatic
// fit, no pane-size dependence, no fractional scale. A user who wants the whole picture in the pane presses
// FIT, which picks the largest whole zoom that fits and WRITES THAT NUMBER INTO THE ZOOM CONTROL, so the
// magnification is always a number on screen rather than a hidden state of the layout.
//
// Two things this file deliberately does NOT do:
//  • It does not re-render anything. Zoom is how big the picture is drawn, never what the renderer produces,
//    so no bake, no cache and no exported frame can be changed by it.
//  • It does not decide WHERE the picture goes — ZuiPixel already does that correctly in device pixels (see
//    that file's header for the points-versus-device-pixels trap). This adds the missing half: which device
//    pixel count a zoom NUMBER stands for.
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// <summary>The shared zoom rule for every pixel-art preview stage: zoom 1 = one simulated pixel at the
    /// game's own integer screen scale, in whole device pixels. Reach for <see cref="Place"/> to put a buffer
    /// on screen at a dialled zoom, <see cref="FitZoom"/> for the Fit action, and <see cref="ZoomControl"/>
    /// for the standard Zoom slider + Fit button pair.</summary>
    public static class ZuiPixelStage
    {
        /// <summary>The lowest zoom the rule allows. Zoom 0 would mean "smaller than the game shows it",
        /// which is a different feature (a thumbnail) and not what a zoom dial is for.</summary>
        public const int MinZoom = 1;

        /// <summary>The highest zoom every stage offers, so the control reads the same everywhere. Matches
        /// Pyre's own pre-existing ceiling.</summary>
        public const int MaxZoom = 16;

        /// <summary>Used when the project has no pixel-scale settings asset — the reference project's own
        /// number (a 320x200 world on a 1280x800 display). Stated in every zoom tooltip when it is in force,
        /// because a preview claiming to show game size while guessing at the game's scale would be a lie.
        /// </summary>
        public const int FallbackTargetScale = 4;

        const string SettingsTypeName = "Laubrary.PixelScale.PixelScaleProjectSettings";

        static bool _resolved;
        static int _targetScale = FallbackTargetScale;
        static bool _fromSettings;
        static string _summary;

        /// <summary>Screen pixels per simulated pixel at zoom 1 — the game's own integer upscale.</summary>
        public static int TargetScale { get { Resolve(); return _targetScale; } }

        /// <summary>False when <see cref="TargetScale"/> is the fallback because this project carries no
        /// pixel-scale settings asset (the Shaper worktree is one such project).</summary>
        public static bool TargetScaleFromSettings { get { Resolve(); return _fromSettings; } }

        /// <summary>One clause naming where the scale came from, for a tooltip or a readout.</summary>
        public static string TargetScaleSummary { get { Resolve(); return _summary; } }

        /// <summary>Forget the cached scale — for a settings asset edited during a session. A domain reload
        /// clears it anyway, so this is only for the same-session case.</summary>
        public static void ClearCache() { _resolved = false; }

        // The settings type lives in a Runtime assembly this editor toolkit deliberately does not reference:
        // ZUI is the bottom of the stack and must not gain a dependency on a gameplay module to draw a
        // slider, and the type is genuinely absent from some checkouts of this package. Reflection by name is
        // therefore the honest coupling — present, use it; absent, say so in the tooltip and use the
        // reference project's number.
        static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            _targetScale = FallbackTargetScale;
            _fromSettings = false;
            _summary = FallbackTargetScale + " screen pixels per game pixel (this project has no Pixel Scale "
                     + "settings asset, so the reference 320x200-on-1280x800 scale is assumed)";

            try
            {
                Type type = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { type = asm.GetType(SettingsTypeName, false); } catch { type = null; }
                    if (type != null) break;
                }
                if (type == null) return;

                var instance = type.GetProperty("Instance",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
                if (instance == null) return;

                if (!(type.GetField("targetResolution")?.GetValue(instance) is Vector2Int game)) return;
                if (!(type.GetField("referenceDisplayResolution")?.GetValue(instance) is Vector2Int display)) return;

                int gameH = Mathf.Max(1, game.y), displayH = Mathf.Max(1, display.y);
                _targetScale = Mathf.Max(1, displayH / gameH);
                _fromSettings = true;
                _summary = _targetScale + " screen pixels per game pixel (a " + display.x + "x" + display.y
                         + " display showing " + game.x + "x" + game.y + ")";
            }
            catch
            {
                // A malformed or half-loaded settings type must not take a preview window down with it.
            }
        }

        /// <summary>The zoom a control is allowed to hold.</summary>
        public static int Clamp(int zoom) => Mathf.Clamp(zoom, MinZoom, MaxZoom);

        /// <summary>A zoom stored before this rule existed — a free float such as 3.4, or Pyre's old
        /// 4-means-4-points — read back as the nearest whole zoom. Preview state is never authored data, so
        /// this migrates on read and nothing has to be written anywhere.</summary>
        public static int Migrate(float storedZoom)
            => Clamp(storedZoom <= 0f ? MinZoom : Mathf.RoundToInt(storedZoom));

        /// <summary>Whole SCREEN pixels one simulated pixel occupies at this zoom — the number every
        /// placement below is built from.</summary>
        public static int DeviceScale(int zoom) => Clamp(zoom) * TargetScale;

        /// <summary>The same thing in GUI points, for a host that has to size a layout box rather than draw a
        /// texture. Fractional by nature (a whole number of device pixels rarely is a whole number of points),
        /// and must not be rounded by the caller — rounding in point space is the bug ZuiPixel exists to
        /// remove.</summary>
        public static float PointsPerPixel(int zoom)
            => DeviceScale(zoom) / Mathf.Max(0.01f, EditorGUIUtility.pixelsPerPoint);

        /// <summary>Where a buffer of this size goes inside this viewport at this zoom: centred, every
        /// simulated pixel an identical whole block of device pixels, the corner snapped onto a device-pixel
        /// boundary. A picture bigger than the viewport is placed bigger and clipped by the container — the
        /// zoom is never quietly reduced to make it fit, that is what Fit is for.</summary>
        public static ZuiPixelPlacement Place(Rect viewport, Vector2 panelOrigin, int width, int height, int zoom)
            => ZuiPixel.FitLargest(viewport, panelOrigin).PlaceExact(width, height, DeviceScale(zoom));

        /// <inheritdoc cref="Place(Rect,Vector2,int,int,int)"/>
        /// <remarks>Takes the panel origin off the container itself — the usual call from a window whose
        /// preview canvas sits somewhere inside it rather than at its corner.</remarks>
        public static ZuiPixelPlacement Place(Rect viewport, VisualElement container, int width, int height, int zoom)
            => Place(viewport, ZuiPixel.PanelOrigin(container), width, height, zoom);

        /// <summary>Fit and draw in one call, for a stage that already has its texture. Returns the placement
        /// so the caller can report the size actually used.</summary>
        public static ZuiPixelPlacement Draw(Rect viewport, VisualElement container, Texture buffer, int zoom,
            Material material = null)
        {
            if (buffer == null) return default;
            var placement = Place(viewport, container, buffer.width, buffer.height, zoom);
            ZuiPixel.Draw(placement, buffer, material);
            return placement;
        }

        /// <summary>The largest whole zoom at which a buffer of this size fits inside this viewport — what the
        /// Fit button sets. Never below <see cref="MinZoom"/>: a pane too small to hold even one game-scale
        /// copy gets zoom 1 and a clipped picture, which is honest, rather than a fractional zoom, which
        /// would contradict the rule this file exists to state.</summary>
        public static int FitZoom(Rect viewport, int width, int height)
        {
            float ppp = Mathf.Max(0.01f, EditorGUIUtility.pixelsPerPoint);
            float deviceW = Mathf.Max(0f, viewport.width) * ppp;
            float deviceH = Mathf.Max(0f, viewport.height) * ppp;
            int block = Mathf.Max(1, TargetScale);
            int byW = Mathf.FloorToInt(deviceW / (Mathf.Max(1, width) * block));
            int byH = Mathf.FloorToInt(deviceH / (Mathf.Max(1, height) * block));
            return Clamp(Mathf.Min(byW, byH));
        }

        /// <inheritdoc cref="FitZoom(Rect,int,int)"/>
        public static int FitZoom(float viewportWidth, float viewportHeight, int width, int height)
            => FitZoom(new Rect(0f, 0f, viewportWidth, viewportHeight), width, height);

        /// <summary>The tooltip every zoom control in the package carries, so the number's meaning is one
        /// hover away in every tool. <paramref name="what"/> names the thing being magnified ("the preview",
        /// "the paint canvas") and is used to end the sentence naturally.</summary>
        public static string ZoomTooltip(string what)
            => "How big " + (string.IsNullOrEmpty(what) ? "the preview" : what) + " is drawn. Zoom 1 shows "
             + "each simulated pixel at the size it has in game — " + TargetScaleSummary + " — snapped to "
             + "whole screen pixels so every pixel is an identical block. Zoom 2 is twice that, and so on. "
             + "Cosmetic: the same rendered pixels are drawn larger, never re-rendered, so a bake is "
             + "unaffected.";

        /// <summary>The Fit button's tooltip, worded as what pressing it does.</summary>
        public static string FitTooltip(string what)
            => "Set Zoom to the largest whole zoom at which " + (string.IsNullOrEmpty(what) ? "the preview" : what)
             + " fits the pane, and show that number in Zoom — the magnification stays a number you can read "
             + "and dial, never a hidden fit.";

        /// <summary>The standard zoom control: an integer <c>Z.MicroSlider</c> over the shared range plus a
        /// Fit button that writes its result back into the slider. Every pixel stage builds its zoom this way
        /// so the control means, reads and behaves the same in every tool.</summary>
        /// <param name="zoom">The zoom to show now.</param>
        /// <param name="onZoom">Applies a new zoom to the host's own preview state.</param>
        /// <param name="fitZoom">Computes the Fit answer at the moment Fit is pressed — the pane's size is
        /// only known then, so this is a callback rather than a value.</param>
        /// <param name="what">Names the thing being magnified, for both tooltips.</param>
        /// <param name="slider">The slider itself, for a host that has to re-show the zoom later (a document
        /// swap, an external change).</param>
        public static VisualElement ZoomControl(int zoom, Action<int> onZoom, Func<int> fitZoom, string what,
            out ZuiMicroSlider slider, float sliderWidth = 150f)
        {
            var s = Z.MicroSlider("Zoom", Clamp(zoom), MinZoom, MaxZoom, ZoomTooltip(what),
                v => onZoom?.Invoke(Clamp(Mathf.RoundToInt(v))), sliderWidth, showValue: true, decimals: 0);
            slider = s;

            var fit = Z.Button("Fit", FitTooltip(what), () =>
            {
                int z = Clamp(fitZoom != null ? fitZoom() : MinZoom);
                s.value = z;                 // shows the number BEFORE it is applied, so Fit is never invisible
                onZoom?.Invoke(z);
            });
            fit.style.width = 38f;
            fit.style.flexShrink = 0f;

            var row = Z.Row(s, fit);
            row.style.flexShrink = 0f;
            return row;
        }
    }
}
