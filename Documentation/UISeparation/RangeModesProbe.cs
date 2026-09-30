var w=System.Array.Find(UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.UISeparationPilot.PilotWindow>(),x=>x.titleContent.text.Contains("Candidate"));
var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
var control=UnityEngine.UIElements.UQueryExtensions.Q<Laubrary.Zui.ZuiSkinMinMax>(w.rootVisualElement);
var track=(UnityEngine.UIElements.VisualElement)control.GetType().GetField("_track",flags).GetValue(control);
float lo=control.min,hi=control.max;
var point=new UnityEngine.Vector2(track.worldBound.x+track.worldBound.width*(lo+hi)*.5f,track.worldBound.center.y);
UnityEditor.Undo.IncrementCurrentGroup();
var mouse=new UnityEngine.Event {type=UnityEngine.EventType.MouseDown,button=0,clickCount=1,mousePosition=point};
using(var e=UnityEngine.UIElements.PointerDownEvent.GetPooled(mouse)) {e.target=track;track.SendEvent(e);}
mouse.type=UnityEngine.EventType.MouseDrag; mouse.mousePosition=point+new UnityEngine.Vector2(15,0);
using(var e=UnityEngine.UIElements.PointerMoveEvent.GetPooled(mouse)) {e.target=track;track.SendEvent(e);}
mouse.type=UnityEngine.EventType.MouseUp;
using(var e=UnityEngine.UIElements.PointerUpEvent.GetPooled(mouse)) {e.target=track;track.SendEvent(e);}
if(control.min<=lo || UnityEngine.Mathf.Abs((control.max-control.min)-(hi-lo))>.001f) throw new System.Exception("Interval pan failed");
UnityEditor.Undo.FlushUndoRecordObjects();UnityEditor.Undo.PerformUndo();
if(UnityEngine.Mathf.Abs(control.min-lo)>.001f) throw new System.Exception("Interval Undo failed");
return "PASS interval drag pans both edges with fixed span and Undo restores values";
