// ── T-0312 · zlib.cs — the ONE shared probe library for every ZUI window audit ───────────────────────
//
// WHY THIS EXISTS.  Every archived probe in this programme recursed with `e[i]` / `e.childCount`, which
// enumerate an element's CONTENT CONTAINER, not its hierarchy.  Anything parented outside a container's
// content is therefore never visited: a ZuiSection's own bar, a ZuiColumnFlow's non-rightmost columns
// (contentContainer is the RIGHTMOST column), a ZuiBox's header, the whole ZuiViewBar.  Measured on one
// Shaper window with one line changed: 107 laid-out text elements via `e[i]` vs 272 via `e.hierarchy[i]`.
// EVERY walk below therefore uses `e.hierarchy.childCount` / `e.hierarchy[i]`, and nothing else.
//
// HOW TO RUN.   sh probes/zrun.sh probes/<probe>.cs
// zrun.sh concatenates THIS file and <probe>.cs into one temp body and hands it to
// `unity.exe command eval_file --project-path "D:/UNITY/Laubrary Dev - Shaper"`.  A probe may use every
// Z* delegate defined here and must end with `return <string>;`.  Nothing here returns, so it composes.
//
// WHAT IT EXPOSES (all as delegates, because an eval_file body cannot declare methods):
//   ZType(name)          → System.Type by short name, across every loaded assembly
//   ZWin(typeName)       → the live EditorWindow of that type ("ShaperWindow", "PyreWindow"), or null
//   ZWalk(e, list)       → recursive HIERARCHY walk, appending every element
//   ZAll(root)           → the same as a fresh List
//   ZDisplayed(e)        → no ancestor has display:None
//   ZLaidOut(e)          → worldBound is a real, non-NaN, non-zero rect
//   ZDrawn(e)            → both of the above (what the user can actually see)
//   ZOwnText(e)          → the element's own text / `label` property, "" if it has neither
//   ZCaption(e)          → its best human identity: own text, else first descendant text, else the
//                          nearest preceding sibling's text, else the parent's label
//   ZCls(e)              → its USS classes, dot-joined
//   ZPath(e)             → the last 6 ancestors as Type#name, '>'-joined
//   ZTip(e)              → the EFFECTIVE tooltip (own, else nearest ancestor's) — what a hover shows
//   ZIsCtrl(e)           → is this an interactive control (Button / BaseField<> / BaseSlider<> / any
//                          named ZUI control), as opposed to chrome, a label or a layout box
//   ZIsLeafCtrl(e)       → a control with no control inside it (the thing a finger actually lands on)
//   ZNeed(te)/ZHave(te)  → the width a TextElement's string needs vs the width its content box gives
//   ZAudit(win, tag)     → runs all four audits and returns the full report text
//   ZCount["key"]        → the counts ZAudit last produced (see the keys it fills, below)
//   ZDump(name, text)    → writes the text under T0312.out (default workspace/T-0312/out) and returns
//                          the path, so a big report never has to fit through eval_file's return value
//
// THE FOUR AUDITS, and the rule each applies:
//   captions   a laid-out non-wrapping TextElement whose string measures wider than its content box,
//              by more than ZTOL (1.5 px, the device-pixel tolerance T-0304 established)
//   overflow   a drawn element spilling out of its PARENT's content box, or out of the WINDOW root, by
//              more than ZTOL.  Horizontal spill is always a finding (the UI guide: a horizontal
//              scrollbar on a one-pane window is a bug signal); vertical spill is a finding only
//              outside a ScrollView, where scrolling is the legitimate answer.
//   tooltips   a drawn control whose EFFECTIVE tooltip is empty ("every control gets a tooltip")
//   inert      a drawn control that is disabled (enabledInHierarchy == false) with no effective
//              tooltip to say why — a greyed control that does not explain itself is a dead end
//
// ────────────────────────────────────────────────────────────────────────────────────────────────────

float ZTOL = 1.5f;
string ZOUTDIR = UnityEditor.EditorPrefs.GetString("T0312.out", "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0312/out");

System.Func<string, System.Type> ZType = n =>
{
    foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
    {
        System.Type[] ts; try { ts = a.GetTypes(); } catch { continue; }
        foreach (var t in ts) if (t.Name == n) return t;
    }
    return null;
};

