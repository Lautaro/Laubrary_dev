using Laubrary.AssetKit.Editor;
using Laubrary.PreviewKit;
using Laubrary.Zui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    /// A CustomEditor that augments ChunkSpec's default inspector with a live preview of its optional
    /// animationSource, plus a jump straight into that animation's own authoring tool (Pyre, the Animation
    /// Builder, ...) when one is registered via LauAssetEditors — Chunks itself never references those tools.
    ///
    /// UI TOOLKIT PORT (ZUI → UI Toolkit migration): fully native. The preview needs no IMGUI island — it is
    /// only a texture blit, which <see cref="Image"/> (ScaleToFit) does directly. The IMGUI original had to
    /// stay off ZUI entirely because ZUI's IMGUI styles were globally scoped and would have leaked into the
    /// rest of the Inspector; a UI Toolkit stylesheet is attached per-element, so the Z controls used here
    /// affect nothing outside this block.
    [CustomEditor(typeof(ChunkSpec))]
    public class ChunkSpecEditor : UnityEditor.Editor
    {
        const double AnimateInterval = 1.0 / 12.0;
        const float PreviewBox = 96f;

        Texture2D previewTex;
        Object lastSource;
        double lastTick;
        // A source that renders no preview must be asked ONCE, not on every tick — rendering one can be
        // genuinely expensive (a Pyre blast bakes frames), and a null result would otherwise re-bake it
        // twelve times a second for as long as the inspector is alive.
        bool previewUnavailable;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);

            var block = new VisualElement();
            Z.Attach(block);
            block.style.paddingTop = 6f;
            root.Add(block);

            var section = Z.Section("Animation preview",
                "The optional animated content every chunk plays, drawn by the asset itself.");
            block.Add(section);

            var image = new Image { scaleMode = ScaleMode.ScaleToFit, tooltip = "The animation source's own preview frame." };
            image.style.width = PreviewBox;
            image.style.height = PreviewBox;
            image.style.backgroundColor = new Color(0.11f, 0.12f, 0.15f);

            var message = Z.Text("", ZuiText.Subtle, "Why there's no preview to show.");
            var editButton = Z.Button("Edit…", "Open the animation source in its own authoring tool.", null);

            section.Add(message);
            section.Add(image);
            section.Add(editButton);

            void Refresh()
            {
                var spec = target as ChunkSpec;
                var source = spec != null ? spec.animationSource : null;

                if (source != lastSource) { DisposePreview(); lastSource = source; previewUnavailable = false; }

                bool hasImage = false;
                string text = null;
                if (source == null)
                {
                    text = "No animation source — chunks use a static/procedural sprite.";
                }
                else if (source is IVisualPreview preview)
                {
                    if (previewTex == null && !previewUnavailable)
                    {
                        previewTex = preview.RenderPreviewTexture();
                        previewUnavailable = previewTex == null;
                        image.image = previewTex;
                    }
                    hasImage = previewTex != null;
                    if (!hasImage) text = $"'{source.name}' produced no preview frame.";
                }
                else
                {
                    text = $"{source.GetType().Name} does not implement IVisualPreview — no preview available.";
                }

                if (message.text != (text ?? "")) message.text = text ?? "";
                image.Shown(hasImage);
                message.Shown(text != null);

                bool canOpen = source != null && LauAssetEditors.CanOpen(source);
                editButton.Shown(canOpen);
                string wanted = canOpen ? $"Edit '{source.name}'…" : editButton.text;
                if (editButton.text != wanted) editButton.text = wanted;
            }

            editButton.clicked += () =>
            {
                var spec = target as ChunkSpec;
                if (spec != null && spec.animationSource != null) LauAssetEditors.Open(spec.animationSource);
            };

            // One scheduled tick covers both jobs the IMGUI version needed a per-frame OnInspectorGUI for:
            // noticing the source changed, and advancing an animated preview. It only runs while the block is
            // actually on screen — a UI Toolkit schedule keeps ticking on a hidden inspector otherwise.
            root.schedule.Execute(() =>
            {
                if (root.panel == null || root.resolvedStyle.display == DisplayStyle.None) return;
                Refresh();
                var spec = target as ChunkSpec;
                if (spec?.animationSource is IVisualPreview p && p.CanAnimatePreview && previewTex != null)
                {
                    double now = EditorApplication.timeSinceStartup;
                    if (now - lastTick >= AnimateInterval)
                    {
                        lastTick = now;
                        p.UpdateAnimatedPreview(previewTex, now);
                        image.MarkDirtyRepaint();
                    }
                }
            }).Every((long)(AnimateInterval * 1000));

            Refresh();
            return root;
        }

        void DisposePreview()
        {
            if (previewTex != null) DestroyImmediate(previewTex);
            previewTex = null;
        }

        void OnDisable() => DisposePreview();
    }
}
