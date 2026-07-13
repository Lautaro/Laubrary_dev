using UnityEngine;

namespace Laubrary.PreviewKit
{
    /// Contract for a Runtime asset type that HAS a visual look and can render its own preview, so any picker or
    /// browser can show it without hand-rolling a renderer per asset type. Mirrors the RenderThumbnail /
    /// AnimateThumbnails / UpdateAnimatedThumbnail trio LaubraryAssetWindow&lt;T&gt; subclasses already implement
    /// per-window (see PyreWindow) — this moves that same contract onto the asset itself so ANY consumer (a
    /// browser grid, a picker field, a SerializeReference dropdown) can use it, not just that asset's own tool.
    /// Implementations must call the SAME deterministic renderer their own tool's live-editing preview uses
    /// (e.g. BlastRenderer for a Pyre asset) — never a second render path.
    public interface IVisualPreview
    {
        /// A fresh static preview texture (a representative frame). Caller owns and must destroy it.
        Texture2D RenderPreviewTexture();

        /// True if this asset's preview can usefully animate (more than one frame/state).
        bool CanAnimatePreview { get; }

        /// Preferred playback speed for the animated preview, in frames per second. Only meaningful when
        /// CanAnimatePreview is true.
        float PreviewFps { get; }

        /// Mutate tex's pixels in place for the frame at time `time` (e.g. EditorApplication.timeSinceStartup).
        /// tex is the same instance RenderPreviewTexture returned. No-op if !CanAnimatePreview.
        void UpdateAnimatedPreview(Texture2D tex, double time);
    }
}
