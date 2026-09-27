using UnityEditor;
using UnityEngine;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds.EditorTools {

    /// <summary>
    /// Converts the older per-sound effect settings — a gain, an equaliser, compression, normalisation, a fade, and
    /// volume and pitch curves — into the equivalent effect chain, once and permanently.
    ///
    /// **Why this exists.** Those settings only ever took effect by being rendered into a new audio file ahead of
    /// time. The chain can express all of them and is applied as the sound plays, so once a sound has been converted
    /// it needs no rendered file, its settings become visible and editable in the chain editor, and changing one is
    /// heard immediately. The fixed row of on/off toggles that used to edit them has been removed for that reason.
    ///
    /// **Nothing is lost by not running it.** Playback converts an unconverted sound's settings on the fly, so it
    /// still sounds as its author intended either way. Running this makes the conversion permanent and visible
    /// instead of implicit, which is what lets someone then edit it. It is offered as an action rather than done
    /// automatically because it rewrites authored data, and that should be a decision somebody takes knowingly.
    ///
    /// It is safe to run more than once: converting a sound clears the old settings, so a second run finds nothing
    /// left to do.
    /// </summary>
    public static class ZoundsChainMigrationMenu {

        [MenuItem("Laubrary/Zounds/Convert old per-sound effects into chains")]
        public static void Migrate() {
            var project = ZoundsProject.Instance;
            if (project == null || project.zoundLibrary == null) {
                EditorUtility.DisplayDialog("Nothing to convert", "No Zounds project is loaded.", "OK");
                return;
            }

            int pending = 0;
            var klips = project.zoundLibrary.klips;
            for (int i = 0; i < klips.Count; i++) {
                if (klips[i] != null && ChainMigration.HasLegacyEdits(klips[i])) pending++;
            }

            if (pending == 0) {
                EditorUtility.DisplayDialog("Nothing to convert",
                    "No sound is still using the older effect settings. Everything is already on chains.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Convert " + pending + " sound(s) to effect chains?",
                    "Each one's gain, equaliser, compression, normalisation, fade and volume/pitch curves become an " +
                    "equivalent chain, and the old settings are cleared.\n\n" +
                    "They will then be visible and editable in the chain editor, applied as the sound plays, with no " +
                    "rendered file needed. This rewrites authored data.",
                    "Convert", "Cancel")) {
                return;
            }

            int migrated = 0;
            ZoundsWindow.ModifyAndSaveZoundsProject("convert old effects to chains", () => {
                migrated = ChainMigration.MigrateProject(project.zoundLibrary);
            });

            // The cached layouts were built from the pre-conversion state, so they no longer describe these sounds.
            ZoundDspPlayback.InvalidateLayouts();

            Debug.Log("[Zounds] Converted " + migrated + " sound(s) from the older effect settings to effect chains.");
            EditorUtility.DisplayDialog("Converted", "Converted " + migrated + " sound(s). Their effects are now in " +
                                        "the chain editor and are applied as the sound plays.", "OK");
        }
    }
}
