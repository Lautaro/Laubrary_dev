using System;
using UnityEditor;
using UnityEngine;

namespace Laubrary.BackSplash.Editor
{
    /// <summary>
    /// The inline BackSplash editor — Recall/Save toolbar + the compact colour/image/position/zoom/tint combo.
    /// Originally hand-rolled inside Pyre's own window; lifted out here so any tool (Pyre, Mirage, future ones)
    /// can embed the exact same editing surface, instead of each tool re-implementing it or settling for a
    /// bare ObjectField.
    ///
    /// Operates on a caller-owned <see cref="BackSplashSettings"/> instance, NOT a shared BackSplash asset
    /// reference — editing the fields below only ever mutates the caller's own private copy. Recall and Save
    /// both go through the same shared <see cref="LauAssetPicker"/> thumbnail browser (not a bespoke one) —
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
        public const float DefaultPadSize = 68f;
        public const float DefaultSliderWidth = 150f;
        public const float DefaultColorWidth = 130f;

        // Default domain is in PIXELS — correct for Pyre's hand-painted IMGUI preview, where imagePos is added
        // directly to a screen-space Rect (its own viewport is ~300-900 x 320px, so +-40px stays comfortably
        // inside it). Mirage is a DIFFERENT unit space entirely: its imagePos feeds a real Transform.position
        // read by an orthographic Camera (MirageRig.SyncBackSplash), so the same +-40 there means world UNITS,
        // not pixels — measured against this project's actual preview camera (orthographicSize 5, aspect 1.6),
        // the visible frustum is only +-8 x +-5 world units, so +-40 was 5-8x past the edge of the camera's
        // view before the pad was even touched. Mirage passes its own domain, derived from its live camera's
        // real half-extents, instead of relying on this pixel-space default.
        // onChanged fires after Recall actually copies new values in — needed because that happens inside a
        // POPUP's click callback, on a LATER GUI event than the one that opened it, so a caller's own
        // EditorGUI.BeginChangeCheck/EndChangeCheck bracket around this whole call (the usual way callers
        // dirty+repaint on edit) can never see it: EndChangeCheck already ran and returned before the popup
        // was ever clicked. Direct field edits below (colour/pad/zoom/tint) DON'T need this — those mutate
        // settings synchronously within this same call, which BeginChangeCheck/EndChangeCheck catches fine.
        public static void DrawInline(BackSplashSettings settings,
            float padSize = DefaultPadSize, float sliderWidth = DefaultSliderWidth, float colorWidth = DefaultColorWidth,
            float domainHalfWidth = 40f, float domainHalfHeight = 40f, Action onChanged = null)
        {
            using (ZUI.HRow())
            {
                bool openRecall = ZUI.Button(new GUIContent("Recall…", "Copy colour/image/position/zoom/tint FROM an existing preset — a one-time copy, not a live link."));
                Rect recallRect = GUILayoutUtility.GetLastRect();
                if (openRecall) ShowRecall(recallRect, settings, onChanged);
                ZUI.HorizontalSpace();

                bool openSave = ZUI.Button(new GUIContent("Save…", "Write this copy's current values TO a preset you pick (overwriting it) or a new one you name."));
                Rect saveRect = GUILayoutUtility.GetLastRect();
                if (openSave) ShowSave(saveRect, settings);

                GUILayout.FlexibleSpace();
                ZUI.HelpIcon("A private backdrop copy: Recall copies values FROM an existing preset (a one-time " +
                              "copy, not a live link). Save writes this copy's current values TO a preset you " +
                              "pick from a thumbnail browser (overwriting it) or a new one you name. Editing the " +
                              "fields below only ever changes this copy, never a shared asset used elsewhere.");
            }

            using (ZUI.HRow())
            {
                using (ZUI.NarrowLabel("Colour"))
                    settings.cameraColor = EditorGUILayout.ColorField(
                        new GUIContent("Colour", "Solid background fill behind the image."), settings.cameraColor, true, true, false,
                        GUILayout.Width(colorWidth), GUILayout.Height(18f));
                ZUI.HorizontalSpace();

                var thumbRect = GUILayoutUtility.GetRect(padSize, padSize, GUILayout.Width(padSize), GUILayout.Height(padSize));
                settings.image = (Sprite)EditorGUI.ObjectField(thumbRect,
                    new GUIContent("", "The backdrop image sprite."), settings.image, typeof(Sprite), false);
                ZUI.HorizontalSpace();

                // domainHalfWidth/Height is caller-supplied, in whatever unit space that caller's imagePos
                // actually renders in — see the DrawInline doc comment above. ClampImagePos is a much larger
                // (+-250) hard backstop that only matters for the raw X/Y fields and free-drag elsewhere; it's
                // a no-op here since domainHalfWidth/Height is always tighter.
                settings.imagePos = BackSplash.ClampImagePos(
                    ZUI.PositionPad(settings.imagePos, new Rect(-domainHalfWidth, -domainHalfHeight, domainHalfWidth * 2f, domainHalfHeight * 2f), padSize));
                ZUI.HorizontalSpace();

                GUILayout.BeginVertical(GUILayout.Width(sliderWidth), GUILayout.Height(padSize));
                GUILayout.FlexibleSpace();
                settings.imageZoom = ZUI.MicroSlider(settings.imageZoom, 0.1f, 16f, "Zoom", defaultValue: 1f,
                                                        options: new[] { GUILayout.Width(sliderWidth), GUILayout.Height(18f) });
                EditorGUILayout.Space(3f);
                GUILayout.Label(new GUIContent("Tint", "Multiplies the image's own colours."), EditorStyles.miniLabel);
                settings.imageTint = EditorGUILayout.ColorField(GUIContent.none, settings.imageTint, true, true, false,
                                                                   GUILayout.Width(sliderWidth), GUILayout.Height(18f));
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
            }
        }

        static void Overwrite(BackSplashSettings settings, BackSplash target)
        {
            if (target == null) return;
            Undo.RecordObject(target, "Update BackSplash preset");
            settings.CopyTo(target);
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();
        }

        /// Open the Recall browser anchored at `anchor` (screen rect of the button that asked for it) and copy
        /// the picked preset's values into `settings`. Split out of DrawInline so a RETAINED-mode window — a
        /// UI Toolkit tool that builds its own controls and can't call an IMGUI drawer — gets the identical
        /// picker and the identical copy semantics instead of re-implementing them. `onChanged` fires after the
        /// copy actually happens, which is a later event than the click that opened the popup.
        public static void ShowRecall(Rect anchor, BackSplashSettings settings, Action onChanged = null)
        {
            if (settings == null) return;
            Laubrary.AssetKit.Editor.LauAssetPicker.Show(anchor, typeof(BackSplash), picked =>
            {
                var src = (BackSplash)picked;
                if (src == null) return;
                settings.CopyFrom(src);
                onChanged?.Invoke();
            }, null);
        }

        /// The Save half of the same split: pick a preset to overwrite, or name a new one.
        public static void ShowSave(Rect anchor, BackSplashSettings settings)
        {
            if (settings == null) return;
            Laubrary.AssetKit.Editor.LauAssetPicker.Show(anchor, typeof(BackSplash), picked =>
                Overwrite(settings, (BackSplash)picked), null,
                onCreateNew: name => CreateNew(settings, name),
                pickHint: "Click an existing preset to overwrite it:");
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
