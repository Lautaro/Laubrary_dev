// PyrePlusWindow.Import.cs — Slice 9: the "Import from Pyre…" section.
//
// A Pyre (vanilla BlastSpec) object-field + a "Convert to Pyre Plus" button that runs the pure
// PyreToPlusConverter.ConvertFromPyre, CreateAssets a NEW .asset beside the source (never overwrites it),
// pings it, and surfaces the per-layer warnings (a Debug.Log always; a dialog when there are notes). NOT a
// menu item — it lives in the window, so it can never clutter the Laubrary menu.
using System.Collections.Generic;
using System.IO;
using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.PyrePlus.Editor
{
    public partial class PyrePlusWindow
    {
        // The Pyre asset the "Convert" button reads. Held on the window (not serialized) — a transient picker.
        Laubrary.Pyre.Pyre _importSrc;

        // Thumbnail cache for the import-source swatch. Cleared at the top of RebuildImportPicker (its only
        // builder), so it stays bounded to the one selected source. This partial can't reach the window's
        // teardown (OnDisable lives in PyrePlusWindow.cs, which this task may not touch), so the last swatch
        // texture is released on the next rebuild / a domain reload rather than on window close.
        readonly Dictionary<Object, Texture2D> _importThumbs = new Dictionary<Object, Texture2D>();
        VisualElement _importPickerHost;   // the picker row; refilled by RebuildImportPicker when _importSrc changes

        void BuildImport(VisualElement root)
        {
            var box = Z.BoxKeyed("Import from Pyre",
                "Convert an existing vanilla Pyre (BlastSpec) asset into a NEW Pyre Plus asset. The source is "
                + "never touched — a fresh .asset is written beside it. Every dropped or approximated field is "
                + "reported after the conversion.",
                "pyreplus.import");

            // The Pyre asset picker — the shared LauAsset picker+preview (thumbnail swatch + Recall…/New ▾/Edit ✎),
            // the same row every other Laubrary asset field uses (mirrors Chunks' animation/trail sources). Once
            // Pyre implements IVisualPreview the swatch shows a real blast thumbnail. Held in its own host so a pick
            // can refill just this row (the swatch/name must reflect the new source). _importSrc is a transient
            // window field (not spec state), so picking just sets it and rebuilds — no Undo/Dirty.
            _importPickerHost = new VisualElement();
            box.Add(_importPickerHost);
            RebuildImportPicker();

            // WrapRow so the button sizes to its own content rather than stretching to the full box width — a
            // bare Button added to a box column stretches on the cross axis (measured 340px), the same reason
            // every other PyrePlus action button (+ Add layer / + Add modifier / + Add simulation) is wrapped.
            box.Add(WrapRow(Z.Button("Convert to Pyre Plus",
                "Convert the picked Pyre asset into a new Pyre Plus asset beside it (undoable). Shows a report of "
                + "anything dropped or approximated.",
                DoImport)));
            root.Add(box);
        }

        // Fills the import-source picker row; re-run on every pick so the swatch + name reflect the new source.
        // Clears the swatch thumb cache first (bounded to the one selected source — no window-close teardown
        // reaches this partial). Constraint is Pyre, so Recall/New ▾ offer only Pyre assets.
        void RebuildImportPicker()
        {
            if (_importPickerHost == null) return;
            LauAssetGridGUI.ClearCache(_importThumbs);
            _importPickerHost.Clear();
            const string pyreTip = "The vanilla Pyre asset to import.";
            _importPickerHost.Add(LauAssetElement.Build(_importSrc,
                picked => { _importSrc = picked as Laubrary.Pyre.Pyre; RebuildImportPicker(); },
                typeof(Laubrary.Pyre.Pyre), _importThumbs, "Pyre", "Assets", pyreTip));
        }

        void DoImport()
        {
            if (_importSrc == null) { ShowNotification(new GUIContent("Pick a Pyre asset to convert first.")); return; }

            var converted = PyreToPlusConverter.ConvertFromPyre(_importSrc, out List<string> warnings);

            string srcPath = AssetDatabase.GetAssetPath(_importSrc);
            string dir = string.IsNullOrEmpty(srcPath) ? "Assets" : Path.GetDirectoryName(srcPath).Replace('\\', '/');
            string baseName = (string.IsNullOrEmpty(_importSrc.name) ? "Pyre" : _importSrc.name) + " Plus";
            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{baseName}.asset");

            AssetDatabase.CreateAsset(converted, path);                 // a NEW asset — never overwrites the source
            Undo.RegisterCreatedObjectUndo(converted, "Import Pyre → Pyre Plus");
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(converted);
            Selection.activeObject = converted;

            int layerCount = converted.layers != null ? converted.layers.Count : 0;
            if (warnings.Count == 0)
            {
                Debug.Log($"[PyrePlus] Imported '{_importSrc.name}' → '{path}' ({layerCount} layer(s)). No warnings.", converted);
                ShowNotification(new GUIContent($"Converted → {Path.GetFileName(path)} (no warnings)."));
            }
            else
            {
                string bullets = "• " + string.Join("\n• ", warnings);
                Debug.Log($"[PyrePlus] Imported '{_importSrc.name}' → '{path}' ({layerCount} layer(s)), {warnings.Count} note(s):\n{bullets}", converted);
                EditorUtility.DisplayDialog("Pyre → Pyre Plus",
                    $"Created {path}\n\n{layerCount} layer(s), {warnings.Count} note(s):\n\n{bullets}", "OK");
            }
        }
    }
}
