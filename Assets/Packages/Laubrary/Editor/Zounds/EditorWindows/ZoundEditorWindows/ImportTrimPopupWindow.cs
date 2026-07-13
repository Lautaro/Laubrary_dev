using System.IO;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds {

    /// <summary>
    /// Modal popup shown when importing an external WAV with trimming. Reuses AudioSpectrumView
    /// (waveform + trim handles + volume envelope) over a temporary in-memory Klip, then bakes
    /// the kept region into the project via ZoundsSourceImport. Only the trimmed slice is saved,
    /// keeping the source project lean. See ZoundsSourceImport for the disk/metadata side.
    /// </summary>
    public class ImportTrimPopupWindow : EditorWindow {

        private string externalPath;
        private System.Action<Klip> onKlipAdded;
        private string klipName;

        private Klip tempKlip;
        private AudioSpectrumView spectrumView;
        private bool initialized;

        public static void Open(string externalPath, System.Action<Klip> onKlipAdded, string nameOverride) {
            var w = CreateInstance<ImportTrimPopupWindow>();
            w.titleContent = new GUIContent("Import & Trim");
            w.externalPath = externalPath;
            w.onKlipAdded = onKlipAdded;
            w.klipName = string.IsNullOrEmpty(nameOverride)
                ? Path.GetFileNameWithoutExtension(externalPath)
                : nameOverride;
            w.minSize = new Vector2(540f, 300f);
            w.Init();
            w.ShowUtility();
        }

        private void Init() {
            // Temporary, library-less Klip used only to drive AudioSpectrumView's editing UI.
            tempKlip = new Klip(0);
            tempKlip.externalSourcePath = externalPath;
            tempKlip.name = klipName;
            tempKlip.trimEnabled = true;
            tempKlip.clampToTrim = true;
            tempKlip.volumeEnvelope = new Envelope(Zound.MinVolumeRange, Zound.MaxVolumeRange);
            tempKlip.pitchEnvelope = new Envelope(Zound.MinPitchRange, Zound.MaxPitchRange);

            var preview = WavDecoder.LoadFromDisk(externalPath);
            if (preview == null) return;
            tempKlip.trimStart = 0f;
            tempKlip.trimEnd = preview.length;

            spectrumView = new AudioSpectrumView(this);
            spectrumView.height = 130f;
            // Trim handles and the volume envelope mutate tempKlip directly. The envelope
            // object is shared by reference (InitFromKlip assigns it), so edits land on tempKlip
            // automatically; we only need explicit callbacks for the scalar/bool fields.
            spectrumView.onTrimStartChanged = v => tempKlip.trimStart = v;
            spectrumView.onTrimEndChanged = v => tempKlip.trimEnd = v;
            spectrumView.onTrimEnabledChanged = v => tempKlip.trimEnabled = v;
            spectrumView.onClampToTrimChanged = v => tempKlip.clampToTrim = v;
            spectrumView.onVolumeEnabledChanged = v => tempKlip.volumeEnvelope.enabled = v;
            spectrumView.onPitchEnabledChanged = v => tempKlip.pitchEnvelope.enabled = v;
            spectrumView.InitFromKlip(tempKlip);
            initialized = true;
        }

        private void OnDisable() {
            if (spectrumView != null) {
                spectrumView.Destroy();
                spectrumView = null;
            }
        }

        private void OnGUI() {
            if (!initialized || tempKlip == null) {
                EditorGUILayout.HelpBox("Could not load the audio file:\n" + externalPath, MessageType.Error);
                if (GUILayout.Button("Close")) Close();
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Source: " + Path.GetFileName(externalPath), EditorStyles.miniLabel);
            klipName = EditorGUILayout.TextField("Klip Name", klipName);
            EditorGUILayout.Space();

            spectrumView.DrawLayout();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Only the trimmed region is saved into Sources, with the volume envelope baked in. " +
                "A link to the original file and trim is stored in the WAV metadata.",
                MessageType.Info);

            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(90f), GUILayout.Height(24f))) {
                Close();
            }
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.6f, 0.9f, 0.6f);
            if (GUILayout.Button("Import", GUILayout.Width(120f), GUILayout.Height(24f))) {
                DoImport();
            }
            GUI.backgroundColor = prevBg;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        private void DoImport() {
            // Capture everything before closing (close destroys the spectrum view).
            string path = externalPath;
            var cb = onKlipAdded;
            string name = klipName;
            var volEnv = tempKlip.volumeEnvelope;

            float ts, te;
            if (tempKlip.trimEnabled) {
                ts = tempKlip.trimStart;
                te = tempKlip.trimEnd;
            }
            else {
                // No trim: bake the whole file.
                ts = 0f;
                var preview = WavDecoder.LoadFromDisk(path);
                te = preview != null ? preview.length : tempKlip.trimEnd;
            }

            Close();
            // Defer the heavy asset work out of the GUI callback.
            EditorApplication.delayCall += () =>
                ZoundsSourceImport.ImportTrimmed(path, ts, te, volEnv, name, cb);
        }
    }
}