System.Func<string, UnityEditor.EditorWindow> ZWin = n =>
{
    var t = ZType(n); if (t == null) return null;
    UnityEditor.EditorWindow found = null;
    foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
        if (w != null && w.GetType() == t) found = w;
    return found;
};

// THE walk. e.hierarchy, never e[i].
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> ZWalk = null;
ZWalk = (e, into) =>
{
    if (e == null) return;
    into.Add(e);
    int n = e.hierarchy.childCount;
    for (int i = 0; i < n; i++) ZWalk(e.hierarchy[i], into);
};

System.Func<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> ZAll = r =>
{
    var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
    ZWalk(r, l);
    return l;
};

System.Func<UnityEngine.UIElements.VisualElement, bool> ZDisplayed = e =>
{
    for (var p = e; p != null; p = p.hierarchy.parent)
        if (p.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None) return false;
    return true;
};

System.Func<UnityEngine.UIElements.VisualElement, bool> ZLaidOut = e =>
{
    var b = e.worldBound;
    return !float.IsNaN(b.width) && !float.IsNaN(b.height) && b.width > 0.5f && b.height > 0.5f;
};

System.Func<UnityEngine.UIElements.VisualElement, bool> ZDrawn = e => ZDisplayed(e) && ZLaidOut(e);

System.Func<UnityEngine.UIElements.VisualElement, string> ZOwnText = e =>
{
    var te = e as UnityEngine.UIElements.TextElement;
    if (te != null && !string.IsNullOrEmpty(te.text)) return te.text;
    var pi = e.GetType().GetProperty("label", System.Reflection.BindingFlags.Instance
                                            | System.Reflection.BindingFlags.Public
                                            | System.Reflection.BindingFlags.FlattenHierarchy);
    if (pi != null && pi.PropertyType == typeof(string) && pi.GetIndexParameters().Length == 0)
    {
        string s = null; try { s = pi.GetValue(e) as string; } catch { }
        if (!string.IsNullOrEmpty(s)) return s;
    }
    return "";
};

System.Func<UnityEngine.UIElements.VisualElement, string> ZFirstText = e =>
{
    foreach (var d in ZAll(e))
    {
        var te = d as UnityEngine.UIElements.TextElement;
        if (te != null && !string.IsNullOrEmpty(te.text)) return te.text;
    }
    return "";
};

System.Func<UnityEngine.UIElements.VisualElement, string> ZCaption = e =>
{
    var own = ZOwnText(e); if (!string.IsNullOrEmpty(own)) return own;
    var inner = ZFirstText(e); if (!string.IsNullOrEmpty(inner)) return inner;
    var par = e.hierarchy.parent;
    if (par != null)
    {
        int idx = par.hierarchy.IndexOf(e);
        for (int i = idx - 1; i >= 0; i--)
        {
            var s = ZFirstText(par.hierarchy[i]);
            if (!string.IsNullOrEmpty(s)) return s;
        }
        var pown = ZOwnText(par); if (!string.IsNullOrEmpty(pown)) return pown;
    }
    return "<unnamed>";
};

System.Func<UnityEngine.UIElements.VisualElement, string> ZCls = e =>
{
    var s = new System.Text.StringBuilder();
    foreach (var c in e.GetClasses()) { if (s.Length > 0) s.Append('.'); s.Append(c); }
    return s.ToString();
};

System.Func<UnityEngine.UIElements.VisualElement, string> ZPath = e =>
{
    var parts = new System.Collections.Generic.List<string>();
    for (var p = e; p != null && parts.Count < 6; p = p.hierarchy.parent)
    {
        var n = p.GetType().Name;
        if (!string.IsNullOrEmpty(p.name)) n = n + "#" + p.name;
        parts.Add(n);
    }
    parts.Reverse();
    return string.Join(">", parts.ToArray());
};

System.Func<UnityEngine.UIElements.VisualElement, string> ZTip = e =>
{
    for (var p = e; p != null; p = p.hierarchy.parent)
        if (!string.IsNullOrEmpty(p.tooltip)) return p.tooltip;
    return "";
};

