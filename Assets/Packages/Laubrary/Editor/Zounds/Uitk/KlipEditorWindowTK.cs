using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The UI Toolkit twin of the Klip editor window (T-0456 port, T-0461 shell). It edits the very same Klip through
    /// the very same project paths as the IMGUI window, so the two can be open side by side on one sound and every edit
    /// shows in both. The layout reproduces the old window's measure for measure: the sheet's 10 px row spacing, the
    /// header row at single-line height, the content box, the Source field, the waveform, the action row, the
    /// time-stretch strip and the effect chain.
    ///
    /// Opened from the old window's temporary "UITK" button (owner's decision D3), which exists only while both exist.
    /// </summary>
    public class KlipEditorWindowTK : ZuiWindow {

        [SerializeField] int targetZoundID;
        [SerializeField] bool isLocalZound;

        const float Row = 10f;   // the Zounds sheet's verticalSpacing (ZUI.RowSpace)

        Klip klip;
        ZoundFieldsRowTK fields;
        IVisualElementScheduledItem syncTick;
        Button playButton;
        ZoundToken currentToken;

        public static KlipEditorWindowTK Open(Klip klip, bool isLocalZound) {
            var w = CreateInstance<KlipEditorWindowTK>();
            w.targetZoundID = klip.id;
            w.isLocalZound = isLocalZound;
            w.titleContent = new GUIContent(TitleFor(klip) + " (UITK)");
            w.minSize = new Vector2(479.2f, 400f);
            w.Show();
            return w;
        }

        static string TitleFor(Klip k) {
            string t = "Klip: " + k.name;
            if (k.parentId != 0 && ZoundDictionary.TryGetZoundById(k.parentId, out var parent)) t += " (" + parent.name + ")";
            return t;
        }

        /// <summary>The same search the old window does: top-level Klips, then each Zequence's local Klips.</summary>
        public static Klip FindKlip(int id) {
            var lib = ZoundsProject.Instance.zoundLibrary;
            var k = lib.klips.Find(x => x.id == id);
            if (k != null) return k;
            foreach (var z in lib.zequences) {
                k = z.localKlips.Find(x => x.id == id);
                if (k != null) return k;
                foreach (var lz in z.localZequences) {
                    k = lz.zequence.localKlips.Find(x => x.id == id);
                    if (k != null) return k;
                }
            }
            return null;
        }

        static VisualElement VSpace(float h) {
            var e = new VisualElement();
            e.style.height = h; e.style.flexShrink = 0;
            return e;
        }

        static VisualElement HRow(float height) {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row;
            r.style.flexShrink = 0;
            if (height > 0f) r.style.height = height;
            return r;
        }

        protected override void BuildUI(VisualElement root) {
            ZS.Attach(root);
            klip = ZoundsProject.isJSONLoaded ? FindKlip(targetZoundID) : null;
            if (klip == null) { root.Add(new Label(ZoundsProject.isJSONLoaded ? "Klip no longer exists in the project." : "Zounds Project is not loaded.")); return; }
            titleContent = new GUIContent(TitleFor(klip) + " (UITK)");

            // ── header row (ZoundInspector.DrawSimple) ──
            root.Add(VSpace(Row));
            fields = new ZoundFieldsRowTK(klip, isLocalZound, () => titleContent = new GUIContent(TitleFor(klip) + " (UITK)"));
            root.Add(fields);
            root.Add(VSpace(Row));

            // ── content box (ZUI.Box, "Default") ──
            var box = new VisualElement();
            box.AddToClassList("zs-box-default");
            box.style.flexGrow = 1;
            root.Add(box);
            box.Add(VSpace(Row));

            // Source (EditorGUILayout.ObjectField "Source:")
            var source = new UnityEditor.UIElements.ObjectField("Source:") { objectType = typeof(AudioClip), allowSceneObjects = false };
            source.AddToClassList("zs-sourcefield");
            source.SetValueWithoutNotify(Dsp.ZoundSapPlayback.LoadSourceClip(klip));
            box.Add(source);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            box.Add(scroll);
            scroll.Add(VSpace(Row));

            // Waveform block: toolbar + half a row + the waveform (ported in T-0468; a same-size placeholder until then).
            scroll.Add(BuildSpectrumToolbar());
            scroll.Add(VSpace(Row * 0.5f));
            var wave = new VisualElement();
            wave.AddToClassList("zs-waveform-placeholder");
            wave.style.height = 150f; wave.style.flexShrink = 0;
            scroll.Add(wave);

            // ── action row ──
            scroll.Add(VSpace(Row));
            scroll.Add(BuildActionRow());
            scroll.Add(VSpace(Row * 2f));

            // ── time-stretch strip, then the chain editor (T-0462 onward) ──
            scroll.Add(VSpace(Row));
            scroll.Add(new TimeStretchTK(klip));
            scroll.Add(VSpace(Row));
            scroll.Add(new ChainEditorTK(klip));

            syncTick = root.schedule.Execute(Sync).Every(200);
        }

        VisualElement BuildSpectrumToolbar() {
            float lh = EditorGUIUtility.singleLineHeight;
            var r = HRow(lh);
            var t = klip.trimEnabled;
            r.Add(ZS.Toggle("Trim", "", klip.trimEnabled, null, "RichToggle", ZUICornerMask.Left, 60f, lh));
            r.Add(ZS.Toggle("Clamp", "", false, null, "RichToggle", ZUICornerMask.Right, 60f, lh));
            r.Add(Gap(6f));
            r.Add(ZS.Toggle("Volume", "", false, null, "RichToggle", ZUICornerMask.Left, 75f, lh));
            r.Add(ZS.Toggle("", "", false, null, "RichToggle", ZUICornerMask.Right, 25f, lh));
            r.Add(Gap(6f));
            r.Add(ZS.Toggle("Pitch", "", false, null, "RichToggle", ZUICornerMask.Left, 65f, lh));
            r.Add(ZS.Toggle("", "", false, null, "RichToggle", ZUICornerMask.Right, 25f, lh));
            r.Add(Flex());
            var len = new Label("0.000s");
            len.AddToClassList("zs-minilabel");
            len.style.width = 50f;
            r.Add(len);
            return r;
        }

        VisualElement BuildActionRow() {
            const float h = 20f;
            var r = HRow(h);
            r.Add(ZS.Button("Render", "", "RichButton", () => { KlipEditorWindow.RenderToAudioClip(klip); }, ZUICornerMask.All, 60f, h));
            r.Add(Gap(4f));
            r.Add(ZS.Button("Remove", "", "RichButton", Remove, ZUICornerMask.All, 70f, h));
            if (klip.parentId == 0 && ZoundsProject.Instance.browserSettings.showConvertToZequence) {
                r.Add(Gap(4f));
                r.Add(ZS.Button("Convert to Zeq", "", "RichButton", ConvertToZeq, ZUICornerMask.All, 100f, h));
            }
            r.Add(Flex());
            r.Add(Gap(4f));
            r.Add(ZS.Button("Force GC", EditorTools.ZoundGcStressTest.Tooltip + "\n\n" + EditorTools.ZoundGcStressTest.lastResult, "RichButton",
                            () => EditorTools.ZoundGcStressTest.Run(IsPlaying()), ZUICornerMask.All, 72f, h));
            r.Add(Gap(8f));
            playButton = ZS.Button("Play", "", "RichButton", PlayOrStop, ZUICornerMask.All, 60f, h);
            r.Add(playButton);
            return r;
        }

        static VisualElement Gap(float w) { var e = new VisualElement(); e.style.width = w; e.style.flexShrink = 0; return e; }
        static VisualElement Flex() { var e = new VisualElement(); e.style.flexGrow = 1; return e; }

        bool IsPlaying() => currentToken != null && currentToken.state == ZoundToken.State.Playing;

        /// <summary>The old window's Play/Stop (SimulatePlay), step for step.</summary>
        void PlayOrStop() {
            if (IsPlaying()) { currentToken.Kill(); currentToken = null; Sync(); return; }
            if (!Application.isPlaying && klip.needsRender) KlipEditorWindow.RenderToAudioClip(klip);
            bool needsRenderTemp = klip.needsRender;
            klip.needsRender = false;
            currentToken = ZoundEngine.PlayZound(klip, new ZoundArgs() {
                startImmediately = true, delay = 0f,
                volumeOverride = Random.Range(klip.minVolume, klip.maxVolume),
                pitchOverride = Random.Range(klip.minPitch, klip.maxPitch),
                chanceOverride = 1f, useFixedAverageValues = false, bypassGlobalSolo = isLocalZound, ignoreCooldown = true
            });
            klip.needsRender = needsRenderTemp;
            Sync();
        }

        void Remove() {
            if (!AudioAssetUtility.DisplayZoundRemoveDialog(klip)) return;
            ZoundsWindow.ModifyZoundsProject("remove zound", () => AudioAssetUtility.RemoveZound(klip), true);
            Close();
        }

        void ConvertToZeq() {
            if (!EditorUtility.DisplayDialog("Convert to Zequence: " + klip.name,
                    "Convert this Klip into a Zequence containing it as a local klip?\n" + klip.name, "Convert", "Cancel")) return;
            BrowserTab.Instance?.ConvertKlipToZequence(klip);
            Close();
        }

        void Sync() {
            if (klip == null) return;
            if (FindKlip(targetZoundID) != klip) { Rebuild(); return; }
            fields?.Sync();
            if (playButton != null) {
                bool p = IsPlaying();
                playButton.text = p ? "Stop" : "Play";
            }
        }
    }
}
