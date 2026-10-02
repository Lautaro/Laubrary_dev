using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.MetaMapper.Editor
{
    /// <summary>
    /// THE PROVIDER REGISTRY — how the generic window draws a subject it is not allowed to know about.
    ///
    /// A standalone <see cref="MetaMap"/> carries a <see cref="MetaSubjectRef"/> = (kind, Object, key). The
    /// window resolves that into a <see cref="MetaSubjectVisual"/> by asking whichever editor OWNS that kind
    /// — Cartographer's editor answers <see cref="MetaSubjectKind.Clump"/> and <see cref="MetaSubjectKind.Level"/>,
    /// a future tool answers <see cref="MetaSubjectKind.Other"/> — so <b>the core window never references
    /// Cartographer types</b> and the dependency arrow keeps pointing one way.
    ///
    /// This is the <c>LauAssetEditors.RegisterOpen</c> precedent applied to pictures instead of windows:
    /// register from your own Editor assembly in an <c>[InitializeOnLoad]</c> static constructor.
    ///
    /// <code>
    /// [InitializeOnLoad]
    /// static class CartographerMetaSubjects
    /// {
    ///     static CartographerMetaSubjects()
    ///         => MetaSubjectProviders.Register(MetaSubjectKind.Clump, r =>
    ///         {
    ///             var set = r.subject as Tileset;
    ///             var clump = set?.FindClump(r.key);
    ///             return clump == null ? null : ComposeClumpVisual(set, clump);
    ///         });
    /// }
    /// </code>
    ///
    /// A provider returns NULL for "I know this kind but cannot resolve THIS one" (the clump was renamed, the
    /// tileset lost its atlas) — never throws. An unresolvable subject is not an error: the window falls back
    /// to a blank canvas of the map's own recorded size, because authoring IDENTITY (a layer that merely
    /// exists) must never be blocked by missing art.
    /// </summary>
    public static class MetaSubjectProviders
    {
        static readonly Dictionary<MetaSubjectKind, Func<MetaSubjectRef, MetaSubjectVisual>> providers =
            new Dictionary<MetaSubjectKind, Func<MetaSubjectRef, MetaSubjectVisual>>();

        static readonly Dictionary<MetaSubjectKind, Func<MetaSubjectRef, MetaEditSession>> sessions =
            new Dictionary<MetaSubjectKind, Func<MetaSubjectRef, MetaEditSession>>();

        static MetaSubjectProviders()
        {
            // Sprite is built in: it is the ONE subject type this module can name (Sprite is UnityEngine's
            // own), so §7's "hosts only implement providers for subjects the window cannot render alone"
            // starts from a non-empty registry.
            providers[MetaSubjectKind.Sprite] = r => MetaSubjectVisual.ForSprite(r?.AsSprite);
        }

        /// <summary>Register (or replace) the provider for one subject kind. Last registration wins, so a
        /// domain reload that re-runs an [InitializeOnLoad] ctor is idempotent.</summary>
        public static void Register(MetaSubjectKind kind, Func<MetaSubjectRef, MetaSubjectVisual> resolver)
        {
            if (resolver == null) providers.Remove(kind);
            else providers[kind] = resolver;
        }

        /// <summary>Register how to reach the DATA for an EMBEDDED subject — the other half of the contract,
        /// and the half that makes an embedded edit survive a domain reload.
        ///
        /// A pushed <see cref="MetaEditSession"/> is a live object graph: it dies with the domain, and its
        /// <c>onChanged</c> delegate cannot be serialized at all. But a <see cref="MetaSubjectRef"/> is
        /// (kind + Object + key) — serializable, survives everything. So the window remembers the REF and
        /// asks the owner to re-open a session from it, which is why editing a clump's embedded metadata is
        /// still there after a script recompile instead of dumping the author back to an empty browser.
        ///
        /// The resolver returns a session whose <c>data</c> is the embedded model and whose <c>undoTarget</c>
        /// is the real Object it lives on (for a clump: the Tileset). Getting undoTarget wrong is not a
        /// cosmetic mistake — undo silently does nothing.</summary>
        public static void RegisterSession(MetaSubjectKind kind, Func<MetaSubjectRef, MetaEditSession> resolver)
        {
            if (resolver == null) sessions.Remove(kind);
            else sessions[kind] = resolver;
        }

        /// <summary>Re-open an editing session on an embedded subject, or null when nobody can. Never throws.</summary>
        public static MetaEditSession ResolveSession(MetaSubjectRef subject)
        {
            if (subject == null || !subject.IsSet) return null;
            if (!sessions.TryGetValue(subject.kind, out var fn) || fn == null) return null;
            try { return fn(subject); }
            catch (Exception e)
            {
                Debug.LogWarning($"[MetaMapper] The {subject.kind} session provider threw resolving " +
                                 $"'{subject}': {e.Message}");
                return null;
            }
        }

        public static bool IsRegistered(MetaSubjectKind kind) => providers.ContainsKey(kind);

        public static bool HasSession(MetaSubjectKind kind) => sessions.ContainsKey(kind);

        /// <summary>Every kind someone can currently draw — what the window offers as pickable subject kinds.</summary>
        public static IEnumerable<MetaSubjectKind> RegisteredKinds => providers.Keys;

        /// <summary>Resolve a subject reference into a drawable visual, or null. Never throws: a provider that
        /// blows up on a half-migrated asset must not take the window down with it.</summary>
        public static MetaSubjectVisual Resolve(MetaSubjectRef subject)
        {
            if (subject == null || !subject.IsSet) return null;
            if (!providers.TryGetValue(subject.kind, out var fn) || fn == null) return null;
            try { return fn(subject); }
            catch (Exception e)
            {
                Debug.LogWarning($"[MetaMapper] The {subject.kind} subject provider threw resolving " +
                                 $"'{subject}': {e.Message}");
                return null;
            }
        }

        /// <summary>Touching this type is enough to run its static constructor — the one-liner an
        /// [InitializeOnLoad] link calls so the built-in providers exist before anything asks.</summary>
        public static void EnsureBuiltIns() { }
    }
}
