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

            var label = new Label("Views")
            {
                tooltip = "Saved arrangements of this window's view state — which sections are folded, "
                    + "which gear settings are open, which optional controls are shown. Never an authored "
                    + "value; the shared views asset only stores view state."
            };
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginRight = 6f;
            Add(label);

            _picker = new DropdownField
            {
                tooltip = "Pick a saved view to apply it. Switching a view changes only how the window is "
                    + "arranged — never the asset's authored values — and never dirties the views asset."
            };
            _picker.style.minWidth = 150f;
            _picker.style.marginRight = 6f;
            _picker.choices = PresetNames();
            if (_picker.choices.Count > 0) _picker.SetValueWithoutNotify(_picker.choices[0]);
            _picker.RegisterValueChangedCallback(ev =>
            {
                if (!string.IsNullOrEmpty(ev.newValue)) ApplyPreset(ev.newValue);
            });
            Add(_picker);

            Add(Z.Button("Update", "Overwrite the selected view with the window's current arrangement.",
                () => { if (!string.IsNullOrEmpty(_picker.value)) SaveInto(_picker.value); }));
            // T-0276 — "Delete view", not "Delete". Every ZuiAssetWindow puts a "Delete" in its toolbar that
            // deletes the ASSET FILE and cannot be undone, and this bar sits in the same window a few rows
            // below it: one word, two nouns, one of them irreversible. The noun is what tells them apart.
            Add(Z.Button("Delete view", "Remove the selected view from the shared views asset. The asset "
                + "itself and everything authored in it are untouched.",
                () => { if (!string.IsNullOrEmpty(_picker.value)) DeletePreset(_picker.value); }));

            _newName = Z.TextInput("",
                "Type a name, then Save as, to store the current arrangement as a new view.",
                _ => { }, 120f);
            _newName.style.marginLeft = 10f;
            Add(_newName);
            Add(Z.Button("Save as",
                "Save the window's current arrangement as a new view under the typed name (creates the "
                + "views asset the first time).",
                () =>
                {
                    if (string.IsNullOrWhiteSpace(_newName.value)) return;
                    SaveInto(_newName.value);
                    _newName.SetValueWithoutNotify("");
                }));
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
            AssetDatabase.SaveAssets();

            EditorPrefs.SetString(_prefsKey, name);
            RefreshPicker(name);
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
            AssetDatabase.SaveAssets();

            if (EditorPrefs.GetString(_prefsKey, "") == name) EditorPrefs.DeleteKey(_prefsKey);
            RefreshPicker("");
        }

        void RefreshPicker(string select)
        {
            var names = PresetNames();
            _picker.choices = names;
            _picker.SetValueWithoutNotify(
                !string.IsNullOrEmpty(select) && names.Contains(select) ? select
                : names.Count > 0 ? names[0]
                : "");
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
        }
    }
}
