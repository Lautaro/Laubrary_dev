using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace Laubrary.Zounds {

    public class RenderZequenceToKlipPopup : PopupWindowContent {

        private string klipName;
        private Zequence zequence;
        private static AudioMixerGroup mixerGroup;
        private float duration;

        private bool isPreviewing;
        private float startTime;
        private GUIStyle timerStyle;

        private GUIContent label_mixerGroup = new GUIContent("Mixer Group", "You can use a mixer group to apply effects on the klip result.");

        public static RenderZequenceToKlipPopup Show(Vector2 position, Zequence zequence, float initialDuration) {
            if (zequence == null) return null;
            var popup = new RenderZequenceToKlipPopup(zequence, initialDuration);
            PopupWindow.Show(new Rect(position.x, position.y, 0, 0), popup);
            return popup;
        }

        public RenderZequenceToKlipPopup(Zequence zequence, float duration) {
            this.zequence = zequence;
            this.duration = duration;
            klipName = ZoundDictionary.EnsureUniqueZoundName(zequence.name + " (Rendered)");
        }

        public override Vector2 GetWindowSize() {
            return new Vector3(300f, 142f);
        }

        public override void OnGUI(Rect rect) {
            var prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 85f;

            var fieldRect = new Rect(rect.x + 5f, rect.y + 5f, rect.width - 10f, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(fieldRect, "Render: " + zequence.name, EditorStyles.centeredGreyMiniLabel);
            fieldRect.y += fieldRect.height + 2f;
            EditorGUI.BeginChangeCheck();
            klipName = EditorGUI.TextField(fieldRect, "Klip Name", klipName);
            if (EditorGUI.EndChangeCheck()) {
                klipName = ZoundDictionary.EnsureUniqueZoundName(klipName);
            }
            fieldRect.y += fieldRect.height + 2f;

            GUI.Label(new Rect(fieldRect.x-1f, fieldRect.y, 85f, fieldRect.height), label_mixerGroup);
            if (GUI.Button(new Rect(fieldRect.x + 86f, fieldRect.y, fieldRect.width - 85f, fieldRect.height), mixerGroup != null? mixerGroup.name : "-No Effect-", EditorStyles.popup)) {
                OpenMixerGroupDropdown(tempMixerGroup => mixerGroup = tempMixerGroup);
            }
            fieldRect.y += fieldRect.height + 2f;

            duration = EditorGUI.FloatField(fieldRect, "Duration", duration);
            if (duration <= 0f) duration = Mathf.Epsilon;
            fieldRect.y += fieldRect.height + 7f;

            if (isPreviewing) {
                if (timerStyle == null) {
                    timerStyle = new GUIStyle(EditorStyles.label);
                    timerStyle.alignment = TextAnchor.MiddleRight;
                    timerStyle.normal.textColor = Color.green;
                }
                var timerRect = new Rect(fieldRect.x, fieldRect.y, fieldRect.width / 2f, fieldRect.height);
                EditorGUI.LabelField(timerRect, (Time.realtimeSinceStartup - startTime).ToString("0.00") + " s ", timerStyle);
                var endButtonRect = new Rect(timerRect.xMax, timerRect.y, timerRect.width, timerRect.height);
                if (GUI.Button(endButtonRect, "Set as Duration")) {
                    duration = Time.realtimeSinceStartup - startTime;
                    isPreviewing = false;
                }
                editorWindow.Repaint();
            }
            fieldRect.y += fieldRect.height + 7f;

            if (GUI.Button(new Rect(fieldRect.x, fieldRect.y, fieldRect.width / 2f, fieldRect.height), "Preview")) {
                Preview();
                editorWindow.Repaint();
            }
            if (GUI.Button(new Rect(fieldRect.x + fieldRect.width/2f, fieldRect.y, fieldRect.width/2f, fieldRect.height), "Render")) {
                isPreviewing = false;
                RenderToKlip();
                editorWindow.Close();
            }

            EditorGUIUtility.labelWidth = prevLabelWidth;
        }

        private void OpenMixerGroupDropdown(System.Action<AudioMixerGroup> onSelectMixerGroup) {
#if ADDRESSABLES_INSTALLED
            List<AudioMixerGroup> allMixerGroups = new List<AudioMixerGroup>();
            RoutingTab.GetAllAddresableMixerGroups(ref allMixerGroups);
            if (allMixerGroups.Count == 0) {
                Debug.LogWarning("There is no MixerGroup found that is set as Addressable.");
            }
            else {
                var mixerGroupMenu = new GenericMenu();

                mixerGroupMenu.AddItem(new GUIContent("-No Effect-"), false, () => {
                    onSelectMixerGroup?.Invoke(null);
                });
                foreach (var mixerGroup in allMixerGroups) {
                    var mg = mixerGroup;
                    mixerGroupMenu.AddItem(new GUIContent(mixerGroup.name), false, () => {
                        onSelectMixerGroup?.Invoke(mg);
                    });
                }

                mixerGroupMenu.ShowAsContext();
            }
#else
            Debug.LogError("Please import Addressables package.");
#endif
        }

        private static void StopTokenCompletely(ZoundToken token) {
            var audioSources = token.audioSources;
            token.Kill();
            //foreach (var audioSource in audioSources) {
            //    Debug.Log("Stopping: " + audioSource.name, audioSource);
            //    audioSource.Stop();
            //    audioSource.volume = 0f;
            //    audioSource.outputAudioMixerGroup = null;
            //}
        }

        private void Preview() {
            isPreviewing = true;
            startTime = Time.realtimeSinceStartup;
            EnsureAllKlipsRendered(zequence);
            var token = ZoundEngine.PlayZound(zequence, new ZoundArgs() {
                startImmediately = true,
                delay = 0f,
                volumeOverride = 1f,
                pitchOverride = 1f,
                chanceOverride = 1f,
                useFixedAverageValues = false,
                overrideMixerGroup = true,
                mixerGroupOverride = mixerGroup,
                overrideDuration = duration
            });
            token.onComplete += () => {
                StopTokenCompletely(token);
            };
        }

        private void RenderToKlip() {
            EnsureAllKlipsRendered(zequence);

            var token = ZoundEngine.PlayZound(zequence, new ZoundArgs() {
                startImmediately = true,
                delay = 0f,
                volumeOverride = 1f,
                pitchOverride = 1f,
                chanceOverride = 1f,
                useFixedAverageValues = false,
                overrideMixerGroup = true,
                mixerGroupOverride = mixerGroup,
                overrideDuration = duration
            });
            token.onComplete += () => {
                StopTokenCompletely(token);
            };
            var recordingData = new RecordingData(token, renderedClip => {
                var zoundsProject = ZoundsProject.Instance;
                string filePath = Path.Combine(zoundsProject.projectSettings.workFolderPath, klipName + ".wav");
                var reloadedAudio = AudioRenderUtility.SaveAudio(renderedClip, filePath);
                var audioRef = AudioRenderUtility.GetAudioReference(reloadedAudio);

                ZoundsWindow.ModifyZoundsProject("add rendered klip", () => {
                    var newKlip = new Klip(ZoundLibrary.GetUniqueZoundId());

                    newKlip.audioClipRef = audioRef;
                    newKlip.name = klipName;

                    newKlip.trimStart = 0f;
                    if (audioRef.editorAsset is AudioClip audioClip) {
                        newKlip.trimEnd = audioClip.length;
                    }
                    newKlip.volumeEnvelope = new Envelope(Zound.MinVolumeRange, Zound.MaxVolumeRange);
                    newKlip.pitchEnvelope = new Envelope(Zound.MinPitchRange, Zound.MaxPitchRange);

                    newKlip.tags = new List<int>();
                    newKlip.tags.AddRange(zequence.tags);

                    newKlip.manuallySetMixerGroupRef = new UnityEngine.AddressableAssets.AssetReference(zequence.manuallySetMixerGroupRef.AssetGUID);
                    newKlip.manuallySetMixerGroupRef.SubObjectName = zequence.manuallySetMixerGroupRef.SubObjectName;

                    //if (Application.isPlaying) {
                        if (ZoundEngine.IsInitialized()) {
                            ZoundDictionary.ValidateZoundRuntime(newKlip);
                        }
                    //}

                    var zoundLibrary = ZoundsProject.Instance.zoundLibrary;
                    zoundLibrary.klips.Add(newKlip);
                    zoundLibrary.klips = zoundLibrary.klips.OrderBy(it => it.name).ToList();
                    ZoundsWindowProperties.Instance.zoundTabProperties[0].dirty = true;

                    Debug.Log("Rendered to a Klip: " + klipName, reloadedAudio);
                    //ZoundsWindow.PingWindow();
                    //ZoundsWindowProperties.Instance.selectedZoundTab = 0;
                    //ZoundsWindow.RepaintWindow();
                    //KlipsTab.Instance.SelectZound(newKlip);
                    BrowserTab.Instance?.OpenZoundEditor(newKlip);
                }, true);
            });
        }

        public static void EnsureAllKlipsRendered(Zequence zeq) {
            foreach (var entry in zeq.zoundEntries) {
                if (zeq.TryGetEntryZound(entry, out var zound)) {
                    if (zound is Klip klip) {
                        if (klip.needsRender) {
                            if (KlipEditorWindow.TryGetEditor(klip, out var klipEditor)) {
                                klipEditor.Render();
                            }
                            else {
                                KlipEditorWindow.RenderToAudioClip(klip);
                            }
                        }
                    }
                    else if (zound is Zequence childZeq) {
                        EnsureAllKlipsRendered(childZeq);
                    }
                }
            }
        }

    }

}
