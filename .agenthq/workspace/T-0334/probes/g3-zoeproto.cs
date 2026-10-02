var sb = new System.Text.StringBuilder();
sb.Append(ZBind("ZoeWindow", "Assets/Demos/ProtoGuyDemo/ProtoGuy.asset")).Append("\n");
sb.Append(ZBind("ChunkWindow", "Assets/Chunks/WallDebris.asset")).Append("\n");
return sb.ToString();
