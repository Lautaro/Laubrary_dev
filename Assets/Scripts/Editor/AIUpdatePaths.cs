using System.Text;
using AssetInventory;

// Repoint all Asset Inventory references from the old bracketed folder "[GAMEDEV ASSETS]" to the renamed
// bracket-free "GAMEDEV ASSETS". Replaces only that exact substring, so the inner "[UNITY ASSET INVENTORY DB]"
// name (used by excludedDirectories) is untouched. Done via the config API + SaveConfig so the running
// plugin can't overwrite it.
public static class AIUpdatePaths
{
    const string OldName = "[GAMEDEV ASSETS]";
    const string NewName = "GAMEDEV ASSETS";

    static string Fix(string s) => string.IsNullOrEmpty(s) ? s : s.Replace(OldName, NewName);

    public static string Execute()
    {
        var c = AI.Config;
        var sb = new StringBuilder();

        for (int i = 0; i < c.folders.Count; i++)
        {
            string before = c.folders[i].location;
            c.folders[i].location = Fix(before);
            sb.Append($"folder[{i}]: '{before}' -> '{c.folders[i].location}' | ");
        }
        string bb = c.backupFolder, cb = c.cacheFolder, pb = c.previewFolder;
        c.backupFolder  = Fix(bb);
        c.cacheFolder   = Fix(cb);
        c.previewFolder = Fix(pb);
        sb.Append($"backup: '{bb}' -> '{c.backupFolder}' | ");
        sb.Append($"cache: '{cb}' -> '{c.cacheFolder}' | ");
        sb.Append($"preview: '{pb}' -> '{c.previewFolder}'");

        AI.SaveConfig();
        return sb.ToString();
    }
}
