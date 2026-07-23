using UnityEditor;
using UnityEngine;
using Laubrary.Pyre;
using Laubrary.Pyre.Editor;
using Laubrary.PreviewKit.Editor;
using Laubrary.Launimator;

namespace Laubrary.PyreZoetrope.Editor
{
    /// <summary>
    /// Registers the Launimator-backed <see cref="IPyrePreviewSubject"/> resolver with Pyre — the bridge that
    /// lets a Pyre's "Subject asset" field (a plain Object, set in Pyre's own preview panel) hold a
    /// <see cref="ReelVersion"/>, so Pyre can play one of its clips live in its own preview with the blast's
    /// origin tracking a named MetaLayer point every frame. Neither Pyre core nor the generic panel that sets
    /// these fields knows Launimator exists — only this bridge does, matching the same pattern as
    /// Zoetrope.Launimator/Zoetrope.Pyre.
    /// </summary>
    [InitializeOnLoad]
    static class ZoetropePreviewSubjectRegistration
    {
        static ZoetropePreviewSubjectRegistration()
        {
            PyrePreviewSubjectProvider.Resolve = spec =>
            {
                var version = spec != null ? spec.previewSubjectAsset as ReelVersion : null;
                return version != null ? new ZoetropePreviewSubject(version, spec.previewSubjectClip, spec.previewSubjectAttachId) : null;
            };

            // Attach ids for a Reel are its MetaLayer names on the selected clip — the same "id" TryGetMetaPoint
            // looks up by. Lets Pyre's panel show a dropdown of what's actually painted instead of a free-text
            // field the user has to get exactly right by memory.
            PyrePreviewSubjectProvider.GetAttachPointOptions = (asset, clip) =>
            {
                var version = asset as ReelVersion;
                if (version == null || string.IsNullOrEmpty(clip)) return null;
                var anim = version.animations?.Find(a => a.name == clip);
                if (anim?.metaLayers == null || anim.metaLayers.Count == 0) return null;
                var ids = new string[anim.metaLayers.Count];
                for (int i = 0; i < ids.Length; i++) ids[i] = anim.metaLayers[i].id;
                return ids;
            };
        }
    }

    /// <summary>
    /// Spawns a hidden, editor-only GameObject with a real <see cref="ZonedAnimationPlayer"/> (and the
    /// SpriteRenderer it requires) — the SAME runtime component gameplay uses, not a reimplementation — and
    /// hands it to a <see cref="LiveScenePreview"/> to render through a real camera. Because
    /// ZonedAnimationPlayer already drives a real SpriteRenderer (pivot, pixelsPerUnit, flipX — all of it)
    /// every Tick, there is no custom drawing code left here at all: the preview literally IS gameplay's own
    /// rendering, just viewed through an isolated camera.
    /// </summary>
    class ZoetropePreviewSubject : IPyrePreviewSubject
    {
        readonly GameObject go;
        readonly ZonedAnimationPlayer player;
        readonly string clip;
        readonly string attachId;
        bool adopted;

        // The attach point updates live via OnMetaLayerReached — fired once per frame-entry whenever that
        // frame has painted data for `attachId`. Per the authoring convention (a muzzle-style layer should
        // only have one painted pixel on one frame), this fires once per Restart() in practice, matching the
        // old capture-at-play-time behavior — but now sourced from the SAME live notification gameplay code
        // can subscribe to (see ZonedAnimationPlayer.OnMetaLayerReached), rather than a separate manual sample.
        // Stored as a LOCAL-space offset (not a world position) so it stays correct regardless of where
        // SpawnInto later repositions the subject — world position = go.transform.TransformPoint(capturedLocalOffset).
        bool hasCapturedOffset;
        Vector3 capturedLocalOffset;

        public ZoetropePreviewSubject(ReelVersion version, string clip, string attachId)
        {
            this.clip = clip;
            this.attachId = attachId;
            go = new GameObject("~PyrePreviewSubject");
            player = go.AddComponent<ZonedAnimationPlayer>();
            player.SetVersion(version);
            player.OnMetaLayerReached += OnMetaLayerReached;
            Restart();
        }

        public void Tick(float deltaTime) => player.Tick(deltaTime);

        public void Restart()
        {
            hasCapturedOffset = false;
            if (string.IsNullOrEmpty(clip)) return;

            // The SAME component gameplay uses (ZonedAnimationPlayer) — plays the clip as a one-shot; the
            // attach point arrives via the OnMetaLayerReached subscription above the instant the painted
            // frame is entered, so preview and game can never quietly diverge on "where/when that point is".
            player.Play(clip, loop: false);
        }

        void OnMetaLayerReached(string layerId, Vector3 worldPos)
        {
            if (!string.Equals(layerId, attachId, System.StringComparison.OrdinalIgnoreCase)) return;
            hasCapturedOffset = true;
            capturedLocalOffset = go.transform.InverseTransformPoint(worldPos);
        }

        public void SpawnInto(LiveScenePreview preview, Vector3 worldPosition)
        {
            if (!adopted) { preview.Adopt(go); adopted = true; }
            go.transform.position = worldPosition;
        }

        public bool TryGetAttachWorldPos(out Vector3 worldPos)
        {
            worldPos = go.transform.TransformPoint(capturedLocalOffset);
            return hasCapturedOffset;
        }

        public void Dispose()
        {
            if (player != null) player.OnMetaLayerReached -= OnMetaLayerReached;
            if (go != null) Object.DestroyImmediate(go);
        }
    }
}