var ZCtrlSet = new System.Collections.Generic.HashSet<string>(new string[] {
    "Button","Toggle","Slider","SliderInt","MinMaxSlider","FloatField","IntField","LongField","DoubleField",
    "TextField","ObjectField","EnumField","ColorField","GradientField","CurveField","Foldout","RadioButton",
    "RadioButtonGroup","DropdownField","ToolbarButton","ToolbarMenu","ToolbarToggle","ToolbarSearchField",
    "ZuiToggleButton","ZuiMicroSlider","ZuiMicroMinMax","ZuiSegmented","ZuiChip","ZuiPad","ZuiValueControl",
    "ZuiValue2DControl","ZuiGradientControl","ZuiRampControl","ZuiSwatchControl","ZuiEnvelope","ZuiVectorDial",
    "ZuiStepSequencer","ZuiThumbGrid","ZuiFillControl","ZuiTimeline","ZuiPositionPad","ZuiBreadcrumb",
    "ZuiDirection3D","ZuiGallery","ZuiShapeBrowser"
});

System.Func<UnityEngine.UIElements.VisualElement, bool> ZIsCtrl = e =>
{
    for (var t = e.GetType(); t != null && t != typeof(UnityEngine.UIElements.VisualElement); t = t.BaseType)
    {
        var n = t.Name;
        if (n == "BaseField`1" || n == "BaseSlider`1" || n == "BasePopupField`2" || n == "TextInputBaseField`1") return true;
        if (ZCtrlSet.Contains(n)) return true;
    }
    return false;
};

System.Func<UnityEngine.UIElements.VisualElement, bool> ZIsLeafCtrl = e =>
{
    if (!ZIsCtrl(e)) return false;
    foreach (var d in ZAll(e)) { if (d == e) continue; if (ZIsCtrl(d)) return false; }
    return true;
};

System.Func<UnityEngine.UIElements.TextElement, float> ZNeed = te =>
    te.MeasureTextSize(te.text, 0f, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined,
                                0f, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined).x;
System.Func<UnityEngine.UIElements.TextElement, float> ZHave = te => te.contentRect.width;

// An element's CONTENT box in world space. `VisualElement.LocalToWorld(Rect)` is internal in this Unity
// version, so it is composed by hand: worldBound is the border box with its origin at the element's local
// (0,0), and contentRect is that same local space offset by the border + padding — no transform in play
// in these windows, so the two simply add.
System.Func<UnityEngine.UIElements.VisualElement, UnityEngine.Rect> ZContentWorld = p =>
{
    var wb = p.worldBound; var cr = p.contentRect;
    return new UnityEngine.Rect(wb.x + cr.x, wb.y + cr.y, cr.width, cr.height);
};

System.Func<UnityEngine.UIElements.VisualElement, bool> ZInScroll = e =>
{
    for (var p = e; p != null; p = p.hierarchy.parent)
        if (p is UnityEngine.UIElements.ScrollView) return true;
    return false;
};

// Chrome that legitimately lives outside its parent's content box or is not a user-facing caption.
var ZSkipCls = new System.Collections.Generic.HashSet<string>(new string[] {
    "unity-scroller","unity-scroller--vertical","unity-scroller--horizontal",
    "unity-base-slider__tracker","unity-base-slider__dragger","unity-repeat-button",
    "unity-scroll-view__content-viewport","unity-scroll-view__content-container",
    // TwoPaneSplitView's grab strip is an 11 px dragline deliberately overhanging a 0.89 px anchor — by
    // design, not a layout fault, and it is the one false positive the overflow audit produced on a
    // clean window. Skipping it here keeps the finding list honest.
    "unity-two-pane-split-view__dragline","unity-two-pane-split-view__dragline-anchor"
});
var ZSkipName = new System.Collections.Generic.HashSet<string>(new string[] {
    "unity-dragline","unity-dragline-anchor",
    // A MinMaxSlider's two thumbs are drawn deliberately overhanging the dragger they bracket (Unity's own
    // control, 7.1 px each side, at every width) — chrome, not a layout fault.
    "unity-thumb-min","unity-thumb-max"
});
System.Func<UnityEngine.UIElements.VisualElement, bool> ZIsChrome = e =>
{
    if (!string.IsNullOrEmpty(e.name) && ZSkipName.Contains(e.name)) return true;
    foreach (var c in e.GetClasses()) if (ZSkipCls.Contains(c)) return true;
    return false;
};

