using UnityEditor;
using UnityEngine;
using Laubrary.AssetKit.Editor;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// The enemy-authoring hub: browse / create / duplicate / rename / delete Zoetrope Defs, and configure the
    /// selected one — all from the shared AssetKit base (empty-state shows the library). The per-asset body is drawn
    /// through a SerializedObject so Unity renders the pluggable <c>[SerializeReference]</c> pickers (the view type,
    /// the effect types) and edits get Undo for free.
    /// </summary>
    public abstract class ZoetropeDefWindow<T> : LaubraryAssetWindow<T> where T : ScriptableObject
    {
        protected override string DefaultFolder => "Assets/Zoetrope";

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

    public class ZoeWindow : ZoetropeDefWindow<Zoe>
    {
        [MenuItem("Laubrary/Zoetrope/Zoes")]
        public static void Open() => GetWindow<ZoeWindow>("Zoes");
        protected override string TypeLabel => "Zoe";
        protected override string NewAssetName => "Zoe";
    }

    public class WeaponDefWindow : ZoetropeDefWindow<WeaponDef>
    {
        [MenuItem("Laubrary/Zoetrope/Weapons")]
        public static void Open() => GetWindow<WeaponDefWindow>("Weapons");
        protected override string TypeLabel => "Weapon";
        protected override string NewAssetName => "Weapon";
    }

    public class AmmoDefWindow : ZoetropeDefWindow<AmmoDef>
    {
        [MenuItem("Laubrary/Zoetrope/Ammo")]
        public static void Open() => GetWindow<AmmoDefWindow>("Ammo");
        protected override string TypeLabel => "Ammo";
        protected override string NewAssetName => "Ammo";
    }
}
