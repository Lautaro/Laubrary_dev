// A19 — a three-layer document: layer A is masked by layer B (a pure mask), layer C hosts a Pyre form.
// Built through the window's own NewLayer, bound in the window, and measured frame by frame.
var SB = new System.Text.StringBuilder();
var BFs = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
var WT = typeof(Laubrary.Shaper.Editor.ShaperWindow);
var newLayerM = WT.GetMethod("NewLayer", BFs);

string DIR = "Assets/Shaper/AuditA19";
if (!UnityEditor.AssetDatabase.IsValidFolder(DIR)) UnityEditor.AssetDatabase.CreateFolder("Assets/Shaper", "AuditA19");
string path = DIR + "/A19Mask.asset";
UnityEditor.AssetDatabase.DeleteAsset(path);

var d = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
d.canvasWidth = 96; d.canvasHeight = 64; d.frameCount = 8; d.seed = 11u;
d.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });

var A = (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[] { "Painted", d });
A.root.primitive.kind = Laubrary.Shaper.ShaperPrimitiveKind.Rect;
A.root.primitive.rectHalfWDial = new ZUIValue(34f);
A.root.primitive.rectHalfHDial = new ZUIValue(22f);
d.layers.Add(A);

var B = (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[] { "Stencil", d });
B.root.primitive.kind = Laubrary.Shaper.ShaperPrimitiveKind.Ellipse;
B.root.primitive.ellipseRxDial = new ZUIValue(20f);
B.root.primitive.ellipseRyDial = new ZUIValue(20f);
B.contributesToPicture = false;                       // a PURE mask
d.layers.Add(B);

var C = (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[] { "Generator", d });
C.root.kind = Laubrary.Shaper.ShaperNodeKind.Composite;
C.root.composite.source = new Laubrary.PyreShaper.PyreFormCompositeSource { form = new Laubrary.Pyre.Forms.Kiln.ArcBurstForm() };
C.root.composite.halfExtentX = 48f; C.root.composite.halfExtentY = 32f;
C.root.composite.bakeWidth = 96; C.root.composite.bakeHeight = 64;
d.layers.Add(C);

System.Func<int, UnityEngine.Color32[]> R = f => Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(d, f);
System.Func<UnityEngine.Color32[], int> Lit = px => { int n = 0; foreach (var c in px) if (c.a > 0) n++; return n; };
var noMask = R(0); int litNoMask = Lit(noMask);

// point A's mask at B, by the id the document itself allocates — never by name or index
int bid = d.IdOf(B);
A.mask.sourceLayerId = bid;
A.mask.mode = Laubrary.Shaper.ShaperMaskMode.Clip;
A.mask.quantity = Laubrary.Shaper.ShaperMaskQuantity.Coverage;
var masked = R(0); int litMasked = Lit(masked);
int diffMask = 0; for (int i = 0; i < noMask.Length; i++) if (!noMask[i].Equals(masked[i])) diffMask++;
SB.Append("mask source id=").Append(bid)
  .Append("  lit unmasked=").Append(litNoMask).Append("  lit masked=").Append(litMasked)
  .Append("  changed=").Append(diffMask).Append('\n');

A.mask.invert = true; int litInv = Lit(R(0)); A.mask.invert = false;
SB.Append("invert -> lit=").Append(litInv).Append('\n');
B.contributesToPicture = true; int litDrawn = Lit(R(0)); B.contributesToPicture = false;
SB.Append("stencil drawn into picture -> lit=").Append(litDrawn)
  .Append(" (pure mask keeps it out: ").Append(litDrawn != litMasked).Append(")\n");

SB.Append("per frame (lit): ");
for (int f = 0; f < d.frameCount; f++) SB.Append(Lit(R(f))).Append(f == d.frameCount - 1 ? "\n" : ", ");

UnityEditor.AssetDatabase.CreateAsset(d, path);
UnityEditor.AssetDatabase.SaveAssetIfDirty(d);
var reload = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>(path);
int litSaved = Lit(Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(reload, 0));
SB.Append("saved+reloaded lit=").Append(litSaved).Append(" (round trip identical: ").Append(litSaved == litMasked).Append(")\n");

var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
System.Reflection.MethodInfo setAssetM = null;
for (var t = WT; t != null; t = t.BaseType) { setAssetM = t.GetMethod("SetAsset", BFi); if (setAssetM != null) break; }
setAssetM.Invoke(win, new object[] { reload });
System.Reflection.FieldInfo selF = null;
for (var t = WT; t != null; t = t.BaseType) { selF = t.GetField("selectedLayer", BFi); if (selF != null) break; }
selF.SetValue(win, 0);
System.Reflection.MethodInfo rebuildM = null;
for (var t = WT; t != null; t = t.BaseType) { rebuildM = t.GetMethod("Rebuild", BFi, null, System.Type.EmptyTypes, null); if (rebuildM != null) break; }
rebuildM?.Invoke(win, null);
Laubrary.Zui.ZuiAudit.ExpandAll(win);
win.Repaint();
SB.Append("window bound to the saved 3-layer document, layer 1 selected\n");
return SB.ToString();
