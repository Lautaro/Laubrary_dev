var log = new System.Text.StringBuilder();
int passed = 0;
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
void Check(bool okay, string label) { if (!okay) throw new System.Exception(label); passed++; log.AppendLine("PASS " + label); }
void Pointer(UnityEngine.UIElements.VisualElement e, UnityEngine.EventType type, float x, float y, int clicks=1, bool shift=false) {
    var mouse = new UnityEngine.Event { type=type, button=0, clickCount=clicks, mousePosition=new UnityEngine.Vector2(e.worldBound.x+x,e.worldBound.y+y), modifiers=shift?UnityEngine.EventModifiers.Shift:UnityEngine.EventModifiers.None };
    if (type==UnityEngine.EventType.MouseDown) { using (var ev=UnityEngine.UIElements.PointerDownEvent.GetPooled(mouse)) { ev.target=e; e.SendEvent(ev); } }
    else if (type==UnityEngine.EventType.MouseUp) { using (var ev=UnityEngine.UIElements.PointerUpEvent.GetPooled(mouse)) { ev.target=e; e.SendEvent(ev); } }
    else { using (var ev=UnityEngine.UIElements.PointerMoveEvent.GetPooled(mouse)) { ev.target=e; e.SendEvent(ev); } }
}
void UndoChange() { UnityEditor.Undo.FlushUndoRecordObjects(); UnityEditor.Undo.PerformUndo(); }
var windows = UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.UISeparationPilot.PilotWindow>();
var w=System.Array.Find(windows,x=>x.titleContent.text.Contains("Candidate"));
var root=w.rootVisualElement;
var slider=UnityEngine.UIElements.UQueryExtensions.Q<Laubrary.Zui.ZuiMicroSlider>(root);
float old=slider.value;
UnityEditor.Undo.IncrementCurrentGroup();
Pointer(slider,UnityEngine.EventType.MouseDown,30,8);
Pointer(slider,UnityEngine.EventType.MouseDrag,slider.layout.width*.77f,8);
Pointer(slider,UnityEngine.EventType.MouseUp,slider.layout.width*.77f,8);
Check(slider.value>.7f,"slider drag retains pointer capture and updates live value");
UndoChange(); Check(UnityEngine.Mathf.Abs(slider.value-old)<.001f,"slider drag Undo");
UnityEditor.Undo.IncrementCurrentGroup();
Pointer(slider,UnityEngine.EventType.MouseDown,60,8,1,true);
Pointer(slider,UnityEngine.EventType.MouseDrag,70,8,1,true);
Pointer(slider,UnityEngine.EventType.MouseUp,70,8,1,true);
Check(slider.value>old && slider.value<old+.05f,"Shift fine adjustment");
UndoChange();
UnityEditor.Undo.IncrementCurrentGroup();
Pointer(slider,UnityEngine.EventType.MouseDown,125,8);
Pointer(slider,UnityEngine.EventType.MouseUp,125,8);
Pointer(slider,UnityEngine.EventType.MouseDown,125,8,2);
Pointer(slider,UnityEngine.EventType.MouseUp,125,8,2);
Check(UnityEngine.Mathf.Abs(slider.value-.42f)<.001f,"double click default reset");
UnityEditor.Undo.FlushUndoRecordObjects();
var bands=UnityEngine.UIElements.UQueryExtensions.Q<Laubrary.Zui.ZuiSkinBandSliders>(root);
float[] Values() => (float[])bands.GetType().GetField("_values",flags).GetValue(bands);
var original=(float[])Values().Clone();
UnityEditor.Undo.IncrementCurrentGroup();
Pointer(bands,UnityEngine.EventType.MouseDown,5,10);
Pointer(bands,UnityEngine.EventType.MouseDrag,bands.layout.width*.69f,20);
Pointer(bands,UnityEngine.EventType.MouseUp,bands.layout.width*.69f,20);
Check(Values()[0]!=original[0] && Values()[2]!=original[2],"band drag interpolates skipped bands");
Check(Values()[4]==original[4] && Values()[4]>1f,"untouched out-of-range band preserved");
UndoChange(); Check(Values()[0]==original[0] && Values()[2]==original[2],"band Undo");
UnityEditor.Undo.IncrementCurrentGroup();
Pointer(bands,UnityEngine.EventType.MouseDown,5,10,2); Pointer(bands,UnityEngine.EventType.MouseUp,5,10,2);
Check(UnityEngine.Mathf.Abs(Values()[0]-.5f)<.001f,"band double click default reset"); UndoChange();
var range=UnityEngine.UIElements.UQueryExtensions.Q<Laubrary.Zui.ZuiSkinRangeSlider>(root);
float lo=range.min, hi=range.max;
var thumb=(UnityEngine.UIElements.VisualElement)range.GetType().GetField("_thumbMin",flags).GetValue(range);
float tx=thumb.worldBound.center.x-range.worldBound.x,ty=thumb.worldBound.center.y-range.worldBound.y;
UnityEditor.Undo.IncrementCurrentGroup();
Pointer(range,UnityEngine.EventType.MouseDown,tx,ty); Pointer(range,UnityEngine.EventType.MouseDrag,tx+20,ty); Pointer(range,UnityEngine.EventType.MouseUp,tx+20,ty);
Check(range.min>lo && UnityEngine.Mathf.Abs(range.max-hi)<.001f,"range thumb drag preserves opposite bound");
UndoChange(); Check(UnityEngine.Mathf.Abs(range.min-lo)<.001f,"range Undo");
var env=UnityEngine.UIElements.UQueryExtensions.Q<Laubrary.Zui.ZuiSkinEnvelope>(root);
var plot=(UnityEngine.Rect)env.GetType().GetProperty("Plot",flags).GetValue(env);
float t=env.points[1].time,v=env.points[1].value;
float ex=plot.x+plot.width*t,ey=plot.y+plot.height*(1-v);
UnityEditor.Undo.IncrementCurrentGroup();
Pointer(env,UnityEngine.EventType.MouseDown,ex,ey); Pointer(env,UnityEngine.EventType.MouseDrag,ex+15,ey+8); Pointer(env,UnityEngine.EventType.MouseUp,ex+15,ey+8);
Check(env.points[1].time>t && env.points[1].value<v,"envelope painted point and hit mapping agree");
UndoChange(); Check(UnityEngine.Mathf.Abs(env.points[1].time-t)<.001f && UnityEngine.Mathf.Abs(env.points[1].value-v)<.001f,"envelope Undo rebinds live data");
var toggle=UnityEngine.UIElements.UQueryExtensions.Q<Laubrary.Zui.ZuiToggleButton>(root);
bool enabled=toggle.value;
UnityEditor.Undo.IncrementCurrentGroup();
Pointer(toggle,UnityEngine.EventType.MouseDown,toggle.layout.width/2,toggle.layout.height/2);
Pointer(toggle,UnityEngine.EventType.MouseUp,toggle.layout.width/2,toggle.layout.height/2);
Check(toggle.value!=enabled,"toggle click updates fixture and preview"); UndoChange(); Check(toggle.value==enabled,"toggle Undo");
log.AppendLine("TOTAL "+passed+" passed");
return log.ToString();
