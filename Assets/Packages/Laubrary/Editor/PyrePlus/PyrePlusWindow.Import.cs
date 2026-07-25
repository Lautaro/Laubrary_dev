// PyrePlusWindow.Import.cs — Slice 9: the "Import from Pyre…" section.
//
// A Pyre (vanilla BlastSpec) object-field + a "Convert to Pyre Plus" button that runs the pure
// PyreToPlusConverter.ConvertFromPyre, CreateAssets a NEW .asset beside the source (never overwrites it),
// pings it, and surfaces the per-layer warnings (a Debug.Log always; a dialog when there are notes). NOT a
// menu item — it lives in the window, so it can never clutter the Laubrary menu.
using System.Collections.Generic;
using System.IO;
using Laubrary.Zui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.PyrePlus.Editor
{
    public partial class PyrePlusWindow
    {
        // The Pyre asset the "Convert" button reads. Held on the window (not serialized) — a transient picker.
        Laubrary.Pyre.Pyre _importSrc;

        void BuildImport(VisualElement root)
        {
            var box = Z.BoxKeyed("Import from Pyre",
                "Convert an existing vanilla Pyre (BlastSpec) asset into a NEW Pyre Plus asset. The source is "
                + "never touched — a fresh .asset is written beside it. Every dropped or approximated field is "
                + "reported after the conversion.",
                "pyreplus.import");

            // Raw UITK ObjectField — an asset picker is a known ZUI gap (a legitimate raw fallback today).
            var picker = new ObjectField("Pyre asset")
            {
                objectType = typeof(Laubrary.Pyre.Pyre),
                allowSceneObjects = false,
                value = _importSrc,
                tooltip = "The vanilla Pyre asset to import.",
            };
            picker.RegisterValueChangedCallback(e => _importSrc = e.newValue as Laubrary.Pyre.Pyre);
            box.Add(picker);

            box.Add(Z.Button("Convert to Pyre Plus",
                "Convert the picked Pyre asset into a new Pyre Plus asset beside it (undoable). Shows a report of "
                + "anything dropped or approximated.",
                DoImport));
            root.Add(box);
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
