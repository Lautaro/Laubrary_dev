using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor;

namespace Laubrary.Zounds {

    /// <summary>
    /// A file-open dialog that can return several files. Unity's EditorUtility.OpenFilePanel is
    /// single-select, so on Windows this goes to the native dialog with multi-select allowed; on
    /// other platforms it falls back to Unity's panel and returns at most one file.
    /// </summary>
    internal static class ExternalFileDialog {

        /// <summary>Absolute paths of the chosen files, empty when cancelled.</summary>
        public static string[] OpenFiles(string title, string startDir, string extension) {
#if UNITY_EDITOR_WIN
            try {
                return OpenFilesWindows(title, startDir, extension);
            }
            catch (Exception e) {
                UnityEngine.Debug.LogWarning("[Zounds] Native multi-select dialog failed, using the single-file panel. " + e.Message);
            }
#endif
            string one = EditorUtility.OpenFilePanel(title, startDir, extension);
            return string.IsNullOrEmpty(one) ? Array.Empty<string>() : new[] { one };
        }

#if UNITY_EDITOR_WIN
        private const int OFN_EXPLORER = 0x00080000;
        private const int OFN_ALLOWMULTISELECT = 0x00000200;
        private const int OFN_FILEMUSTEXIST = 0x00001000;
        private const int OFN_PATHMUSTEXIST = 0x00000800;
        private const int OFN_NOCHANGEDIR = 0x00000008;
        private const int FileBufferChars = 64 * 1024;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private class OpenFileName {
            public int lStructSize = Marshal.SizeOf(typeof(OpenFileName));
            public IntPtr hwndOwner = IntPtr.Zero;
            public IntPtr hInstance = IntPtr.Zero;
            public string lpstrFilter = null;
            public string lpstrCustomFilter = null;
            public int nMaxCustFilter = 0;
            public int nFilterIndex = 0;
            public IntPtr lpstrFile = IntPtr.Zero;
            public int nMaxFile = 0;
            public string lpstrFileTitle = null;
            public int nMaxFileTitle = 0;
            public string lpstrInitialDir = null;
            public string lpstrTitle = null;
            public int Flags = 0;
            public short nFileOffset = 0;
            public short nFileExtension = 0;
            public string lpstrDefExt = null;
            public IntPtr lCustData = IntPtr.Zero;
            public IntPtr lpfnHook = IntPtr.Zero;
            public string lpTemplateName = null;
            public IntPtr pvReserved = IntPtr.Zero;
            public int dwReserved = 0;
            public int FlagsEx = 0;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetOpenFileNameW([In, Out] OpenFileName ofn);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        private static string[] OpenFilesWindows(string title, string startDir, string extension) {
            string ext = string.IsNullOrEmpty(extension) ? "*" : extension.TrimStart('.');
            var ofn = new OpenFileName {
                hwndOwner = GetActiveWindow(),
                lpstrFilter = ext.ToUpperInvariant() + " files\0*." + ext + "\0All files\0*.*\0\0",
                lpstrInitialDir = string.IsNullOrEmpty(startDir) ? null : startDir.Replace('/', '\\'),
                lpstrTitle = title,
                lpstrDefExt = ext == "*" ? null : ext,
                Flags = OFN_EXPLORER | OFN_ALLOWMULTISELECT | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR,
                nMaxFile = FileBufferChars,
                lpstrFile = Marshal.AllocHGlobal(FileBufferChars * 2)
            };
            try {
                // The buffer starts with an empty string: no preselected file name.
                Marshal.WriteInt16(ofn.lpstrFile, 0, 0);
                if (!GetOpenFileNameW(ofn)) return Array.Empty<string>();
                return ParseMultiSelectBuffer(ofn.lpstrFile, FileBufferChars);
            }
            finally {
                Marshal.FreeHGlobal(ofn.lpstrFile);
            }
        }

        // With OFN_EXPLORER the buffer holds "directory\0file1\0file2\0\0" for several files, or a
        // single full path followed by "\0\0" for one file.
        private static string[] ParseMultiSelectBuffer(IntPtr buffer, int maxChars) {
            var parts = new List<string>();
            var current = new System.Text.StringBuilder();
            for (int i = 0; i < maxChars; i++) {
                char c = (char)Marshal.ReadInt16(buffer, i * 2);
                if (c != '\0') { current.Append(c); continue; }
                if (current.Length == 0) break;
                parts.Add(current.ToString());
                current.Clear();
            }
            if (parts.Count == 0) return Array.Empty<string>();
            if (parts.Count == 1) return new[] { parts[0].Replace('\\', '/') };

            string dir = parts[0].TrimEnd('\\');
            var files = new string[parts.Count - 1];
            for (int i = 1; i < parts.Count; i++)
                files[i - 1] = (dir + "\\" + parts[i]).Replace('\\', '/');
            return files;
        }
#endif
    }

}
