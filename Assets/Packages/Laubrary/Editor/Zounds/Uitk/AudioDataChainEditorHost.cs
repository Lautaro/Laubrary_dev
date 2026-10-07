using System;
using System.Linq;
using Laubrary.Audio;
using Laubrary.Zounds.Dsp;
using UnityEngine;

namespace Laubrary.Zounds.Uitk {
    /// <summary>A stable authoring copy over portable audio data. All write-back occurs inside the owner's undo transaction.</summary>
    public sealed class AudioDataChainEditorHost : IChainEditorHost {
        readonly Func<AudioEffectChainData> source;
        readonly Action<string, Action> edit;
        readonly Action<string> begin;
        readonly Action continuous, end;
        readonly Action<string[]> written;
        readonly ZoundEffectChain chain = new ZoundEffectChain();
        string[] nodeIds;
        public AudioDataChainEditorHost(Func<AudioEffectChainData> source, Action<string, Action> edit,
            Action<string> beginGesture, Action continuousChanged, Action endGesture,
            Action<string[]> afterWrite = null, float previewLength = 1.5f) {
            this.source = source; this.edit = edit; begin = beginGesture; continuous = continuousChanged;
            end = endGesture; written = afterWrite; PreviewLength = previewLength; Reload();
        }
        public ZoundEffectChain Chain => chain;
        public ChainEditorFeatures Features => ChainEditorFeatures.None;
        public float PreviewLength { get; }
        public ZoundsProject.ProjectSettings.EditorStyle EditorStyle { get; } = new ZoundsProject.ProjectSettings.EditorStyle();
        public void Reload() {
            var data = source();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(data ?? new AudioEffectChainData()), chain);
            chain.EnsureUids();
            nodeIds = chain.nodes.Select(n => n.uid).ToArray();
        }
        void WriteBack() {
            chain.EnsureUids();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(chain), source());
            written?.Invoke(nodeIds);
            nodeIds = chain.nodes.Select(n => n.uid).ToArray();
        }
        public void Edit(string label, Action action) => edit(label, () => { action(); WriteBack(); });
        public void BeginGesture(string label) => begin(label);
        public void EditContinuous(Action action) { action(); WriteBack(); continuous?.Invoke(); }
        public void EndGesture() => end();
    }
}
