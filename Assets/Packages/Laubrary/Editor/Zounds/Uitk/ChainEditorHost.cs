using System;
using Laubrary.Zounds.Dsp;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds.Uitk {
    [Flags]
    public enum ChainEditorFeatures {
        None = 0, Library = 1, Snapshots = 2, Overrides = 4, Zpoc = 8,
        CodeModifier = 16, SourceStage = 32, LiveReadouts = 64, Analyser = 128, ZpocTest = 256,
        All = Library | Snapshots | Overrides | Zpoc | CodeModifier | SourceStage | LiveReadouts | Analyser | ZpocTest
    }

    /// <summary>Owns persistence and preview; the editor owns only the controls and view state.</summary>
    public interface IChainEditorHost {
        ZoundEffectChain Chain { get; }
        ChainEditorFeatures Features { get; }
        float PreviewLength { get; }
        ZoundsProject.ProjectSettings.EditorStyle EditorStyle { get; }
        void Edit(string label, Action action);
        void BeginGesture(string label);
        void EditContinuous(Action action);
        void EndGesture();
        void Reload();
    }

    public interface IChainEditorZoundHost {
        Zound Zound { get; }
        EditorWindow PreviewOwner { get; }
    }

    public interface IChainEditorPresetHost {
        ZoundChainPreset Preset { get; }
        float EffectiveValue(ZoundEffectChain chain, int node, int parameter, float authored);
    }

    public interface IChainEditorLiveHost {
        void PushParameter(int node, int parameter, float value);
    }

    public sealed class ZoundChainEditorHost : IChainEditorHost, IChainEditorZoundHost, IChainEditorPresetHost, IChainEditorLiveHost {
        public Zound Zound { get; }
        public EditorWindow PreviewOwner { get; }
        public ZoundChainEditorHost(Zound zound, EditorWindow previewOwner = null) { Zound = zound; PreviewOwner = previewOwner; }
        public ZoundEffectChain Chain => ZoundDspPlayback.ResolveChain(Zound, out _);
        public ZoundChainPreset Preset { get { ZoundDspPlayback.ResolveChain(Zound, out var preset); return preset; } }
        public ChainEditorFeatures Features => ChainEditorFeatures.All;
        public float PreviewLength => ZoundSapPlayback.TryGetPlayLength(Zound, out float length) ? length : 1.5f;
        public ZoundsProject.ProjectSettings.EditorStyle EditorStyle => ZoundsProject.Instance.projectSettings.editorStyle;
        public float EffectiveValue(ZoundEffectChain chain, int node, int parameter, float authored) => ChainEditorGUI.EffectiveValue(Zound, chain, node, parameter, authored);
        public void Edit(string label, Action action) => ZoundsWindow.ModifyAndSaveZoundsProject(label, () => { action(); ZoundDspPlayback.InvalidateLayout(Zound); });
        public void BeginGesture(string label) => ZoundsWindow.BeginDragUndo(label);
        public void EditContinuous(Action action) { action(); ZoundDspPlayback.InvalidateLayout(Zound); EditorUtility.SetDirty(ZoundsProject.Instance); }
        public void EndGesture() => ZoundsWindow.EndDragUndo();
        public void PushParameter(int node, int parameter, float value) => ZoundDspPlayback.PushLiveParam(Zound, Chain, node, parameter, value);
        public void Reload() { }
    }
}
