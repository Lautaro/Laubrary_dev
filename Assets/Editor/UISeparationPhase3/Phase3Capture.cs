// Phase 3 (AHQ T-0551) capture harness for the UI-separation programme.
//
// Why this exists: phase 3 migrates static inline presentation out of five tool windows into semantic USS.
// "Preserve current appearance" is only a claim unless the same window is measured before and after, so this
// harness produces two artefacts per window state:
//
//   * a RESOLVED-STYLE dump of the whole visual tree (the numeric oracle). If every element resolves to the
//     same geometry and the same cosmetics after the migration, appearance is preserved by construction, and
//     a diff names the exact element that drifted instead of a pixel region.
//   * a PNG of the window's real client area (the by-eye artefact), captured through the existing
//     PrintWindow path so a GPU-drawn Unity window is captured regardless of z-order.
//
// Appearance comparison is order-sensitive: a window left in a hovered state captures differently. Every entry
// point here reopens and repositions the window rather than reusing whatever is already on screen.
//
// This is harness/evidence apparatus under Assets/Editor, not a source surface to migrate, and it deliberately
// registers no menu items.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.UISeparationPhase3 {

    public static class Phase3Capture {

        /// <summary>Evidence root, beside the phase 0/1 evidence rather than inside it.</summary>
        public static string EvidenceFolder {
            get {
                string root = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
                return root + "/Documentation/UISeparation/Evidence/Phase3";
            }
        }

        // ── opening a window in a known, repeatable state ──

        /// <summary>
        /// Opens the window type by name as a floating utility window at an exact size, optionally pointing it at
        /// an asset, and returns it. The window is reopened from scratch so no hover/focus state carries over.
        /// </summary>
        public static EditorWindow Open(string typeName, float width, float height, string assetPath = null) {
            Type type = FindType(typeName);
            if (type == null) throw new ArgumentException("No editor window type named " + typeName);

            foreach (EditorWindow existing in Resources.FindObjectsOfTypeAll(type) as EditorWindow[] ?? new EditorWindow[0])
                existing.Close();

            var window = ScriptableObject.CreateInstance(type) as EditorWindow;
            if (window == null) throw new ArgumentException(typeName + " is not an EditorWindow");
            window.ShowUtility();
            window.position = new Rect(120f, 120f, width, height);

            if (string.IsNullOrEmpty(assetPath)) {
                // An asset window remembers the asset it last showed, so a window opened with nothing chosen
                // comes back holding that one. Clear it, or the "empty" state is never actually captured.
                ClearAsset(window);
            } else {
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                if (asset == null) throw new ArgumentException("No asset at " + assetPath);
                if (!TrySetAsset(window, asset)) throw new ArgumentException(typeName + " would not accept " + assetPath);
            }

            Settle(window);
            return window;
        }

        /// <summary>Pushes an asset into an AssetKit-style window through whichever public entry point it exposes.</summary>
        private static bool TrySetAsset(EditorWindow window, UnityEngine.Object asset) {
            Type t = window.GetType();
            foreach (string name in new[] { "SetAsset", "Select", "Show", "OpenFor" }) {
                var method = t.GetMethod(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                                             | System.Reflection.BindingFlags.Instance);
                if (method == null) continue;
                var ps = method.GetParameters();
                if (ps.Length != 1 || !ps[0].ParameterType.IsInstanceOfType(asset)) continue;
                method.Invoke(window, new object[] { asset });
                return true;
            }
            // AssetKit keeps the current asset in a protected field; assign it directly and rebuild.
            for (Type walk = t; walk != null; walk = walk.BaseType) {
                foreach (var field in walk.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                                                   | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)) {
                    if (!field.FieldType.IsInstanceOfType(asset)) continue;
                    field.SetValue(window, asset);
                    Rebuild(window);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Puts an asset window back into its no-asset state. A window that exposes no way to do that
        /// is left alone and reported, rather than being forced.</summary>
        public static void ClearAsset(EditorWindow window) {
            for (Type walk = window.GetType(); walk != null; walk = walk.BaseType) {
                var method = walk.GetMethod("SetAsset", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                                                      | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
                if (method == null || method.GetParameters().Length != 1) continue;
                method.Invoke(window, new object[] { null });
                Rebuild(window);
                return;
            }
        }

        /// <summary>Sets a private int on the window - a list's selected index - and rebuilds it.</summary>
        public static void SetIntField(EditorWindow window, string field, int value) {
            for (Type walk = window.GetType(); walk != null; walk = walk.BaseType) {
                var f = walk.GetField(field, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                                           | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
                if (f == null) continue;
                f.SetValue(window, value);
                Rebuild(window);
                return;
            }
            // Some windows keep the selection behind a property (an EditorPrefs-backed one, so it survives a
            // domain reload); set that instead rather than reaching for its hidden backing store.
            for (Type walk = window.GetType(); walk != null; walk = walk.BaseType) {
                var p = walk.GetProperty(field, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                                              | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
                if (p == null || !p.CanWrite) continue;
                p.SetValue(window, value);
                Rebuild(window);
                return;
            }
            throw new ArgumentException(window.GetType().Name + " has no writable int named " + field);
        }

        private static void Rebuild(EditorWindow window) {
            for (Type walk = window.GetType(); walk != null; walk = walk.BaseType) {
                var method = walk.GetMethod("Rebuild", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                                                     | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly,
                                            null, Type.EmptyTypes, null);
                if (method == null) continue;
                method.Invoke(window, null);
                return;
            }
            window.Repaint();
        }

        /// <summary>Forces layout and repaint so resolvedStyle and the native surface are both current.</summary>
        public static void Settle(EditorWindow window) {
            window.Focus();
            window.Repaint();
            var root = window.rootVisualElement;
            for (int i = 0; i < 4; i++) {
                root?.panel?.visualTree?.MarkDirtyRepaint();
                window.Repaint();
                System.Threading.Thread.Sleep(30);
            }
        }

        // ── the numeric oracle ──

        /// <summary>
        /// Writes the whole resolved visual tree of the window to Evidence/Phase3/&lt;label&gt;.styles.json and
        /// returns a one-line summary. This is the artefact a parity check diffs.
        /// </summary>
        public static string DumpStyles(EditorWindow window, string label) {
            Directory.CreateDirectory(EvidenceFolder);
            var sb = new StringBuilder();
            sb.Append("{\n  \"label\": ").Append(Json(label));
            sb.Append(",\n  \"window\": ").Append(Json(window.GetType().FullName));
            sb.Append(",\n  \"position\": \"")
              .Append(F(window.position.width)).Append('x').Append(F(window.position.height)).Append('"');
            sb.Append(",\n  \"elements\": [\n");
            int count = 0;
            Walk(window.rootVisualElement, "", sb, ref count);
            sb.Append("\n  ]\n}\n");
            string path = EvidenceFolder + "/" + label + ".styles.json";
            File.WriteAllText(path, sb.ToString());
            return "styles " + label + ": " + count + " elements -> " + path;
        }

        private static void Walk(VisualElement e, string path, StringBuilder sb, ref int count) {
            if (e == null) return;
            string id = path + "/" + (string.IsNullOrEmpty(e.name) ? e.GetType().Name : e.name)
                      + "[" + string.Join(".", ClassesOf(e)) + "]";
            if (count > 0) sb.Append(",\n");
            var r = e.resolvedStyle;
            sb.Append("    {\"p\": ").Append(Json(id));
            sb.Append(", \"rect\": \"").Append(F(e.layout.x)).Append(',').Append(F(e.layout.y))
              .Append(',').Append(F(e.layout.width)).Append(',').Append(F(e.layout.height)).Append('"');
            sb.Append(", \"s\": \"")
              .Append("bg=").Append(C(r.backgroundColor))
              .Append(" fg=").Append(C(r.color))
              .Append(" br=").Append(F(r.borderTopLeftRadius)).Append('/').Append(F(r.borderBottomRightRadius))
              .Append(" bw=").Append(F(r.borderLeftWidth)).Append('/').Append(F(r.borderTopWidth))
              .Append('/').Append(F(r.borderRightWidth)).Append('/').Append(F(r.borderBottomWidth))
              .Append(" bc=").Append(C(r.borderTopColor))
              .Append(" pad=").Append(F(r.paddingLeft)).Append('/').Append(F(r.paddingTop))
              .Append('/').Append(F(r.paddingRight)).Append('/').Append(F(r.paddingBottom))
              .Append(" mar=").Append(F(r.marginLeft)).Append('/').Append(F(r.marginTop))
              .Append('/').Append(F(r.marginRight)).Append('/').Append(F(r.marginBottom))
              .Append(" flex=").Append(F(r.flexGrow)).Append('/').Append(F(r.flexShrink))
              .Append(' ').Append(r.flexDirection).Append(' ').Append(r.alignItems).Append(' ').Append(r.justifyContent)
              .Append(" min=").Append(F(r.minWidth.value)).Append('/').Append(F(r.minHeight.value))
              .Append(" max=").Append(F(r.maxWidth.value)).Append('/').Append(F(r.maxHeight.value))
              .Append(" disp=").Append(r.display).Append(" pos=").Append(r.position)
              .Append(" opa=").Append(F(r.opacity))
              .Append(" font=").Append(F(r.fontSize)).Append(' ').Append(r.unityFontStyleAndWeight)
              .Append(" align=").Append(r.unityTextAlign)
              .Append('"');
            sb.Append('}');
            count++;
            for (int i = 0; i < e.childCount; i++) Walk(e[i], id, sb, ref count);
        }

        private static IEnumerable<string> ClassesOf(VisualElement e) {
            var list = new List<string>();
            foreach (string c in e.GetClasses()) list.Add(c);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        // ── the by-eye artefact ──

        /// <summary>Saves the window's client area as Evidence/Phase3/&lt;label&gt;.png.</summary>
        public static string Shoot(EditorWindow window, string label) {
            Directory.CreateDirectory(EvidenceFolder);
            Settle(window);
            var tex = Laubrary.Zounds.Uitk.ZoundsUitkCompare.Capture(window);
            if (tex == null) return "shoot " + label + ": FAILED (no native window found)";
            string path = EvidenceFolder + "/" + label + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            string size = tex.width + "x" + tex.height;
            UnityEngine.Object.DestroyImmediate(tex);
            return "shoot " + label + ": " + size + " -> " + path;
        }

        /// <summary>Open, dump styles, shoot, close: one window state captured end to end.</summary>
        public static string Capture(string typeName, string label, float width, float height, string assetPath = null) {
            var window = Open(typeName, width, height, assetPath);
            try {
                string styles = DumpStyles(window, label);
                string shot = Shoot(window, label);
                return styles + "\n" + shot;
            } finally { window.Close(); }
        }

        /// <summary>
        /// Hosts the custom editor of a surface that has no window of its own - Cabinets is a ScriptableObject
        /// inspector - against a throwaway instance, so the same element tree the Inspector would show can be
        /// measured and photographed. <see cref="CloseInspectorFixture"/> disposes the instance afterwards.
        /// </summary>
        public static EditorWindow OpenInspector(string targetTypeName, float width, float height) {
            Type target = FindAnyType(targetTypeName);
            if (target == null) throw new ArgumentException("No type named " + targetTypeName);

            CloseInspectorFixture();
            UnityEngine.Object instance;
            if (typeof(ScriptableObject).IsAssignableFrom(target)) {
                instance = ScriptableObject.CreateInstance(target);
                instance.name = "Phase3 " + target.Name + " fixture";
            } else if (typeof(Component).IsAssignableFrom(target)) {
                s_fixtureHost = new GameObject("Phase3 " + target.Name + " fixture");
                instance = s_fixtureHost.AddComponent(target);
            } else {
                throw new ArgumentException(targetTypeName + " is neither a ScriptableObject nor a Component");
            }
            s_fixtureInstance = instance;
            return Phase3SurfaceWindow.Host(instance, width, height);
        }

        /// <summary>Disposes the throwaway inspector fixture.</summary>
        public static void CloseInspectorFixture() {
            if (s_fixtureHost != null) { UnityEngine.Object.DestroyImmediate(s_fixtureHost); s_fixtureHost = null; }
            else if (s_fixtureInstance != null) UnityEngine.Object.DestroyImmediate(s_fixtureInstance);
            s_fixtureInstance = null;
        }

        private static GameObject s_fixtureHost;
        private static UnityEngine.Object s_fixtureInstance;

        private static Type FindAnyType(string name) {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                var t = asm.GetType(name, false);
                if (t != null) return t;
            }
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                foreach (var t in asm.GetTypes())
                    if (t.Name == name) return t;
            return null;
        }

        // ── helpers ──

        private static Type FindType(string name) {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                var t = asm.GetType(name, false);
                if (t != null) return t;
            }
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                foreach (var t in asm.GetTypes())
                    if (t.Name == name && typeof(EditorWindow).IsAssignableFrom(t)) return t;
            return null;
        }

        private static string F(float v) {
            if (float.IsNaN(v)) return "nan";
            if (float.IsInfinity(v)) return "inf";
            return (Mathf.Round(v * 100f) / 100f).ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string C(Color c) {
            return "#" + Mathf.RoundToInt(c.r * 255f).ToString("X2")
                       + Mathf.RoundToInt(c.g * 255f).ToString("X2")
                       + Mathf.RoundToInt(c.b * 255f).ToString("X2")
                       + Mathf.RoundToInt(c.a * 255f).ToString("X2");
        }

        private static string Json(string s) {
            var sb = new StringBuilder("\"");
            foreach (char ch in s ?? "") {
                if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
                else if (ch == '\n') sb.Append("\\n");
                else if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("X4"));
                else sb.Append(ch);
            }
            return sb.Append('"').ToString();
        }
    }
}
