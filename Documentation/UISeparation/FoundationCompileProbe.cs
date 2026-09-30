if (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating) throw new System.InvalidOperationException("Editor still compiling/importing");
var curve = typeof(Laubrary.Zui.Z).GetMethod("Curve");
if ((float)curve.GetParameters()[3].DefaultValue != -1f) throw new System.InvalidOperationException("Old factory assembly loaded");
if (typeof(Laubrary.Zui.ZuiPresentationContext).GetMethod("Capture").GetParameters().Length != 2) throw new System.InvalidOperationException("Old context assembly loaded");
return "Current factory/context assemblies loaded; consumer reference type=" + typeof(Laubrary.UISeparationConsumerBaseline.FrozenBackSplashWindow).FullName + "; editor idle; project=" + UnityEngine.Application.dataPath;