System.IO.Directory.CreateDirectory(ZOUTDIR);
System.Func<string, string, string> ZDump = (name, text) =>
{
    var p = ZOUTDIR + "/" + name;
    System.IO.File.WriteAllText(p, text);
    return p;
};

var ZCount = new System.Collections.Generic.Dictionary<string, int>();

// ── ZAudit — all four audits over one window, in one hierarchy walk ──────────────────────────────────
// Fills ZCount with: elements, drawn, text, controls, leafControls, captionShort, overflowParentX,
// overflowParentY, overflowWindow, noTooltip, inertNoReason, inertWithReason.
// Returns the whole report as text (dump it with ZDump; return only a summary through eval_file).
System.Func<UnityEditor.EditorWindow, string, string> ZAudit = (win, tag) =>
{
    var sb = new System.Text.StringBuilder();
    if (win == null) { ZCount["elements"] = 0; return "NO WINDOW for " + tag + "\n"; }
    var root = win.rootVisualElement;
    var all = ZAll(root);
    var winRect = root.worldBound;

    sb.Append("=== ").Append(tag).Append(" ===\n");
    sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
    sb.Append("window=").Append(win.position).Append(" rootWorld=").Append(winRect).Append("\n");

    int nDrawn = 0, nText = 0, nCtrl = 0, nLeaf = 0;
    var capt = new System.Text.StringBuilder();
    var ovfP = new System.Text.StringBuilder();
    var ovfW = new System.Text.StringBuilder();
    var noTip = new System.Text.StringBuilder();
    var inertNo = new System.Text.StringBuilder();
    var inertYes = new System.Text.StringBuilder();
    var leaves = new System.Text.StringBuilder();
    int cShort = 0, oPX = 0, oPY = 0, oW = 0, noT = 0, inN = 0, inY = 0;

    foreach (var e in all)
    {
        if (!ZDrawn(e)) continue;
        nDrawn++;
        var b = e.worldBound;

        // ── captions ────────────────────────────────────────────────────────────────
        var te = e as UnityEngine.UIElements.TextElement;
        if (te != null && !string.IsNullOrEmpty(te.text)
            && te.resolvedStyle.whiteSpace != UnityEngine.UIElements.WhiteSpace.Normal
            && !float.IsNaN(te.contentRect.width) && te.contentRect.width > 0f)
        {
            nText++;
            float need = ZNeed(te), have = ZHave(te);
            if (need > have + ZTOL)
            {
                cShort++;
                capt.Append("CAPTION '").Append(te.text.Length > 44 ? te.text.Substring(0, 44) : te.text)
                    .Append("' need=").Append(need.ToString("F1")).Append(" have=").Append(have.ToString("F1"))
                    .Append(" short=").Append((need - have).ToString("F1"))
                    .Append(" | ").Append(ZPath(te)).Append(" | cls=").Append(ZCls(te)).Append("\n");
            }
        }

        // ── overflow vs the parent's content box ────────────────────────────────────
        var par = e.hierarchy.parent;
        if (par != null && ZDrawn(par) && !ZIsChrome(e) && !ZIsChrome(par))
        {
            var pc = ZContentWorld(par);
            if (!float.IsNaN(pc.width) && pc.width > 0f)
            {
                float dl = pc.xMin - b.xMin, dr = b.xMax - pc.xMax;
                float dt = pc.yMin - b.yMin, db = b.yMax - pc.yMax;
                float sx = UnityEngine.Mathf.Max(dl, dr), sy = UnityEngine.Mathf.Max(dt, db);
                if (sx > ZTOL)
                {
                    oPX++;
                    ovfP.Append("OVERFLOW-X ").Append(sx.ToString("F1")).Append("px '").Append(ZCaption(e))
                        .Append("' ").Append(b).Append(" out of ").Append(pc)
                        .Append(" | ").Append(ZPath(e)).Append("\n");
                }
                if (sy > ZTOL && !ZInScroll(e))
                {
                    oPY++;
                    ovfP.Append("OVERFLOW-Y ").Append(sy.ToString("F1")).Append("px '").Append(ZCaption(e))
                        .Append("' ").Append(b).Append(" out of ").Append(pc)
                        .Append(" | ").Append(ZPath(e)).Append("\n");
                }
            }
        }

        bool isCtrl = ZIsCtrl(e);
        if (isCtrl) nCtrl++;

        // ── overflow past the WINDOW (controls and captions only) ───────────────────
        if ((isCtrl || te != null) && !ZInScroll(e))
        {
            float wl = winRect.xMin - b.xMin, wr = b.xMax - winRect.xMax;
            float wt = winRect.yMin - b.yMin, wb2 = b.yMax - winRect.yMax;
            float sw = UnityEngine.Mathf.Max(UnityEngine.Mathf.Max(wl, wr), UnityEngine.Mathf.Max(wt, wb2));
            if (sw > ZTOL)
            {
                oW++;
                ovfW.Append("OFF-WINDOW ").Append(sw.ToString("F1")).Append("px '").Append(ZCaption(e))
                    .Append("' ").Append(b).Append(" | ").Append(ZPath(e)).Append("\n");
            }
        }

        if (!isCtrl) continue;

        string tip = ZTip(e);
        bool leaf = ZIsLeafCtrl(e);
        if (leaf)
        {
            nLeaf++;
            leaves.Append("LEAF '").Append(ZCaption(e)).Append("' ").Append(e.GetType().Name)
                  .Append(" rect=").Append(b.x.ToString("F0")).Append(",").Append(b.y.ToString("F0"))
                  .Append(" ").Append(b.width.ToString("F1")).Append("x").Append(b.height.ToString("F1"))
                  .Append(" enabled=").Append(e.enabledInHierarchy)
                  .Append(" cls=").Append(ZCls(e))
                  .Append(" tip=").Append(string.IsNullOrEmpty(tip) ? "<NONE>"
                        : (tip.Length > 70 ? tip.Substring(0, 70).Replace("\n", " ") + "…" : tip.Replace("\n", " ")))
                  .Append(" | ").Append(ZPath(e)).Append("\n");
        }

        // ── tooltips ────────────────────────────────────────────────────────────────
        if (string.IsNullOrEmpty(tip))
        {
            noT++;
            noTip.Append("NO-TOOLTIP '").Append(ZCaption(e)).Append("' ").Append(e.GetType().Name)
                 .Append(leaf ? " (leaf)" : " (compound)")
                 .Append(" cls=").Append(ZCls(e)).Append(" | ").Append(ZPath(e)).Append("\n");
        }

        // ── inert without a reason ──────────────────────────────────────────────────
        if (!e.enabledInHierarchy)
        {
            if (string.IsNullOrEmpty(tip))
            {
                inN++;
                inertNo.Append("INERT-NO-REASON '").Append(ZCaption(e)).Append("' ").Append(e.GetType().Name)
                       .Append(" | ").Append(ZPath(e)).Append("\n");
            }
            else
            {
                inY++;
                inertYes.Append("inert '").Append(ZCaption(e)).Append("' → ")
                        .Append(tip.Length > 90 ? tip.Substring(0, 90).Replace("\n", " ") + "…" : tip.Replace("\n", " "))
                        .Append("\n");
            }
        }
    }

    ZCount["elements"] = all.Count; ZCount["drawn"] = nDrawn; ZCount["text"] = nText;
    ZCount["controls"] = nCtrl; ZCount["leafControls"] = nLeaf;
    ZCount["captionShort"] = cShort; ZCount["overflowParentX"] = oPX; ZCount["overflowParentY"] = oPY;
    ZCount["overflowWindow"] = oW; ZCount["noTooltip"] = noT;
    ZCount["inertNoReason"] = inN; ZCount["inertWithReason"] = inY;

    sb.Append("elements=").Append(all.Count).Append(" drawn=").Append(nDrawn)
      .Append(" textElements=").Append(nText).Append(" controls=").Append(nCtrl)
      .Append(" leafControls=").Append(nLeaf).Append("\n");
    sb.Append("captionShort=").Append(cShort)
      .Append(" overflowParentX=").Append(oPX).Append(" overflowParentY=").Append(oPY)
      .Append(" offWindow=").Append(oW)
      .Append(" noTooltip=").Append(noT)
      .Append(" inertNoReason=").Append(inN).Append(" inertWithReason=").Append(inY).Append("\n\n");
    sb.Append("-- captions --\n").Append(capt)
      .Append("-- overflow (parent) --\n").Append(ovfP)
      .Append("-- overflow (window) --\n").Append(ovfW)
      .Append("-- controls with no effective tooltip --\n").Append(noTip)
      .Append("-- disabled without a reason --\n").Append(inertNo)
      .Append("-- disabled, reason given --\n").Append(inertYes)
      .Append("-- every leaf control --\n").Append(leaves);
    return sb.ToString();
};

