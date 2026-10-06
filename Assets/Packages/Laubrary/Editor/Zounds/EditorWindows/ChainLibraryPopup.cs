using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds {

    /// <summary>
    /// The chain library: every preset with its usage count and node summary; use one on the zound,
    /// audition it without assigning, rename, duplicate, delete (blocked while referenced, with
    /// "detach users and delete" as the explicit escape).
    /// </summary>
    internal class ChainLibraryPopup : PopupWindowContent {

        private Zound zound;
        private EditorWindow previewOwner;
        private readonly List<ZoundToken> auditionTokens = new List<ZoundToken>();
        private readonly HashSet<ZoundChainPreset> previewControls = new HashSet<ZoundChainPreset>();
        private Vector2 scroll;
        private ZoundChainPreset renaming;
        private string renameText;
        private ZoundToken auditionToken;

        public static void Show(Rect activator, Zound zound, EditorWindow previewOwner) {
            var popup = new ChainLibraryPopup { zound = zound, previewOwner = previewOwner };
            PopupWindow.Show(activator, popup);
        }

        public override Vector2 GetWindowSize() {
            int rows = Mathf.Clamp(ZoundChainLibrary.Presets.Count, 1, 12);
            return new Vector2(420f, 30f + rows * 22f + 8f);
        }

        public override void OnClose() {
            foreach (var control in previewControls) ZoundPreviewPlayback.StopControl(previewOwner, control);
            previewControls.Clear();
            foreach (var token in auditionTokens) if (token != null && token.state != ZoundToken.State.Killed) token.Kill();
            auditionTokens.Clear();
        }

        public override void OnGUI(Rect rect) {
            editorWindow.Repaint();
            using var _sheet = ZUI.UseSheet("Zounds");
            var presets = ZoundChainLibrary.Presets;
            var header = new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 20f);
            ZUI.Label(header, "Chain presets", ZUI.ZTextStyle.Subheader);
            var newRect = new Rect(header.xMax - 90f, header.y, 90f, 20f);
            if (ZUI.Button(newRect, new GUIContent("New from this", "Creates a preset from this zound's current chain and links the zound to it."), ZUI.Style.RichButton)) {
                ZoundsWindow.ModifyAndSaveZoundsProject("create chain preset", () => {
                    var chain = ZoundDspPlayback.ResolveChain(zound, out _);
                    var p = ZoundChainLibrary.Create(zound.name + " chain", chain);
                    ZoundChainLibrary.Assign(zound, p);
                    ZoundDspPlayback.InvalidateLayout(zound);
                });
            }

            var listRect = new Rect(rect.x, header.yMax + 4f, rect.width, rect.height - header.yMax - 4f);
            if (presets.Count == 0) {
                GUI.Label(new Rect(listRect.x + 8f, listRect.y, listRect.width, 20f), "No presets yet. 'New from this' saves the current chain as one.", EditorStyles.miniLabel);
                return;
            }
            var content = new Rect(0f, 0f, listRect.width - 16f, presets.Count * 22f);
            scroll = GUI.BeginScrollView(listRect, scroll, content);
            for (int i = 0; i < presets.Count; i++) {
                var p = presets[i];
                var row = new Rect(4f, i * 22f, content.width - 4f, 20f);
                bool inUseHere = zound.chainPresetId == p.id;
                if (inUseHere) EditorGUI.DrawRect(row, new Color(0.4f, 0.8f, 1f, 0.12f));
                int users = ZoundChainLibrary.CountUsers(p.id);

                var nameRect = new Rect(row.x, row.y, 150f, row.height);
                if (renaming == p) {
                    GUI.SetNextControlName("chain_rename");
                    renameText = EditorGUI.TextField(nameRect, renameText);
                    GUI.FocusControl("chain_rename");
                    var e = Event.current;
                    if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) {
                        var pp = p; string nn = renameText;
                        ZoundsWindow.ModifyAndSaveZoundsProject("rename chain preset", () => pp.name = ZoundChainLibrary.EnsureUniqueName(nn, pp));
                        renaming = null; e.Use();
                    }
                    else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { renaming = null; e.Use(); }
                }
                else {
                    GUI.Label(nameRect, new GUIContent(p.name, Summary(p) + "\nDouble-click to rename."), EditorStyles.boldLabel);
                    var e = Event.current;
                    if (e.type == EventType.MouseDown && e.clickCount == 2 && nameRect.Contains(e.mousePosition)) { renaming = p; renameText = p.name; e.Use(); }
                }
                var usersRect = new Rect(nameRect.xMax + 4f, row.y, 52f, row.height);
                GUI.Label(usersRect, new GUIContent(users + (users == 1 ? " user" : " users"), "Zounds playing this preset by live reference. Editing the preset changes all of them."), EditorStyles.miniLabel);

                float x = usersRect.xMax + 4f;
                var useRect = new Rect(x, row.y, 40f, row.height);
                var prev = GUI.enabled; GUI.enabled = prev && !inUseHere;
                if (ZUI.Button(useRect, new GUIContent("Use", inUseHere ? "Already in use on this zound." : "Links this zound to the preset (live reference)."), ZUI.Style.RichButton, ZUICornerMask.Left)) {
                    var pp = p;
                    ZoundsWindow.ModifyAndSaveZoundsProject("use chain preset", () => { ZoundChainLibrary.Assign(zound, pp); ZoundDspPlayback.InvalidateLayout(zound); });
                }
                GUI.enabled = prev;
                var playRect = new Rect(useRect.xMax, row.y, 26f, row.height);
                if (ZUI.Button(playRect, new GUIContent("▶", ZoundPreviewPlayback.Tooltip(previewOwner, p, "Plays this zound through the preset without assigning it.")), ZUI.Style.RichButton, ZoundPreviewPlayback.IsLoopPlaying(previewOwner, p) ? ZUI.Tint.Confirm : null, ZUICornerMask.None)) {
                    Audition(p);
                }
                var dupRect = new Rect(playRect.xMax, row.y, 40f, row.height);
                if (ZUI.Button(dupRect, new GUIContent("Copy", "Duplicates the preset."), ZUI.Style.RichButton, ZUICornerMask.None)) {
                    var pp = p;
                    ZoundsWindow.ModifyAndSaveZoundsProject("duplicate chain preset", () => ZoundChainLibrary.Duplicate(pp));
                }
                var delRect = new Rect(dupRect.xMax, row.y, 46f, row.height);
                string delTip = users > 0 ? "Referenced by " + users + " zound(s): deleting detaches each of them to a private copy first (confirmed)." : "Deletes the preset.";
                if (ZUI.Button(delRect, new GUIContent("Delete", delTip), ZUI.Style.RichButton, ZUI.Tint.Danger, ZUICornerMask.Right)) {
                    var pp = p;
                    if (users == 0 || EditorUtility.DisplayDialog("Delete chain preset", "'" + p.name + "' is used by " + users + " zound(s). Detach all of them to private copies and delete the preset?", "Detach all and delete", "Cancel")) {
                        ZoundsWindow.ModifyAndSaveZoundsProject("delete chain preset", () => { ZoundChainLibrary.Delete(pp, true); ZoundDspPlayback.InvalidateLayouts(); });
                        GUIUtility.ExitGUI();
                    }
                }
            }
            GUI.EndScrollView();
        }

        // The audition path IS the playback path: assign, play, restore — a plain ZoundEngine.PlayZound
        // call, same as any other preview button in this editor. The voice took its layout at start.
        private void Audition(ZoundChainPreset p) {
            previewControls.Add(p);
            if (ZoundPreviewPlayback.IsLoopPlaying(previewOwner, p)) { ZoundPreviewPlayback.StopControl(previewOwner, p); return; }
            int savedPreset = zound.chainPresetId, savedDetached = zound.detachedChainPresetId;
            var savedOverrides = new List<ChainParamOverride>(zound.chainOverrides);
            zound.chainPresetId = p.id; zound.chainOverrides.Clear();
            ZoundDspPlayback.InvalidateLayout(zound);
            foreach (var control in previewControls) if (ZoundPreviewPlayback.IsLoopPlaying(previewOwner, control)) ZoundPreviewPlayback.StopControl(previewOwner, control);
            var args = ZoundArgs.Default; args.ignoreCooldown = true;
            auditionToken = ZoundPreviewPlayback.Play(previewOwner, zound, args, p);
            if (auditionToken != null) auditionTokens.Add(auditionToken);
            zound.chainPresetId = savedPreset; zound.detachedChainPresetId = savedDetached; zound.chainOverrides.AddRange(savedOverrides);
            ZoundDspPlayback.InvalidateLayout(zound);
        }

        private static string Summary(ZoundChainPreset p) {
            var sb = new System.Text.StringBuilder();
            foreach (var n in p.chain.nodes) { if (sb.Length > 0) sb.Append(" → "); sb.Append(ZoundEffectDescriptors.Get(n.type).displayName); }
            if (p.chain.modifiers.Count > 0) sb.Append("  (" + p.chain.modifiers.Count + " modifier" + (p.chain.modifiers.Count == 1 ? ")" : "s)"));
            return sb.Length == 0 ? "empty chain" : sb.ToString();
        }
    }

}
