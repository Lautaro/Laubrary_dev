using System.Collections.Generic;
using System.IO;
using System.Linq;
using Laubrary.Launimator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Laubrary.Launimator.Editor
{
    /// <summary>
    /// The deterministic, UI-free core that turns a <see cref="LauminaryVersion"/>'s animations into
    /// game-ready Unity assets: one looping AnimationClip per animation, an AnimatorController with a
    /// state per clip, and a SpriteRenderer+Animator prefab — all written into the version's own folder.
    /// Both draft saves and version commits regenerate through here, so a committed snapshot's intra-folder
    /// references (controller→clips, prefab→controller) are correct by construction. <see cref="LauminaryRepo"/>
    /// owns the folder/version layout and calls this; the self-test exercises the same path.
    /// </summary>
    public static class LauminaryBuilder
    {
        public const string RootFolder = "Assets/Launimator/Lauminaries";

        /// <summary>
        /// (Re)generate all game-ready assets for <paramref name="version"/> into <paramref name="targetFolder"/>:
        /// per animation, a self-contained uniform-size <c>Atlases/&lt;anim&gt;.png</c> baked from its recipe
        /// (see <see cref="AtlasBaker"/>) and a looping <c>Clips/&lt;anim&gt;.anim</c> over the baked frames;
        /// then a <c>&lt;lauminary&gt;.controller</c> (one state per animation, first = default) and a
        /// <c>&lt;lauminary&gt;.prefab</c>. Assigns the generated refs and marks the version dirty. The caller
        /// is responsible for Save/Refresh batching.
        /// </summary>
        public static void BuildVersionAssets(LauminaryVersion version, string targetFolder, string lauminaryName, float ppu)
        {
            if (version == null) throw new System.ArgumentNullException(nameof(version));
            string safeChar = Sanitize(string.IsNullOrWhiteSpace(lauminaryName) ? "Lauminary" : lauminaryName);

            EnsureFolder(targetFolder);
            string clipsFolder = $"{targetFolder}/Clips";
            string atlasFolder = $"{targetFolder}/Atlases";
            EnsureFolder(clipsFolder);
            EnsureFolder(atlasFolder);

            float bakePpu = ppu <= 0 ? 16f : ppu;

            // Phase 1: bake each animation's self-contained uniform atlas (registration + trimming) and
            // resolve its baked frames. These per-animation atlases are temporary scaffolding — the packer
            // folds their frames into one shared lauminary atlas below.
            var baked = new List<Laumination>();
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
                    Debug.LogWarning($"Launimator: could not bake atlas for '{def.name}': {err}");
                    continue;
                }
                perAnimAtlasPaths.Add(atlasPath);
                def.frames = frames;
                def.atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
                baked.Add(def);
            }

            // Phase 2: pack all baked frames into ONE shared, readable, deduped lauminary atlas and repoint
            // every animation's frames at it. On failure, keep the per-animation atlases (graceful fallback).
            var keepAtlasPaths = new HashSet<string>(perAnimAtlasPaths);
            string packedPath = $"{targetFolder}/{safeChar}.png";
            var packed = LauminaryAtlasPacker.Pack(baked, packedPath, bakePpu, out string packErr);
            if (packed != null)
            {
                keepAtlasPaths.Clear(); // per-animation atlases are now redundant

                // Corruption guard. This bake path has a known, unfixed reimport race (a rebuild can silently
                // drop and MERGE frames — observed twice on the same asset: 56 sprites collapsing to 43 while
                // the atlas tripled in size, showing up in-game as one sprite containing two characters). It
                // is silent: nothing throws, the save reports success, and the damage is only noticed later
                // when the character renders wrong. Until the race itself is fixed, at least refuse to let it
                // pass unremarked — a loud error naming the file is the difference between reverting one edit
                // and losing an afternoon's painting.
                int expectedFrames = 0;
                foreach (var def in baked) expectedFrames += def.frames != null ? def.frames.Count : 0;
                int actualSprites = 0;
                foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(packedPath))
                    if (sub is Sprite) actualSprites++;
                if (actualSprites != expectedFrames)
                    Debug.LogError($"Launimator: atlas rebuild for '{safeChar}' produced {actualSprites} sprites " +
                                   $"but {expectedFrames} frames were baked — '{packedPath}' is CORRUPT. This is " +
                                   $"the known rebuild race, not something you did. Undo/revert this lauminary " +
                                   $"before editing further; saving again on top will not repair it.");
            }
            else if (baked.Count > 0)
            {
                Debug.LogWarning($"Launimator: atlas packing failed ({packErr}); using per-animation atlases.");
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

        private static AnimatorController BuildController(LauminaryVersion version, string targetFolder, string safeChar)
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

        private static GameObject BuildPrefab(LauminaryVersion version, string targetFolder, string safeChar, AnimatorController controller)
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
