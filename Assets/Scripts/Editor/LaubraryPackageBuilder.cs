using System.IO;
using UnityEditor;
using UnityEngine;

public class LaubraryPackageBuilder
{
    private const string DEMOS_SOURCE_PATH = "Assets/Demos";
    private const string DEMOS_TARGET_PATH = "Assets/Packages/Laubrary/Samples~/Demos";
    
    private const string SIMPLEMENU_UI_SOURCE_PATH = "Assets/Samples/SimpleMenu UI";
    private const string SIMPLEMENU_UI_TARGET_PATH = "Assets/Packages/Laubrary/Samples~/SimpleMenu UI";

    private const string SIMPLEUI_SOURCE_PATH = "Assets/Samples/SimpleUI";
    private const string SIMPLEUI_TARGET_PATH = "Assets/Packages/Laubrary/Samples~/SimpleUI";

    [MenuItem("Laubrary Dev/Copy Demos to Package")]
    public static void CopyDemosToPackage()
    {
        if (!Directory.Exists(DEMOS_SOURCE_PATH))
        {
            EditorUtility.DisplayDialog(
                "Source Not Found",
                $"The source directory '{DEMOS_SOURCE_PATH}' does not exist.",
                "OK"
            );
            return;
        }

        bool proceed = EditorUtility.DisplayDialog(
            "Copy Demos to Package",
            $"This will copy all demos from:\n\n{DEMOS_SOURCE_PATH}\n\nto:\n\n{DEMOS_TARGET_PATH}\n\nAny existing content in the target will be replaced.\n\nContinue?",
            "Copy",
            "Cancel"
        );

        if (!proceed)
        {
            return;
        }

        try
        {
            if (Directory.Exists(DEMOS_TARGET_PATH))
            {
                Directory.Delete(DEMOS_TARGET_PATH, true);
            }

            CopyDirectory(DEMOS_SOURCE_PATH, DEMOS_TARGET_PATH);

            EditorUtility.DisplayDialog(
                "Success",
                $"Demos copied successfully!\n\n{GetFileCount(DEMOS_TARGET_PATH)} files copied to:\n{DEMOS_TARGET_PATH}\n\nDon't forget to commit these changes to the package repository.",
                "OK"
            );

            Debug.Log($"[Laubrary Package Builder] Demos copied to {DEMOS_TARGET_PATH}");
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog(
                "Error",
                $"Failed to copy demos:\n\n{e.Message}",
                "OK"
            );
            Debug.LogError($"[Laubrary Package Builder] Error: {e}");
        }
    }
    
    [MenuItem("Laubrary Dev/Copy SimpleMenu UI to Package")]
    public static void CopySimpleMenuUIToPackage()
    {
        if (!Directory.Exists(SIMPLEMENU_UI_SOURCE_PATH))
        {
            EditorUtility.DisplayDialog(
                "Source Not Found",
                $"The source directory '{SIMPLEMENU_UI_SOURCE_PATH}' does not exist.\n\nPlease create this folder and place the SimpleMenu UI prefabs and settings there.",
                "OK"
            );
            return;
        }

        bool proceed = EditorUtility.DisplayDialog(
            "Copy SimpleMenu UI to Package",
            $"This will copy SimpleMenu UI assets from:\n\n{SIMPLEMENU_UI_SOURCE_PATH}\n\nto:\n\n{SIMPLEMENU_UI_TARGET_PATH}\n\nAny existing content in the target will be replaced.\n\nContinue?",
            "Copy",
            "Cancel"
        );

        if (!proceed)
        {
            return;
        }

        try
        {
            if (Directory.Exists(SIMPLEMENU_UI_TARGET_PATH))
            {
                Directory.Delete(SIMPLEMENU_UI_TARGET_PATH, true);
            }

            CopyDirectory(SIMPLEMENU_UI_SOURCE_PATH, SIMPLEMENU_UI_TARGET_PATH);

            EditorUtility.DisplayDialog(
                "Success",
                $"SimpleMenu UI copied successfully!\n\n{GetFileCount(SIMPLEMENU_UI_TARGET_PATH)} files copied to:\n{SIMPLEMENU_UI_TARGET_PATH}\n\nDon't forget to commit these changes to the package repository.",
                "OK"
            );

            Debug.Log($"[Laubrary Package Builder] SimpleMenu UI copied to {SIMPLEMENU_UI_TARGET_PATH}");
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog(
                "Error",
                $"Failed to copy SimpleMenu UI:\n\n{e.Message}",
                "OK"
            );
            Debug.LogError($"[Laubrary Package Builder] Error: {e}");
        }
    }
    
