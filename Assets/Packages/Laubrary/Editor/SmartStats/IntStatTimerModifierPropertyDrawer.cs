using Lautaro.Stats;
using UnityEditor;

[CustomPropertyDrawer(typeof(IntStatTimerModifier), true)]
public class IntStatTimerModifierPropertyDrawer : TimerModifierPropertyDrawerBase
{
    protected override string FormatValue(SerializedProperty property)
    {
        var modValueProp = property.FindPropertyRelative("modValue");
        int value = modValueProp?.intValue ?? 0;
        string sign = value >= 0 ? "+" : "";
        return $"{sign}{value}";
    }
}