System.Func<string, string> ZSummary = tag =>
{
    var sb = new System.Text.StringBuilder();
    sb.Append(tag).Append(" | ");
    foreach (var k in new string[] { "elements", "drawn", "text", "controls", "leafControls",
                                     "captionShort", "overflowParentX", "overflowParentY", "offWindow",
                                     "noTooltip", "inertNoReason", "inertWithReason" })
    {
        int v; if (!ZCount.TryGetValue(k == "offWindow" ? "overflowWindow" : k, out v)) continue;
        sb.Append(k).Append("=").Append(v).Append(" ");
    }
    return sb.Append("\n").ToString();
};

// ── T-0318: T-0317's proposed tightening of the inert rule, applied to THIS copy ─────────────────────
// The archived rule counts a disabled control as "explained" the moment it has ANY effective tooltip, so
// a tooltip that merely restates the control's normal effect scores identically to one that says why the
// control is dead.  T-0317 proposed a two-state test: sample every control's (identity, enabled, tooltip)
// in two states that flip its enabled boolean, and flag inertNoReason whenever the tooltip string is
// IDENTICAL across both samples despite the enabled state differing — the exact signature of "describes
// the effect, not the reason".  ZTipSample emits one machine-comparable line per drawn control; the
// comparison across two dumps is done outside the editor.
System.Func<UnityEditor.EditorWindow, string, string> ZTipSample = (win, tag) =>
{
    var sb = new System.Text.StringBuilder();
    if (win == null) return "NO WINDOW for " + tag + "\n";
    foreach (var e in ZAll(win.rootVisualElement))
    {
        if (!ZDrawn(e) || !ZIsCtrl(e)) continue;
        sb.Append(e.GetType().Name).Append('\u0001').Append(ZCaption(e)).Append('\u0001')
          .Append(ZCls(e)).Append('\u0001')
          .Append(e.enabledInHierarchy ? "ENABLED" : "DISABLED").Append('\u0001')
          .Append((ZTip(e) ?? "").Replace("\n", " ")).Append("\n");
    }
    return sb.ToString();
};
// ZClick(e): press an element the way a mouse does — PointerDown then PointerUp at its own centre, with a
// real pointer id, which is what a Clickable manipulator listens for.  Returns false if it could not.
System.Func<UnityEngine.UIElements.VisualElement,bool> ZClick = el => {
  if (el == null) return false;
  var c = el.worldBound.center;
  var pd = UnityEngine.UIElements.PointerDownEvent.GetPooled();
  var pdT = typeof(UnityEngine.UIElements.PointerDownEvent);
  var setPos = pdT.GetProperty("position"); var setLocal = pdT.GetProperty("localPosition"); var setBtn = pdT.GetProperty("button"); var setId = pdT.GetProperty("pointerId");
  if (setPos != null && setPos.GetSetMethod(true) != null) setPos.GetSetMethod(true).Invoke(pd, new object[]{ new UnityEngine.Vector3(c.x, c.y, 0) });
  if (setLocal != null && setLocal.GetSetMethod(true) != null) setLocal.GetSetMethod(true).Invoke(pd, new object[]{ new UnityEngine.Vector3(el.worldBound.width/2f, el.worldBound.height/2f, 0) });
  if (setBtn != null && setBtn.GetSetMethod(true) != null) setBtn.GetSetMethod(true).Invoke(pd, new object[]{ 0 });
  if (setId != null && setId.GetSetMethod(true) != null) setId.GetSetMethod(true).Invoke(pd, new object[]{ UnityEngine.UIElements.PointerId.mousePointerId });
  pd.target = el; using (pd) el.SendEvent(pd);
  var pu = UnityEngine.UIElements.PointerUpEvent.GetPooled();
  var puT = typeof(UnityEngine.UIElements.PointerUpEvent);
  var sp = puT.GetProperty("position"); var sl = puT.GetProperty("localPosition"); var sb2 = puT.GetProperty("button"); var si = puT.GetProperty("pointerId");
  if (sp != null && sp.GetSetMethod(true) != null) sp.GetSetMethod(true).Invoke(pu, new object[]{ new UnityEngine.Vector3(c.x, c.y, 0) });
  if (sl != null && sl.GetSetMethod(true) != null) sl.GetSetMethod(true).Invoke(pu, new object[]{ new UnityEngine.Vector3(el.worldBound.width/2f, el.worldBound.height/2f, 0) });
  if (sb2 != null && sb2.GetSetMethod(true) != null) sb2.GetSetMethod(true).Invoke(pu, new object[]{ 0 });
  if (si != null && si.GetSetMethod(true) != null) si.GetSetMethod(true).Invoke(pu, new object[]{ UnityEngine.UIElements.PointerId.mousePointerId });
  pu.target = el; using (pu) el.SendEvent(pu);
  return true;
};
// ZBind(winTypeName, assetPath) — bind a ZuiAssetWindow to an asset via its SetAsset + Rebuild.
System.Func<string,string,string> ZBind = (winName, path) => {
  var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
  var w = ZWin(winName); if (w==null) return winName+": no window";
  System.Reflection.MethodInfo sa=null, rb=null; System.Reflection.FieldInfo af=null;
  for (var t=w.GetType(); t!=null; t=t.BaseType) { if (sa==null) sa=t.GetMethod("SetAsset", BFi|System.Reflection.BindingFlags.DeclaredOnly); if (rb==null) rb=t.GetMethod("Rebuild", BFi|System.Reflection.BindingFlags.DeclaredOnly); if (af==null) af=t.GetField("asset", BFi|System.Reflection.BindingFlags.DeclaredOnly); }
  if (sa==null) return winName+": no SetAsset";
  var ty = sa.GetParameters()[0].ParameterType;
  var obj = UnityEditor.AssetDatabase.LoadAssetAtPath(path, ty);
  if (obj==null) return winName+": asset not found "+path;
  sa.Invoke(w, new object[]{ obj }); if (rb!=null) rb.Invoke(w,null); w.Repaint();
  return winName+" -> "+path;
};
// ZOpen(typeName) — open a window by its static Open()/ShowWindow(), return it.
System.Func<string,UnityEditor.EditorWindow> ZOpen = n => {
  var w = ZWin(n); if (w!=null) return w;
  var t = ZType(n); if (t==null) return null;
  var BFs = System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
  foreach (var m in t.GetMethods(BFs)) if (m.GetParameters().Length==0 && (m.Name=="Open"||m.Name=="ShowWindow"||m.Name=="Show"||m.Name.StartsWith("Open"))) { m.Invoke(null,null); break; }
  return ZWin(n);
};
var sb=new System.Text.StringBuilder();
sb.Append("before userSel=").Append(UnityEditor.EditorPrefs.GetString("ZuiSectionToggleBar.ShaperWindow.userSel","<none>")).Append("\n");
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel","Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0");
UnityEditor.EditorPrefs.SetBool("ZuiSectionToggleBar.ShaperWindow.barMode", true);
foreach (var k in new string[]{"T323.auditWin","T323.pressWin","T323.pressText","T323.pressNth","T323.pressAsset","T323.capOut","T323.capRect","T323.delay","T323.t0","T320.capWin","T320.capOut","T321.src","T321.dst","T321.rect","T321.scale","T322.auditWin","T322.t0","Shaper.lastView"}) UnityEditor.EditorPrefs.DeleteKey(k);
UnityEditor.EditorPrefs.SetString("T0312.out","D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0312/out");
sb.Append("after  userSel=").Append(UnityEditor.EditorPrefs.GetString("ZuiSectionToggleBar.ShaperWindow.userSel","<none>")).Append("\n");
return sb.ToString();
