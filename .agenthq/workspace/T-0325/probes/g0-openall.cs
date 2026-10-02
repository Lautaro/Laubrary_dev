// Open every Laubrary tool window worth sweeping, bound to a real asset where one exists.
var sb = new System.Text.StringBuilder();
string[] wins = { "ZoeWindow","MirageWindow","ShaperWindow","PyreWindow","ChunkWindow","LatheWindow",
                  "LarderWindow","SpriteFxStackWindow","TextSplashWindow","CartographerWindow","PropWindow",
                  "TilesetBuilderWindow","LauminaryBrowserWindow","LauminationBuilderWindow","TapestryWindow",
                  "RulesEditorWindow","LazorWindow","ChoreographerWindow","LatheMoldWindow","SpriteCatalogWindow" };
foreach (var n in wins)
{
    var t = ZType(n);
    if (t == null) { sb.Append(n).Append(": no type\n"); continue; }
    var w = ZOpen(n);
    sb.Append(n).Append(": ").Append(w == null ? "could not open" : "open").Append("\n");
}
return sb.ToString();
