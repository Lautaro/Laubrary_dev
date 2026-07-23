using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Laubrary.AssetKit.Editor
{
    /// The one inline "pick or create a LauAsset" field control — a thumbnail swatch, name, Recall (opens
    /// LauAssetPicker), New ▾ (sourced from LauAssetEditors' registered creators for the constraint), and an
    /// Edit pen (LauAssetEditors.Open, shown whenever the current asset has one registered). Replaces the
    /// per-field bespoke combos this grew out of (AmmoDef.visual's ad-hoc New (Aseprite)/New Pyre buttons,
    /// BackSplashGUI's Recall row) — those migrate onto this one control rather than staying separate.
    public static class LauAssetField
    {
        /// Returns true if a NEW asset was just created via the New ▾ menu (the caller's onPick delegate has
        /// already been invoked either way — Recall and New both assign through it directly).
        public static bool Draw(Object current, Action<Object> onPick, Type constraint,
            Dictionary<Object, Texture2D> thumbCache, string suggestedName, string folder, float swatchSize = 40f)
        {
            bool created = false;
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect swatchRect = GUILayoutUtility.GetRect(swatchSize, swatchSize, GUILayout.Width(swatchSize), GUILayout.Height(swatchSize));
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(swatchRect, new Color(0.11f, 0.12f, 0.15f));
                    var tex = current != null ? LauAssetGridGUI.GetThumbnail(current, null, thumbCache) : null;
                    if (tex != null)
                    {
                        float sc = Mathf.Min((swatchSize - 4f) / Mathf.Max(1, tex.width), (swatchSize - 4f) / Mathf.Max(1, tex.height));
                        float w = tex.width * sc, h = tex.height * sc;
                        GUI.DrawTexture(new Rect(swatchRect.x + (swatchSize - w) * 0.5f, swatchRect.y + (swatchSize - h) * 0.5f, w, h),
                            tex, ScaleMode.StretchToFill, true);
                    }
                }

                GUILayout.Label(current != null ? new GUIContent(current.name, current.GetType().Name) : new GUIContent("· none ·", "No asset assigned yet."),
                    EditorStyles.miniBoldLabel, GUILayout.MinWidth(50), GUILayout.MaxWidth(160));

                bool openRecall = GUILayout.Button(new GUIContent("Recall…", "Pick an existing asset from a thumbnail browser."), GUILayout.Width(64));
                Rect recallRect = GUILayoutUtility.GetLastRect();
                if (openRecall)
                    LauAssetPicker.Show(recallRect, constraint, picked => onPick?.Invoke(picked), current);

                var creatable = LauAssetEditors.RegisteredTypesFor(constraint).Where(LauAssetEditors.CanCreate).ToList();
                using (new EditorGUI.DisabledScope(creatable.Count == 0))
                {
                    if (GUILayout.Button(new GUIContent("New ▾", "Create a brand new asset and assign it here."), GUILayout.Width(48)))
                    {
                        if (creatable.Count == 1)
                        {
                            var made = LauAssetEditors.Create(creatable[0], suggestedName, folder);
                            if (made != null)
                            {
                                onPick?.Invoke(made);
                                if (LauAssetEditors.CanOpen(made)) LauAssetEditors.Open(made);
                                created = true;
                            }
                        }
                        else
                        {
                            var menu = new GenericMenu();
                            foreach (var t in creatable)
                            {
                                var concrete = t;
                                menu.AddItem(new GUIContent(concrete.Name), false, () =>
                                {
                                    var made = LauAssetEditors.Create(concrete, suggestedName, folder);
                                    if (made == null) return;
                                    onPick?.Invoke(made);
                                    if (LauAssetEditors.CanOpen(made)) LauAssetEditors.Open(made);
                                });
                            }
                            menu.ShowAsContext();
                        }
                    }
                }

                using (new EditorGUI.DisabledScope(current == null || !LauAssetEditors.CanOpen(current)))
                    if (GUILayout.Button(new GUIContent("✎", "Edit — open this asset in its own editor."), GUILayout.Width(24)))
                        LauAssetEditors.Open(current);
            }
            return created;
        }
    }
}
