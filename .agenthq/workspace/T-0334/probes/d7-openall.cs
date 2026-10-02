foreach (var n in new string[]{"ShaperWindow","PyreWindow","LatheWindow","LarderWindow","SpriteFxStackWindow","TextSplashWindow","ZoeWindow","MirageWindow","ChunkWindow","CartographerWindow","BackSplashWindow","ChoreographerWindow","TapestryWindow","LatheMoldWindow","WeaponDefWindow","AmmoDefWindow","LauminaryBrowserWindow","SpriteCatalogWindow","LauminationBuilderWindow"})
{ var w = ZOpen(n); if (w != null) w.position = new UnityEngine.Rect(40, 20, 900, 880); }
return "opened";
