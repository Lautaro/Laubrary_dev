using UnityEngine;

// Runtime proof that a Zheet's box styles render outside the editor. Assign the demo Zheet, press
// Play: every panel below is painted by ZUISheet.DrawBox from the sheet's ZUIBoxDef — gradients,
// rounded corners and borders, all drawn by the SAME style-definition code the editor uses, now
// living in the runtime assembly.
public class ZuiZheetTestHud : MonoBehaviour
{
    public ZUIStyleSheetAsset zheet;

    void OnGUI()
    {
        if (zheet == null)
        {
            GUI.Label(new Rect(20, 20, 600, 24), "Assign a Zheet (ZUIDemo ▸ Create Demo Runtime Zheet), then Play.");
            return;
        }

        var label = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
        label.normal.textColor = Color.white;

        DrawCard("Panel", "Panel — solid, rounded", new Rect(60, 60, 300, 120), label);
        DrawCard("Card", "Card — gradient + border", new Rect(60, 210, 300, 120), label);
        DrawCard("Accent", "Accent — solid blue", new Rect(400, 60, 300, 120), label);
        DrawCard("Danger", "Danger — gradient + border", new Rect(400, 210, 300, 120), label);
        // 9-slice: one frame texture stretched wide — corners stay fixed, edges/center stretch.
        DrawCard("Framed", "Framed — 9-slice from a texture (stretched wide)", new Rect(60, 360, 640, 130), label);

        var title = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
        title.normal.textColor = Color.white;
        GUI.Label(new Rect(60, 20, 700, 30), "Runtime Zheet — box styles drawn from a ZUIStyleSheetAsset", title);
    }

    void DrawCard(string style, string caption, Rect r, GUIStyle label)
    {
        ZUISheet.DrawBox(zheet, style, r);
        GUI.Label(r, caption, label);
    }
}
