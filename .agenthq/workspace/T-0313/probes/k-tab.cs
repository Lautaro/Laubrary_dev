// T-0313 — keyboard reachability through ONE card.  Two readings, because they answer different halves
// of "nothing is unreachable":
//   (1) STATIC: every leaf control in the card, with focusable / canGrabFocus / tabIndex / delegatesFocus
//       — a control that is not focusable can never be reached by Tab, whatever the traversal does.
//   (2) LIVE: focus the card's first focusable control and dispatch NavigationMoveEvent(Next) — the event
//       UI Toolkit turns Tab into — recording where focus lands each step, so the ORDER is read from the
//       running panel rather than assumed from the tree.
// Prefs: T313.card (the ZuiBox/ZuiSection title to walk), T313.win.
string wname = UnityEditor.EditorPrefs.GetString("T313.win", "ShaperWindow");
string cardName = UnityEditor.EditorPrefs.GetString("T313.card", "Canvas");
var win = ZWin(wname);
if (win == null) return "NO WINDOW " + wname;
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append(" window=").Append(win.position.width.ToString("F0"))
  .Append(" card='").Append(cardName).Append("'\n");

UnityEngine.UIElements.VisualElement card = null;
foreach (var e in ZAll(win.rootVisualElement))
{
    var n = e.GetType().Name;
    if (n != "ZuiBox" && n != "ZuiSection") continue;
    if (!ZDrawn(e)) continue;
    if (ZCaption(e) != cardName) continue;
    card = e; break;
}
if (card == null) return sb.Append("card not found\n").ToString();
sb.Append("card ").Append(card.GetType().Name).Append(" w=").Append(card.worldBound.width.ToString("F1"))
  .Append(" at ").Append(card.worldBound.yMin.ToString("F0")).Append("\n");

var leaves = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
foreach (var e in ZAll(card)) if (ZDrawn(e) && ZIsLeafCtrl(e)) leaves.Add(e);
leaves.Sort((a, b) =>
{
    int c = a.worldBound.yMin.CompareTo(b.worldBound.yMin);
    return c != 0 ? c : a.worldBound.xMin.CompareTo(b.worldBound.xMin);
});

int notFocusable = 0;
sb.Append("\n-- static: every leaf control in reading order --\n");
foreach (var e in leaves)
{
    bool f = e.focusable, g = e.canGrabFocus;
    if (!f || !g) notFocusable++;
    sb.Append(f && g ? "  focusable   " : "  UNREACHABLE ")
      .Append("'").Append(ZCaption(e)).Append("' ").Append(e.GetType().Name)
      .Append(" tabIndex=").Append(e.tabIndex)
      .Append(" delegatesFocus=").Append(e.delegatesFocus)
      .Append(" enabled=").Append(e.enabledInHierarchy)
      .Append(" at ").Append(e.worldBound.xMin.ToString("F0")).Append(",").Append(e.worldBound.yMin.ToString("F0"))
      .Append("\n");
}
sb.Append("leafControls=").Append(leaves.Count).Append(" notFocusable=").Append(notFocusable).Append("\n");

// ── live traversal ──────────────────────────────────────────────────────────────
UnityEngine.UIElements.VisualElement start = null;
foreach (var e in leaves) if (e.focusable && e.canGrabFocus && e.enabledInHierarchy) { start = e; break; }
sb.Append("\n-- live: NavigationMoveEvent(Next), the event Tab raises --\n");
if (start == null) return sb.Append("nothing in this card can take focus at all\n").ToString();
start.Focus();
var fc = win.rootVisualElement.focusController;
var visited = new System.Collections.Generic.HashSet<UnityEngine.UIElements.VisualElement>();
var cur = fc.focusedElement as UnityEngine.UIElements.VisualElement;
sb.Append("  step 0 focus='").Append(cur == null ? "<null>" : ZCaption(cur)).Append("' ")
  .Append(cur == null ? "" : cur.GetType().Name).Append("\n");
if (cur != null) visited.Add(cur);
for (int i = 1; i <= 40 && cur != null; i++)
{
    using (var ev = UnityEngine.UIElements.NavigationMoveEvent.GetPooled(UnityEngine.UIElements.NavigationMoveEvent.Direction.Next))
    {
        ev.target = cur;
        cur.SendEvent(ev);
    }
    var next = fc.focusedElement as UnityEngine.UIElements.VisualElement;
    if (next == cur) { sb.Append("  step ").Append(i).Append(" focus did not move — traversal stops here\n"); break; }
    cur = next;
    if (cur == null) { sb.Append("  step ").Append(i).Append(" focus left the panel\n"); break; }
    visited.Add(cur);
    bool inCard = false;
    for (var p = cur; p != null; p = p.hierarchy.parent) if (p == card) { inCard = true; break; }
    sb.Append("  step ").Append(i).Append(" focus='").Append(ZCaption(cur)).Append("' ").Append(cur.GetType().Name)
      .Append(inCard ? "" : "   (left the card)").Append("\n");
    if (!inCard) break;
}
int reached = 0;
foreach (var e in leaves)
{
    bool hit = visited.Contains(e);
    if (!hit) foreach (var v in visited) { for (var p = v; p != null; p = p.hierarchy.parent) if (p == e) { hit = true; break; } if (hit) break; }
    if (hit) reached++;
}
sb.Append("leafControls reached by Tab=").Append(reached).Append(" of ").Append(leaves.Count).Append("\n");
var path = ZDump("tab-" + cardName.Replace(' ', '_') + ".txt", sb.ToString());
return path + "\n" + sb.ToString();
