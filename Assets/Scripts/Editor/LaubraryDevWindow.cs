using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class LaubraryDevWindow : EditorWindow
{
    private const string DEMOS_SOURCE = "Assets/Demos";
    private const string DEMOS_TARGET = "Assets/Packages/Laubrary/Samples~/Demos";
    private const string SIMPLEMENU_UI_SOURCE = "Assets/SimpleMenu UI";
    private const string SIMPLEMENU_UI_TARGET = "Assets/Packages/Laubrary/Samples~/SimpleMenu UI";
    private const string PACKAGE_JSON = "Assets/Packages/Laubrary/package.json";
    private const string CHANGELOG = "Assets/Packages/Laubrary/CHANGELOG.md";
    private const string PACKAGE_PATH = "Assets/Packages/Laubrary";
    private const string MAIN_BRANCH = "master";
    private const string RELEASE_BRANCH = "release";

    private const string HELP_FIRST_RELEASE = 
        "FIRST RELEASE WORKFLOW\n\n" +
        "1. git checkout master\n   → Switch to development branch\n\n" +
        "2. git add .\n   → Stage all changes\n\n" +
        "3. git commit -m \"...\"\n   → Commit changes to master\n\n" +
        "4. git push origin master\n   → Push master branch to remote\n\n" +
        "5. git checkout --orphan release\n   → Create new branch with NO history\n\n" +
        "6. git add .\n   → Stage all package files\n\n" +
        "7. git commit -m \"Release vX.X.X\"\n   → Create first release commit\n\n" +
        "8. git tag -a vX.X.X -m \"...\"\n   → Tag this version\n\n" +
        "9. git push origin release\n   → Push release branch\n\n" +
        "10. git push origin vX.X.X\n   → Push version tag\n\n" +
        "11. git checkout master\n   → Return to development branch";

    private const string HELP_SUBSEQUENT_RELEASE = 
        "SUBSEQUENT RELEASE WORKFLOW\n\n" +
        "1. git checkout master\n   → Switch to development branch\n\n" +
        "2. git add .\n   → Stage all changes\n\n" +
        "3. git commit -m \"...\"\n   → Commit changes to master\n\n" +
        "4. git push origin master\n   → Push master branch to remote\n\n" +
        "5. git checkout release\n   → Switch to release branch\n\n" +
        "6. git merge --squash master --allow-unrelated-histories -X theirs\n   → Combine all master commits into one\n   → Allow merging unrelated histories (orphan branch)\n   → Auto-resolve conflicts using master's version\n\n" +
        "7. git commit -m \"Release vX.X.X\"\n   → Create release commit\n\n" +
        "8. git tag -a vX.X.X -m \"...\"\n   → Tag this version\n\n" +
        "9. git push origin release\n   → Push release branch\n\n" +
        "10. git push origin vX.X.X\n   → Push version tag\n\n" +
        "11. git checkout master\n   → Return to development branch";

    private const string HELP_DELETE_RELEASE = 
        "DELETE RELEASE & TAGS (RESET)\n\n" +
        "⚠️ WARNING: This deletes your release branch and ALL tags!\n\n" +
        "1. git checkout master\n   → Switch to safe branch\n\n" +
        "2. git branch -D release\n   → Force delete local release branch\n\n" +
        "3. git push origin --delete release\n   → Delete remote release branch\n\n" +
        "4. for /f %i in ('git tag') do git tag -d %i\n   → Delete all local tags (Windows CMD only)\n   → Run this in CMD, not PowerShell\n\n" +
        "5. git ls-remote --tags origin\n   → List all remote tags\n   → Then manually delete each with:\n   → git push origin :refs/tags/v0.1.0\n   → git push origin :refs/tags/v0.1.1\n   → etc.\n\n" +
        "After this you can create a fresh release branch.";

    private bool copyDemos = false;
    private bool copySimpleMenuUI = false;
    private bool createRelease = false;

    private string versionNumber = "";
    private string commitMessage = "";
    private Vector2 scrollPosition;
    
    private string firstReleaseCommands = "";
    private string subsequentReleaseCommands = "";
    private string deleteReleaseCommands = "";
    
    private Vector2 firstReleaseScrollPos;
    private Vector2 subsequentReleaseScrollPos;
    private Vector2 deleteReleaseScrollPos;
    
    private bool showFirstRelease = false;
    private bool showSubsequentRelease = true;
    private bool showDeleteRelease = false;

    [MenuItem("Window/Laubrary Dev")]
    public static void ShowWindow()
    {
        var window = GetWindow<LaubraryDevWindow>("Laubrary Dev");
        window.minSize = new Vector2(500, 600);
    }

    private void OnEnable()
    {
        createRelease = false;
        versionNumber = GetSuggestedVersion();
        commitMessage = "";
        UpdateGitCommandsPreview();
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        DrawHeader();
        EditorGUILayout.Space(10);

        DrawCopySection();
        EditorGUILayout.Space(10);

        DrawReleaseSection();

        EditorGUILayout.EndScrollView();
    }

    private void DrawHeader()
    {
        EditorGUILayout.LabelField("Laubrary Package Builder", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Manage package samples and releases", EditorStyles.miniLabel);
        EditorGUILayout.Space(5);
        EditorGUILayout.HelpBox(
            "Select operations to perform. Copy operations update the Samples~ folder. " +
            "Create Release automates git operations for a clean release workflow.",
            MessageType.Info
        );
    }

    private void DrawCopySection()
    {
        EditorGUILayout.LabelField("Copy Operations", EditorStyles.boldLabel);

        copyDemos = EditorGUILayout.ToggleLeft(
            "Copy Demos to Package Output",
            copyDemos
        );
        EditorGUI.indentLevel++;
        EditorGUILayout.LabelField($"Source: {DEMOS_SOURCE}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Target: {DEMOS_TARGET}", EditorStyles.miniLabel);
        EditorGUI.indentLevel--;

        EditorGUILayout.Space(5);

        copySimpleMenuUI = EditorGUILayout.ToggleLeft(
            "Copy SimpleMenu UI to Package Output",
            copySimpleMenuUI
        );
        EditorGUI.indentLevel++;
        EditorGUILayout.LabelField($"Source: {SIMPLEMENU_UI_SOURCE}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Target: {SIMPLEMENU_UI_TARGET}", EditorStyles.miniLabel);
        EditorGUI.indentLevel--;

        EditorGUILayout.Space(10);

        if (GUILayout.Button("Execute Copy Operations", GUILayout.Height(30)))
        {
            ExecuteCopyOperations();
        }
    }

    private void DrawReleaseSection()
    {
        EditorGUILayout.LabelField("Release Preparation", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        createRelease = EditorGUILayout.ToggleLeft(
            "Enable Release Mode",
            createRelease
        );
        if (EditorGUI.EndChangeCheck())
        {
            UpdateGitCommandsPreview();
        }

        if (!createRelease) return;

        EditorGUILayout.Space(5);
        
        DrawReleaseOptions();
        EditorGUILayout.Space(10);
        
        if (GUILayout.Button("Prepare Release Files (Update package.json & CHANGELOG.md)", GUILayout.Height(30)))
        {
            PrepareReleaseFiles();
        }
        
        EditorGUILayout.Space(10);
        DrawGitCommandsPreview();
    }

    private void DrawReleaseOptions()
    {
        EditorGUILayout.LabelField("Release Configuration", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Version Number:", GUILayout.Width(120));
        EditorGUI.BeginChangeCheck();
        versionNumber = EditorGUILayout.TextField(versionNumber);
        if (EditorGUI.EndChangeCheck())
        {
            UpdateGitCommandsPreview();
        }
        if (GUILayout.Button("Suggest", GUILayout.Width(70)))
        {
            versionNumber = GetSuggestedVersion();
            UpdateGitCommandsPreview();
        }
        EditorGUILayout.EndHorizontal();

        string currentVersion = GetCurrentVersion();
        EditorGUILayout.LabelField($"Current version: {currentVersion}", EditorStyles.miniLabel);

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("Commit Message:", EditorStyles.label);
        EditorGUI.BeginChangeCheck();
        commitMessage = EditorGUILayout.TextArea(
            commitMessage,
            GUILayout.Height(80)
        );
        if (EditorGUI.EndChangeCheck())
        {
            UpdateGitCommandsPreview();
        }

        EditorGUILayout.HelpBox(
            "This message will be used for both the main branch commit and the release commit.",
            MessageType.Info
        );
    }

    private void DrawGitCommandsPreview()
    {
        EditorGUILayout.LabelField("Git Commands", EditorStyles.boldLabel);
        
        if (GUILayout.Button("📂 Open Terminal Here", GUILayout.Height(30)))
        {
            OpenTerminal();
        }
        
        EditorGUILayout.Space(10);
        
        showSubsequentRelease = EditorGUILayout.BeginFoldoutHeaderGroup(showSubsequentRelease, "Subsequent Release (Release branch exists)");
        if (showSubsequentRelease)
        {
            DrawCommandBlock(subsequentReleaseCommands, ref subsequentReleaseScrollPos, HELP_SUBSEQUENT_RELEASE);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
        
        EditorGUILayout.Space(5);
        
        showFirstRelease = EditorGUILayout.BeginFoldoutHeaderGroup(showFirstRelease, "First Release (No release branch yet)");
        if (showFirstRelease)
        {
            DrawCommandBlock(firstReleaseCommands, ref firstReleaseScrollPos, HELP_FIRST_RELEASE);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
        
        EditorGUILayout.Space(5);
        
        showDeleteRelease = EditorGUILayout.BeginFoldoutHeaderGroup(showDeleteRelease, "Delete Release Branch & Tags");
        if (showDeleteRelease)
        {
            DrawCommandBlock(deleteReleaseCommands, ref deleteReleaseScrollPos, HELP_DELETE_RELEASE);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    private void DrawCommandBlock(string commands, ref Vector2 scrollPos, string helpText = "")
    {
        if (string.IsNullOrEmpty(commands)) return;
        
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        EditorGUILayout.BeginHorizontal();
        
        if (!string.IsNullOrEmpty(helpText) && GUILayout.Button("❓", GUILayout.Width(30), GUILayout.Height(25)))
        {
            EditorUtility.DisplayDialog("Command Explanation", helpText, "OK");
        }
        
        if (GUILayout.Button("📋 Copy All Commands", GUILayout.Height(25)))
        {
            EditorGUIUtility.systemCopyBuffer = commands;
            Debug.Log("[Laubrary Dev] All commands copied to clipboard!");
        }
        
        EditorGUILayout.EndHorizontal();
        
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(250));
        EditorGUILayout.TextArea(commands, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
        
        EditorGUILayout.EndVertical();
    }

    private void ExecuteCopyOperations()
    {
        if (!copyDemos && !copySimpleMenuUI)
        {
            EditorUtility.DisplayDialog("Info", "No copy operations selected.", "OK");
            return;
        }

        try
        {
            if (copyDemos) CopyDemos();
            if (copySimpleMenuUI) CopySimpleMenuUI();

            var sb = new StringBuilder();
            sb.AppendLine("Copy operations completed successfully!\n");
            if (copyDemos) sb.AppendLine("✓ Copied Demos to package");
            if (copySimpleMenuUI) sb.AppendLine("✓ Copied SimpleMenu UI to package");

            EditorUtility.DisplayDialog("Success", sb.ToString(), "OK");
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("Error", $"Copy operation failed:\n\n{e.Message}", "OK");
            Debug.LogError($"[Laubrary Dev] {e}");
        }
    }

    private void PrepareReleaseFiles()
    {
        if (string.IsNullOrWhiteSpace(versionNumber))
        {
            EditorUtility.DisplayDialog("Error", "Version number is required.", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(commitMessage))
        {
            EditorUtility.DisplayDialog("Error", "Commit message is required.", "OK");
            return;
        }

        bool confirm = EditorUtility.DisplayDialog(
            "Confirm Release Preparation",
            $"Prepare release v{versionNumber}?\n\n" +
            $"This will:\n" +
            $"1. Update package.json to v{versionNumber}\n" +
            $"2. Update CHANGELOG.md with release notes\n\n" +
            $"Continue?",
            "Yes, Prepare",
            "Cancel"
        );

        if (!confirm) return;

        try
        {
            Debug.Log("[Laubrary Dev] Preparing release...");
            
            Debug.Log("[Laubrary Dev] Updating package.json...");
            UpdatePackageVersion(versionNumber);
            
            Debug.Log("[Laubrary Dev] Updating CHANGELOG.md...");
            UpdateChangelog(versionNumber, commitMessage);

            Debug.Log($"[Laubrary Dev] Release v{versionNumber} files prepared!");
            
            EditorUtility.DisplayDialog(
                "Success", 
                $"Release v{versionNumber} files prepared!\n\n" +
                $"✓ Updated package.json\n" +
                $"✓ Updated CHANGELOG.md\n\n" +
                $"Next: Copy git commands and run in terminal.",
                "OK"
            );
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("Error", $"Preparation failed:\n\n{e.Message}", "OK");
            Debug.LogError($"[Laubrary Dev] {e}");
        }
    }

    private void CopyDemos()
    {
        if (!Directory.Exists(DEMOS_SOURCE))
        {
            throw new Exception($"Source directory not found: {DEMOS_SOURCE}");
        }

        if (Directory.Exists(DEMOS_TARGET))
        {
            Directory.Delete(DEMOS_TARGET, true);
        }

        CopyDirectory(DEMOS_SOURCE, DEMOS_TARGET);
        UnityEngine.Debug.Log($"[Laubrary Dev] Copied demos: {DEMOS_SOURCE} → {DEMOS_TARGET}");
    }

    private void CopySimpleMenuUI()
    {
        if (!Directory.Exists(SIMPLEMENU_UI_SOURCE))
        {
            throw new Exception($"Source directory not found: {SIMPLEMENU_UI_SOURCE}");
        }

        if (Directory.Exists(SIMPLEMENU_UI_TARGET))
        {
            Directory.Delete(SIMPLEMENU_UI_TARGET, true);
        }

        CopyDirectory(SIMPLEMENU_UI_SOURCE, SIMPLEMENU_UI_TARGET);
        UnityEngine.Debug.Log($"[Laubrary Dev] Copied SimpleMenu UI: {SIMPLEMENU_UI_SOURCE} → {SIMPLEMENU_UI_TARGET}");
    }

    private void PrepareRelease()
    {
        Debug.Log("[Laubrary Dev] Preparing release...");
        
        Debug.Log("[Laubrary Dev] Updating package.json...");
        UpdatePackageVersion(versionNumber);
        
        Debug.Log("[Laubrary Dev] Updating CHANGELOG.md...");
        UpdateChangelog(versionNumber, commitMessage);

        Debug.Log($"[Laubrary Dev] Release v{versionNumber} files prepared!");
        Debug.Log("[Laubrary Dev] Copy the git commands and run them in your terminal.");
    }

    private void OpenTerminal()
    {
        string packagePath = Path.Combine(Directory.GetCurrentDirectory(), PACKAGE_PATH);
        
        try
        {
            #if UNITY_EDITOR_WIN
            Process.Start("cmd.exe", $"/K cd /d \"{packagePath}\"");
            Debug.Log($"[Laubrary Dev] Opened terminal at: {packagePath}");
            #elif UNITY_EDITOR_OSX
            Process.Start("open", $"-a Terminal \"{packagePath}\"");
            Debug.Log($"[Laubrary Dev] Opened terminal at: {packagePath}");
            #elif UNITY_EDITOR_LINUX
            Process.Start("x-terminal-emulator", $"--working-directory=\"{packagePath}\"");
            Debug.Log($"[Laubrary Dev] Opened terminal at: {packagePath}");
            #endif
        }
        catch (Exception e)
        {
            Debug.LogError($"[Laubrary Dev] Failed to open terminal: {e.Message}");
            EditorUtility.DisplayDialog("Error", $"Failed to open terminal:\n\n{e.Message}", "OK");
        }
    }

    private string GetCurrentVersion()
    {
        if (!File.Exists(PACKAGE_JSON))
            return "0.0.0";

        string json = File.ReadAllText(PACKAGE_JSON);
        var match = Regex.Match(json, @"""version""\s*:\s*""([^""]+)""");
        return match.Success ? match.Groups[1].Value : "0.0.0";
    }

    private string GetSuggestedVersion()
    {
        string current = GetCurrentVersion();
        var parts = current.Split('.');
        
        if (parts.Length != 3)
            return "1.0.0";

        int major = int.Parse(parts[0]);
        int minor = int.Parse(parts[1]);
        int patch = int.Parse(parts[2]);

        return $"{major}.{minor}.{patch + 1}";
    }

    private void UpdatePackageVersion(string newVersion)
    {
        string json = File.ReadAllText(PACKAGE_JSON);
        json = Regex.Replace(
            json,
            @"""version""\s*:\s*""[^""]+""",
            $"\"version\": \"{newVersion}\""
        );
        File.WriteAllText(PACKAGE_JSON, json);
        AssetDatabase.Refresh();
    }

    private void UpdateChangelog(string version, string message)
    {
        string date = DateTime.Now.ToString("yyyy-MM-dd");
        string newEntry = BuildChangelogEntry(version, date, message);

        string existingContent = "";
        if (File.Exists(CHANGELOG))
        {
            existingContent = File.ReadAllText(CHANGELOG);
        }

        string updatedChangelog;
        if (string.IsNullOrEmpty(existingContent))
        {
            updatedChangelog = BuildInitialChangelog(version, date, message);
        }
        else
        {
            updatedChangelog = InsertChangelogEntry(existingContent, newEntry);
        }

        File.WriteAllText(CHANGELOG, updatedChangelog);
        AssetDatabase.Refresh();
        
        UnityEngine.Debug.Log($"[Laubrary Dev] Updated CHANGELOG.md with version {version}");
    }

    private string BuildInitialChangelog(string version, string date, string message)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Changelog");
        sb.AppendLine();
        sb.AppendLine("All notable changes to this project will be documented in this file.");
        sb.AppendLine();
        sb.AppendLine("The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),");
        sb.AppendLine("and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).");
        sb.AppendLine();
        sb.Append(BuildChangelogEntry(version, date, message));
        return sb.ToString();
    }

    private string BuildChangelogEntry(string version, string date, string message)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## [{version}] - {date}");
        sb.AppendLine();
        
        string[] lines = message.Split('\n');
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                sb.AppendLine(trimmed);
            }
        }
        
        sb.AppendLine();
        return sb.ToString();
    }

    private string InsertChangelogEntry(string existingContent, string newEntry)
    {
        int insertIndex = existingContent.IndexOf("## [");
        
        if (insertIndex == -1)
        {
            return existingContent + "\n" + newEntry;
        }

        return existingContent.Insert(insertIndex, newEntry);
    }

    private void UpdateGitCommandsPreview()
    {
        if (!createRelease)
        {
            firstReleaseCommands = "";
            subsequentReleaseCommands = "";
            deleteReleaseCommands = "";
            return;
        }

        string releaseCommitMsg = $"Release v{versionNumber}";
        string escapedCommitMsg = commitMessage.Replace("\"", "\\\"");
        
        var sbFirst = new StringBuilder();
        sbFirst.AppendLine($"git checkout {MAIN_BRANCH}");
        sbFirst.AppendLine("git add .");
        sbFirst.AppendLine($"git commit -m \"{escapedCommitMsg}\"");
        sbFirst.AppendLine($"git push origin {MAIN_BRANCH}");
        sbFirst.AppendLine($"git checkout --orphan {RELEASE_BRANCH}");
        sbFirst.AppendLine("git add .");
        sbFirst.AppendLine($"git commit -m \"{releaseCommitMsg}\"");
        sbFirst.AppendLine($"git tag -a v{versionNumber} -m \"{releaseCommitMsg}\"");
        sbFirst.AppendLine($"git push origin {RELEASE_BRANCH}");
        sbFirst.AppendLine($"git push origin v{versionNumber}");
        sbFirst.AppendLine($"git checkout {MAIN_BRANCH}");
        firstReleaseCommands = sbFirst.ToString();
        
        var sbSubsequent = new StringBuilder();
        sbSubsequent.AppendLine($"git checkout {MAIN_BRANCH}");
        sbSubsequent.AppendLine("git add .");
        sbSubsequent.AppendLine($"git commit -m \"{escapedCommitMsg}\"");
        sbSubsequent.AppendLine($"git push origin {MAIN_BRANCH}");
        sbSubsequent.AppendLine($"git checkout {RELEASE_BRANCH}");
        sbSubsequent.AppendLine($"git merge --squash {MAIN_BRANCH} --allow-unrelated-histories -X theirs");
        sbSubsequent.AppendLine($"git commit -m \"{releaseCommitMsg}\"");
        sbSubsequent.AppendLine($"git tag -a v{versionNumber} -m \"{releaseCommitMsg}\"");
        sbSubsequent.AppendLine($"git push origin {RELEASE_BRANCH}");
        sbSubsequent.AppendLine($"git push origin v{versionNumber}");
        sbSubsequent.AppendLine($"git checkout {MAIN_BRANCH}");
        subsequentReleaseCommands = sbSubsequent.ToString();
        
        var sbDelete = new StringBuilder();
        sbDelete.AppendLine($"git checkout {MAIN_BRANCH}");
        sbDelete.AppendLine($"git branch -D {RELEASE_BRANCH}");
        sbDelete.AppendLine($"git push origin --delete {RELEASE_BRANCH}");
        sbDelete.AppendLine("for /f %i in ('git tag') do git tag -d %i");
        sbDelete.AppendLine("git ls-remote --tags origin");
        deleteReleaseCommands = sbDelete.ToString();
    }

    private void CopyDirectory(string sourceDir, string targetDir)
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

    private string EscapeCommitMessage(string message)
    {
        return message.Replace("\"", "\\\"").Replace("\n", "\\n");
    }
}
