using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// The project-wide half of the honesty surface (ZOE_PALETTE_BUILD_PLAN.md task 7): the two checks that
    /// don't belong on a single character's own row, because they're about the WHOLE project at once.
    ///
    /// <list type="bullet">
    /// <item><b>Requested but undeclared</b> — every name something asked a Zoe for, by id, that did not match
    /// a declared row at the time (<see cref="ZoePaletteUsageLog.AllMisses"/>). The per-instance version of
    /// this already exists (<see cref="ReactionFxPlayer.WarnNoSuchState"/> logs it to the console the moment
    /// it happens); this is the same fact, collected across every session instead of scattered through console
    /// history one line at a time.</item>
    /// <item><b>Bypass risk</b> — the REVERSE check (<see cref="ZoePaletteBypassAudit"/>): a heuristic scan for
    /// code spawning a visual straight onto a character instead of asking through the declared palette. This is
    /// the one direction nothing else here can see (a bypass never asks a Zoe for anything by name, so it never
    /// shows up as a miss above either) — run on demand, not automatically, since it walks every .cs file in
    /// the project.</item>
    /// </list>
    /// </summary>
    public class ZoePaletteHealthWindow : ZuiWindow
    {
        [MenuItem("Laubrary/Zoetrope/Palette Health")]
        public static void Open() => GetWindow<ZoePaletteHealthWindow>("Palette Health");

        List<ZoePaletteBypassAudit.Hit> _bypassHits;
        bool _scanned;

        protected override void BuildUI(VisualElement root)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("lau-tool-shell__column");
            root.Add(scroll);
            var body = scroll.contentContainer;

            body.Add(Z.Text("Zoe palette honesty checks", ZuiText.Section,
                "Project-wide visibility so nobody — including an AI writing game code — quietly starts making " +
                "a character show something without going through its declared list of named states."));

            BuildMissesSection(body);
            BuildBypassSection(body);
        }

        void BuildMissesSection(VisualElement root)
        {
            var section = Z.Section("Requested but undeclared", "Every name something asked a character for, " +
                "by id, that the character did not declare AT THE TIME it was asked. Observed live — through " +
                "Play mode or a row's own Preview button — since the last time this cache was cleared.");

            var misses = ZoePaletteUsageLog.AllMisses();
            if (misses.Count == 0)
            {
                section.Add(Z.Text("Nothing observed yet. This fills in as characters actually run — nothing to " +
                    "worry about on a project that hasn't been played yet.", ZuiText.Subtle,
                    "Only reports what has genuinely been asked for; it does not scan code."));
            }
            else
            {
                foreach (var (zoeName, id, count, lastSeen) in misses)
                {
                    var row = Z.Row();
                    row.Add(Z.Text($"{zoeName}: \"{id}\"", ZuiText.Body,
                        $"Requested {count} time(s), last seen {lastSeen} UTC. Either add a row named \"{id}\" " +
                        $"to {zoeName}, or fix the caller — it's asking for a state that doesn't exist."));
                    section.Add(row);
                }
            }

            section.Add(Z.Button("Clear log", "Wipe every observed request/miss. Not undoable — this is " +
                "observed history, not authored data.", () => { ZoePaletteUsageLog.ClearAll(); Rebuild(); }).W(120f));

            root.Add(section);
        }

        void BuildBypassSection(VisualElement root)
        {
            var section = Z.Section("Bypass risk (heuristic)", "A character showing something that did NOT " +
                "come from its declared palette — the direction nothing else here checks. This is a TEXT SCAN, " +
                "not a compiler analysis: every hit is a review prompt, not a verdict.");

            section.Add(Z.Button("Scan Project", "Search every .cs file under Assets/ (excluding Laubrary's " +
                "own package) for calls that spawn a visual directly instead of asking through a declared " +
                "state. A few thousand small files — fast, but not instant.", () =>
                {
                    _bypassHits = ZoePaletteBypassAudit.Scan();
                    _scanned = true;
                    Rebuild();
                }).W(120f));

            if (!_scanned)
            {
                section.Add(Z.Text("Not scanned yet this session.", ZuiText.Subtle,
                    "Click Scan Project to run it. Not automatic — it walks every script in the project."));
            }
            else if (_bypassHits.Count == 0)
            {
                section.Add(Z.Text("No matches for the known bypass patterns.", ZuiText.Subtle,
                    "Absence of a hit is not proof of nothing wrong — this only knows the patterns it was given."));
            }
            else
            {
                section.Add(Z.Text($"{_bypassHits.Count} line(s) to review:", ZuiText.Subtle,
                    "Each one calls an API that spawns a visual directly. Some may be legitimate (unrelated to " +
                    "any Zoe) — read the line before acting on it."));
                foreach (var hit in _bypassHits)
                {
                    var card = Z.Box($"{hit.file}:{hit.line}", hit.reason);
                    card.Add(Z.Text(hit.text, ZuiText.Small, hit.text));
                    card.Add(Z.Button("Open", $"Open {hit.file} at line {hit.line}.", () => OpenAt(hit.file, hit.line)).W(70f));
                    section.Add(card);
                }
            }

            root.Add(section);
        }

        static void OpenAt(string relPath, int line)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(relPath);
            if (asset != null) AssetDatabase.OpenAsset(asset, line);
            else Debug.LogWarning($"[Palette Health] Could not find '{relPath}' any more — it may have moved.");
        }
    }
}
