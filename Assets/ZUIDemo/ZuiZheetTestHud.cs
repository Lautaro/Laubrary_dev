using UnityEngine;

// Runtime proof that a Zheet's box styles render outside the editor. Assign the demo Zheet, press
// Play: every panel is painted by ZUISheet.DrawBox from the sheet's ZUIBoxDef — background
// (gradients, rounded corners, borders, 9-slice frames) AND the title/content text, styled by the
// box's own text defs — all drawn by the same style-definition code the editor uses.
public class ZuiZheetTestHud : MonoBehaviour
{
    public ZUIStyleSheetAsset zheet;
    int _clicks;

    void OnGUI()
    {
        if (zheet == null)
        {
            GUI.Label(new Rect(20, 20, 600, 24), "Assign a Zheet (ZUIDemo ▸ Create Demo Runtime Zheet), then Play.");
            return;
        }

        Card("Panel",  "Panel",  "solid, rounded — title & content use the box text styles", new Rect(60, 60, 300, 130));
        Card("Card",   "Card",   "gradient + border", new Rect(60, 220, 300, 130));
        Card("Accent", "Accent", "solid blue", new Rect(400, 60, 300, 130));
        Card("Danger", "Danger", "gradient + border", new Rect(400, 220, 300, 130));
        // 9-slice: one frame texture stretched wide — corners stay fixed, edges/center stretch.
        Card("Framed", "Framed", "9-slice from a texture, stretched wide", new Rect(60, 380, 640, 120));

        // Stretch vs tile: SAME patterned texture, same insets — top box stretches edges+centre, bottom
        // tiles them. Corners stay pixel-fixed in both. The pattern makes the difference obvious.
        ZUISheet.DrawBox(zheet, "PatStretch", new Rect(60, 520, 640, 80));
        ZUISheet.DrawBox(zheet, "PatTile",    new Rect(60, 620, 640, 80));
        Label(60, 500, "Edges/centre STRETCHED");
        Label(60, 600, "Edges/centre TILED");

        // Interactive 9-slice (sprite) BUTTON — hover brightens, press darkens (per-state frames).
        if (ZUISheet.Button(zheet, "SpriteButton", new Rect(760, 60, 220, 60), "Sprite Button")) _clicks++;
        var l = new GUIStyle(GUI.skin.label) { fontSize = 14 }; l.normal.textColor = Color.white;
        GUI.Label(new Rect(760, 128, 260, 24), "9-slice button clicks: " + _clicks, l);

        var title = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
        title.normal.textColor = Color.white;
        GUI.Label(new Rect(60, 20, 800, 30), "Runtime Zheet — box background + styled title/content text", title);
    }

    // Title + content are drawn by ZUISheet using THIS box's title/content text styles from the Zheet.
    void Card(string style, string title, string content, Rect r) => ZUISheet.DrawBox(zheet, style, r, title, content);

    static void Label(float x, float y, string text)
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
        s.normal.textColor = Color.white;
        GUI.Label(new Rect(x, y, 400, 20), text, s);
    }
}
