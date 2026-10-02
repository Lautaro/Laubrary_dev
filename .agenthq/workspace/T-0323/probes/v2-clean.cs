var sb=new System.Text.StringBuilder();
string[] paths = new string[]{
 "Assets/Shaper/AuditT323Fx.asset","Assets/Shaper/AuditT323Lathe 1.asset","Assets/Shaper/AuditT323Lathe.asset",
 "Assets/Shaper/AuditT323Mirage.asset","Assets/Shaper/AuditT323Pyre.asset","Assets/Shaper/AuditT323Splash.asset",
 "Assets/Shaper/AuditT323Ware.asset","Assets/Shaper/AuditT323Zoe.asset","Assets/Shaper/AuditT323Zoe.states.cs",
 "Assets/Shaper/Can_-2069483825_dmg0.png","Assets/Shaper/Can_-2069483825_dmg1.png","Assets/Shaper/Can_-2069483825_dmg2.png",
 "Assets/Shaper/Variations","Assets/Shaper/Audit0277",
 "Assets/SpriteFx/AuditT323Walk1.asset","Assets/SpriteFx/AuditT323Walk3.asset",
 "Assets/Mirage/AuditT323Walk2.asset","Assets/Mirage/AuditT323Walk4.asset"};
foreach (var p in paths) sb.Append(UnityEditor.AssetDatabase.DeleteAsset(p)?"del ":"MISS ").Append(p).Append("\n");
UnityEditor.AssetDatabase.Refresh();
return sb.ToString();
