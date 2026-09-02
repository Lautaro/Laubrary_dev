// BackSplashElement — the UI TOOLKIT half of BackSplashPainter.
//
// Why this exists. BackSplashPainter renders a BackSplashSettings into an IMGUI viewport rect, which is the
// right shape for Pyre, whose preview genuinely is an IMGUI island. A UI Toolkit window cannot call it without
// hosting an IMGUIContainer purely to run four GUI calls — an IMGUI island inside an otherwise pure-UITK tool,
// with its own repaint model (the host has to MarkDirtyRepaint by hand) and its own coordinate rules. Shaper's
// window did exactly that, and it was the only IMGUI left in the whole tool.
//
// The wrong fix is to re-implement the backdrop locally in UITK. BackSplashPainter's own header explains why:
// the same twenty lines had already been written three times, and TextSplash's copy silently never read
// imageZoom or imagePos, so that window's Zoom slider and Position pad moved, saved, and did nothing — "a
// control that does nothing is the most expensive kind of bug: it looks finished." A local UITK copy would be
// the fourth, and would drift the same way.
//
// So this is the same renderer expressed in the other toolkit, living beside the IMGUI one in the shared
// module, reading the SAME BackSplashSettings. Two spellings of one rule, in one place, rather than one
// spelling plus N private re-inventions. If a field is ever added to BackSplashSettings, both live here and
// are changed together.
//
// It has no UnityEditor dependency and could move to Runtime/BackSplash if a runtime UITK surface ever wants a
// backdrop; it sits in Editor only to keep it beside its IMGUI sibling and BackSplashZui.
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.BackSplash.Editor
{
    /// <summary>A backdrop as a VisualElement: the colour fill and, over it, the image at its authored zoom,
    /// position and tint. The UI Toolkit counterpart of <see cref="BackSplashPainter"/>, reading the same
    /// <see cref="BackSplashSettings"/>.</summary>
    public sealed class BackSplashElement : VisualElement
    {
        readonly VisualElement _image;
        BackSplashSettings _settings;
        Color _fallback;

        public BackSplashElement(Color fallback)
        {
            _fallback = fallback;
            pickingMode = PickingMode.Ignore;
            // The clip. BackSplashPainter uses GUI.BeginClip for the same reason: a zoomed-in image must not
            // paint outside the viewport it belongs to.
            style.overflow = Overflow.Hidden;

            _image = new VisualElement { pickingMode = PickingMode.Ignore };
            _image.style.position = Position.Absolute;
            Add(_image);

            // The image rect is a fraction of the VIEW, so it can only be placed once the view has a size.
            RegisterCallback<GeometryChangedEvent>(_ => Layout());
        }

        /// <summary>Point at the settings to render. Null renders the fallback colour, matching the painter's
        /// own empty-state behaviour so a window keeps the colour it always had.</summary>
        public void SetSettings(BackSplashSettings settings)
        {
            _settings = settings;
            Refresh();
        }

        public void Refresh()
        {
            style.backgroundColor = _settings != null ? _settings.cameraColor : _fallback;

            var sprite = _settings?.image;
            if (sprite == null)
            {
                _image.style.backgroundImage = null;
                _image.style.display = DisplayStyle.None;
                return;
            }

            _image.style.display = DisplayStyle.Flex;
            // Background.FromSprite handles a sprite packed into an atlas page natively — the painter has to do
            // that by hand with textureRect-derived tex coords (BackSplashPainter.cs:34-37), and this is the one
            // place the UITK spelling is genuinely simpler rather than merely different.
            _image.style.backgroundImage = Background.FromSprite(sprite);
            _image.style.unityBackgroundImageTintColor = _settings.imageTint;
            Layout();
        }

        void Layout()
        {
            if (_settings?.image == null) return;

            float vw = resolvedStyle.width, vh = resolvedStyle.height;
            if (float.IsNaN(vw) || float.IsNaN(vh) || vw <= 0f || vh <= 0f) return;

            // Identical arithmetic to BackSplashPainter.cs:43-48, deliberately: size is a fraction of the view,
            // centred, then offset. imagePos.y is NEGATED because the authoring pad treats +y as UP while both
            // IMGUI rects and UITK's top/left measure +y DOWN. Keeping the negation here rather than "fixing" it
            // is what makes a backdrop authored in one toolkit look identical in the other.
            float w = vw * _settings.imageZoom, h = vh * _settings.imageZoom;
            _image.style.width = w;
            _image.style.height = h;
            _image.style.left = (vw - w) * 0.5f + _settings.imagePos.x;
            _image.style.top = (vh - h) * 0.5f - _settings.imagePos.y;
        }
    }
}
