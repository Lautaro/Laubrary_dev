using System;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    [InitializeOnLoad]
    public static class ZoundAPIBridge {

        static ZoundAPIBridge() {
            ZoundAPI.onEditorAPIKlipCreated += BrowserTab.OnKlipAdded;
            ZoundAPI.onEditorAPIZequenceCreated += BrowserTab.OnZequenceAdded;
            ZoundAPI.onEditorAPIZequenceAddZound += AddZoundToZequence;
            ZoundAPI.onSetAllTabsDirty += ZoundsWindowProperties.DirtyAll;
            ZoundAPI.onModifyZoundsProject += ZoundsWindow.ModifyZoundsProject;
        }

        private static void AddZoundToZequence(Zequence zequence, Zound zoundToAdd, bool local) {
            if (ZequenceEditorWindow.TryGetEditor(zequence, out var editorWindow)) {
                editorWindow.AddNewZoundEntry(zequence, zoundToAdd, local);
            }
            else {
                ZequenceEditorWindow.AddNewZoundEntryNoEditor(zequence, zoundToAdd, local);
            }
        }

    }
}
