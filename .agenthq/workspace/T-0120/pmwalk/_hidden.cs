var wt = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ChunksMock.Editor.ChunksMockWindow")).FirstOrDefault(x => x != null);
var win = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType() == wt);
if (win == null) return "NO WINDOW";
var sb = new System.Text.StringBuilder();
int n = 0;
System.Action<UnityEngine.UIElements.VisualElement,int> walk = null;
walk = (ve, d) => {
  if (ve.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None) {
    n++;
    int kids = 0; System.Action<UnityEngine.UIElements.VisualElement> cnt = null;
    cnt = v => { kids++; foreach (var c in v.hierarchy.Children()) cnt(c); };
    foreach (var c in ve.hierarchy.Children()) cnt(c);
    sb.AppendLine(n + ": " + ve.GetType().Name + " [" + string.Join(",", ve.GetClasses()) + "] name='" + ve.name + "' descendants=" + kids);
    return;
  }
  foreach (var c in ve.hierarchy.Children()) walk(c, d + 1);
};
walk(win.rootVisualElement, 0);
sb.AppendLine("total hidden subtrees=" + n);
return sb.ToString();
