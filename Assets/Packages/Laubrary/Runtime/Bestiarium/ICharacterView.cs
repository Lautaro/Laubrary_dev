using UnityEngine;

namespace Laubrary.Bestiarium
{
    /// <summary>
    /// A pluggable look for a character: builds the visual onto the spawned GameObject and reports its bounds (used
    /// to size the hurtbox). Concrete implementations are <c>[Serializable]</c> and carry their own data.
    /// <see cref="SpriteView"/> is the dependency-free default that ships in core; richer views (a Zoetrope Zoe, a
    /// Lazor vector shape) come from OPTIONAL bridge modules so Bestiarium core stays Combat2D-only. Assigned via
    /// <c>[SerializeReference]</c> on <see cref="CharacterDef"/>.
    /// </summary>
    public interface ICharacterView
    {
        /// Attach the view to <paramref name="host"/>; return the visual's world-space size (for hurtbox sizing).
        Vector2 Build(GameObject host);
    }

    /// <summary>The default look: one static sprite. No dependency beyond UnityEngine, so it lives in core.</summary>
    [System.Serializable]
    public class SpriteView : ICharacterView
    {
        public Sprite sprite;
        [Min(0.01f)] public float scale = 1f;

        public Vector2 Build(GameObject host)
        {
            var sr = host.AddComponent<SpriteRenderer>();
            if (sprite != null)
            {
                sr.sprite = sprite;
                host.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
                return sprite.bounds.size;
            }
            return Vector2.one;
        }
    }
}
