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
            var card = Z.Button("", $"Edit {animation.name}; hover to preview. {animation.recipe?.Count ?? 0} frames at {animation.fps:0.#} FPS.", open);
            card.AddToClassList("lau-builder-modes__animation");
            if (current) card.AddToClassList("zui-radio__on");
            var image = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.AddToClassList("lau-builder-modes__thumbnail");
            var frames = animation.frames;
            if (frames != null && frames.Count > 0) image.sprite = frames[0];
            card.Add(image);
            var name = Z.Text(animation.name, ZuiText.Small, animation.name);
            name.AddToClassList("lau-builder-modes__animation-name");
            card.Add(name);
            bool hover = false, playing = false; int frame = 0;
            double last = UnityEditor.EditorApplication.timeSinceStartup, remaining = 0;
            var preview = Z.IconButton("play", "Start or stop this thumbnail's animation preview.", () => { playing = !playing; remaining = 0; }, 22);
            preview.RegisterCallback<ClickEvent>(e => e.StopPropagation()); card.Add(preview);
            card.RegisterCallback<MouseEnterEvent>(_ => { hover = true; last = UnityEditor.EditorApplication.timeSinceStartup; });
            card.RegisterCallback<MouseLeaveEvent>(_ => { hover = false; if (!playing) { frame = 0; if (frames != null && frames.Count > 0) image.sprite = frames[0]; } });
            card.schedule.Execute(() =>
            {
                double now = UnityEditor.EditorApplication.timeSinceStartup; double dt = now - last; last = now;
                if ((!hover && !playing) || frames == null || frames.Count == 0) return;
                remaining -= dt;
                if (remaining > 0) return;
                frame = (frame + 1) % frames.Count; image.sprite = frames[frame];
                float timing = animation.recipe != null && frame < animation.recipe.Count ? animation.recipe[frame].timingPercent : 0;
                remaining = FrameRef.TimingFactorOf(timing) / Mathf.Max(1, animation.fps);
            }).Every(25);
            _railItems.Add(card);
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
