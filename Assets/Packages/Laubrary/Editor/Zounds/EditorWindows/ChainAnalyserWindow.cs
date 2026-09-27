using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds.EditorTools;

namespace Laubrary.Zounds {

    /// <summary>
    /// A roomier copy of the analyser that sits under the effect list, for when the inline panel is too small to read.
    ///
    /// **It follows the editor rather than asking you to pick.** The first version of this window had a dropdown of sounds,
    /// and that was the reason it seemed not to work: it analysed whatever the dropdown selected, which was usually not the
    /// sound being edited, and the list left out sounds nested inside a sequence entirely. Now it shows whichever chain the
    /// effect editor last drew, which is the one in front of you. The dropdown remains only as a way to look at a different
    /// sound deliberately, and it defaults to following along.
    ///
    /// Everything it draws is the same panel the effect editor embeds, so the two cannot drift apart.
    /// </summary>
    public class ChainAnalyserWindow : EditorWindow {

        [MenuItem("Laubrary/Zounds/Chain analyser (bigger view)")]
        public static void Open() {
            var w = GetWindow<ChainAnalyserWindow>("Chain analyser");
            w.minSize = new Vector2(480f, 360f);
            w.Show();
        }

        readonly ChainAnalyserPanel panel = new ChainAnalyserPanel();
        readonly List<Zound> candidates = new List<Zound>();
        readonly List<string> candidateNames = new List<string>();
        bool followEditor = true;
        int selected;

        void OnEnable() {
            panel.open = true;
            EditorApplication.update += Tick;
        }

        void OnDisable() {
            EditorApplication.update -= Tick;
        }

        void Tick() => Repaint();

        void OnGUI() {
            RefreshCandidates();

            var target = ChainAnalyserPanel.lastAnalysed;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                followEditor = GUILayout.Toggle(followEditor, "Follow the editor", EditorStyles.toolbarButton,
                                                GUILayout.Width(120f));
                if (!followEditor && candidates.Count > 0) {
                    selected = Mathf.Clamp(selected, 0, candidates.Count - 1);
                    selected = EditorGUILayout.Popup(selected, candidateNames.ToArray(), EditorStyles.toolbarPopup,
                                                     GUILayout.Width(220f));
                    target = candidates[selected];
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label(target != null ? target.name : "nothing", EditorStyles.miniLabel);
            }

            if (target == null) {
                EditorGUILayout.HelpBox(
                    "Open a sound's effect editor and this follows it. The same views also sit directly under the effect "
                  + "list there, which is usually the more convenient place to read them.", MessageType.Info);
                return;
            }

            var chain = Dsp.ZoundDspPlayback.ResolveChain(target, out _);
            // Taller than the inline copy, since having the room is the only reason to open a separate window for this.
            panel.Draw(target, chain, Mathf.Max(160f, position.height - 230f));
        }

        /// <summary>
        /// Every sound with a chain, INCLUDING those nested inside a sequence. Leaving those out was part of why the old
        /// dropdown could not show the sound somebody was actually editing.
        /// </summary>
        void RefreshCandidates() {
            candidates.Clear();
            candidateNames.Clear();
            var project = ZoundsProject.Instance;
            if (project?.zoundLibrary == null) return;

            var library = project.zoundLibrary;
            for (int i = 0; i < library.klips.Count; i++) Consider(library.klips[i], null);
            for (int z = 0; z < library.zequences.Count; z++) {
                var zeq = library.zequences[z];
                if (zeq == null) continue;
                for (int i = 0; i < zeq.localKlips.Count; i++) Consider(zeq.localKlips[i], zeq.name);
                for (int n = 0; n < zeq.localZequences.Count; n++) {
                    var inner = zeq.localZequences[n]?.zequence;
                    if (inner == null) continue;
                    for (int i = 0; i < inner.localKlips.Count; i++) Consider(inner.localKlips[i], zeq.name + " / " + inner.name);
                }
            }
        }

        void Consider(Klip klip, string within) {
            if (klip == null) return;
            var chain = Dsp.ZoundDspPlayback.ResolveChain(klip, out _);
            if (chain == null || chain.IsEmpty) return;
            candidates.Add(klip);
            candidateNames.Add((within == null ? klip.name : within + " / " + klip.name)
                               + "   (" + chain.nodes.Count + " effect" + (chain.nodes.Count == 1 ? "" : "s") + ")");
        }
    }
}
