var sb = new System.Text.StringBuilder();
var windows = UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.UISeparationPilot.PilotWindow>();
var w = System.Array.Find(windows, x => x.titleContent.text.Contains("Candidate"));
if (w == null) throw new System.Exception("Open pair first");
var slider = UnityEngine.UIElements.UQueryExtensions.Q<Laubrary.Zui.ZuiMicroSlider>(w.rootVisualElement);
float before = slider.value;
var position = slider.worldBound.center;
position.x = slider.worldBound.x + slider.worldBound.width * .77f;
var mouse = new UnityEngine.Event { type = UnityEngine.EventType.MouseDown, button = 0, mousePosition = position, clickCount = 1 };
UnityEditor.Undo.IncrementCurrentGroup();
using (var e = UnityEngine.UIElements.PointerDownEvent.GetPooled(mouse)) { e.target = slider; slider.SendEvent(e); }
mouse.type = UnityEngine.EventType.MouseUp;
using (var e = UnityEngine.UIElements.PointerUpEvent.GetPooled(mouse)) { e.target = slider; slider.SendEvent(e); }
UnityEditor.Undo.FlushUndoRecordObjects();
sb.AppendLine("slider click before="+before+" after="+slider.value+" state="+Laubrary.UISeparationPilot.PilotWindow.State());
if (UnityEngine.Mathf.Abs(slider.value-.77f)>.02f) throw new System.Exception("Click mapping failed");
UnityEditor.Undo.PerformUndo();
sb.AppendLine("undo="+slider.value);
if (UnityEngine.Mathf.Abs(slider.value-before)>.001f) throw new System.Exception("Undo failed");
return sb.ToString();
