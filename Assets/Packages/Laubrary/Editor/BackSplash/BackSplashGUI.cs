using System;
using UnityEditor;
using UnityEngine;

namespace Laubrary.BackSplash.Editor
{
    /// <summary>
    /// The Recall / Save half of the BackSplash editor: the preset browser and the copy-in / write-out
    /// semantics. The panel of controls itself is <see cref="BackSplashZui"/> — this file used to carry an
    /// IMGUI DrawInline as well, but with every consumer retained-mode there was no caller left for it.
    ///
    /// Operates on a caller-owned <see cref="BackSplashSettings"/> instance, NOT a shared BackSplash asset
    /// reference — editing the fields below only ever mutates the caller's own private copy. Recall and Save
    /// both go through the same shared <see cref="Laubrary.AssetKit.Editor.LauAssetBrowser"/> thumbnail browser (not a bespoke one) —
    /// Recall's click means "copy values from this," Save's click means "overwrite this," and Save adds the
    /// picker's optional create-new row for naming a fresh preset instead.
    ///
    /// Deliberately has NO "linked preset" concept — an earlier version remembered which preset a copy was
    /// last recalled from and let ★ overwrite it with one click, no picker needed. Dropped on request: an
    /// implicit remembered target is exactly the kind of state that caused the ORIGINAL bug this whole file
    /// exists to fix (one view's edits silently overwriting another view's shared backdrop) — a single
    /// accidental ★ press could just as easily ruin a DIFFERENT view's preset now that saving is deliberately
    /// separated from recalling. Requiring an explicit, visible choice every time (browse thumbnails, pick
    /// one to overwrite, or type a new name) trades one click for making that choice impossible to get wrong
    /// by accident.
    /// </summary>
    public static class BackSplashGUI
    {
        /// Open the Recall browser anchored at `anchor` (screen rect of the button that asked for it) and copy
        /// the picked preset's values into `settings`. Shared by every consumer so the picker and the
        /// copy semantics exist once. `onChanged` fires after the
        /// copy actually happens, which is a later event than the click that opened the popup.
        ///
        /// `owner` is the UnityEngine.Object `settings` actually lives on — when supplied, the copy is
        /// wrapped in `Undo.RecordObject`/`EditorUtility.SetDirty` here, so a Recall dirties (and can be
        /// undone on) the right asset without every caller having to remember to do it.
        public static void ShowRecall(Rect anchor, BackSplashSettings settings, Action onChanged = null,
            UnityEngine.Object owner = null, string editName = "BackSplash")
        {
            if (settings == null) return;
            Laubrary.AssetKit.Editor.LauAssetBrowser.Show(anchor, typeof(BackSplash), picked =>
            {
                var src = (BackSplash)picked;
                if (src == null) return;
                if (owner != null) Undo.RecordObject(owner, $"Recall {editName}");
                settings.CopyFrom(src);
                if (owner != null) EditorUtility.SetDirty(owner);
                onChanged?.Invoke();
            }, null);
        }

        /// The Save half of the same split: pick a preset to overwrite, or name a new one.
        public static void ShowSave(Rect anchor, BackSplashSettings settings)
        {
            if (settings == null) return;
            Laubrary.AssetKit.Editor.LauAssetBrowser.Show(anchor, typeof(BackSplash), picked =>
                Overwrite(settings, (BackSplash)picked), null,
                onCreateNew: name => CreateNew(settings, name),
                pickHint: "Click an existing preset to overwrite it:");
        }

        static void Overwrite(BackSplashSettings settings, BackSplash target)
        {
            if (target == null) return;
            Undo.RecordObject(target, "Update BackSplash preset");
            settings.CopyTo(target);
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();
        }

        static void CreateNew(BackSplashSettings settings, string name)
        {
            var fresh = Laubrary.AssetKit.Editor.AssetLibrary<BackSplash>.Create(name, "Assets/BackSplash");
            settings.CopyTo(fresh);
            EditorUtility.SetDirty(fresh);
            AssetDatabase.SaveAssets();
        }
    }
}
