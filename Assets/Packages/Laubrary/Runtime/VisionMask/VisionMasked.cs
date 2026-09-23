using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.VisionMask
{
    /// Puts every sprite of a rig under per-pixel vision: each SpriteRenderer under this GameObject that
    /// uses a STOCK sprite material is switched to the shared VisionMask material, so it draws only the
    /// pixels of itself that lie inside a <see cref="VisionCone"/>. The renderers stay enabled and their
    /// colour is never touched — the component never decides anything about the object as a whole.
    ///
    /// Re-sweeps every LateUpdate, because animated rigs grow renderers after spawn (layer renderers,
    /// muzzle flashes). A renderer carrying a CUSTOM material (a recolour shader, say) is left alone and
    /// listed in <see cref="Unmasked"/> — silently replacing someone's shader is worse than an honest gap.
    /// Original materials come back when the component is disabled or destroyed.
    [DisallowMultipleComponent]
    [AddComponentMenu("Laubrary/VisionMask/Vision Masked")]
    public class VisionMasked : MonoBehaviour
    {
        [Tooltip("How visible a pixel outside every cone is: 0 = invisible, a small value leaves a faint ghost.")]
        [Range(0f, 1f)] public float hiddenAlpha = 0f;

        [Tooltip("Also replace materials that are not stock sprite materials. Off: such renderers are left " +
                 "unmasked and reported instead.")]
        public bool overrideCustomMaterials = false;

        readonly Dictionary<SpriteRenderer, Material> originals = new();
        readonly List<SpriteRenderer> unmasked = new();
        static readonly List<SpriteRenderer> scratch = new();

        /// Renderers under this rig that were NOT masked because they carry a custom material.
        public IReadOnlyList<SpriteRenderer> Unmasked => unmasked;
        /// How many renderers are currently drawn with the mask material.
        public int MaskedCount => originals.Count;

        void OnEnable() => Sweep();
        void LateUpdate() => Sweep();

        void OnDisable() => RestoreAll();
        void OnDestroy() => RestoreAll();

        /// Mask anything new under the rig now (also runs every LateUpdate).
        public void Sweep()
        {
            var mat = VisionMask.SpriteMaterial(hiddenAlpha);
            if (mat == null) return;
            GetComponentsInChildren(true, scratch);
            for (int i = 0; i < scratch.Count; i++)
            {
                var sr = scratch[i];
                var cur = sr.sharedMaterial;
                if (cur == mat) continue;
                if (originals.ContainsKey(sr))
                {
                    if (VisionMask.IsMaskMaterial(cur)) { sr.sharedMaterial = mat; continue; }   // hiddenAlpha changed
                    originals.Remove(sr);   // someone else swapped it since: theirs now, report it below
                }
                if (!overrideCustomMaterials && !IsStockSpriteMaterial(cur))
                {
                    if (!unmasked.Contains(sr)) unmasked.Add(sr);
                    continue;
                }
                unmasked.Remove(sr);
                originals[sr] = cur;
                sr.sharedMaterial = mat;
            }
            scratch.Clear();
        }

        void RestoreAll()
        {
            foreach (var kv in originals)
                if (kv.Key != null && VisionMask.IsMaskMaterial(kv.Key.sharedMaterial)) kv.Key.sharedMaterial = kv.Value;
            originals.Clear();
        }

        /// The stock sprite shaders a mask can stand in for without changing the look (where no 2D light
        /// lights the sprite).
        public static bool IsStockSpriteMaterial(Material m)
        {
            if (m == null) return true;
            switch (m.shader != null ? m.shader.name : "")
            {
                case "Sprites/Default":
                case "Universal Render Pipeline/2D/Sprite-Lit-Default":
                case "Universal Render Pipeline/2D/Sprite-Unlit-Default":
                    return true;
                default:
                    return false;
            }
        }
    }
}
