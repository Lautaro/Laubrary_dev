using Laubrary.AssetKit.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zoetrope.Editor
{
    /// <summary>
    /// The enemy-authoring hub: browse / create / duplicate / rename / delete Zoetrope Defs, and configure the
    /// selected one — all from the shared AssetKit base (empty-state shows the library). The per-asset body is
    /// bound to a SerializedObject so Unity renders the pluggable <c>[SerializeReference]</c> pickers (the view
    /// type, the effect types) and edits get Undo for free.
    ///
    /// UI TOOLKIT PORT: the IMGUI version carried a `// ZUI-GAP:` note saying a managed-reference picker could
    /// only be drawn by `SerializedObject` + `EditorGUILayout.PropertyField`. UI Toolkit closes that gap without
    /// a workaround — a bound <see cref="PropertyField"/> renders the same type dropdowns, keeps automatic Undo,
    /// AND (unlike the IMGUI loop) tracks external changes to the asset by itself once bound.
    /// </summary>
    public abstract class ZoetropeDefWindow<T> : ZuiAssetWindow<T> where T : ScriptableObject
    {
        protected override string DefaultFolder => "Assets/Zoetrope";

        protected override void BuildAsset(VisualElement root, T asset)
        {
            var so = new SerializedObject(asset);

            var body = new ScrollView(ScrollViewMode.Vertical);
            body.style.flexGrow = 1f;
            body.style.minHeight = 0f;

            // Every visible serialized property except the script reference. PropertyField handles
            // [SerializeReference] pickers, nested classes, lists and Undo on its own.
            var it = so.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.propertyPath == "m_Script") continue;
                var field = new PropertyField(it.Copy());
                field.Bind(so);
                body.Add(field);
            }

            // A managed-reference type change swaps the whole sub-tree of fields, which the bound
            // PropertyFields above can't express on their own — rebuild the body when that happens.
            body.TrackSerializedObjectValue(so, _ => { });
            root.Add(body);
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
