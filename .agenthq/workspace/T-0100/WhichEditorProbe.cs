using UnityEngine;

// T-0100 probe: report which working copy the Coplay bridge is actually driving.
// Lives outside Assets/ on purpose so neither Unity editor imports or compiles it.
public static class WhichEditorProbe
{
    public static string Execute()
    {
        string msg = "T-0100 PROBE | dataPath=" + Application.dataPath
                   + " | productName=" + Application.productName
                   + " | processId=" + System.Diagnostics.Process.GetCurrentProcess().Id;
        Debug.Log(msg);
        return msg;
    }
}
