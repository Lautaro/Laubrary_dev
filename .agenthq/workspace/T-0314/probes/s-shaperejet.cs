// T-0314 — a SCRATCH Shaper document whose single layer hosts an ExplosiveJetForm, so the same compact
// list row can be measured in its OTHER host (Shaper's PyreFormShaperUI drawer, which reproduces the
// window's reflected dump over the form).  Created at Assets/Shaper/AuditT0314.asset and deleted, with
// its .meta, through AssetDatabase.DeleteAsset at the end of the task.  Never touches a committed asset.
const string path = "Assets/Shaper/AuditT0314.asset";
var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>(path);
if (doc == null)
{
    System.IO.Directory.CreateDirectory("Assets/Shaper");
    doc = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
    doc.layers.Clear();
    doc.layers.Add(new Laubrary.Shaper.ShaperLayer { name = "Hosted blast" });
    UnityEditor.AssetDatabase.CreateAsset(doc, path);
}
var lay = doc.layers[0];
lay.root = new Laubrary.Shaper.ShaperNode
{
    kind = Laubrary.Shaper.ShaperNodeKind.Composite,
    composite = new Laubrary.Shaper.ShaperCompositeDef
    {
        source = new Laubrary.PyreShaper.PyreFormCompositeSource
        {
            form = new Laubrary.Pyre.Forms.Kiln.ExplosiveJetForm()
        }
    }
};
UnityEditor.AssetDatabase.SaveAssets();
return "scratch doc ready: " + path + " layers=" + doc.layers.Count
     + " node=" + lay.root.kind + " source=" + lay.root.composite.source.GetType().Name;
