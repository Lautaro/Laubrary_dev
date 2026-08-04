using System;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.SpriteFx.Editor
{
    /// What a SpriteFx stack is being previewed ON, and on whose CLOCK: some frames, the rate they play at,
    /// how long the host event runs, and a label saying where all of it came from. Deliberately just sprites
    /// and numbers — SpriteFx filters pixels and has no interest in what produced them.
    ///
    /// The clock matters as much as the pictures. A stack is a shape over normalized life with no opinion
    /// about seconds, so whoever hosts it owns the timebase; a subject that carried only frames left the
    /// window free to invent a duration of its own, and it did — previewing at the stack's fallback length
    /// while the game played it over the whole death animation.
    public sealed class SpriteFxPreviewSubject
    {
        /// Frames in order. One is a still; more animate.
        public Sprite[] Frames = Array.Empty<Sprite>();
        /// Playback rate for those frames. 0 or less means "a still" and the preview will not advance them.
        public float Fps;
        /// How long the HOST's event lasts, in seconds — the length one play-through of the stack must fill.
        /// 0 when the host has no fixed length, and only then may the window fall back to the stack's own.
        public float Seconds;
        /// Human-readable provenance, shown in the window: "Doom Imp — death". Never a type name.
        public string Label = "";

        public bool HasFrames => Frames != null && Frames.Length > 0;
        /// True when the host has stated a length, and so owns the timebase the preview must run on.
        public bool OwnsTimebase => Seconds > 0f;
    }

    /// The seam that lets the stack window preview against a Zoe's own animation WITHOUT SpriteFx ever
    /// learning what a Zoe is.
    ///
    /// The dependency only runs one way: Zoetrope references SpriteFx (a Zoe event holds a SpriteFxSpec), so
    /// SpriteFx cannot reference Zoetrope back without a cycle. That is why the subject is PUSHED IN by a
    /// resolver the owning module registers, rather than pulled out by the window going looking. Any module
    /// that can see both sides can contribute one.
    ///
    /// Registered resolvers are asked in order and the first non-empty answer wins, so a caller that knows
    /// exactly which subject is meant (the window was opened from a specific event) can register ahead of a
    /// broad "search the project" fallback.
    ///
    /// ⚠️ The stack asset NEVER stores any of this. A preview subject is authoring context, not part of the
    /// recipe — the same rule the window's manual sprite picker already follows, and the reason a stack can
    /// be reused across a dozen characters.
    public static class SpriteFxPreviewSubjects
    {
        static readonly List<Func<SpriteFxSpec, SpriteFxPreviewSubject>> Resolvers = new();

        /// Contribute a way of finding a visual for a spec. Idempotent per delegate, so an
        /// [InitializeOnLoad] registrar that runs again after a domain reload does not stack duplicates.
        public static void Register(Func<SpriteFxSpec, SpriteFxPreviewSubject> resolver)
        {
            if (resolver == null || Resolvers.Contains(resolver)) return;
            Resolvers.Add(resolver);
        }

        /// The first subject any resolver can offer for this spec, or null. A resolver that throws is
        /// swallowed and skipped: a preview is a convenience, and one badly-behaved contributor must not be
        /// able to take down the window that hosts it.
        public static SpriteFxPreviewSubject Resolve(SpriteFxSpec spec)
        {
            if (spec == null) return null;
            for (int i = 0; i < Resolvers.Count; i++)
            {
                SpriteFxPreviewSubject s = null;
                try { s = Resolvers[i](spec); }
                catch (Exception e) { Debug.LogWarning($"[SpriteFx] A preview-subject resolver threw and was skipped: {e.Message}"); }
                if (s != null && s.HasFrames) return s;
            }
            return null;
        }
    }
}
