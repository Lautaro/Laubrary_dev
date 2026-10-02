using System;
using System.Reflection;
using Laubrary.Lathe;
using Laubrary.Lathe.Editor;

public static class T0124_CatalogCheckPrimary
{
    public static string Execute()
    {
        var catalogMethod = typeof(LatheWindow).GetMethod("ModifierCatalog", BindingFlags.NonPublic | BindingFlags.Static);
        var catalog = (System.Collections.IEnumerable)catalogMethod.Invoke(null, null);
        foreach (var entry in catalog)
        {
            var t = (Type)entry.GetType().GetField("type").GetValue(entry);
            if (t == typeof(TextureReliefMeshModifier))
                return "FOUND in primary project's LatheWindow modifier catalog, group=" +
                       entry.GetType().GetField("group").GetValue(entry);
        }
        return "NOT FOUND";
    }
}
