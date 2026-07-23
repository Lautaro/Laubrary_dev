using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Laubrary.Pyre;

namespace Laubrary.Pyre.Editor
{
    /// An editor-only asset that stores reusable <see cref="Layer"/> presets so a layer can be lifted out of one
    /// Pyre and dropped into another. One shared library per project — found by type, created on demand under
    /// <c>Assets/Pyre/</c> (host-project authoring space, never inside the package). Layers are stored as deep
    /// clones, so editing the source blast afterwards can't mutate the saved copy.
    public class PyreLayerLibrary : ScriptableObject
    {
        public List<Layer> layers = new();

        const string DefaultPath = "Assets/Pyre/PyreLayerLibrary.asset";
        static PyreLayerLibrary _cached;

        /// The shared library, loading the existing asset or (optionally) creating a fresh one.
        public static PyreLayerLibrary Load(bool createIfMissing = true)
        {
            if (_cached != null) return _cached;

            foreach (var guid in AssetDatabase.FindAssets("t:PyreLayerLibrary"))
            {
                _cached = AssetDatabase.LoadAssetAtPath<PyreLayerLibrary>(AssetDatabase.GUIDToAssetPath(guid));
                if (_cached != null) return _cached;
            }
            if (!createIfMissing) return null;

            if (!AssetDatabase.IsValidFolder("Assets/Pyre")) AssetDatabase.CreateFolder("Assets", "Pyre");
            _cached = CreateInstance<PyreLayerLibrary>();
            AssetDatabase.CreateAsset(_cached, DefaultPath);
            AssetDatabase.SaveAssets();
            return _cached;
        }

        /// Store a deep clone of a layer under the given name.
        public void Add(Layer layer, string entryName)
        {
            var copy = layer.Clone();
            copy.name = string.IsNullOrWhiteSpace(entryName) ? layer.name : entryName;
            layers.Add(copy);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        public void RemoveAt(int i)
        {
            if (i < 0 || i >= layers.Count) return;
            layers.RemoveAt(i);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }
    }

    /// The recall picker: a popup listing every saved layer with a live thumbnail, an Insert button (drops a clone
    /// into the caller's blast) and a Delete button. Thumbnails are rendered once through the real BlastRenderer so
    /// what you see is what the layer bakes to.
    public class PyreLayerLibraryPopup : PopupWindowContent
    {
        readonly PyreLayerLibrary lib;
        readonly System.Action<Layer> onPick;
        readonly float width;
        readonly Dictionary<int, Texture2D> thumbs = new();
        Vector2 scroll;

        public PyreLayerLibraryPopup(PyreLayerLibrary lib, float width, System.Action<Layer> onPick)
        {
            this.lib = lib;
            this.width = Mathf.Max(300f, width);
            this.onPick = onPick;
        }

        public override Vector2 GetWindowSize()
        {
            int n = lib != null ? lib.layers.Count : 0;
            float rows = Mathf.Clamp(n, 1, 7);
            return new Vector2(width, 40f + rows * 60f);
        }

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.LabelField("Saved layers", EditorStyles.boldLabel);
            if (lib == null || lib.layers.Count == 0)
            {
                EditorGUILayout.HelpBox("No saved layers yet. Use the ★ button on a layer row to add one.",
                                        MessageType.Info);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            int del = -1;
            for (int i = 0; i < lib.layers.Count; i++)
            {
                var layer = lib.layers[i];
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                GUILayout.Label(Thumb(i, layer), GUILayout.Width(48), GUILayout.Height(48));

                EditorGUILayout.BeginVertical();
                GUILayout.Label(string.IsNullOrEmpty(layer.name) ? "(unnamed)" : layer.name, EditorStyles.boldLabel);
                GUILayout.Label($"{layer.shape} · {(layer.modifiers != null ? layer.modifiers.Count : 0)} mod(s)",
                                EditorStyles.miniLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Insert", GUILayout.Width(64)))
                {
                    onPick?.Invoke(layer.Clone());
                    editorWindow.Close();
                }
                if (GUILayout.Button("Delete", GUILayout.Width(64))) del = i;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            if (del >= 0)
            {
                ClearThumbs();            // indices shift on removal — rebuild lazily
                lib.RemoveAt(del);
                editorWindow.Repaint();
            }
        }

        // Render (once, cached) a mid-life frame of the layer on its own tiny canvas so the picker shows the shape.
        Texture2D Thumb(int i, Layer layer)
        {
            if (thumbs.TryGetValue(i, out var cached) && cached != null) return cached;

            var tmp = ScriptableObject.CreateInstance<Pyre>();
            tmp.seed = 7;
            tmp.frameCount = 9;
            tmp.canvasSize = 48;
            tmp.canvasHeight = 48;
            tmp.background = new Color(0f, 0f, 0f, 0f);
            var clone = layer.Clone();
            clone.enabled = true;
            clone.startFrame = 0; clone.endFrame = 8;   // force it visible at the sampled frame
            tmp.layers = new List<Layer> { clone };

            var tex = BlastRenderer.RenderFrameTexture(tmp, 4);
            tex.filterMode = FilterMode.Point;
            Object.DestroyImmediate(tmp);
            thumbs[i] = tex;
            return tex;
        }

        void ClearThumbs()
        {
            foreach (var t in thumbs.Values) if (t != null) Object.DestroyImmediate(t);
            thumbs.Clear();
        }

        public override void OnClose() => ClearThumbs();
    }
}
