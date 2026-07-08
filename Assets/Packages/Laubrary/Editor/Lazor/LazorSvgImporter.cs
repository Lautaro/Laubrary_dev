// LazorSvgImporter.cs — imports common SVG line-art (line, polyline, polygon, rect, path) into a LazorShape.
// SVG is the most common interchange format for vector graphics; a Lazor grid isn't a native SVG concept, so
// the import just fits the paths into the shape's design grid (centered, y-up), flattening curves to segments.
// Menu-driven (no ScriptedImporter) to avoid claiming the .svg extension from other packages.

using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor;
using UnityEngine;
using Laubrary.Lazor;

namespace Laubrary.Lazor.Editor
{
    public static class LazorSvgImporter
    {
        [MenuItem("Laubrary/Lazor/Import SVG…")]
        public static void ImportSvgMenu()
        {
            string file = EditorUtility.OpenFilePanel("Import SVG as Lazor Shape", Application.dataPath, "svg");
            if (string.IsNullOrEmpty(file)) return;

            var raw = ParseSvg(file, out string niceName);
            if (raw.Count == 0)
            {
                EditorUtility.DisplayDialog("Lazor SVG Import", "No supported shapes found (line, polyline, polygon, rect, path).", "OK");
                return;
            }

            var shape = BuildShape(raw, 32);
            const string dir = "Assets/Lazor";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "Lazor");
            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{niceName}.asset");
            AssetDatabase.CreateAsset(shape, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = shape;
            EditorGUIUtility.PingObject(shape);
            Debug.Log($"Lazor: imported {raw.Count} stroke(s) from SVG into {path}");
        }

        struct RawPath { public List<Vector2> pts; public bool closed; }

        // ---- SVG element parsing ----

        static List<RawPath> ParseSvg(string file, out string niceName)
        {
            niceName = System.IO.Path.GetFileNameWithoutExtension(file);
            var result = new List<RawPath>();
            var doc = new XmlDocument();
            try { doc.Load(file); }
            catch (System.Exception ex) { Debug.LogError($"Lazor: failed to read SVG: {ex.Message}"); return result; }

            foreach (XmlNode node in doc.GetElementsByTagName("*"))
            {
                switch (node.LocalName)
                {
                    case "line":
                    {
                        var pts = new List<Vector2>
                        {
                            new Vector2(F(node, "x1"), F(node, "y1")),
                            new Vector2(F(node, "x2"), F(node, "y2")),
                        };
                        result.Add(new RawPath { pts = pts, closed = false });
                        break;
                    }
                    case "polyline":
                    case "polygon":
                    {
                        var pts = ParsePointList(Attr(node, "points"));
                        if (pts.Count >= 2) result.Add(new RawPath { pts = pts, closed = node.LocalName == "polygon" });
                        break;
                    }
                    case "rect":
                    {
                        float x = F(node, "x"), y = F(node, "y"), w = F(node, "width"), h = F(node, "height");
                        var pts = new List<Vector2>
                        {
                            new Vector2(x, y), new Vector2(x + w, y),
                            new Vector2(x + w, y + h), new Vector2(x, y + h),
                        };
                        result.Add(new RawPath { pts = pts, closed = true });
                        break;
                    }
                    case "path":
                        result.AddRange(ParsePathData(Attr(node, "d")));
                        break;
                }
            }
            return result;
        }

        static string Attr(XmlNode n, string name) => n.Attributes?[name]?.Value;
        static float F(XmlNode n, string name)
        {
            var s = Attr(n, name);
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
        }

