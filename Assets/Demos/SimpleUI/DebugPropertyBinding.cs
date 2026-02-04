using UnityEngine;
using System.Reflection;
using Laubrary.SimpleUI.Demo;

public class DebugPropertyBinding : MonoBehaviour
{
    void Start()
    {
        var type = typeof(CharacterSheet);
        
        Debug.Log("=== CharacterSheet Members ===");
        
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        Debug.Log($"Fields ({fields.Length}):");
        foreach (var field in fields)
        {
            Debug.Log($"  - {field.Name} ({field.FieldType.Name})");
        }
        
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Debug.Log($"\nProperties ({properties.Length}):");
        foreach (var property in properties)
        {
            Debug.Log($"  - {property.Name} ({property.PropertyType.Name}) CanRead={property.CanRead} CanWrite={property.CanWrite}");
            
            var attrs = property.GetCustomAttributes(typeof(Laubrary.SimpleUI.SimpleUIPathAttribute), false);
            if (attrs.Length > 0)
            {
                foreach (Laubrary.SimpleUI.SimpleUIPathAttribute attr in attrs)
                {
                    Debug.Log($"    [SimpleUIPath(\"{attr.Path}\")]");
                }
            }
        }
    }
}
