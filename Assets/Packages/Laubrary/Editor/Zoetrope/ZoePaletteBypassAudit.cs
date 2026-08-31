using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// The REVERSE check (ZOE_PALETTE_BUILD_PLAN.md task 7 / ZOE_PALETTE_TAKE.md "the bypass question") — "a
    /// character showing something that did NOT come from its declared palette". Per round 3's own analysis
    /// this is the direction nothing else here checks: <see cref="ZoePaletteUsageLog"/> can only ever see a
    /// name that was ASKED of a Zoe by id (a row that was never named — because code spawned a visual directly
    /// instead of asking — produces no row, no chip, nothing to be amber. That is the exact hole round 3 named
    /// in its own first draft's amber chip).
    ///
    /// This is a HEURISTIC project-wide text scan, not a compiler-level analysis — Unity ships nothing that
    /// finds "which method calls this API" cheaply outside a full Roslyn pass, and building one is out of scope
    /// for an afternoon's honesty tooling. It greps every project .cs file (excluding Laubrary's own package,
    /// whose bridge modules are the ONLY code allowed to call these APIs directly) for a short, named list of
    /// APIs that spawn a visual straight onto a character rather than asking through
    /// <see cref="ReactionFxPlayer"/> by name — exactly the three concrete bypasses ZOE_PALETTE_TAKE.md's
    /// bypass section names: "spawning a Pyre/Chunks/SpriteFx effect straight from game code onto a Zoe... by
    /// calling a view's PlayClip directly... or by bolting a bespoke 'play this on hit' component onto the
    /// character."
    ///
    /// A hit here is a REVIEW PROMPT, not a verdict — the pattern list is necessarily approximate (it cannot
    /// tell "this genuinely bypasses a Zoe" from "this is unrelated code that happens to call the same API for
    /// something that isn't a character at all"), so it always shows the matching line for a human to judge.
    /// </summary>
    public static class ZoePaletteBypassAudit
    {
        public readonly struct Hit
        {
            public readonly string file;
            public readonly int line;
            public readonly string text;
            public readonly string reason;
            public Hit(string file, int line, string text, string reason)
            { this.file = file; this.line = line; this.text = text; this.reason = reason; }
        }

        // (substring to search for, human reason) — matched per-line, case-sensitive (these are real C#
        // identifiers, so case matters and case-sensitivity avoids false hits on unrelated prose/comments that
        // happen to contain the words in a different case).
        static readonly (string needle, string reason)[] Patterns =
        {
            ("PyreBlastPool.Get(", "Spawns a Pyre blast directly — a Zoe's own Hit/Death/custom-event " +
                "effect list already does this through its declared reaction; consider adding/using a row " +
                "instead of spawning here."),
            ("ChunkPool.Get(", "Spawns a Chunks debris piece directly — same as above, consider a declared " +
                "reaction's effect list instead."),
            (".PlayFollowable(", "Triggers an ICombatFx's follow-spawn directly, outside ReactionFxPlayer.Fire " +
                "— the declared-palette path a reaction's effect list already provides."),
            (".AddComponent<SpriteFxFilter>", "Attaches a SpriteFx filter directly — ReactionFxPlayer.PlayBodyFx " +
                "already does this for a reaction's own Body FX; a second one bolted on elsewhere is exactly " +
                "the \"bespoke 'play this on hit' component\" bypass ZOE_PALETTE_TAKE.md names."),
            (".PlayClip(", "Plays an animation clip directly on a view — outside Zoetrope, the only legal " +
                "caller is AnimationArbiter/ReactionFxPlayer routing a declared reaction; a direct call here " +
                "skips interruption handling, targeting and the declared-palette check entirely."),
        };

        // The package itself is the ONLY code allowed to call these directly (it's what implements the
        // declared-palette path in the first place) — scanning it would just report every one of its own
        // legitimate internals as a "bypass" of itself.
        const string ExcludedRoot = "Packages/Laubrary/";
        const string ExcludedRootAlt = "Assets/Packages/Laubrary/";

        /// Scan every .cs file under Assets/ (excluding Laubrary's own package) for the patterns above. Cheap
        /// enough to run on demand from a button — a few thousand small text files, once.
        public static List<Hit> Scan()
        {
            var hits = new List<Hit>();
            string assetsRoot = System.IO.Path.Combine(UnityEngine.Application.dataPath);
            string[] files;
            try { files = Directory.GetFiles(assetsRoot, "*.cs", SearchOption.AllDirectories); }
            catch (Exception e) { Debug.LogWarning($"[ZoePaletteBypassAudit] Could not enumerate project scripts: {e.Message}"); return hits; }

            foreach (var abs in files)
            {
                string rel = "Assets" + abs.Substring(assetsRoot.Length).Replace('\\', '/');
                if (rel.Contains(ExcludedRoot) || rel.Contains(ExcludedRootAlt)) continue;

                string[] lines;
                try { lines = File.ReadAllLines(abs); }
                catch { continue; }

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    // Skip an obvious comment line — cuts a lot of doc-comment noise (this very file's own
                    // doc comment above would otherwise self-report every time it runs).
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("///")) continue;

                    foreach (var (needle, reason) in Patterns)
                        if (line.Contains(needle))
                            hits.Add(new Hit(rel, i + 1, line.Trim(), reason));
                }
            }
            return hits;
        }
    }
}
