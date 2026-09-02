using Laubrary.AssetKit.Editor;
using Laubrary.PreviewKit;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    /// A CustomEditor for ChunkSpec that is deliberately NOT a field editor. Every serialized value on a
    /// ChunkSpec is authored in the Chunks window (checked field by field — emission, physics, life/look,
    /// the sprite list, floor, sampled debris, tint, the pixel-modifier stack, the animation and trail
    /// sources, all seven Chunks 2.0 modules, hit detection), so rendering the default inspector here would
    /// only re-render that same data as native checkboxes, bare numbers and native enum dropdowns beside the
    /// ZUI ones — exactly the reason the sibling ChunkFollowEmitterEditor refuses to fall back to it.
    ///
    /// What the inspector owns instead are the two jumps only IT is positioned to offer: into the Chunks
    /// window for this spec (the primary affordance — this is where the spec is actually edited), and into
    /// the animation source's own authoring tool (Pyre, the Laumination Builder, ...) when one is registered
    /// via LauAssetEditors — Chunks itself never references those tools.
    ///
    /// Nothing here edits asset data, so there is no Undo wrapper to route through: both controls open a
    /// window and the preview is read-only.
    ///
    /// Layout: the action row, the preview slot and the status line are all PERMANENTLY reserved — fixed
    /// sizes, the optional pieces switched via `visibility` (which keeps its layout space) and the status
    /// line's TEXT changing rather than its geometry — so a source arriving, or a preview failing to render,
    /// never reflows the inspector under the user's cursor (ui-layout-rules.md → "Stable workspace"). Only a
    /// texture blit is needed for the preview, which <see cref="Image"/> (ScaleToFit) does directly, so no
    /// IMGUI island is involved; the Z controls are scoped to this root by Z.Attach and affect nothing else
    /// in the Inspector.
    [CustomEditor(typeof(ChunkSpec))]
    public class ChunkSpecEditor : UnityEditor.Editor
    {
        const double AnimateInterval = 1.0 / 12.0;
        const float PreviewBox = 96f;
        const float ButtonW = 140f;
        const float StatusH = 16f;

        Texture2D previewTex;
        Object lastSource;
        double lastTick;
        // A source that renders no preview must be asked ONCE, not on every tick — rendering one can be
        // genuinely expensive (a Pyre blast bakes frames), and a null result would otherwise re-bake it
        // twelve times a second for as long as the inspector is alive.
        bool previewUnavailable;
        // Cached so the two visibility switches are written only when they actually change, rather than
        // twelve times a second.
        bool imageVisible, editVisible;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            // MANDATORY first line for any root ZUI does not own: without it every Z control renders
            // unstyled, and no headless probe would catch it.
            Z.Attach(root);
            root.style.paddingTop = 6f;

            var openButton = Z.Button("Open in Chunks Window",
                "Open this Chunk Spec in the Chunks window — the authoring tool for every value on it.",
                () =>
                {
                    if (target is ChunkSpec spec) ChunkWindow.OpenFor(spec);
                }).W(ButtonW);

            var editButton = Z.Button("Edit Source…",
                "Open the animation source in its own authoring tool.", null).W(ButtonW);

            // ONE fixed-height, non-wrapping row holding every action at all times. The optional one is
            // switched by visibility and sits LAST, so nothing can be pushed when it comes and goes.
            var actions = Z.Row(openButton, Z.HSpace(), editButton);
            actions.style.flexWrap = Wrap.NoWrap;
            root.Add(actions);

            var section = Z.Section("Animation preview",
                "The optional animated content every chunk plays, drawn by the asset itself.");
            root.Add(section);

            // Permanently reserved single line: fixed height, no wrap, truncate rather than grow. Only its
            // text ever changes, so nothing below it can move while the user is aiming at a control.
            var message = Z.Text("", ZuiText.Subtle, "What the preview below is showing, or why there is nothing to show.");
            message.style.height = StatusH;
            message.style.whiteSpace = WhiteSpace.NoWrap;
            message.style.overflow = Overflow.Hidden;
            section.Add(message);

            // A permanently reserved preview stage, matching the Chunks window's own. The frame always
            // occupies its 96px; only the frame's CONTENT appears and disappears, via visibility.
            var slot = new VisualElement { tooltip = "The animation source's own preview frame." };
            slot.style.width = PreviewBox;
            slot.style.height = PreviewBox;
            slot.style.backgroundColor = new Color(0.11f, 0.12f, 0.15f);

            var image = new Image { scaleMode = ScaleMode.ScaleToFit };
            image.style.width = PreviewBox;
            image.style.height = PreviewBox;
            image.style.visibility = Visibility.Hidden;
            slot.Add(image);
            section.Add(slot);

            void Refresh()
            {
                var source = AnimationSourceOf(target as ChunkSpec);

                if (source != lastSource)
                {
                    DisposePreview();
                    image.image = null;
                    lastSource = source;
                    previewUnavailable = false;
                }

                bool hasImage = false;
                string text;
                if (source == null)
                {
                    // No animation source still means a LOOK — procedural squares tinted along colorOverLife —
                    // and the spec can draw it. Leaving the reserved 96px slot empty would read as a picture
                    // that failed to load, which is the one thing the thumbnail rules forbid outright.
                    var self = target as ChunkSpec;
                    if (previewTex == null && !previewUnavailable && self != null)
                    {
                        previewTex = ((IVisualPreview)self).RenderPreviewTexture();
                        previewUnavailable = previewTex == null;
                        image.image = previewTex;
                    }
                    hasImage = previewTex != null;
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
                    text = hasImage
                        ? $"Showing '{source.name}'."
                        : $"'{source.name}' produced no preview frame.";
                }
                else
                {
                    text = $"{source.GetType().Name} does not implement IVisualPreview — no preview available.";
                }

                if (message.text != text) message.text = text;
                if (imageVisible != hasImage)
                {
                    imageVisible = hasImage;
                    image.style.visibility = hasImage ? Visibility.Visible : Visibility.Hidden;
                }

                bool canOpen = source != null && LauAssetEditors.CanOpen(source);
                if (editVisible != canOpen)
                {
                    editVisible = canOpen;
                    editButton.style.visibility = canOpen ? Visibility.Visible : Visibility.Hidden;
                }
                // Reads for the CURRENT state — never "when a source is set…" wording while there is none.
                editButton.tooltip = canOpen
                    ? $"Open '{source.name}' in its own authoring tool."
                    : "No animation source with a registered authoring tool.";
            }

            editButton.clicked += () =>
            {
                var source = AnimationSourceOf(target as ChunkSpec);
                if (source != null) LauAssetEditors.Open(source);
            };

            // One scheduled tick covers both jobs the IMGUI version needed a per-frame OnInspectorGUI for:
            // noticing the source changed, and advancing an animated preview. It only runs while the block is
            // actually on screen — a UI Toolkit schedule keeps ticking on a hidden inspector otherwise.
            root.schedule.Execute(() =>
            {
                if (root.panel == null || root.resolvedStyle.display == DisplayStyle.None) return;
                Refresh();
                if (AnimationSourceOf(target as ChunkSpec) is IVisualPreview p && p.CanAnimatePreview && previewTex != null)
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

            // editVisible/imageVisible start false and both controls start hidden, so the first Refresh only
            // writes a style when the state actually differs from that.
            imageVisible = false;
            editVisible = false;
            editButton.style.visibility = Visibility.Hidden;
            Refresh();
            return root;
        }

        /// The recipe's animated debris content, if any capability names one. Read through the stack rather
        /// than off a field on the spec: which capability owns the animation is the recipe's business, and an
        /// inspector that reached for a fixed field would go blind the moment a second scatter was added.
        static Object AnimationSourceOf(ChunkSpec spec)
        {
            var debris = spec != null ? spec.FirstEnabled<DebrisScatter>() : null;
            return debris != null ? debris.animationSource : null;
        }

        void DisposePreview()
        {
            if (previewTex != null) DestroyImmediate(previewTex);
            previewTex = null;
        }

        void OnDisable() => DisposePreview();
    }
}
