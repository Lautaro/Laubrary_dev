using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.BackSplash.Editor
{
    /// <summary>
    /// Browse/create/duplicate/rename/delete <see cref="BackSplash"/> assets (free from
    /// <see cref="ZuiAssetWindow{T}"/>, same base Pyre/Mirage use). A BackSplash is recallable from any
    /// tool's preview via a plain ObjectField — no bespoke cross-tool picker needed, see Pyre's own
    /// "Preview backdrop" box and Mirage's per-view Background field.
    ///
    /// UI TOOLKIT PORT (ZUI → UI Toolkit migration): every control surface is Laubrary.Zui (Z.*) retained-mode
    /// controls. The ONE deliberate exception is the PREVIEW viewport, which stays IMGUI inside an
    /// IMGUIContainer — it is bespoke canvas painting (a tinted, zoomed, offset texture blit clipped to the
    /// viewport) plus a direct-manipulation drag gizmo; <see cref="DrawPreview"/> and <see cref="HandleDrag"/>
    /// are carried over unchanged so behaviour cannot regress. The IMGUI drag and the retained position
    /// controls stay in sync in one direction each: dragging the preview pushes the new value into the pad and
    /// the numeric fields (<see cref="PushPosition"/>), and editing those repaints the preview.
    /// </summary>
    public class BackSplashWindow : ZuiAssetWindow<BackSplash>
    {
        [MenuItem("Laubrary/BackSplash")]
        public static void Open() => GetWindow<BackSplashWindow>("BackSplash");

        /// Same OpenFor shape as PyreWindow/MirageWindow — the entry point LauAssetEditors' Open registration
        /// uses to jump straight into a specific BackSplash picked elsewhere.
        public static void OpenFor(BackSplash b)
        {
            var w = GetWindow<BackSplashWindow>("BackSplash");
            if (b != null) w.SetAsset(b);
        }

        protected override string TypeLabel => "BackSplash";
        protected override string NewAssetName => "BackSplash";
        protected override string DefaultFolder => "Assets/BackSplash";

        protected override Texture2D RenderThumbnail(BackSplash item) => null;   // default asset icon is fine

        bool dragging;

        // retained elements the IMGUI island writes back into
        IMGUIContainer previewContainer;
        ZuiPad posPad;
        FloatField posXField, posYField;

        BackSplash Bs => Current;

        // ── mutation helper (the Undo contract every control routes through) ─────────────────
        void Dial(string undoLabel, System.Action apply)
        {
            var bs = Bs;
            if (bs == null) return;
            Undo.RecordObject(bs, undoLabel);
            apply();
            EditorUtility.SetDirty(bs);
            previewContainer?.MarkDirtyRepaint();
        }

        protected override void OnBeforeRebuild()
        {
            previewContainer = null; posPad = null; posXField = null; posYField = null;
        }

        protected override void BuildAsset(VisualElement root, BackSplash bs)
        {
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            var body = scroll.contentContainer;

            const string cameraTip = "Solid colour a real Camera clears to behind the image (drawn as a flat fill by tools that paint their preview instead).";
            body.Add(Z.Text("Camera", ZuiText.Section, "How the backdrop's empty space is filled."));
            body.Add(Z.Field("Colour", cameraTip,
                Z.Color(bs.cameraColor, cameraTip, v => Dial("Edit BackSplash colour", () => bs.cameraColor = v), 110f)));

            body.Add(Z.VSpace());
            body.Add(Z.Text("Image", ZuiText.Section, "The one picture drawn over the camera colour."));

            const string spriteTip = "The backdrop picture. Leave empty for a plain colour backdrop.";
            body.Add(Z.Field("Sprite", spriteTip,
                Z.Object<Sprite>(bs.image, spriteTip, v => Dial("Edit BackSplash sprite", () => bs.image = v), 200f)));

            const string tintTip = "Multiplies the image's own colours — dim it, or wash it toward a hue.";
            body.Add(Z.Field("Tint", tintTip,
                Z.Color(bs.imageTint, tintTip, v => Dial("Edit BackSplash tint", () => bs.imageTint = v), 110f)));

            const string zoomTip = "How much of the viewport the image fills — 1 fits the viewport exactly.";
            body.Add(Z.Field("Zoom", zoomTip,
                Z.Slider(bs.imageZoom, 0.05f, 16f, zoomTip,
                    v => Dial("Edit BackSplash zoom", () => bs.imageZoom = v), 150f)));

            body.Add(BuildPositionRow(bs));

            body.Add(Z.VSpace());
            body.Add(Z.Text("Preview — drag to reposition the image", ZuiText.Subtle,
                "Shows this backdrop exactly as a tool's viewport draws it. Left-drag anywhere inside to move the image."));
            previewContainer = new IMGUIContainer(DrawPreviewGUI)
            {
                tooltip = "The live backdrop. Left-drag to reposition the image (one undo step per drag)."
            };
            previewContainer.style.height = 140f;
            previewContainer.style.flexShrink = 0f;
            body.Add(previewContainer);

            root.Add(scroll);
        }

        // A spatial X/Y pair is a 2D-drag-target case, never two independent 1D fields (ui-layout-rules):
        // the pad IS the control, with the numeric fields kept beside it for exact entry — the one edit path
        // that used to be the ONLY way to type a value back to 0 after a runaway drag.
        VisualElement BuildPositionRow(BackSplash bs)
        {
            const float max = BackSplash.MaxImageOffset;
            const string posTip = "Offsets the image within the viewport. Drag the pad (or the preview below), or type exact values.";

            posPad = Z.Pad(bs.imagePos, new Rect(-max, -max, max * 2f, max * 2f), posTip, v =>
            {
                Dial("Reposition BackSplash image", () => bs.imagePos = BackSplash.ClampImagePos(v));
                posXField?.SetValueWithoutNotify(bs.imagePos.x);
                posYField?.SetValueWithoutNotify(bs.imagePos.y);
            }, 68f);

            posXField = Z.Float(bs.imagePos.x, "Horizontal offset in the viewport's own units.", v =>
            {
                Dial("Reposition BackSplash image", () => bs.imagePos = BackSplash.ClampImagePos(new Vector2(v, bs.imagePos.y)));
                if (posPad != null) posPad.Value = bs.imagePos;
            }, 68f);
            posYField = Z.Float(bs.imagePos.y, "Vertical offset in the viewport's own units.", v =>
            {
                Dial("Reposition BackSplash image", () => bs.imagePos = BackSplash.ClampImagePos(new Vector2(bs.imagePos.x, v)));
                if (posPad != null) posPad.Value = bs.imagePos;
            }, 68f);

            return Z.Field("Position", posTip, Z.Row(posPad,
                Z.Field("X", "Horizontal offset in the viewport's own units.", posXField),
                Z.Field("Y", "Vertical offset in the viewport's own units.", posYField)));
        }

        /// Push a value written by the IMGUI drag back into the retained controls.
        void PushPosition(Vector2 v)
        {
            if (posPad != null) posPad.Value = v;
            posXField?.SetValueWithoutNotify(v.x);
            posYField?.SetValueWithoutNotify(v.y);
        }

        // ── the IMGUI island (bespoke canvas painting + a direct-manipulation drag) ──────────
        void DrawPreviewGUI()
        {
            var bs = Bs;
            if (bs == null || previewContainer == null) return;
            var r = previewContainer.contentRect;
            var view = new Rect(0f, 0f, r.width, r.height);   // IMGUIContainer draws in its own local space
            if (Event.current.type == EventType.Repaint) DrawPreview(view, bs);
            if (HandleDrag(view, bs)) { EditorUtility.SetDirty(bs); PushPosition(bs.imagePos); }
        }

        static void DrawPreview(Rect view, BackSplash bs)
        {
            EditorGUI.DrawRect(view, bs.cameraColor);
            if (bs.image == null) return;

            var tex = bs.image.texture;
            if (tex == null) return;
            var r = bs.image.textureRect;
            var tc = new Rect(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height);

            float w = view.width * bs.imageZoom, h = view.height * bs.imageZoom;
            var imgRect = new Rect(view.x + (view.width - w) * 0.5f + bs.imagePos.x,
                                    view.y + (view.height - h) * 0.5f - bs.imagePos.y, w, h);

            var prevCol = GUI.color;
            GUI.color = bs.imageTint;
            GUI.BeginClip(view);
            GUI.DrawTextureWithTexCoords(new Rect(imgRect.x - view.x, imgRect.y - view.y, w, h), tex, tc, true);
            GUI.EndClip();
            GUI.color = prevCol;
        }

        // Direct-mutate-and-dirty rather than routing through Dial above — a drag is a single continuous
        // gesture (one Undo step bracketed by MouseDown/MouseUp), not a per-frame field commit.
        bool HandleDrag(Rect view, BackSplash bs)
        {
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && view.Contains(e.mousePosition))
            {
                Undo.RecordObject(bs, "Reposition BackSplash image");
                dragging = true;
                e.Use();
                return false;
            }
            if (dragging && e.type == EventType.MouseDrag)
            {
                // Was completely unbounded — the ONE edit path most likely to actually reach an extreme value,
                // since a fast/long mouse drag accumulates raw pixel deltas with nothing stopping it.
                bs.imagePos = BackSplash.ClampImagePos(bs.imagePos + new Vector2(e.delta.x, -e.delta.y));
                e.Use();
                return true;
            }
            if (dragging && e.type == EventType.MouseUp) { dragging = false; e.Use(); }
            return false;
        }
    }
}
