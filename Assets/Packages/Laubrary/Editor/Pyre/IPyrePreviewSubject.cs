using System;
using UnityEngine;
using Laubrary.PreviewKit.Editor;

namespace Laubrary.Pyre.Editor
{
    /// <summary>
    /// Contract for an animated "preview subject" a <see cref="BlastSpec"/> can optionally reference (e.g. a
    /// Zoetrope character playing a clip) so Pyre's own preview shows the blast in the context it'll actually
    /// play in, and can align the blast's render origin to a LIVE point on that subject (a muzzle, a blade
    /// tip) every frame. The subject renders through a <see cref="LiveScenePreview"/> — a REAL isolated scene
    /// with a real camera — by spawning its own real gameplay components into it (e.g. a SpriteRenderer-driven
    /// ZonedAnimationPlayer), not by hand-drawing itself via IMGUI. That's what guarantees the preview is
    /// pixel-for-pixel what gameplay would render: it IS gameplay's own rendering components, just viewed
    /// through an isolated camera instead of the real one — the same guarantee <c>BlastRenderer</c> already
    /// gives for the blast's own frames (preview == bake == runtime).
    /// Pyre's own code has zero dependency on whatever implements this: a bridge module (e.g.
    /// Pyre.Zoetrope.Editor) provides the real implementation and registers it via
    /// <see cref="PyrePreviewSubjectProvider"/>, so Pyre core never references Zoetrope.
    /// </summary>
    public interface IPyrePreviewSubject : IDisposable
    {
        /// Advance the subject's own playback by dt seconds.
        void Tick(float deltaTime);

        /// Restart the subject's clip from its own beginning — called whenever Pyre's own preview loops or
        /// the user hits Restart, so the two stay roughly in lockstep (they started together in-game too).
        void Restart();

        /// Move this subject's real rendering GameObject into `preview`'s isolated scene (once; safe to call
        /// every frame afterward — implementations track whether they've already been adopted) and position it
        /// at `worldPosition`, exactly as a real scene would place it.
        void SpawnInto(LiveScenePreview preview, Vector3 worldPosition);

        /// The live attach point (named by whatever id the BlastSpec was configured with, e.g. "Muzzle"), in
        /// WORLD SPACE, reflecting wherever <see cref="SpawnInto"/> last placed the subject. Returns false if
        /// unavailable (no subject configured, or the point couldn't be resolved this frame) — the caller
        /// should fall back to the subject's own position.
        bool TryGetAttachWorldPos(out Vector3 worldPos);
    }

    /// <summary>
    /// Resolves a BlastSpec's preview-subject fields (<c>previewSubjectAsset</c>/<c>previewSubjectClip</c>/
    /// <c>previewSubjectAttachId</c>) into a live <see cref="IPyrePreviewSubject"/>. Null until a bridge
    /// module registers a resolver — Pyre's preview simply skips the subject entirely when none is set.
    /// </summary>
    public static class PyrePreviewSubjectProvider
    {
        public static Func<BlastSpec, IPyrePreviewSubject> Resolve;

        /// Optional: given a subject asset + clip name, returns the known attach-point ids available for
        /// that combination (e.g. a Zoe's MetaLayer names), or null if the bridge can't enumerate them (an
        /// unresolved/unsupported asset, no clip selected yet, ...). Pyre's UI falls back to a free-text field
        /// when this is null or empty — enumeration is a nice-to-have, not a requirement any bridge must
        /// implement.
        public static Func<UnityEngine.Object, string, string[]> GetAttachPointOptions;
    }
}
