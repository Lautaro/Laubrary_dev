int checkedCount=0;
foreach(var w in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.UISeparationPilot.PilotWindow>()) {
    var root=w.rootVisualElement;
    UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.VisualElement>(root).ForEach(e=> {
        if(e is Laubrary.Zui.ZuiSkinBandSliders b) { b.SetValues(null); checkedCount++; }
        else if(e is Laubrary.Zui.PilotBaseline.ZuiSkinBandSliders bb) { bb.SetValues(null); checkedCount++; }
        else if(e is Laubrary.Zui.ZuiSkinEnvelope env) { env.points=new System.Collections.Generic.List<ZUIEnvelopePoint>(); env.Repaint(); checkedCount++; }
        else if(e is Laubrary.Zui.PilotBaseline.ZuiSkinEnvelope old) { old.points=new System.Collections.Generic.List<ZUIEnvelopePoint>(); old.Repaint(); checkedCount++; }
    });
}
if(checkedCount!=4) throw new System.Exception("Missing empty-state fixtures");
return "Empty band and envelope views prepared in both windows; shared fixture unchanged";
