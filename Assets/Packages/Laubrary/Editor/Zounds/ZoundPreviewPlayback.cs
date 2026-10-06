using System;
using System.Collections.Generic;
using UnityEditor;
using Laubrary.Zounds.Uitk;

namespace Laubrary.Zounds {
    // Actual windows own previews, including previews launched from their popups and secondary links.
    // All teardown remains in ZoundAudition and the token's normal SAP-safe Kill path.
    internal static class ZoundPreviewPlayback {
        static readonly Dictionary<EditorWindow, ZoundAudition> owners = new Dictionary<EditorWindow, ZoundAudition>();

        internal static void Register(EditorWindow owner, ZoundAudition session) {
            Dispose(owner);
            owners[owner] = session;
        }

        internal static ZoundAudition Session(EditorWindow owner) {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (!owners.TryGetValue(owner, out var session) || session.IsDisposed) {
                session = new ZoundAudition(0, () => owner != null, null, null);
                session.changed += () => { if (owner != null) owner.Repaint(); };
                owners[owner] = session;
            }
            return session;
        }

        internal static void Dispose(EditorWindow owner) {
            AudioPreviewUtility.StopPreviewClip(owner);
            if (ReferenceEquals(owner, null) || !owners.TryGetValue(owner, out var session)) return;
            owners.Remove(owner);
            session.Dispose();
        }

        internal static ZoundToken Play(EditorWindow owner, Zound zound, ZoundArgs? args = null, object control = null, bool toggle = true, EditorWindow secondaryOwner = null) {
            if (zound == null) return null;
            var session = Session(owner);
            object key = control ?? zound;
            return session.PlayPreview(key, () => {
                var playArgs = args ?? ZoundArgs.Default;
                var primaryAlive = session.PreviewAlive(key);
                var secondary = secondaryOwner != null ? Session(secondaryOwner) : null;
                var secondaryAlive = secondary?.PreviewAlive(key);
                playArgs.editorPreviewAlive = () => primaryAlive() && (secondaryAlive == null || secondaryAlive());
                playArgs.editorPreviewStarted = token => { session.TrackPreview(key, token); secondary?.TrackPreview(key, token); };
                return ZoundEngine.PlayZound(zound, playArgs);
            }, toggle);
        }

        internal static bool IsLoopPlaying(EditorWindow owner, object control) {
            return owner != null && owners.TryGetValue(owner, out var session) && !session.IsDisposed && session.IsLoopPlaying(control);
        }

        internal static string Tooltip(EditorWindow owner, object control, string idle = "Play this sound.")
            => IsLoopPlaying(owner, control) ? "Stop loop" : idle;

        internal static void StopControl(EditorWindow owner, object control) {
            if (owner != null && owners.TryGetValue(owner, out var session)) session.StopPreview(control);
        }
    }
}
