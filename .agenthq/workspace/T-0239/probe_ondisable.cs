var go = new UnityEngine.GameObject("probe_follow");
var f = go.AddComponent<Laubrary.Zoetrope.FxFollowTarget>();
int applied = 0;
f.Init(() => new Laubrary.Zoetrope.FxPose(UnityEngine.Vector3.one, 0f, false), p => applied++);
var late = typeof(Laubrary.Zoetrope.FxFollowTarget).GetMethod("LateUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
late.Invoke(f, null);
int afterInit = applied;
go.SetActive(false);
late.Invoke(f, null);
int afterDisable = applied;
// Does the ENGINE call OnDisable in edit mode at all? Check with a direct call for comparison.
var od = typeof(Laubrary.Zoetrope.FxFollowTarget).GetMethod("OnDisable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
go.SetActive(true);
f.Init(() => new Laubrary.Zoetrope.FxPose(UnityEngine.Vector3.one, 0f, false), p => applied++);
od.Invoke(f, null);
late.Invoke(f, null);
int afterManualOnDisable = applied;
UnityEngine.Object.DestroyImmediate(go);
return "applyAfterInit=" + afterInit + " (want 1); applyAfterSetActiveFalse=" + afterDisable
     + " (want 1 if engine fired OnDisable, 2 if it did not); applyAfterManualOnDisable=" + afterManualOnDisable
     + " (want same as previous => Stop() itself works)";
