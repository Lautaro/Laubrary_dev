using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The picker requests the Zounds editors make: new Klips from the workspace's clips (the browser's +, a Zequence's
    /// + Local Klip), a shared sound for a Zequence track, and a Klip's source clip. Each builds the listing through
    /// <see cref="ZoundPickerCatalog"/> and says what a pick does, so every call site gets the same window.
    /// </summary>
    internal static class ZoundPickerRequests {

        /// <summary>Pick one or more clips; each becomes a new Klip handed to <paramref name="onKlipAdded"/> (one undo step for all).
        /// The bottom row carries the other ways to get a Klip: import a file, import and trim one, an external file, an empty placeholder.</summary>
        public static ZoundPickerRequest NewKlips(Action<Klip> onKlipAdded, string nameOverride, EditorWindow previewOwner) {
            var req = new ZoundPickerRequest {
                title = "New Klip from a clip", listing = ZoundPickerRequest.Listing.Clips, multi = true, pickLabel = "Add",
                items = ZoundPickerCatalog.Clips(), previewOwner = previewOwner, stateKey = "clips",
            };
            req.onPick = items => {
#if ADDRESSABLES_INSTALLED
                ZoundsWindow.ModifyZoundsProject("add new klips", () => {
                    for (int i = 0; i < items.Count; i++) {
                        if (items[i].audioRef == null) continue;
                        // One override cannot name several Klips: it goes to the first, the rest keep their clip names.
                        var klip = BrowserTab.CreateKlipFromAudioRef(items[i].audioRef, i == 0 ? nameOverride : null);
                        if (klip != null) onKlipAdded?.Invoke(klip);
                    }
                }, true);
#endif
            };
#if ADDRESSABLES_INSTALLED
            req.actions.Add(("Import file…", "Copies a WAV from outside the project into the Sources folder and makes a Klip of it.", () => BrowserTab.ImportExternalFile(nameOverride, onKlipAdded), true));
            req.actions.Add(("Import & trim…", "Opens a WAV from outside the project in a trim window; only the kept part is copied into Sources, as a new Klip.", () => BrowserTab.ImportAndTrimExternalFile(nameOverride, onKlipAdded), true));
#endif
            req.actions.Add(("External file…", "One Klip per WAV picked from the external source root (the file stays where it is).", () => BrowserTab.AddKlipFromExternalFile(nameOverride, onKlipAdded), true));
            req.actions.Add(("Empty Klip", "A Klip with no audio yet: a name to reserve for a sound that does not exist yet.", () => {
                ZoundsWindow.ModifyZoundsProject("add empty placeholder klip", () => onKlipAdded?.Invoke(BrowserTab.CreateEmptyPlaceholderKlip(nameOverride)), true);
            }, true));
            return req;
        }

        /// <summary>Pick one or more library sounds to become tracks of <paramref name="parent"/>: itself and anything that
        /// contains it are left out; what is already a track is ticked and listed first.</summary>
        public static ZoundPickerRequest SharedZounds(CompositeZound parent, Action<Zound> onChosen, EditorWindow previewOwner) {
            var already = new HashSet<string>();
            foreach (var e in parent.zoundEntries) if (e != null && !e.local) already.Add("zound:" + e.zoundId);
            var req = new ZoundPickerRequest {
                title = "Add a shared sound to " + parent.name, listing = ZoundPickerRequest.Listing.Zounds, multi = true, pickLabel = "Add",
                items = ZoundPickerCatalog.Zounds(z => z.id != parent.id && !(z is CompositeZound cz && ZequenceHandler.CheckRecursiveness(cz, parent))),
                previewOwner = previewOwner, stateKey = "zounds", alreadyPicked = already,
            };
            req.onPick = items => {
                int group = Undo.GetCurrentGroup();
                foreach (var it in items) if (it.zound != null) onChosen?.Invoke(it.zound);
                Undo.CollapseUndoOperations(group);
            };
            return req;
        }

        /// <summary>Pick the one clip a Klip plays from.</summary>
        public static ZoundPickerRequest KlipSource(Klip klip, Action<AudioClip> onChosen, EditorWindow previewOwner) {
            var req = new ZoundPickerRequest {
                title = "Source clip for " + klip.name, listing = ZoundPickerRequest.Listing.Clips, multi = false, pickLabel = "Use",
                items = ZoundPickerCatalog.Clips(), previewOwner = previewOwner, stateKey = "clips",
            };
            req.onPick = items => { if (items.Count > 0 && items[0].clip != null) onChosen?.Invoke(items[0].clip); };
            return req;
        }
    }
}
