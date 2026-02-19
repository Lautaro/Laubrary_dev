using Lautaro.Stats;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

[CustomPropertyDrawer(typeof(FloatStatConditionalModifier), true)]
public class FloatStatConditionalModifierPropertyDrawer : PropertyDrawer
{
    public override VisualElement CreatePropertyGUI(SerializedProperty property)
    {
        var isPaused = property.FindPropertyRelative("isPaused");
        var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Stats/Editor/UI/FloatStatConditionalModifier.uxml");
        var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Stats/Editor/UI/USS_FloatStatModifier.uss");

        var root = visualTree.CloneTree();
        root.styleSheets.Add(styleSheet);

        var modifierLabel = root.Q<Label>("modifierLabel");
        var description = root.Q<Label>("descriptionLabel");
  
        var descriptionProperty = property.FindPropertyRelative("description").stringValue;
        description.text = descriptionProperty + " : ";

        var modValueProperty = property.FindPropertyRelative("modValue");
        modifierLabel.BindProperty(modValueProperty);

        root.TrackPropertyValue(isPaused, (prop) =>
        {
            var conditionMetValue = isPaused.boolValue;
            UpdateCondition(conditionMetValue);
        });

        void UpdateCondition(bool isPaused)
        {
            if (isPaused)
                root.AddToClassList("inactive");
            else
                root.RemoveFromClassList("inactive");
        }
        UpdateCondition(isPaused.boolValue);
        return root;
    }


}
