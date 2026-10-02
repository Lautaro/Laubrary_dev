// T-0314/T-0315 — is every section button of T313.win's toggle bar actually OPERABLE, not merely present?
// Two questions, because "drawn inside the window" is not the same as "a click lands on it":
//   1. panel.Pick at the button's own centre must return that button (or a descendant of it) — this is the
//      editor's real hit test, so it accounts for clipping and for anything drawn over the top;
//   2. a real PointerDown + PointerUp on the LAST button must flip its section's IsOpen — Clickable only
//      tracks the left button, so this is the same path a user's click takes.
// The window is left exactly as it was found: the section is toggled back with a second click.
string wname = UnityEditor.EditorPrefs.GetString("T313.win", "ChunkWindow");
var win = ZWin(wname);
if (win == null) return "NO WINDOW " + wname;
var root = win.rootVisualElement;
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath)
  .Append(" ").Append(wname).Append(" w=").Append(win.position.width.ToString("F0"))
  .Append(" root=").Append(root.worldBound.xMin.ToString("F1")).Append("..").Append(root.worldBound.xMax.ToString("F1")).Append("\n");

UnityEngine.UIElements.VisualElement bar = null;
foreach (var e in ZAll(root)) if (e.GetType().Name == "ZuiSectionToggleBar" && ZDrawn(e)) { bar = e; break; }
if (bar == null) return sb.Append("NO BAR DRAWN\n").ToString();

// the per-section ZuiSegmented is the LAST ZuiSegmented in the bar (the first is the mode switch)
UnityEngine.UIElements.VisualElement seg = null;
foreach (var e in ZAll(bar)) if (e.GetType().Name == "ZuiSegmented") seg = e;
if (seg == null) return sb.Append("NO SECTION SEGMENTED\n").ToString();
sb.Append("bar rows: seg w=").Append(seg.worldBound.width.ToString("F1"))
  .Append(" x=").Append(seg.worldBound.xMin.ToString("F1")).Append("..").Append(seg.worldBound.xMax.ToString("F1"))
  .Append(" height=").Append(seg.worldBound.height.ToString("F1")).Append("\n");

int n = seg.hierarchy.childCount, picked = 0, offWin = 0;
UnityEngine.UIElements.Button lastBtn = null;
float firstY = n > 0 ? seg.hierarchy[0].worldBound.yMin : 0f;
for (int i = 0; i < n; i++)
{
    var b = seg.hierarchy[i] as UnityEngine.UIElements.Button;
    if (b == null) continue;
    lastBtn = b;
    var wb = b.worldBound;
    var pt = new UnityEngine.Vector2(wb.center.x, wb.center.y);
    var hit = root.panel.Pick(pt);
    bool ok = hit != null && (hit == b || IsUnder(hit, b));
    if (ok) picked++;
    bool off = wb.xMax > root.worldBound.xMax + ZTOL || wb.yMax > root.worldBound.yMax + ZTOL;
    if (off) offWin++;
    sb.Append("  '").Append(ZCaption(b)).Append("' x=").Append(wb.xMin.ToString("F1")).Append("..").Append(wb.xMax.ToString("F1"))
      .Append(" row=").Append(wb.yMin > firstY + 2f ? 1 : 0)
      .Append(ok ? "  PICK ok" : "  PICK **MISS** -> " + (hit == null ? "<null>" : hit.GetType().Name + "/" + ZCaption(hit)))
      .Append(off ? "  << OFF THE WINDOW" : "")
      .Append("\n");
}
sb.Append("buttons=").Append(n).Append(" pickable=").Append(picked).Append(" offWindow=").Append(offWin).Append("\n");

// a real click on the LAST button (the one that used to be off the window), then a click back
if (lastBtn != null)
{
    var secT = ZType("ZuiSection");
    var sections = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
    foreach (var e in ZAll(root)) if (e.GetType().Name == "ZuiSection") sections.Add(e);
    var openProp = secT.GetProperty("IsOpen");
    var before = new bool[sections.Count];
    for (int i = 0; i < sections.Count; i++) before[i] = (bool)openProp.GetValue(sections[i]);
    Click(lastBtn);
    int flipped = 0;
    for (int i = 0; i < sections.Count; i++) if ((bool)openProp.GetValue(sections[i]) != before[i]) flipped++;
    sb.Append("click '").Append(ZCaption(lastBtn)).Append("' -> sections whose IsOpen flipped: ").Append(flipped).Append("\n");
    Click(lastBtn);
    int back = 0;
    for (int i = 0; i < sections.Count; i++) if ((bool)openProp.GetValue(sections[i]) != before[i]) back++;
    sb.Append("click again -> still differing from the start: ").Append(back).Append(" (0 = window left as found)\n");
}
return sb.ToString();

bool IsUnder(UnityEngine.UIElements.VisualElement e, UnityEngine.UIElements.VisualElement anc)
{
    for (var p = e; p != null; p = p.hierarchy.parent) if (p == anc) return true;
    return false;
}
void Click(UnityEngine.UIElements.VisualElement b)
{
    var c = b.worldBound.center;
    var evDown = new UnityEngine.Event { type = UnityEngine.EventType.MouseDown, mousePosition = c, button = 0, clickCount = 1 };
    var evUp = new UnityEngine.Event { type = UnityEngine.EventType.MouseUp, mousePosition = c, button = 0, clickCount = 1 };
    using (var down = UnityEngine.UIElements.PointerDownEvent.GetPooled(evDown))
    { down.target = b; b.SendEvent(down); }
    using (var up = UnityEngine.UIElements.PointerUpEvent.GetPooled(evUp))
    { up.target = b; b.SendEvent(up); }
}
