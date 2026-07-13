// ZUIPlayground.cs
// Empty editor window for learning ZUI layout and controls.
// Open via Tools > Zounds > ZUI Playground

using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds
{
    public class ZUIPlayground : ZUIWindow
    {
        protected override string ConsumerSheetName => "Zounds";

        [MenuItem("Laubrary/ZUI/Playground")]
        static void Open() => GetWindow<ZUIPlayground>("ZUI Playground");

        protected override void OnZUI()
        {
            float rowH = ZUI.ControlHeight;
            float gap = 2f;
            float totalH = rowH * 2f + gap;
            float btnW = 24f;
            float sp = 2f;

            var itemRect = GUILayoutUtility.GetRect(1f, totalH, GUILayout.ExpandWidth(true));

            var row1 = new Rect(itemRect.x, itemRect.y, itemRect.width, rowH);
            var row2 = new Rect(itemRect.x, itemRect.y + rowH + gap, itemRect.width, rowH);

            // Row 1: [Edit][M][S] [══ Name ══] [×]
            float x = row1.x;

            var editIcon = new GUIContent(ZUI.FindIcon("open-editor"), "Open editor");
            var removeIcon = new GUIContent(ZUI.FindIcon("remove"), "Remove");
            var muteLabel = new GUIContent("M", "Mute");
            var soloLabel = new GUIContent("S", "Solo");

            var editRect = new Rect(x, row1.y, btnW, rowH); x += btnW + sp;
            var muteRect = new Rect(x, row1.y, btnW, rowH); x += btnW + sp;
            var soloRect = new Rect(x, row1.y, btnW, rowH); x += btnW + sp;

            float removeW = btnW;
            var removeRect = new Rect(row1.xMax - removeW, row1.y, removeW, rowH);

            float nameX = x + sp;
            float nameW = removeRect.x - nameX - sp;
            var nameRect = new Rect(nameX, row1.y, nameW, rowH);

            this.Button(editRect, editIcon, "ZoundBtnFlat");
            this.Toggle(muteRect, false, muteLabel, "ZoundBtnFlatToggle");
            this.Toggle(soloRect, false, soloLabel, "ZoundBtnFlatToggle");
            this.Button(nameRect, "Knight Attack", "ZoundBtn");
            this.Button(removeRect, removeIcon, "ZoundBtnFlat");

            // Row 2: placeholder
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(row2, new Color(1f, 1f, 1f, 0.03f));
        }
    }
}