    [MenuItem("Laubrary Dev/Copy All Samples to Package")]
    public static void CopyAllSamplesToPackage()
    {
        bool proceed = EditorUtility.DisplayDialog(
            "Copy All Samples to Package",
            "This will copy:\n\n1. Demos → Samples~/Demos\n2. SimpleMenu UI → Samples~/SimpleMenu UI\n3. SimpleUI → Samples~/SimpleUI\n\nContinue?",
            "Copy All",
            "Cancel"
        );

        if (!proceed)
        {
            return;
        }

        int totalFiles = 0;
        
        try
        {
            if (Directory.Exists(DEMOS_SOURCE_PATH))
            {
                if (Directory.Exists(DEMOS_TARGET_PATH))
                {
                    Directory.Delete(DEMOS_TARGET_PATH, true);
                }
                CopyDirectory(DEMOS_SOURCE_PATH, DEMOS_TARGET_PATH);
                totalFiles += GetFileCount(DEMOS_TARGET_PATH);
                Debug.Log($"[Laubrary Package Builder] Demos copied to {DEMOS_TARGET_PATH}");
            }
            
            if (Directory.Exists(SIMPLEMENU_UI_SOURCE_PATH))
            {
                if (Directory.Exists(SIMPLEMENU_UI_TARGET_PATH))
                {
                    Directory.Delete(SIMPLEMENU_UI_TARGET_PATH, true);
                }
                CopyDirectory(SIMPLEMENU_UI_SOURCE_PATH, SIMPLEMENU_UI_TARGET_PATH);
                totalFiles += GetFileCount(SIMPLEMENU_UI_TARGET_PATH);
                Debug.Log($"[Laubrary Package Builder] SimpleMenu UI copied to {SIMPLEMENU_UI_TARGET_PATH}");
            }

            if (Directory.Exists(SIMPLEUI_SOURCE_PATH))
            {
                if (Directory.Exists(SIMPLEUI_TARGET_PATH))
                {
                    Directory.Delete(SIMPLEUI_TARGET_PATH, true);
                }
                CopyDirectory(SIMPLEUI_SOURCE_PATH, SIMPLEUI_TARGET_PATH);
                totalFiles += GetFileCount(SIMPLEUI_TARGET_PATH);
                Debug.Log($"[Laubrary Package Builder] SimpleUI copied to {SIMPLEUI_TARGET_PATH}");
            }

            EditorUtility.DisplayDialog(
                "Success",
                $"All samples copied successfully!\n\n{totalFiles} total files copied.\n\nDon't forget to commit these changes to the package repository.",
                "OK"
            );
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog(
                "Error",
                $"Failed to copy samples:\n\n{e.Message}",
                "OK"
            );
            Debug.LogError($"[Laubrary Package Builder] Error: {e}");
        }
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string fileName = Path.GetFileName(file);
            string targetFile = Path.Combine(targetDir, fileName);
            File.Copy(file, targetFile, true);
        }

        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            string dirName = Path.GetFileName(subDir);
            string targetSubDir = Path.Combine(targetDir, dirName);
            CopyDirectory(subDir, targetSubDir);
        }
    }

    private static int GetFileCount(string directory)
    {
        int count = Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length;
        return count;
    }
}
