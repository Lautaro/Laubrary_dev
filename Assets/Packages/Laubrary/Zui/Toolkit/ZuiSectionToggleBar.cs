// ZuiSectionToggleBar — an OPTIONAL, full-width alternative to folding sections one header-click at a
// time: a bar of latching buttons, one per ZuiSection, that shows/hides a whole section in bulk.
//
// The two styles are MUTUALLY EXCLUSIVE (T-0065): a leading "Sections / Toggle Bar" mode switch decides
// which control owns folding.
//   • Sections mode (default) — every section folds exactly as it always has, via its own header click;
//     the per-section buttons below are hidden.
//   • Toggle Bar mode — each section's own header click is disabled (ZuiSection.HeaderFoldDisabled), so
//     ONLY this bar's buttons show/hide it. A section is never fought over by two controls at once.
// Both modes drive the SAME ZuiSection.IsOpen — there is no separate "shown" state to keep in sync, and
// whichever section a caller left open/closed stays that way across a mode switch. The mode choice itself
// persists per `prefsKey` (EditorPrefs), so it survives window rebuilds and Unity restarts.
//
// Reusable: any ZUI tool built on ZuiSection can adopt this by building the sections it wants toggleable,
// then constructing one bar over them — see PyreWindow.BuildSectionToggleBar for the reference call site.
//
// T-0076 — SOLO. In Toggle Bar mode, right-clicking a section's button solos it: every other section
// disappears and the soloed button gets a green border. More than one section can be soloed at once (the
// union of soloed sections stays visible). There are, in effect, two sets — the NORMAL set (each section's
// own show/hide, exactly as the bar always worked) and the SOLO set (which buttons were right-clicked).
// While the solo set is non-empty it overrides the normal set entirely; a left-click on a button is
// swallowed during solo (there is nothing sensible for it to do while a section's visibility is
// solo-controlled). The moment the LAST solo is removed, every section returns to wherever the normal set
// had it — the snapshot taken the instant the first solo was engaged.
//
// T-0085 — QUICK VIEWS. In Toggle Bar mode the bar carries three quick views, cycled by RIGHT-CLICKING
// the "Toggle Bar" segment of the mode switch: "Your selection" (the set the user actually arranged),
// "Show all" (every section visible) and "Show none" (every section hidden). The two latter are
// TEMPORARY — a way to sweep the whole window open or shut and then get the arrangement back — so the
// user's own set is snapshotted before either is entered, and cycling round to "Your selection" restores
// it. The instant the user toggles ANY section by hand, that new arrangement becomes "Your selection"
// and the quick view reverts to it: there is no mode to remember to leave. A right-click while the bar
// isn't even in Toggle Bar mode only switches INTO it, at "Your selection", so the gesture can never
// wipe an arrangement on first contact.
//
// Both the current quick view and the remembered selection live in EditorPrefs alongside the mode, NOT
// in instance fields, because most consumer windows throw this control away and rebuild it on every
// asset refresh — state held on the instance would silently reset under the user mid-session. The
// selection is keyed BY SECTION LABEL, not by position, because several tools build their roster
// conditionally: two different rosters of the same LENGTH would otherwise restore the wrong sections.
// Leaving Toggle Bar mode ENDS a sweep (the user's set goes back first, then the flag drops) — a sweep
// that could be carried out of bar mode and back in would look like the user's own arrangement, and the
// next right-click would save it over the real one.
//
// T-0197 — the SOLO set and its pre-solo snapshot are persisted the same way, for the same reason: a
// window that rebuilds on nearly every edit (Shaper, unlike Pyre which rebuilds rarely) was recreating
// this control mid-solo constantly, and `_solo`/`_preSolo` being instance-only fields meant every one of
// those rebuilds silently dropped the solo the user had just set. Both are keyed by section LABEL like
// the user selection above, for the same conditional-roster reason.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiSectionToggleBar : VisualElement
    {
        const string ModeTooltip =
            "Sections: each section folds via its own header, like before. Toggle Bar: only the buttons "
            + "to the right show/hide a section — headers stop folding, so the two never disagree.";

        readonly (string label, ZuiSection section)[] _sections;
        readonly string _modeKey;
        readonly string _quickKey;
        readonly string _userSelKey;
        readonly string _soloKey;
        readonly string _preSoloKey;
        readonly ZuiSegmented _mode;
        readonly ZuiSegmented _bar;

        // ── solo (T-0076) ──
        readonly HashSet<int> _solo = new();
        bool[] _preSolo;   // snapshot of the normal set, taken when the first solo engages; null otherwise

        /// The three quick views cycled by right-clicking the "Toggle Bar" segment. ShowAll/ShowNone are
        /// temporary sweeps over the top of UserSelection, which is the set the user actually arranged.
        enum QuickView { UserSelection = 0, ShowAll = 1, ShowNone = 2 }

        bool BarMode
        {
            get => EditorPrefs.GetBool(_modeKey, false);
            set => EditorPrefs.SetBool(_modeKey, value);
        }

        QuickView Quick
        {
            get => (QuickView)EditorPrefs.GetInt(_quickKey, 0);
            set => EditorPrefs.SetInt(_quickKey, (int)value);
        }

        /// `prefsKey` scopes the persisted mode choice — pass something unique per tool/window (e.g. "Pyre").
        /// `sections` is (button label, the ZuiSection it shows/hides); a null section is skipped harmlessly
        /// (its button still shows but does nothing), matching ZuiSection's own null-safety elsewhere.
        public ZuiSectionToggleBar(string prefsKey, params (string label, ZuiSection section)[] sections)
        {
            _sections = sections;
            _modeKey = "ZuiSectionToggleBar." + prefsKey + ".barMode";
            _quickKey = "ZuiSectionToggleBar." + prefsKey + ".quickView";
            _userSelKey = "ZuiSectionToggleBar." + prefsKey + ".userSel";
            _soloKey = "ZuiSectionToggleBar." + prefsKey + ".solo";
            _preSoloKey = "ZuiSectionToggleBar." + prefsKey + ".preSolo";
            AddToClassList("zui-section-togglebar");
            style.width = new StyleLength(Length.Percent(100));
            style.flexDirection = FlexDirection.Row;
            style.flexWrap = Wrap.Wrap;
            style.alignItems = Align.Center;

            var labels = new string[sections.Length];
            for (int i = 0; i < sections.Length; i++) labels[i] = sections[i].label;

            _mode = Z.Segmented(BarMode ? 1 : 0, new[] { "Sections", "Toggle Bar" }, ModeTooltip,
                i => { BarMode = i == 1; Apply(); });
            Add(_mode);

            // Right-click the "Toggle Bar" segment to cycle the quick views — safe for the same reason the
            // per-section solo gesture below is: Clickable only tracks the LEFT button, so a right-click
            // PointerDownEvent never reaches it.
            _mode.SegmentAt(1).RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 1) { CycleQuickView(); e.StopPropagation(); }
            });

            _bar = ZuiSegmented.Multi(
                i => _sections[i].section != null && _sections[i].section.IsOpen,
                labels,
                "Show or hide a whole section — only active in Toggle Bar mode. Right-click to Solo it "
                + "(hide every other section); solo more than one at once, and they all return to where "
                + "they were once every solo is cleared.",
                (i, on) =>
                {
                    // A left-click while any solo is active has nothing sensible to do — the section's
                    // visibility is solo-controlled — so just re-light the bar back to the solo mask (the
                    // segment's "on" class already flipped optimistically before this callback ran).
                    if (_solo.Count > 0) { _bar.SetOn(i2 => _solo.Contains(i2)); return; }
                    if (_sections[i].section != null) _sections[i].section.IsOpen = on;

                    // The user just arranged the sections by hand — whatever is on screen now IS their
                    // selection, whichever quick view was showing, so record it and drop back to it.
                    SaveUserSelection();
                    Quick = QuickView.UserSelection;
                    RefreshQuickCue();
                });
            _bar.style.marginLeft = 8f;
            Add(_bar);

            // Right-click a segment to solo it. Clickable (the button's own click tracking) only engages
            // for the LEFT mouse button, so a right-click PointerDownEvent never reaches it — safe to fully
            // own here.
            for (int i = 0; i < _sections.Length; i++)
            {
                int idx = i;
                _bar.SegmentAt(i).RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button == 1) { ToggleSolo(idx); e.StopPropagation(); }
                });
            }

            // If a section instead folds via its own header (Sections mode), relight this bar to match —
            // so switching back to Toggle Bar mode later reflects whatever the user left open/closed.
            foreach (var (_, section) in sections)
            {
                if (section == null) continue;
                section.ViewChanged += () =>
                {
                    _bar.SetOn(i => _sections[i].section != null && _sections[i].section.IsOpen);
                    SaveUserSelection();   // a header fold is the user arranging things too
                };
            }

            Apply();

            // A rebuilt bar must LOOK like the state it claims to be in. This runs on EVERY construction,
            // not only for a temporary sweep, because ZuiSection's open/closed state is a STATIC dictionary
            // that a domain reload wipes back to "everything open" while these prefs survive — so without
            // it a recompile silently loses the user's arrangement, and the first hand-toggle afterwards
            // saves the wrong set on top of it. In steady state it is a no-op: every hand-toggle already
            // stored exactly this set.
            //
            // T-0197 — solo takes priority over whatever quick view was showing: if a solo was active when
            // this control was last torn down (a Rebuild() mid-solo, or a domain reload), restore THAT
            // instead, before first layout, so nothing flashes and nothing resets under the user's cursor.
            if (BarMode)
            {
                var soloLabels = LoadSoloLabels();
                if (soloLabels != null)
                    for (int i = 0; i < _sections.Length; i++)
                        if (soloLabels.Contains(_sections[i].label)) _solo.Add(i);

                if (_solo.Count > 0)
                {
                    var preSel = LoadPreSolo();
                    _preSolo = new bool[_sections.Length];
                    for (int i = 0; i < _sections.Length; i++)
                        _preSolo[i] = preSel == null || !preSel.TryGetValue(_sections[i].label, out bool open) || open;

                    ApplySoloMask();
                    for (int i = 0; i < _sections.Length; i++)
                        _bar.SegmentAt(i).EnableInClassList("zui-segmented__solo", _solo.Contains(i));
                }
                else
                {
                    ApplyQuickView(Quick);
                }
            }
        }

        void Apply()
        {
            bool barMode = BarMode;
            if (!barMode)
            {
                ClearSolo();   // solo only makes sense while the bar owns visibility
                // …and so does a quick view. A sweep is TEMPORARY, so leaving Toggle Bar mode ENDS it:
                // put the user's own selection back BEFORE dropping the flag. Merely clearing the flag
                // would let a Show none be carried out of bar mode and straight back in, at which point
                // the bar believes an all-hidden window IS the user's selection — and the next right-click
                // saves it over the real one, unrecoverably.
                if (Quick != QuickView.UserSelection)
                {
                    Quick = QuickView.UserSelection;
                    ApplyQuickView(QuickView.UserSelection);
                }
            }
            _bar.style.display = barMode ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var (_, section) in _sections)
                if (section != null) section.HeaderFoldDisabled = barMode;
            RefreshQuickCue();
        }

        // ── solo (T-0076) ────────────────────────────────────────────────────────────────────────────

        /// Right-click on segment `idx`: flip it in/out of the solo set, then reapply. Entering solo (the
        /// set going 0 → 1) snapshots every section's current visibility as the normal set to return to;
        /// leaving solo (the set going 1 → 0) restores exactly that snapshot.
        void ToggleSolo(int idx)
        {
            bool wasEmpty = _solo.Count == 0;
            if (!_solo.Add(idx)) _solo.Remove(idx);

            if (wasEmpty && _solo.Count > 0)
            {
                _preSolo = new bool[_sections.Length];
                for (int i = 0; i < _sections.Length; i++)
                    _preSolo[i] = _sections[i].section != null && _sections[i].section.IsOpen;
                SavePreSolo();
            }

            SaveSolo();

            if (_solo.Count > 0)
            {
                ApplySoloMask();
            }
            else
            {
                RestoreNormal();
            }

            for (int i = 0; i < _sections.Length; i++)
                _bar.SegmentAt(i).EnableInClassList("zui-segmented__solo", _solo.Contains(i));
        }

        /// Push the current solo set onto every section and relight the bar — the same mask logic used both
        /// from a live right-click (ToggleSolo) and when restoring a solo persisted from before a rebuild.
        void ApplySoloMask()
        {
            for (int i = 0; i < _sections.Length; i++)
                if (_sections[i].section != null) _sections[i].section.IsOpen = _solo.Contains(i);
            _bar.SetOn(i => _solo.Contains(i));
        }

        /// Restore every section to the snapshot taken when solo began. No-op if solo was never engaged.
        void RestoreNormal()
        {
            if (_preSolo == null) return;
            for (int i = 0; i < _sections.Length; i++)
                if (_sections[i].section != null) _sections[i].section.IsOpen = _preSolo[i];
            _bar.SetOn(i => _sections[i].section != null && _sections[i].section.IsOpen);
            _preSolo = null;
            ClearPersistedSolo();
        }

        /// Drop every solo (e.g. the bar just left Toggle Bar mode), restoring the normal set.
        void ClearSolo()
        {
            if (_solo.Count == 0) return;
            _solo.Clear();
            RestoreNormal();
            for (int i = 0; i < _sections.Length; i++)
                _bar.SegmentAt(i).RemoveFromClassList("zui-segmented__solo");
        }

        // ── quick views (T-0085) ─────────────────────────────────────────────────────────────────────

        /// Right-click on the "Toggle Bar" segment: step Your selection → Show all → Show none → Your
        /// selection. The very first right-click while the bar is NOT in Toggle Bar mode only switches
        /// into it and stops — a right-click must never destroy the user's arrangement on first contact,
        /// before they have seen that the gesture does anything at all.
        void CycleQuickView()
        {
            if (!BarMode)
            {
                BarMode = true;
                Quick = QuickView.UserSelection;
                _mode.SetOn(i => i == 1);
                Apply();
                return;
            }

            ClearSolo();   // solo and quick views are competing overrides; never let them tangle

            // Leaving the user's own set: record what is on screen first, so cycling back around always
            // has something real to restore — including the very first time the gesture is ever used.
            if (Quick == QuickView.UserSelection) SaveUserSelection();

            QuickView next = Quick == QuickView.UserSelection ? QuickView.ShowAll
                           : Quick == QuickView.ShowAll ? QuickView.ShowNone
                           : QuickView.UserSelection;
            Quick = next;
            ApplyQuickView(next);
        }

        /// Push `q`'s visibility onto every section and relight the bar. A section the stored selection
        /// says nothing about — nothing stored yet, or a label this roster has never carried — defaults
        /// to OPEN: a section the user cannot see is also a section they cannot get back, so the safe
        /// direction to fail is visible. Never leave a blank window whose cue claims "Your selection".
        void ApplyQuickView(QuickView q)
        {
            if (q == QuickView.UserSelection)
            {
                var sel = LoadUserSelection();
                foreach (var (label, section) in _sections)
                    if (section != null)
                        section.IsOpen = sel == null || !sel.TryGetValue(label, out bool open) || open;
            }
            else
            {
                bool open = q == QuickView.ShowAll;
                foreach (var (_, section) in _sections)
                    if (section != null) section.IsOpen = open;
            }

            _bar.SetOn(i => _sections[i].section != null && _sections[i].section.IsOpen);
            RefreshQuickCue();
        }

        /// Remember the sections' CURRENT visibility as the user's own selection, keyed BY LABEL rather
        /// than by position. Several tools build their roster conditionally (Launimator drops "Identify
        /// Sprites" when the left pane collapses and adds "Meta Layers" when the laumination has them),
        /// so a positional set of the same LENGTH can describe an entirely different list of sections —
        /// restoring it would show the wrong ones with nothing to detect the mismatch.
        void SaveUserSelection()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var (label, section) in _sections)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(label).Append('=').Append(section != null && section.IsOpen ? '1' : '0');
            }
            EditorPrefs.SetString(_userSelKey, sb.ToString());
        }

        /// The remembered selection as label → visible, or null when there is nothing stored. Split on
        /// the LAST '=' so a label containing one still round-trips; an unparseable entry is skipped
        /// rather than failing the whole set, and a caller treats an absent label as open.
        Dictionary<string, bool> LoadUserSelection()
        {
            string s = EditorPrefs.GetString(_userSelKey, string.Empty);
            if (string.IsNullOrEmpty(s)) return null;
            var sel = new Dictionary<string, bool>();
            foreach (string entry in s.Split(';'))
            {
                int eq = entry.LastIndexOf('=');
                if (eq > 0) sel[entry.Substring(0, eq)] = entry[eq + 1] == '1';
            }
            return sel.Count > 0 ? sel : null;
        }

        // ── solo persistence (T-0197) ────────────────────────────────────────────────────────────────

        /// Remember the CURRENT solo set, keyed by section label like SaveUserSelection above. An empty set
        /// deletes the key rather than writing an empty string, so LoadSoloLabels' "nothing stored" and
        /// "solo is empty" both read back as null without a separate sentinel.
        void SaveSolo()
        {
            if (_solo.Count == 0) { EditorPrefs.DeleteKey(_soloKey); return; }
            var sb = new System.Text.StringBuilder();
            foreach (int i in _solo)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(_sections[i].label);
            }
            EditorPrefs.SetString(_soloKey, sb.ToString());
        }

        /// The persisted solo set as labels, or null when nothing is soloed.
        HashSet<string> LoadSoloLabels()
        {
            string s = EditorPrefs.GetString(_soloKey, string.Empty);
            if (string.IsNullOrEmpty(s)) return null;
            var set = new HashSet<string>(s.Split(';'));
            return set.Count > 0 ? set : null;
        }

        /// Remember the pre-solo snapshot (the normal set to return to once every solo clears), keyed by
        /// label exactly like SaveUserSelection — the snapshot is itself a label → visible selection.
        void SavePreSolo()
        {
            if (_preSolo == null) { EditorPrefs.DeleteKey(_preSoloKey); return; }
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _sections.Length; i++)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(_sections[i].label).Append('=').Append(_preSolo[i] ? '1' : '0');
            }
            EditorPrefs.SetString(_preSoloKey, sb.ToString());
        }

        /// The persisted pre-solo snapshot as label → visible, or null when nothing is stored.
        Dictionary<string, bool> LoadPreSolo()
        {
            string s = EditorPrefs.GetString(_preSoloKey, string.Empty);
            if (string.IsNullOrEmpty(s)) return null;
            var sel = new Dictionary<string, bool>();
            foreach (string entry in s.Split(';'))
            {
                int eq = entry.LastIndexOf('=');
                if (eq > 0) sel[entry.Substring(0, eq)] = entry[eq + 1] == '1';
            }
            return sel.Count > 0 ? sel : null;
        }

        /// Drop both persisted solo keys — called once the normal set is restored, so a later rebuild finds
        /// nothing to re-engage.
        void ClearPersistedSolo()
        {
            EditorPrefs.DeleteKey(_soloKey);
            EditorPrefs.DeleteKey(_preSoloKey);
        }

        /// Repaint the "Toggle Bar" segment's state cue and rewrite its tooltip for the CURRENT state.
        /// The cue is colour only — no label change, no border width — so the segment keeps its exact
        /// size and the bar beside it can never re-wrap under the user's cursor. It is also only a
        /// confirmation: the per-section bar already tells the honest story on its own (every button lit
        /// is Show all, none lit is Show none).
        void RefreshQuickCue()
        {
            var seg = _mode.SegmentAt(1);
            QuickView q = Quick;
            bool barMode = BarMode;
            seg.EnableInClassList("zui-segmented__quickview", barMode && q != QuickView.UserSelection);
            // In Sections mode a right-click here does not cycle anything — it only switches modes — so
            // the tooltip must not promise a cycle the gesture will not perform.
            seg.tooltip = barMode
                ? ModeTooltip
                  + " Right-click to cycle quick views: Your selection → Show all → Show none. Currently: "
                  + QuickName(q) + ". Toggling any section makes that the new 'Your selection'."
                : ModeTooltip
                  + " Right-click to switch here; once in Toggle Bar mode, right-click again to cycle the "
                  + "quick views (Your selection → Show all → Show none).";
        }

        static string QuickName(QuickView q)
            => q == QuickView.ShowAll ? "Show all" : q == QuickView.ShowNone ? "Show none" : "Your selection";
    }
}
