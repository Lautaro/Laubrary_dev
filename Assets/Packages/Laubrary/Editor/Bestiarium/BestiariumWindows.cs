using UnityEditor;
using UnityEngine;
using Laubrary.AssetKit.Editor;

namespace Laubrary.Bestiarium.Editor
{
    /// <summary>
    /// The enemy-authoring hub: browse / create / duplicate / rename / delete Bestiarium Defs, and configure the
    /// selected one — all from the shared AssetKit base (empty-state shows the library). The per-asset body is drawn
    /// through a SerializedObject so Unity renders the pluggable <c>[SerializeReference]</c> pickers (the view type,
    /// the effect types) and edits get Undo for free.
    /// </summary>
    public abstract class BestiariumDefWindow<T> : LaubraryAssetWindow<T> where T : ScriptableObject
    {
        protected override string DefaultFolder => "Assets/Bestiarium";

        SerializedObject _so;

        // ZUI-GAP: a [SerializeReference] managed-reference picker. Unity's SerializedObject + PropertyField is the
        // only thing that renders the "which ICharacterView / ICombatFx" type dropdowns — and it makes every field
        // edit Undo-able automatically. So the recipe body is a SerializedObject inspector inside the ZUI chrome.
        protected override void DrawAsset(T asset)
        {
            if (_so == null || _so.targetObject != asset) _so = new SerializedObject(asset);
            _so.Update();
            var it = _so.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.propertyPath == "m_Script") continue;
                EditorGUILayout.PropertyField(it, true);
            }
            _so.ApplyModifiedProperties();
        }
    }

    public class CharacterDefWindow : BestiariumDefWindow<CharacterDef>
    {
        [MenuItem("Laubrary/Bestiarium/Characters")]
        public static void Open() => GetWindow<CharacterDefWindow>("Characters");
        protected override string TypeLabel => "Character";
        protected override string NewAssetName => "Character";
    }

    public class WeaponDefWindow : BestiariumDefWindow<WeaponDef>
    {
        [MenuItem("Laubrary/Bestiarium/Weapons")]
        public static void Open() => GetWindow<WeaponDefWindow>("Weapons");
        protected override string TypeLabel => "Weapon";
        protected override string NewAssetName => "Weapon";
    }

    public class ProjectileDefWindow : BestiariumDefWindow<ProjectileDef>
    {
        [MenuItem("Laubrary/Bestiarium/Projectiles")]
        public static void Open() => GetWindow<ProjectileDefWindow>("Projectiles");
        protected override string TypeLabel => "Projectile";
        protected override string NewAssetName => "Projectile";
    }
}
