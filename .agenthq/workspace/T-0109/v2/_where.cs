using UnityEngine;
using UnityEditor;
public static class V2Where
{
    public static string Execute()
    {
        return "dataPath=" + Application.dataPath
             + " | compileFailed=" + EditorUtility.scriptCompilationFailed
             + " | compiling=" + EditorApplication.isCompiling
             + " | heightAudit=" + (System.Type.GetType("Laubrary.Shaper.ShaperHeightAudit, com.Lautaro-Arino.Laubrary.Shaper.Editor") != null);
    }
}
