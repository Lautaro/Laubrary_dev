using System;
using System.Linq;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Launimator.Editor
{
    public partial class LauminationBuilderWindow
    {
        private bool _leftCollapsed;
        private string _animationSearch = "";
        private int _animationSort;
        private VisualElement _railItems;

        private void BuildAnimationRail(VisualElement root)
        {
            root.Add(Z.TextInput(_animationSearch, "Search animations by name.", v => { _animationSearch = v; FillAnimationRail(); }, 154));
            root.Add(Z.Segmented(_animationSort, new[] { "Authored", "Name" }, "Sort animations in authored order or alphabetically.", v => { _animationSort = v; FillAnimationRail(); }));
            root.Add(Z.Button("New animation", "Start an empty animation after resolving unsaved changes.", () =>
            {
                if (!ConfirmDocumentTransition()) return;
                StartNewAnimation(SuggestNewAnimName()); BeginCleanDocument();
                _workspaceMode = _sheet == null ? WorkspaceMode.Sheet : WorkspaceMode.Sprites;
                Refresh();
            }));
            _railItems = new VisualElement(); root.Add(_railItems); FillAnimationRail();
        }

        private void FillAnimationRail()
        {
            if (_railItems == null) return;
            _railItems.Clear();
            if (_boundLauminary != null)
            {
                var items = LauminaryRepo.EnsureDraft(_boundLauminary).animations.AsEnumerable();
                if (_animationSort == 1) items = items.OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase);
                foreach (var item in items)
                {
                    var animation = item;
                    if (!MatchesAnimation(animation.name)) continue;
                    AddAnimationCard(animation, NameEq(animation.name, _boundAnimName), () => SwitchToLauminaryAnimation(animation.name));
                }
            }
            else
            {
                var items = AnimationLibrary.Enumerate().Where(a => a != null && a.animation != null);
                if (_animationSort == 1) items = items.OrderBy(a => a.animation.name, StringComparer.OrdinalIgnoreCase);
                foreach (var item in items)
                {
                    var asset = item;
                    if (MatchesAnimation(asset.animation.name)) AddAnimationCard(asset.animation, asset == _orphanAsset, () => SwitchToOrphan(asset));
                }
            }
        }

        private bool MatchesAnimation(string name) => string.IsNullOrWhiteSpace(_animationSearch) || (name ?? "").IndexOf(_animationSearch, StringComparison.OrdinalIgnoreCase) >= 0;

        private void AddAnimationCard(Laumination animation, bool current, Action open)
        {
            int frameCount = animation.recipe?.Count > 0 ? animation.recipe.Count : animation.frames?.Count ?? 0;
            // The preview action is a sibling of the edit button. A nested Button's pointer-up can
            // activate its parent Clickable before a ClickEvent propagation handler can stop it.
            var card = Z.Row();
            card.AddToClassList("lau-builder-modes__animation");
            if (current) card.AddToClassList("zui-radio__on");
            var edit = Z.Button("", $"Edit {animation.name}; hover to preview. {frameCount} frames at {animation.fps:0.#} FPS.", open);
            edit.style.width = 118; edit.style.height = 44; edit.style.minWidth = 0;
            edit.style.flexShrink = 1; edit.style.flexDirection = FlexDirection.Row;
            edit.style.paddingLeft = 0; edit.style.paddingRight = 0;
            edit.style.marginLeft = 0; edit.style.marginRight = 0;
            card.Add(edit);
            var image = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.AddToClassList("lau-builder-modes__thumbnail");
            edit.Add(image);
            var name = Z.Text(animation.name, ZuiText.Small, animation.name);
            name.AddToClassList("lau-builder-modes__animation-name");
            edit.Add(name);
            var generated = new System.Collections.Generic.Dictionary<int, Texture2D>();
            void ShowFrame(int index)
            {
                var sprite = animation.frames != null && index < animation.frames.Count ? animation.frames[index] : null;
                if (sprite != null) { image.image = null; image.sprite = sprite; return; }
                if (!generated.TryGetValue(index, out var texture))
                {
                    texture = CreateAnimationRailFrame(animation, index);
                    generated[index] = texture;
                }
                image.sprite = null; image.image = texture;
                image.tooltip = texture != null ? animation.name : "This animation's source image is missing or unreadable.";
            }
            bool hover = false, playing = false; int frame = 0;
            double last = UnityEditor.EditorApplication.timeSinceStartup, remaining = 0;
            var preview = Z.IconButton("play", "Start or stop this thumbnail's animation preview.", () => { playing = !playing; remaining = 0; }, 22);
            preview.SetEnabled(frameCount > 0); card.Add(preview);
            card.RegisterCallback<MouseEnterEvent>(_ => { hover = true; last = UnityEditor.EditorApplication.timeSinceStartup; });
            card.RegisterCallback<MouseLeaveEvent>(_ => { hover = false; if (!playing && frameCount > 0) { frame = 0; ShowFrame(0); } });
            card.RegisterCallback<AttachToPanelEvent>(_ => { if (frameCount > 0) ShowFrame(frame); });
            card.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                playing = hover = false; image.sprite = null; image.image = null;
                foreach (var texture in generated.Values) if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                generated.Clear();
            });
            card.schedule.Execute(() =>
            {
                double now = UnityEditor.EditorApplication.timeSinceStartup; double dt = now - last; last = now;
                if ((!hover && !playing) || frameCount == 0) return;
                remaining -= dt;
                if (remaining > 0) return;
                frame = (frame + 1) % frameCount; ShowFrame(frame);
                float timing = animation.recipe != null && frame < animation.recipe.Count ? animation.recipe[frame].timingPercent : 0;
                remaining = FrameRef.TimingFactorOf(timing) / Mathf.Max(1, animation.fps);
            }).Every(25);
            _railItems.Add(card);
        }

        // Standalone assets retain source recipes without a baked atlas. Make just the first picture
        // at attachment; other frames are cached lazily during audition and disposed with their card.
        private Texture2D CreateAnimationRailFrame(Laumination animation, int index)
        {
            if (animation.recipe == null || index < 0 || index >= animation.recipe.Count) return null;
            var frame = animation.recipe[index];
            if (frame == null) return null;
            string guid = string.IsNullOrEmpty(frame.sourceTextureGuid) ? animation.sourceTextureGuid : frame.sourceTextureGuid;
            if (string.IsNullOrEmpty(guid)) return null;
            var source = ResolveTexture(guid);
            var pixels = GetPixelsFor(guid);
            if (source == null || pixels == null) return null;
            var key = new RegionSlicer.ColorKey { enabled = animation.bgKeyEnabled, color = animation.bgKey, tolerance = animation.bgKeyTolerance };
            var block = AtlasBaker.TransformCell(pixels, source.width, source.height, frame.cell, frame.transform, key, frame.pivot,
                out int width, out int height, out _);
            var result = new Texture2D(width, height, TextureFormat.RGBA32, false)
                { name = animation.name + " thumbnail", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            result.SetPixels32(block); result.Apply();
            return result;
        }

        private void SwitchToLauminaryAnimation(string name)
        {
            if (_boundLauminary == null || NameEq(name, _boundAnimName)) return;
            var definition = LauminaryRepo.GetDraftAnimation(_boundLauminary, name);
            if (definition == null) { SetStatus("That animation is no longer in the draft."); return; }
            if (!ConfirmDocumentTransition()) return;
            _boundAnimName = name; _orphanAsset = null;
            LoadAnimationIntoSequence(definition); Refresh();
        }

        private void SwitchToOrphan(AnimationAsset asset)
        {
            if (asset == null || asset.animation == null || asset == _orphanAsset || !ConfirmDocumentTransition()) return;
            _orphanAsset = asset; _boundLauminary = null; _boundAnimName = null;
            LoadAnimationIntoSequence(asset.animation); Refresh();
        }

        private static bool NameEq(string a, string b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
