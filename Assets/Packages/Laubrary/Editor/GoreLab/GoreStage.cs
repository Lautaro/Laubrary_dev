// GoreStage — the frame being tagged, with the members drawn over it, and the surface every authoring gesture
// happens on. Modelled on MetaMapper's MetaStage (and through it the Tileset Builder's SheetStage):
//   • a checkerboard behind the art, so transparent never reads as black art;
//   • the sprite as a child image, the baked overlays (member highlight, paint masks, the wound preview) as images
//     over it, and the Painter2D overlay as the LAST child, because Painter2D content draws beneath an element's
//     children;
//   • wheel zoom toward the pointer and middle-drag pan from ZuiPanZoom, with the view kept by the window so a rebuild
//     (undo, a frame change) does not throw the zoom away;
//   • text on the canvas (the U F E W S letters, the guide, the warning, the status) as pooled labels in fixed slots,
//     so nothing that appears ever reflows the stage.
//
// COORDINATES. Sprite-local pixels (the rig's own: top-left origin, y down) → canvas pixels (+ the sprite's place on
// a canvas as large as the biggest frame, bottom-aligned and centred, so frames of different sizes stand still) →
// local GUI pixels (× Scale, + Origin). Both are y-down, so there is no flip anywhere in this file.
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.GoreLab.Editor
{
    internal sealed partial class GoreStage : VisualElement
    {
        readonly GoreLabWindow w;
        readonly ZuiPanZoom view;
        readonly VisualElement checker;
        readonly Image sprite, highlight, paint, preview;
        readonly VisualElement overlay;
        readonly Label[] letters = new Label[5];
        readonly Label guide, warning, status;
        Texture2D checkerTex, highlightTex, paintTex, previewTex;

        string flashText;
        double flashUntil;

        public GoreStage(GoreLabWindow window)
        {
            w = window;
            AddToClassList("zui-stage");
            style.flexGrow = 1f;
            style.minHeight = 0f;
            style.overflow = Overflow.Hidden;
            focusable = true;
            tooltip = "The frame and its members. Wheel zooms toward the pointer, middle-drag pans. What a left-drag does is set by the tab.";

            checker = Child(new VisualElement());
            sprite = Child(new Image { scaleMode = ScaleMode.StretchToFill });
            highlight = Child(new Image { scaleMode = ScaleMode.StretchToFill });
            paint = Child(new Image { scaleMode = ScaleMode.StretchToFill });
            preview = Child(new Image { scaleMode = ScaleMode.StretchToFill });

            overlay = Child(new VisualElement());
            overlay.style.left = overlay.style.top = overlay.style.right = overlay.style.bottom = 0f;
            overlay.generateVisualContent += PaintOverlay;

            string[] names = { "S", "W", "E", "F", "U" };
            for (int i = 0; i < letters.Length; i++)
            {
                var l = Child(new Label(names[i]));
                l.style.width = 14f;
                l.style.height = 14f;
                l.style.fontSize = 11f;
                l.style.unityFontStyleAndWeight = FontStyle.Bold;
                l.style.unityTextAlign = TextAnchor.MiddleCenter;
                l.style.paddingLeft = l.style.paddingRight = l.style.paddingTop = l.style.paddingBottom = 0f;
                letters[i] = l;
            }

            guide = Child(new Label());
            guide.style.left = guide.style.right = 0f;
            guide.style.top = 8f;
            guide.style.height = 16f;
            guide.style.unityTextAlign = TextAnchor.MiddleCenter;
            guide.style.color = new Color(1f, 1f, 1f, 0.6f);
            Fixed(guide);

            warning = Child(new Label());
            warning.style.left = 8f;
            warning.style.right = 8f;
            warning.style.top = 26f;
            warning.style.height = 16f;
            warning.style.unityFontStyleAndWeight = FontStyle.Bold;
            Fixed(warning);

            status = Child(new Label());
            status.style.left = 8f;
            status.style.right = 8f;
            status.style.bottom = 4f;
            status.style.height = 16f;
            status.style.fontSize = 11f;
            status.style.color = new Color(1f, 1f, 1f, 0.75f);
            Fixed(status);

            view = new ZuiPanZoom(this) { Zoom = w.stageZoom, Pan = w.stagePan, MaxZoom = 32f };
            view.ViewChanged += () => { w.stageZoom = view.Zoom; w.stagePan = view.Pan; Refresh(); };

            RegisterCallback<GeometryChangedEvent>(_ => Refresh());
            RegisterGestures();
        }

        T Child<T>(T e) where T : VisualElement
        {
            e.pickingMode = PickingMode.Ignore;
            e.style.position = Position.Absolute;
            Add(e);
            return e;
        }

        /// A permanently reserved one-line slot: its text changes, its geometry never does.
        static void Fixed(Label l)
        {
            l.style.whiteSpace = WhiteSpace.NoWrap;
            l.style.overflow = Overflow.Hidden;
            l.style.textOverflow = TextOverflow.Ellipsis;
        }

        public float Scale => view.Scale;

        public void Dispose()
        {
            if (checkerTex != null) Object.DestroyImmediate(checkerTex);
            if (highlightTex != null) Object.DestroyImmediate(highlightTex);
            if (paintTex != null) Object.DestroyImmediate(paintTex);
            if (previewTex != null) Object.DestroyImmediate(previewTex);
            checkerTex = highlightTex = paintTex = previewTex = null;
        }

        internal void Flash(string text)
        {
            flashText = text;
            flashUntil = EditorApplication.timeSinceStartup + 2.5;
            schedule.Execute(Refresh).StartingIn(2600);
            Refresh();
        }

        // ── coordinates ─────────────────────────────────────────────────────────────────────────────

        ShownFrame Shown => w.shown;

        internal Vector2 SpriteToLocal(double x, double y)
        {
            var s = Shown;
            float ox = s != null ? s.sx : 0, oy = s != null ? s.sy : 0;
            return view.Origin + new Vector2((float)x + ox, (float)y + oy) * view.Scale;
        }

        internal Vector2 LocalToSprite(Vector2 local)
        {
            var s = Shown;
            float ox = s != null ? s.sx : 0, oy = s != null ? s.sy : 0;
            var c = (local - view.Origin) / Mathf.Max(0.0001f, view.Scale);
            return new Vector2(c.x - ox, c.y - oy);
        }

        // ── refresh ─────────────────────────────────────────────────────────────────────────────────

        public void Refresh()
        {
            bool laidOut = Layout();
            SyncOverlays();
            PlaceLetters();
            UpdateTexts();
            overlay.MarkDirtyRepaint();
            overlay.BringToFront();
            foreach (var l in letters) l.BringToFront();
            guide.BringToFront();
            warning.BringToFront();
            status.BringToFront();
            if (!laidOut) overlay.MarkDirtyRepaint();
        }

        bool Layout()
        {
            var s = Shown;
            bool show = s != null && w.canvas.x > 0 && w.canvas.y > 0;
            checker.style.display = sprite.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return false;
            if (!view.Layout(new Vector2(w.canvas.x, w.canvas.y))) return false;
            w.stagePan = view.Pan;

            EnsureChecker();
            checker.style.left = view.Origin.x;
            checker.style.top = view.Origin.y;
            checker.style.width = w.canvas.x * view.Scale;
            checker.style.height = w.canvas.y * view.Scale;

            sprite.image = w.tab == GoreLabWindow.Tab.Test && w.resultValid && w.resultTex != null ? w.resultTex : s.pixels.texture;
            var a = SpriteToLocal(0, 0);
            foreach (var img in new[] { sprite, highlight, paint, preview })
            {
                img.style.left = a.x;
                img.style.top = a.y;
                img.style.width = s.W * view.Scale;
                img.style.height = s.H * view.Scale;
            }
            return true;
        }

        void EnsureChecker()
        {
            if (checkerTex == null)
            {
                var dark = new Color32(52, 52, 52, 255);
                var light = new Color32(68, 68, 68, 255);
                checkerTex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Repeat,
                };
                checkerTex.SetPixels32(new[] { dark, light, light, dark });
                checkerTex.Apply();
            }
            checker.style.backgroundImage = Background.FromTexture2D(checkerTex);
            checker.style.backgroundRepeat = new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat);
            checker.style.backgroundSize = new BackgroundSize(new Length(16f, LengthUnit.Pixel), new Length(16f, LengthUnit.Pixel));
        }

        // ── baked overlays: per-pixel layers are textures, never thousands of Painter2D quads ──────────

        static Color32[] s_layer;

        void SyncOverlays()
        {
            var s = Shown;
            bool edit = s != null && w.tab != GoreLabWindow.Tab.Test;
            bool hasTag = false;
            MemberTag tag = default;
            int[] behind = null, exempt = null;
            if (edit) hasTag = w.TryShownMember(w.memberIndex, out tag, out behind, out exempt, out _);

            highlight.style.display = edit && hasTag ? DisplayStyle.Flex : DisplayStyle.None;
            paint.style.display = edit && hasTag ? DisplayStyle.Flex : DisplayStyle.None;
            preview.style.display = s != null && w.tab == GoreLabWindow.Tab.Test && w.previewMask != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (s == null) return;
            int W = s.W, H = s.H, n = W * H;
            if (s_layer == null || s_layer.Length != n) s_layer = new Color32[n];

            if (edit && hasTag)
            {
                // Every pixel inside the outline lights up (solid bright, empty faint), so what is marked is obvious.
                var c = w.MemberColour(w.memberIndex);
                byte r = (byte)(c.r * 255), g = (byte)(c.g * 255), b = (byte)(c.b * 255);
                var ex = new System.Collections.Generic.HashSet<int>(exempt);
                var grid = s.pixels.grid;
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int k = y * W + x;
                        bool on = !ex.Contains(k) && GoreTagEdit.Inside(tag, x + 0.5, y + 0.5);
                        s_layer[k] = on ? new Color32(r, g, b, grid.Solid(x, y) ? (byte)51 : (byte)18) : default;
                    }
                highlightTex = Bake(highlightTex, W, H);
                highlight.image = highlightTex;

                System.Array.Clear(s_layer, 0, n);
                foreach (int k in behind) if (k >= 0 && k < n) s_layer[k] = new Color32(255, 130, 40, 153);
                foreach (int k in exempt) if (k >= 0 && k < n) s_layer[k] = new Color32(190, 90, 255, 166);
                paintTex = Bake(paintTex, W, H);
                paint.image = paintTex;
                paint.tintColor = new Color(1f, 1f, 1f, w.paintAlpha);
            }

            if (w.tab == GoreLabWindow.Tab.Test && w.previewMask != null && w.previewMask.Length == n)
            {
                for (int k = 0; k < n; k++) s_layer[k] = w.previewMask[k] != 0 ? new Color32(255, 190, 40, 128) : default;
                previewTex = Bake(previewTex, W, H);
                preview.image = previewTex;
            }
        }

        static Texture2D Bake(Texture2D tex, int W, int H)
        {
            if (tex == null) tex = GoreSpritePixels.NewTexture(W, H);
            GoreSpritePixels.Write(tex, s_layer, W, H);
            return tex;
        }

        // ── text ────────────────────────────────────────────────────────────────────────────────────

        void UpdateTexts()
        {
            var s = Shown;
            string member = w.MemberName(w.memberIndex);
            string memberLow = member.ToLowerInvariant();

            string g = "";
            if (s == null) g = w.HasFrames ? "This frame could not be read" : "No frames: set the rig's target on the Frame tab";
            else if (w.tab == GoreLabWindow.Tab.Test)
            {
                if (w.RemoverCount == 0) g = w.ActiveRecipe == null ? "No damage type: add one on the left" : "Drag across the frame to wound it";
            }
            else if (s.mirrored) g = $"Mirror of {s.group.source?.label}: read only, tag the drawn direction";
            else if (!w.TryActiveTag(out _))
                g = w.tab == GoreLabWindow.Tab.Shape || w.tab == GoreLabWindow.Tab.Frame
                    ? $"Drag out a box over the {memberLow}"
                    : $"Draw the {memberLow} first on the Shape tab";
            if (flashText != null && EditorApplication.timeSinceStartup < flashUntil) g = flashText;
            else flashText = null;
            guide.text = g;

            string warn = null;
            var warnColour = new Color(0.9f, 0.64f, 0.1f);
            if (s != null && w.tab == GoreLabWindow.Tab.Test)
            {
                if (w.testState == GoreLabWindow.TestState.NotSetUp)
                { warn = "This frame is not set up for this damage: it plays as drawn"; warnColour = new Color(0.9f, 0.2f, 0.18f); }
                else if (w.testState == GoreLabWindow.TestState.Hidden)
                    warn = "The wound is on a side this frame does not show";
            }
            else if (s != null) warn = w.OrientationWarning();
            warning.text = warn != null ? "⚠ " + warn : "";
            warning.style.color = warnColour;

            if (s == null) { status.text = ""; return; }
            string where = $"{s.group.label} {s.index + 1}/{s.group.sprites.Count}";
            if (s.mirrored) where += " · mirror, read only";
            string zoom = $"{view.Scale * 100f:0}%";
            if (w.tab == GoreLabWindow.Tab.Test)
                status.text = $"{where} · {w.RemoverCount} remover(s) · {zoom}";
            else
            {
                var mf = GoreLabWindow.MemberAt(s.tags, w.memberIndex, false);
                string skip = mf != null && mf.skip ? $" · no {memberLow} here" : "";
                status.text = $"{where}{skip} · {zoom}";
            }
        }
    }
}
