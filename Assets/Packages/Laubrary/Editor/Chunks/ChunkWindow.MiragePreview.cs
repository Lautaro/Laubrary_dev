// ChunkWindow.MiragePreview — module 7 of the Chunks 2.0 design ("Mirage Preview"), AgentHQ T-0039.
//
// The shortcut half of the module: one button that puts THIS spec in front of you, playing, in the window built
// for looking at things. The other half lives in Mirage — MirageRig.Realize grows a `case ChunkSpec` that hands
// the entry to MirageChunkBurst, and MirageHud grows the replay control that fires it again.
//
// Copied deliberately, near line-for-line, from ZoetropeWindows.PreviewInMirage(Zoe): a throwaway MirageView is
// created via ScriptableObject.CreateInstance and NEVER passed to AssetDatabase.CreateAsset, so it is
// structurally invisible to every browser and picker in the project (they all enumerate through
// AssetDatabase.FindAssets) with no separate "hidden" flag to maintain. EnsureMirageStageOpen is copied for the
// same reason it exists there: opening the Mirage window only focuses a window and points it at a view — the
// live preview needs a MirageRig, which lives in the stage scene. Without it the button would drop you into a
// window that silently shows nothing until you happened to know you had to open that scene by hand, which is a
// missing step, not a workflow.
//
// The button is a plain row, not a titled Z.Section: ui-layout-rules forbids a box titled with the same text as
// its one control, and a lone button has nothing to group with.

using Laubrary.Mirage;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using MirageWindow = Laubrary.Mirage.Editor.MirageWindow;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        void BuildMiragePreview(VisualElement root, ChunkSpec c)
        {
            // "Worth previewing" = the burst would actually put something on screen. A spec whose count has been
            // dialled to zero AND has no composed 2.0 module switched on spawns literally nothing, so the button
            // would open a stage and show an empty frame — the press would not do what its label says.
            bool anyDebris = c != null && c.countMax > 0;
            bool anyModule = c != null && ChunkModules.AnyEnabled(c);
            bool worthPreviewing = anyDebris || anyModule;

            string tooltip = worthPreviewing
                ? "Open Mirage with a throwaway preview of this burst, playing at real scale against the test " +
                  "backdrop, so you can see the composed result rather than reading dials. Bursts are one-shot, " +
                  "so the Mirage HUD carries a Replay button for this entry (and an optional auto-replay). " +
                  "Chunks only move in Play mode, so press Play once Mirage is up. The preview view is created " +
                  "in memory only — it is never saved as a project asset, so it never appears in Mirage's own " +
                  "Browse list or anywhere else; press this again any time for a fresh one."
                : "Nothing to preview: this spec spawns no chunks (its maximum count is 0) and has no composed " +
                  "module switched on, so a burst would put nothing on screen. Raise the count in Emission, or " +
                  "switch on a module such as Particle Splash or Pyre Spawner.";

            var button = Z.Button("Preview in Mirage", tooltip, () => PreviewInMirage(c)).W(170f);
            button.SetEnabled(worthPreviewing);

            // The tooltip is repeated on the row because a disabled UI Toolkit element does not reliably receive
            // the pointer events a tooltip is resolved from — and the disabled case is exactly the one where the
            // explanation matters most. The row itself stays enabled, so hovering anywhere on it answers "why
            // can't I press this?".
            var row = Z.Row(button);
            row.tooltip = tooltip;

            root.Add(Z.VSpace(8f));   // breathing room: this is an action, not another dial in the Timeline block
            root.Add(row);
        }

        /// <summary>Make sure the scene that actually RENDERS a Mirage preview is open.
        ///
        /// No-op when a rig is already present (any scene providing one is fine — the stage is not special-cased
        /// by name at runtime), and quietly does nothing if the stage scene isn't in the project, leaving the
        /// window usable for editing the spec.</summary>
        static void EnsureMirageStageOpen()
        {
            if (UnityEngine.Object.FindFirstObjectByType<MirageRig>() != null) return;

            string path = null;
            foreach (var guid in AssetDatabase.FindAssets("MirageStage t:Scene"))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(p)) { path = p; break; }
            }
            if (string.IsNullOrEmpty(path)) return;

            // The user pressed a button that opens a preview, so a save prompt here is expected and theirs to
            // answer; a cancel means leave their scene alone and don't open anything.
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                path, UnityEditor.SceneManagement.OpenSceneMode.Single);
        }

        static void PreviewInMirage(ChunkSpec spec)
        {
            if (spec == null) return;
            EnsureMirageStageOpen();

            var view = ScriptableObject.CreateInstance<MirageView>();
            view.name = $"{spec.name} (Burst Preview)";
            view.AddEntry(spec, Vector2.zero);

            // Note what is NOT set here: entry.manualControls. That flag is the Zoe path's opt-in for character
            // controls; a burst's replay control is unconditional (see MirageHud.DrawManualControls), because a
            // one-shot with no way to re-fire it is not previewable at all.
            MirageWindow.OpenFor(view);
        }
    }
}
