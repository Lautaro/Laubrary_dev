using System.Collections.Generic;
using System.IO;
using System.Linq;
using Laubrary.Zoetrope;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// The deterministic, UI-free core that turns a <see cref="ZoeVersion"/>'s animations into
    /// game-ready Unity assets: one looping AnimationClip per animation, an AnimatorController with a
    /// state per clip, and a SpriteRenderer+Animator prefab — all written into the version's own folder.
    /// Both draft saves and version commits regenerate through here, so a committed snapshot's intra-folder
    /// references (controller→clips, prefab→controller) are correct by construction. <see cref="ZoeRepo"/>
    /// owns the folder/version layout and calls this; the self-test exercises the same path.
    /// </summary>
    public static class ZoeBuilder
    {
        public const string RootFolder = "Assets/Zoetrope/Zoes";

        /// <summary>
        /// (Re)generate all game-ready assets for <paramref name="version"/> into <paramref name="targetFolder"/>:
        /// per animation, a self-contained uniform-size <c>Atlases/&lt;anim&gt;.png</c> baked from its recipe
        /// (see <see cref="AtlasBaker"/>) and a looping <c>Clips/&lt;anim&gt;.anim</c> over the baked frames;
        /// then a <c>&lt;zoe&gt;.controller</c> (one state per animation, first = default) and a
        /// <c>&lt;zoe&gt;.prefab</c>. Assigns the generated refs and marks the version dirty. The caller
        /// is responsible for Save/Refresh batching.
        /// </summary>
        public static void BuildVersionAssets(ZoeVersion version, string targetFolder, string zoeName, float ppu)
        {
            if (version == null) throw new System.ArgumentNullException(nameof(version));
            string safeChar = Sanitize(string.IsNullOrWhiteSpace(zoeName) ? "Zoe" : zoeName);

            EnsureFolder(targetFolder);
            string clipsFolder = $"{targetFolder}/Clips";
            string atlasFolder = $"{targetFolder}/Atlases";
            EnsureFolder(clipsFolder);
            EnsureFolder(atlasFolder);

            float bakePpu = ppu <= 0 ? 16f : ppu;

            // Phase 1: bake each animation's self-contained uniform atlas (registration + trimming) and
            // resolve its baked frames. These per-animation atlases are temporary scaffolding — the packer
            // folds their frames into one shared zoe atlas below.
            var baked = new List<AnimationDef>();
            var perAnimAtlasPaths = new HashSet<string>();
            foreach (var def in version.animations)
            {
                if (def == null || def.recipe == null || def.recipe.Count == 0) continue;
                string safeAnim = Sanitize(def.name);

                string atlasPath = $"{atlasFolder}/{safeAnim}.png";
                var key = new RegionSlicer.ColorKey
                {
                    enabled = def.bgKeyEnabled, color = def.bgKey, tolerance = def.bgKeyTolerance
                };
                var box = new AtlasBaker.FrameBox
                {
                    fixedSize = def.fixedFrame, w = def.frameWidth, h = def.frameHeight, pivot = def.framePivot
                };
                var frames = AtlasBaker.Bake(def.recipe, atlasPath, safeAnim, bakePpu, out string err, key, box);
                if (frames == null)
                {
                    Debug.LogWarning($"Zoetrope: could not bake atlas for '{def.name}': {err}");
                    continue;
                }
                perAnimAtlasPaths.Add(atlasPath);
                def.frames = frames;
                def.atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
                baked.Add(def);
            }

            // Phase 2: pack all baked frames into ONE shared, readable, deduped zoe atlas and repoint
            // every animation's frames at it. On failure, keep the per-animation atlases (graceful fallback).
            var keepAtlasPaths = new HashSet<string>(perAnimAtlasPaths);
            string packedPath = $"{targetFolder}/{safeChar}.png";
            var packed = ZoeAtlasPacker.Pack(baked, packedPath, bakePpu, out string packErr);
            if (packed != null)
            {
                keepAtlasPaths.Clear(); // per-animation atlases are now redundant
            }
            else if (baked.Count > 0)
            {
                Debug.LogWarning($"Zoetrope: atlas packing failed ({packErr}); using per-animation atlases.");
            }

            // Phase 3: build one looping clip per animation over its (now packed) frames.
            var keepClipPaths = new HashSet<string>();
            foreach (var def in baked)
            {
                string safeAnim = Sanitize(def.name);
                string clipPath = $"{clipsFolder}/{safeAnim}.anim";
                keepClipPaths.Add(clipPath);
                AnimationClip built = BuildSpriteClip(def.frames, def.fps);
                built.name = safeAnim;

                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(built, existing); // overwrite in place so refs survive
                    def.clip = existing;
                }
                else
                {
                    AssetDatabase.CreateAsset(built, clipPath);
                    def.clip = built;
                }
            }

            // Sweep stale clips + atlases (renamed/removed animations, and the now-redundant per-anim atlases).
            foreach (var p in AssetDatabase.FindAssets("t:AnimationClip", new[] { clipsFolder })
                         .Select(AssetDatabase.GUIDToAssetPath).ToList())
                if (!keepClipPaths.Contains(p)) AssetDatabase.DeleteAsset(p);
            foreach (var p in AssetDatabase.FindAssets("t:Texture2D", new[] { atlasFolder })
                         .Select(AssetDatabase.GUIDToAssetPath).ToList())
                if (!keepAtlasPaths.Contains(p)) AssetDatabase.DeleteAsset(p);

            var controller = BuildController(version, targetFolder, safeChar);
            var prefab = BuildPrefab(version, targetFolder, safeChar, controller);

            version.controller = controller;
            version.prefab = prefab;
            EditorUtility.SetDirty(version);
        }

        /// <summary>
        /// Build an in-memory looping AnimationClip that swaps SpriteRenderer.sprite each frame at the given
        /// fps. The canonical sprite-frame-animation build.
        /// </summary>
        public static AnimationClip BuildSpriteClip(IList<Sprite> frames, float frameRate)
        {
            if (frameRate <= 0f) frameRate = 12f;

            var clip = new AnimationClip { frameRate = frameRate };

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var binding = new EditorCurveBinding
            {
                type = typeof(SpriteRenderer),
                path = "",
                propertyName = "m_Sprite"
            };

            var keys = new ObjectReferenceKeyframe[frames.Count];
            for (int i = 0; i < frames.Count; i++)
                keys[i] = new ObjectReferenceKeyframe { time = i / frameRate, value = frames[i] };

            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
            return clip;
        }

        private static AnimatorController BuildController(ZoeVersion version, string targetFolder, string safeChar)
        {
            string controllerPath = $"{targetFolder}/{safeChar}.controller";

            // Recreate from scratch so the state set matches the current animation set.
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null)
                AssetDatabase.DeleteAsset(controllerPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;

            bool first = true;
            foreach (var def in version.animations)
            {
                if (def == null || def.clip == null) continue;
                var state = sm.AddState(Sanitize(def.name));
                state.motion = def.clip;
                if (first) { sm.defaultState = state; first = false; }
            }

            return controller;
        }

        private static GameObject BuildPrefab(ZoeVersion version, string targetFolder, string safeChar, AnimatorController controller)
        {
            string prefabPath = $"{targetFolder}/{safeChar}.prefab";

            var go = new GameObject(safeChar);
            try
            {
                var sr = go.AddComponent<SpriteRenderer>();
                var firstAnim = version.animations.FirstOrDefault(a => a != null && a.frames != null && a.frames.Count > 0);
                if (firstAnim != null) sr.sprite = firstAnim.frames[0];

                var animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;

                return PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        public static string Sanitize(string raw)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string((raw ?? "").Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            return string.IsNullOrEmpty(cleaned) ? "Unnamed" : cleaned;
        }

        /// <summary>Create a folder (and parents) under Assets if it does not yet exist.</summary>
        public static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;

            string parent = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string leaf = Path.GetFileName(assetPath);
            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