        static readonly Regex NumRx = new Regex(@"-?\d*\.?\d+(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

        static List<Vector2> ParsePointList(string s)
        {
            var nums = new List<float>();
            if (!string.IsNullOrEmpty(s))
                foreach (Match m in NumRx.Matches(s))
                    nums.Add(float.Parse(m.Value, CultureInfo.InvariantCulture));
            var pts = new List<Vector2>();
            for (int i = 0; i + 1 < nums.Count; i += 2) pts.Add(new Vector2(nums[i], nums[i + 1]));
            return pts;
        }

        // ---- SVG path 'd' mini-parser (M L H V C S Q T Z, absolute + relative; A/arc approximated as a line) ----

        static readonly Regex TokenRx = new Regex(@"([MmLlHhVvCcSsQqTtAaZz])|(-?\d*\.?\d+(?:[eE][-+]?\d+)?)", RegexOptions.Compiled);

        static List<RawPath> ParsePathData(string d)
        {
            var paths = new List<RawPath>();
            if (string.IsNullOrEmpty(d)) return paths;

            var tokens = new List<string>();
            foreach (Match m in TokenRx.Matches(d)) tokens.Add(m.Value);

            List<Vector2> cur = null;
            Vector2 pos = Vector2.zero, start = Vector2.zero, prevCtrl = Vector2.zero;
            char cmd = ' ';
            int ti = 0;
            System.Func<float> Next = () => float.Parse(tokens[ti++], CultureInfo.InvariantCulture);
            System.Func<string, bool> IsNum = t => t.Length > 0 && (char.IsDigit(t[0]) || t[0] == '-' || t[0] == '.');

            System.Action Commit = () => { if (cur != null && cur.Count >= 2) paths.Add(new RawPath { pts = cur, closed = false }); cur = null; };
            System.Action<Vector2> MoveStroke = p => { Commit(); cur = new List<Vector2> { p }; };

            while (ti < tokens.Count)
            {
                string tok = tokens[ti];
                if (!IsNum(tok)) { cmd = tok[0]; ti++; }
                else if (cmd == 'M') cmd = 'L';        // implicit lineto after moveto
                else if (cmd == 'm') cmd = 'l';

                bool rel = char.IsLower(cmd);
                switch (char.ToUpper(cmd))
                {
                    case 'M':
                    {
                        float x = Next(), y = Next();
                        pos = rel && cur != null ? pos + new Vector2(x, y) : new Vector2(x, y);
                        start = pos; MoveStroke(pos); prevCtrl = pos; break;
                    }
                    case 'L':
                    {
                        float x = Next(), y = Next();
                        pos = rel ? pos + new Vector2(x, y) : new Vector2(x, y);
                        cur?.Add(pos); prevCtrl = pos; break;
                    }
                    case 'H':
                    {
                        float x = Next();
                        pos = new Vector2(rel ? pos.x + x : x, pos.y);
                        cur?.Add(pos); prevCtrl = pos; break;
                    }
                    case 'V':
                    {
                        float y = Next();
                        pos = new Vector2(pos.x, rel ? pos.y + y : y);
                        cur?.Add(pos); prevCtrl = pos; break;
                    }
                    case 'C':
                    {
                        Vector2 c1 = Pt(rel, pos, Next(), Next());
                        Vector2 c2 = Pt(rel, pos, Next(), Next());
                        Vector2 end = Pt(rel, pos, Next(), Next());
                        FlattenCubic(cur, pos, c1, c2, end);
                        prevCtrl = c2; pos = end; break;
                    }
                    case 'S':
                    {
                        Vector2 c1 = 2f * pos - prevCtrl;   // reflect previous control
                        Vector2 c2 = Pt(rel, pos, Next(), Next());
                        Vector2 end = Pt(rel, pos, Next(), Next());
                        FlattenCubic(cur, pos, c1, c2, end);
                        prevCtrl = c2; pos = end; break;
                    }
                    case 'Q':
                    {
                        Vector2 c = Pt(rel, pos, Next(), Next());
                        Vector2 end = Pt(rel, pos, Next(), Next());
                        FlattenQuad(cur, pos, c, end);
                        prevCtrl = c; pos = end; break;
                    }
                    case 'T':
                    {
                        Vector2 c = 2f * pos - prevCtrl;
                        Vector2 end = Pt(rel, pos, Next(), Next());
                        FlattenQuad(cur, pos, c, end);
                        prevCtrl = c; pos = end; break;
                    }
                    case 'A':
                    {
                        Next(); Next(); Next(); Next(); Next();      // rx ry xrot largeArc sweep (ignored)
                        Vector2 end = Pt(rel, pos, Next(), Next());
                        cur?.Add(end); pos = end; prevCtrl = end; break;   // approximate arc as a straight line
                    }
                    case 'Z':
                    {
                        if (cur != null && cur.Count >= 2) { cur.Add(start); paths.Add(new RawPath { pts = cur, closed = true }); cur = null; }
                        pos = start; break;
                    }
                    default: ti++; break; // unknown — skip
                }
            }
            Commit();
            return paths;
        }

        static Vector2 Pt(bool rel, Vector2 pos, float x, float y) => rel ? pos + new Vector2(x, y) : new Vector2(x, y);

        static void FlattenCubic(List<Vector2> into, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, int segs = 14)
        {
            if (into == null) return;
            for (int i = 1; i <= segs; i++)
            {
                float t = i / (float)segs, u = 1 - t;
                into.Add(u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3);
            }
        }

        static void FlattenQuad(List<Vector2> into, Vector2 p0, Vector2 p1, Vector2 p2, int segs = 12)
        {
            if (into == null) return;
            for (int i = 1; i <= segs; i++)
            {
                float t = i / (float)segs, u = 1 - t;
                into.Add(u * u * p0 + 2 * u * t * p1 + t * t * p2);
            }
        }

        // ---- Fit into the Lazor design grid ----

        static LazorShape BuildShape(List<RawPath> raw, int resolution)
        {
            // Global bounds across all points (SVG y grows downward).
            bool any = false; float minX = 0, minY = 0, maxX = 0, maxY = 0;
            foreach (var rp in raw)
                foreach (var p in rp.pts)
                {
                    if (!any) { minX = maxX = p.x; minY = maxY = p.y; any = true; continue; }
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                    minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                }

            Vector2 center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            float span = Mathf.Max(maxX - minX, maxY - minY);
            if (span < 1e-4f) span = 1f;
            float scale = (resolution - 2f) / span;

            var shape = ScriptableObject.CreateInstance<LazorShape>();
            shape.gridResolution = resolution;
            var layer = new LazorLayer("SVG") { symmetryEnabled = false };
            foreach (var rp in raw)
            {
                var path = new LazorPath(rp.closed);
                foreach (var p in rp.pts)
                    path.points.Add(new Vector2((p.x - center.x) * scale, -(p.y - center.y) * scale)); // flip Y to y-up
                layer.paths.Add(path);
            }
            shape.layers.Add(layer);
            return shape;
        }
    }
}
