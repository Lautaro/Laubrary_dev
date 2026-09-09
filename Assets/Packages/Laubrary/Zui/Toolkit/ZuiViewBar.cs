// ZuiViewBar — a reusable, toolkit-level "saved views" bar any ZuiWindow can drop above its dials. It
// is the UI + preset CRUD half of the Z2 view-preset system; ZuiBox (Z1) is the state half, and
// ZuiViewStore is the committed asset. A "view" is view-state only (folds, gears, which opt-in controls
// are shown) — never an authored value.
//
// The HOST owns the two things a bar can't decide for itself: where the store asset lives, and how the
// window's view state is captured/applied (it aggregates its ZuiBoxes' CaptureView/ApplyView). The bar
// owns the row of controls, the preset CRUD, and the per-user "last used" pointer:
//
//   getStore   — return the tool's ZuiViewStore, or null if none has been authored yet.
//   createStore— MINT the store (AssetDatabase.CreateAsset + Undo.RegisterCreatedObjectUndo) and return
//                it. Invoked ONLY from Save-as, and ONLY when getStore() is null. The bar never touches
//                AssetDatabase itself, so the host keeps full control of the asset path/folder.
//   capture    — snapshot the window's current view state into a fresh dictionary.
//   apply      — push a view-state dictionary back onto the window (programmatic; raises no ViewChanged).
//   prefsKey   — EditorPrefs key under which THIS user's last-used preset name is remembered.
//
// Switching or applying a view never dirties the shared store asset — only Save-as / Update / Delete do
// (those run from a user's button click, the sanctioned save path). The host calls RestoreLast() once
// after building its UI so the window opens on the view the user left it in.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiViewBar : VisualElement
    {
        readonly Func<ZuiViewStore> _getStore;
        readonly Func<ZuiViewStore> _createStore;
        readonly Func<Dictionary<string, bool>> _capture;
        readonly Action<IReadOnlyDictionary<string, bool>> _apply;
        readonly string _prefsKey;

        DropdownField _picker;
        TextField _newName;
        Button _renameBtn;
        // T-0321 — the three buttons that also need a SELECTED view. They used to stay enabled with nothing
        // picked and silently return (`if (!string.IsNullOrEmpty(_picker.value))`), which is reachable in three
        // clicks: Delete view the last saved view and the picker goes empty while all three still look live.
        // Rename already greyed itself with a reason; its siblings now do the same, from the same place.
        Button _applyBtn, _updateBtn, _deleteBtn, _saveAsBtn;

        public ZuiViewBar(Func<ZuiViewStore> getStore, Func<ZuiViewStore> createStore,
            Func<Dictionary<string, bool>> capture, Action<IReadOnlyDictionary<string, bool>> apply,
            string prefsKey)
        {
            _getStore = getStore;
            _createStore = createStore;
            _capture = capture;
            _apply = apply;
            _prefsKey = prefsKey ?? "";
            Build();
        }

        ZuiViewStore Store => _getStore?.Invoke();

        List<string> PresetNames()
        {
            var names = new List<string>();
            var store = Store;
            if (store != null)
                foreach (var p in store.presets) names.Add(p.name);
            return names;
        }

        static ZuiViewPreset Find(ZuiViewStore store, string name)
            => store == null ? null : store.presets.Find(p => p.name == name);

        // ── UI ─────────────────────────────────────────────────────────────────────
        void Build()
        {
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.flexWrap = Wrap.Wrap;          // wraps onto a second line in a narrow dials pane
            style.marginBottom = 6f;

            // T-0310 — the host (ZuiAssetWindow) rebuilds its whole UI on EditorApplication.projectChanged,
            // and the FIRST Save-as creates the views asset, which is itself a project change: the bar that
            // ends up on screen is built mid-CreateAsset, before the preset exists, and nothing ever told it
            // to look again — the view the author just saved was not in the picker until the window was
            // reopened (measured: choices=[] with the store holding the preset). So the bar re-reads the
            // store on every later project change and when the picker is pressed, keeping the current pick.
            RegisterCallback<AttachToPanelEvent>(_ => { Live.Add(this); EditorApplication.projectChanged += OnProjectChanged; });
            RegisterCallback<DetachFromPanelEvent>(_ => { Live.Remove(this); EditorApplication.projectChanged -= OnProjectChanged; });

            var label = new Label("Views")
            {
                // T-0284 — this said "which sections are folded", which a view has never stored: both hosts
                // capture through paneRoot.Query<ZuiBox>() (ShaperWindow.cs:418, PyreWindow.cs:467) and
                // ZuiSection is not a ZuiBox, so a section's fold state is neither captured nor restored.
                // Measured: applying a view moved 9 of 9 cards and 0 of 11 sections.
                tooltip = "Saved arrangements of this window's view state — which CARDS are folded, "
                    + "which gear settings are open, which optional controls are shown. Section folding is "
                    + "not part of a view. Never an authored value; the shared views asset only stores "
                    + "view state."
            };
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginRight = 6f;
            Add(label);

            _picker = new DropdownField
            {
                // T-0286 — picking a DIFFERENT view applies it immediately; re-picking the one already
                // shown raises no change event and does nothing, so the tooltip now says where the
                // re-apply gesture lives instead of implying every pick acts.
                tooltip = "Pick a saved view to apply it. Switching a view changes only how the window is "
                    + "arranged — never the asset's authored values — and never dirties the views asset. "
                    + "Re-selecting the view already shown does nothing by itself — press Apply for that."
            };
            _picker.style.minWidth = 150f;
            _picker.style.marginRight = 6f;
            _picker.RegisterCallback<PointerDownEvent>(_ => ResyncPicker(), TrickleDown.TrickleDown);
            _picker.choices = PresetNames();
            if (_picker.choices.Count > 0) _picker.SetValueWithoutNotify(_picker.choices[0]);
            _picker.RegisterValueChangedCallback(ev =>
            {
                if (!string.IsNullOrEmpty(ev.newValue)) ApplyPreset(ev.newValue);
                RefreshButtonStates();
            });
            Add(_picker);

            // T-0286 — the dropdown only applies a view when its VALUE CHANGES, so re-selecting the view
            // that is already showing (or restoring it after rearranging the window by hand) raises no
            // ChangeEvent and does nothing. "Apply" is the same gesture as picking it, made reachable when
            // the value does not change — it never overwrites the saved view, only re-pushes it onto the
            // window.
            _applyBtn = Z.Button("Apply", "", () => { if (!string.IsNullOrEmpty(_picker.value)) ApplyPreset(_picker.value); });
            Add(_applyBtn);

            _updateBtn = Z.Button("Update", "", () => { if (!string.IsNullOrEmpty(_picker.value)) SaveInto(_picker.value); });
            Add(_updateBtn);
            // T-0276 — "Delete view", not "Delete". Every ZuiAssetWindow puts a "Delete" in its toolbar that
            // deletes the ASSET FILE and cannot be undone, and this bar sits in the same window a few rows
            // below it: one word, two nouns, one of them irreversible. The noun is what tells them apart.
            _deleteBtn = Z.Button("Delete view", "", () => { if (!string.IsNullOrEmpty(_picker.value)) DeletePreset(_picker.value); });
            Add(_deleteBtn);

            // T-0310 — the name field and the two buttons that consume it wrap as ONE unit in a narrow pane,
            // rather than "Delete view | name" on one row and "Rename | Save as" orphaned on the next.
            var nameRow = new VisualElement();
            nameRow.style.flexDirection = FlexDirection.Row;
            nameRow.style.alignItems = Align.Center;
            nameRow.style.flexShrink = 0f;
            Add(nameRow);

            _newName = Z.TextInput("",
                "Type a name, then Save as to store the current arrangement as a new view, or Rename to "
                + "give the SELECTED view this name.",
                _ => RefreshButtonStates(), 120f);
            _newName.style.marginLeft = 10f;
            nameRow.Add(_newName);

            // T-0310 — renaming a saved view used to be Save-as-under-new-name then Delete-view-on-the-old,
            // a two-step the author had to invent. Rename is that pair as one operation, reusing the same
            // name field Save-as already uses. Greyed with a reason (ZuiReflect's SetEnabled+tooltip
            // pattern) rather than silently doing nothing, for: nothing picked, nothing typed, the typed
            // name already IS the picked view's name, or the typed name collides with a different view.
            // T-0321 — "Rename view", not "Rename". This is T-0276's finding one noun over: every
            // ZuiAssetWindow puts a "Rename" in its toolbar that renames the ASSET FILE, and this bar sits a
            // couple of rows below it — measured live in Shaper, both were drawn, ~90pt apart, spelled
            // identically. "Delete view" already carries the noun for exactly this reason; its sibling now
            // does too.
            _renameBtn = Z.Button("Rename view", "", () => RenamePreset(_picker.value, _newName.value));
            nameRow.Add(_renameBtn);

            _saveAsBtn = Z.Button("Save as", "", () =>
            {
                if (string.IsNullOrWhiteSpace(_newName.value)) return;
                SaveInto(_newName.value);
                _newName.SetValueWithoutNotify("");
                RefreshButtonStates();
            });
            nameRow.Add(_saveAsBtn);

            RefreshButtonStates();
        }

        // ── preset CRUD (Save-as / Update / Delete are the sanctioned save path) ─────
        void SaveInto(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;

            var store = Store;
            if (store == null)
            {
                store = _createStore?.Invoke();   // host mints the asset (AssetDatabase + Undo)
                if (store == null) return;          // host declined / failed — nothing to save into
            }

            var captured = _capture != null ? _capture() : new Dictionary<string, bool>();

            Undo.RecordObject(store, "Save Zui View");
            var preset = Find(store, name);
            if (preset == null)
            {
                preset = new ZuiViewPreset { name = name };
                store.presets.Add(preset);
            }
            preset.entries = new List<ZuiViewEntry>();
            if (captured != null)
                foreach (var kv in captured)
                    preset.entries.Add(new ZuiViewEntry { key = kv.Key, val = kv.Value });
            EditorUtility.SetDirty(store);
            // T-0284 — flush the VIEWS asset, never the project. AssetDatabase.SaveAssets() writes every dirty
            // asset there is, so saving a view committed whatever unsaved edits happened to be open in the
            // window beside it — measured here: an unrelated document with an unsaved edit was written to disk
            // and left clean by pressing Save as. Same narrowing T-0276/T-0282 made on the asset toolbar; a
            // view is pure view state and has even less business publishing someone else's work.
            AssetDatabase.SaveAssetIfDirty(store);

            EditorPrefs.SetString(_prefsKey, name);
            RefreshPicker(name);
            ResyncAll();
        }

        void ApplyPreset(string name)
        {
            var preset = Find(Store, name);
            if (preset == null) return;
            var d = new Dictionary<string, bool>();
            foreach (var e in preset.entries) d[e.key] = e.val;
            _apply?.Invoke(d);
            EditorPrefs.SetString(_prefsKey, name);   // last-used pointer only — never dirties the asset
        }

        void DeletePreset(string name)
        {
            var store = Store;
            var preset = Find(store, name);
            if (store == null || preset == null) return;

            Undo.RecordObject(store, "Delete Zui View");
            store.presets.Remove(preset);
            EditorUtility.SetDirty(store);
            AssetDatabase.SaveAssetIfDirty(store);   // T-0284 — this asset, not the project. See SaveInto.

            if (EditorPrefs.GetString(_prefsKey, "") == name) EditorPrefs.DeleteKey(_prefsKey);
            RefreshPicker("");
            ResyncAll();
        }

        // T-0310 — the rename half of the pair; SaveInto/DeletePreset above are the other two thirds of
        // the preset CRUD this bar owns. Renames the preset itself (not a save-as-then-delete), so it
        // never touches `entries` and keeps the picker on the SAME entry under its new name.
        void RenamePreset(string oldName, string newName)
        {
            newName = newName?.Trim();
            var store = Store;
            var preset = Find(store, oldName);
            if (store == null || preset == null || string.IsNullOrEmpty(oldName)
                || string.IsNullOrEmpty(newName) || newName == oldName || Find(store, newName) != null)
                return;   // the Rename button is greyed for all of these; this is the belt-and-braces guard

            Undo.RecordObject(store, "Rename Zui View");
            preset.name = newName;
            EditorUtility.SetDirty(store);
            AssetDatabase.SaveAssetIfDirty(store);   // T-0284 — this asset, not the project. See SaveInto.

            if (EditorPrefs.GetString(_prefsKey, "") == oldName) EditorPrefs.SetString(_prefsKey, newName);

            _newName.SetValueWithoutNotify("");
            RefreshPicker(newName);
            ResyncAll();
        }

        void OnProjectChanged() => ResyncPicker();

        // T-0310 — every bar currently on screen. A store write from THIS bar can land while the host is
        // rebuilding (the first Save-as does: creating the asset is a project change), so the bar that
        // wrote is no longer the bar the author sees; measured: no projectChanged reached the new bar even
        // a tick later, and the saved view stayed out of the picker until it was pressed. Every write
        // therefore ends by making all live bars re-read the store — synchronously, because the host's
        // rebuild has already happened by then (and an EditorApplication.delayCall measured as never
        // arriving before the author's next interaction).
        static readonly HashSet<ZuiViewBar> Live = new HashSet<ZuiViewBar>();
        static void ResyncAll()
        {
            foreach (var b in Live.ToArray()) b.ResyncPicker();
        }

        /// Re-read the store's names without applying anything: the pick survives if it still exists,
        /// otherwise the picker falls back the way RefreshPicker does. Never raises the picker's change
        /// event, so a resync can never re-apply a view behind the author's back.
        void ResyncPicker()
        {
            if (_picker == null) return;
            var names = PresetNames();
            if (names.SequenceEqual(_picker.choices)) return;
            RefreshPicker(_picker.value);
        }

        void RefreshPicker(string select)
        {
            var names = PresetNames();
            _picker.choices = names;
            _picker.SetValueWithoutNotify(
                !string.IsNullOrEmpty(select) && names.Contains(select) ? select
                : names.Count > 0 ? names[0]
                : "");
            RefreshButtonStates();
        }

        // T-0310 — greys the Rename button with a reason (rather than a silent no-op) whenever activating
        // it right now would do nothing or something surprising: nothing picked, nothing typed, the typed
        // text already IS the picked view's name, or it collides with a DIFFERENT saved view.
        // T-0321 — and every OTHER button in the bar on the same principle. Apply / Update / Delete view all
        // read `_picker.value` and all returned in silence when it was empty; Save as returned in silence with
        // an empty name field. A control that looks live and does nothing is worse than a greyed one, because
        // the author's next move is to press it again.
        void RefreshButtonStates()
        {
            string picked = _picker?.value ?? "";
            string typed = _newName?.value?.Trim() ?? "";
            bool hasPick = !string.IsNullOrEmpty(picked);

            const string noPick = "No view is saved yet — type a name and press Save as to make one.";

            Set(_applyBtn, hasPick ? null : noPick,
                "Re-apply the selected view to the window as it is currently saved. Use this to put the "
                + "window back after moving folds/gears around, or the first time you pick a view the "
                + "dropdown was already showing (a re-select alone applies nothing).");
            Set(_updateBtn, hasPick ? null : noPick,
                "Overwrite the selected view with the window's current arrangement.");
            Set(_deleteBtn, hasPick ? null : noPick,
                "Remove the selected view from the shared views asset. The asset itself and everything "
                + "authored in it are untouched.");
            Set(_saveAsBtn, string.IsNullOrEmpty(typed) ? "Type a name for the new view above first." : null,
                "Save the window's current arrangement as a new view under the typed name (creates the "
                + "views asset the first time).");

            if (_renameBtn != null)
            {
                string reason;
                if (!hasPick) reason = "No view is selected to rename.";
                else if (string.IsNullOrEmpty(typed)) reason = "Type the new name above first.";
                else if (typed == picked) reason = "That is already this view's name.";
                else if (PresetNames().Contains(typed)) reason = $"A view named \"{typed}\" already exists.";
                else reason = null;
                Set(_renameBtn, reason, $"Rename the selected view \"{picked}\" to \"{typed}\".");
            }

            void Set(Button b, string reason, string whenEnabled)
            {
                if (b == null) return;
                b.SetEnabled(reason == null);
                b.tooltip = reason ?? whenEnabled;
            }
        }

        /// Re-apply this user's last-used view. The host calls it once after building its UI so a window
        /// opens on the view the user left it in. No-op if there is no store or no saved pointer.
        public void RestoreLast()
        {
            var store = Store;
            if (store == null) return;
            string last = EditorPrefs.GetString(_prefsKey, "");
            if (string.IsNullOrEmpty(last) || Find(store, last) == null) return;
            _picker.SetValueWithoutNotify(last);
            ApplyPreset(last);
            RefreshButtonStates();
        }
    }
}
