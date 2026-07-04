// RulesEditorWindow.cs
// One window to browse + tune every game rule. Left: a category tree (built from RuleTaxonomy) with a
// search box and tag chips. Right: the filtered rule list and the selected rule's fields, rendered by
// reflection (EditorGUILayout). A Rulesets tab manages the RuleSet assets in Resources/Rulesets and points
// the scene's RulesHost at one (hot-swapping it in Play).
//
// Operates on the live rules: the running RulesHost in Play mode, else the authored host's SourceSet asset.
// Plain EditorWindow + reflection — no Odin, no ZUI. Rules use plain primitive fields.
//
// CROSS-ASSEMBLY DISCOVERY: GameRule lives in the Laubrary.Rulesets plugin assembly, but concrete rule
// subclasses live in the consuming game's assembly. AllRuleTypes() therefore scans EVERY loaded assembly
// (AppDomain) for non-abstract GameRule subclasses — not just GameRule's own assembly.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Laubrary.Rulesets;

namespace Laubrary.Rulesets.Editor
{
    public class RulesEditorWindow : EditorWindow
    {
        [MenuItem("Laubrary/Rules Editor")]
        static void Open() => GetWindow<RulesEditorWindow>("Rules");

        enum Tab { Rules, Rulesets, GlobalRules }
        [SerializeField] Tab _tab;

        [SerializeField] string _search = "";
        [SerializeField] string _selectedCategory = "All";
        [SerializeField] int _stateFilter; // 0 = all, 1 = enabled only, 2 = disabled only
        [SerializeField] List<string> _activeTags = new List<string>();
        [SerializeField] string _selectedType;
        [SerializeField] string _lastSingle; // last type auto-opened because it was the sole filtered result
        [SerializeField] Vector2 _leftScroll, _rightScroll, _rulesetsScroll;

        string _renamingPath, _renameBuffer, _peekPath;
        UnityEngine.Object _owner; // what to SetDirty after an edit (the RuleSet asset in edit mode)

        void OnGUI()
        {
            _tab = (Tab)GUILayout.Toolbar((int)_tab, new[] { "Rules", "Rulesets", "Global Rules" });
            GUILayout.Space(4);
            if (_tab == Tab.Rules) DrawRulesTab();
            else if (_tab == Tab.Rulesets) DrawRulesetsTab();
            else DrawGlobalRulesTab();
        }

        void Update()
        {
            if (Application.isPlaying) Repaint();
        }

        // ── Rule discovery ──────────────────────────────────────────────────────────

        GameRule[] CurrentRules()
        {
            _owner = null;
            if (Application.isPlaying && RulesHost.Active != null)
                return RulesHost.Active.Rules.Where(r => r != null).OrderBy(r => r.GetType().Name).ToArray();

            var host = UnityEngine.Object.FindAnyObjectByType<RulesHost>(FindObjectsInactive.Include);
            if (host != null && host.SourceSet != null)
            {
                _owner = host.SourceSet;
                EnsureAllRuleTypes(host.SourceSet);
                return host.SourceSet.Rules.Where(r => r != null).OrderBy(r => r.GetType().Name).ToArray();
            }
            return Array.Empty<GameRule>();
        }

        static Type[] _allRuleTypes;

        // Scan EVERY loaded assembly for concrete GameRule subclasses. Concrete rules live in the consuming
        // game's assembly, not in the plugin, so scanning only typeof(GameRule).Assembly would find nothing.
        // Each assembly's GetTypes() is guarded against ReflectionTypeLoadException (partially-loaded
        // assemblies); we keep whatever types did load. Cached after first scan.
        static Type[] AllRuleTypes()
        {
            if (_allRuleTypes != null) return _allRuleTypes;

            var result = new List<Type>();
            var seen = new HashSet<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }

                if (types == null) continue;
                foreach (var t in types)
                {
                    if (t == null) continue;
                    if (t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(GameRule)) && seen.Add(t))
                        result.Add(t);
                }
            }
            _allRuleTypes = result.OrderBy(t => t.Name).ToArray();
            return _allRuleTypes;
        }

        // The ruleset should contain one of every rule type, so you never "add" a rule — they're all listed,
        // just toggled on/off. Adds any missing type as a disabled instance.
        static void EnsureAllRuleTypes(RuleSet set)
        {
            if (set == null) return;
            var present = new HashSet<Type>(set.Rules.Where(r => r != null).Select(r => r.GetType()));
            bool added = false;
            foreach (var t in AllRuleTypes())
            {
                if (present.Contains(t)) continue;
                if (Activator.CreateInstance(t) is GameRule rule)
                {
                    rule.ResetToDefault();
                    set.Rules.Add(rule);
                    added = true;
                }
            }
            if (added) EditorUtility.SetDirty(set);
        }

        // ── Rules tab ───────────────────────────────────────────────────────────────

        void DrawRulesTab()
        {
            var rules = CurrentRules();
            if (rules.Length == 0)
            {
                EditorGUILayout.HelpBox("No rules found. In Play mode this shows the live RulesHost; in edit mode it shows the authored RulesHost's RuleSet asset. Open a scene with a RulesHost (and define some GameRule subclasses) to edit rules.", MessageType.Info);
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(210f));
            DrawLeftPanel(rules);
            GUILayout.EndVertical();
            GUILayout.Space(6);
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            DrawRightPanel(rules);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        void DrawLeftPanel(GameRule[] rules)
        {
            EditorGUILayout.LabelField("Search", EditorStyles.miniBoldLabel);
            _search = EditorGUILayout.TextField(_search);

            GUILayout.Space(4);
            EditorGUILayout.LabelField("Categories", EditorStyles.miniBoldLabel);

            var cats = rules.Select(r => RuleTaxonomy.CategoryOf(r.GetType())).Distinct().OrderBy(c => c).ToList();
            int enabledN = rules.Count(r => r.Active);
            int disabledN = rules.Length - enabledN;

            _leftScroll = GUILayout.BeginScrollView(_leftScroll, GUILayout.Height(260f));
            if (RowButton("All", _selectedCategory == "All" && _stateFilter == 0, 0) != 0)
            { _selectedCategory = "All"; _stateFilter = 0; _activeTags.Clear(); } // right- or left-click both reset
            if (RowButton($"Enabled ({enabledN})", _stateFilter == 1, 1) != 0) _stateFilter = _stateFilter == 1 ? 0 : 1;
            if (RowButton($"Disabled ({disabledN})", _stateFilter == 2, 1) != 0) _stateFilter = _stateFilter == 2 ? 0 : 2;
            GUILayout.Space(4);
            foreach (var cat in cats)
            {
                int code = RowButton(cat, _selectedCategory == cat, 1);
                if (code == 2) IsolateCategory(cat); // right-click: clear tags + state, show ONLY this category
                else if (code == 1) _selectedCategory = cat;
            }
            GUILayout.EndScrollView();

            GUILayout.Space(4);
            EditorGUILayout.LabelField("Tags", EditorStyles.miniBoldLabel);
            var allTags = rules.SelectMany(r => RuleTaxonomy.TagsOf(r.GetType()))
                               .Concat(new[] { "enabled", "disabled" })
                               .Distinct().OrderBy(t => t).ToArray();
            DrawTagChips(allTags);
            if (_activeTags.Count > 0 && GUILayout.Button("clear tags", EditorStyles.miniButton))
                _activeTags.Clear();
        }

        // 0 = no click, 1 = left-click, 2 = RIGHT-click (used as "isolate this filter").
        int RowButton(string label, bool selected, int indent)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(indent * 12f);
            var style = selected ? EditorStyles.boldLabel : EditorStyles.label;
            bool clicked = GUILayout.Button((selected ? "▸ " : "") + label, style);
            Rect r = GUILayoutUtility.GetLastRect();
            GUILayout.EndHorizontal();
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 1 && r.Contains(e.mousePosition)) { e.Use(); return 2; }
            return clicked ? 1 : 0;
        }

        // Right-click "isolate": drop every other filter and show ONLY the thing clicked.
        void IsolateCategory(string cat) { _activeTags.Clear(); _stateFilter = 0; _selectedCategory = cat; }
        void IsolateTag(string tag) { _activeTags.Clear(); _selectedCategory = "All"; _stateFilter = 0; _activeTags.Add(tag); }

        static bool InCategory(GameRule r, string category)
        {
            if (category == "All") return true;
            var cat = RuleTaxonomy.CategoryOf(r.GetType());
            return cat == category || cat.StartsWith(category + "/");
        }

        void DrawTagChips(string[] tags)
        {
            int perRow = 3, i = 0;
            while (i < tags.Length)
            {
                GUILayout.BeginHorizontal();
                for (int c = 0; c < perRow && i < tags.Length; c++, i++)
                {
                    bool on = _activeTags.Contains(tags[i]);
                    bool now = GUILayout.Toggle(on, tags[i], EditorStyles.miniButton);
                    Rect tr = GUILayoutUtility.GetLastRect();
                    var e = Event.current;
                    if (e.type == EventType.MouseDown && e.button == 1 && tr.Contains(e.mousePosition))
                    { e.Use(); IsolateTag(tags[i]); }      // right-click: show ONLY this tag
                    else if (now && !on) _activeTags.Add(tags[i]);
                    else if (!now && on) _activeTags.Remove(tags[i]);
                }
                GUILayout.EndHorizontal();
            }
        }

        bool PassesFilter(GameRule r)
        {
            var t = r.GetType();
            if (!InCategory(r, _selectedCategory)) return false;

            bool wantEnabled = _stateFilter == 1 || _activeTags.Contains("enabled");
            bool wantDisabled = _stateFilter == 2 || _activeTags.Contains("disabled");
            if (wantEnabled && !r.Active) return false;
            if (wantDisabled && r.Active) return false;

            if (!string.IsNullOrEmpty(_search))
            {
                string hay = t.Name + " " + SafeDescribe(r);
                if (hay.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) return false;
            }

            var contentTags = _activeTags.Where(x => x != "enabled" && x != "disabled").ToList();
            if (contentTags.Count > 0)
            {
                var tags = RuleTaxonomy.TagsOf(t);
                if (!contentTags.Any(tags.Contains)) return false;
            }
            return true;
        }

        void DrawRightPanel(GameRule[] rules)
        {
            var filtered = rules.Where(PassesFilter).ToArray();
            // When a filter narrows the view to exactly ONE rule, open it automatically (once per new
            // single result; the user can still collapse it, and it won't re-pop every frame).
            if (filtered.Length == 1)
            {
                string only = filtered[0].GetType().Name;
                if (_lastSingle != only) { _selectedType = only; _lastSingle = only; }
            }
            else _lastSingle = null;

            string state = _stateFilter == 1 ? " · Enabled" : _stateFilter == 2 ? " · Disabled" : "";
            EditorGUILayout.LabelField($"{filtered.Length} rule(s)  ·  {_selectedCategory}{state}", EditorStyles.miniLabel);

            bool playing = Application.isPlaying;
            _rightScroll = GUILayout.BeginScrollView(_rightScroll);
            foreach (var r in filtered)
            {
                string typeName = r.GetType().Name;
                GUILayout.BeginHorizontal(EditorStyles.helpBox);

                bool en = r.Active;
                bool newEn = GUILayout.Toggle(en, GUIContent.none, GUILayout.Width(16f));
                if (newEn != en) { r.Active = newEn; MarkDirty(); }

                bool isSel = _selectedType == typeName;
                if (GUILayout.Button(typeName, isSel ? EditorStyles.boldLabel : EditorStyles.label, GUILayout.ExpandWidth(false)))
                    _selectedType = isSel ? null : typeName;

                if (playing && r.Active)
                {
                    string info = SafeRuntimeInfo(r);
                    if (!string.IsNullOrEmpty(info))
                        EditorGUILayout.LabelField(info.Replace("\n", "   "), EditorStyles.miniLabel);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                if (_selectedType == typeName) DrawRuleDetail(r);
            }
            GUILayout.EndScrollView();
        }

        void DrawRuleDetail(GameRule r)
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(SafeDescribe(r), EditorStyles.wordWrappedMiniLabel);
            var brief = SafeBrief(r);
            if (!string.IsNullOrEmpty(brief))
                EditorGUILayout.LabelField("Brief: " + brief.Replace("\n", " / "), EditorStyles.wordWrappedMiniLabel);
            if (Application.isPlaying)
            {
                string info = SafeRuntimeInfo(r);
                if (!string.IsNullOrEmpty(info))
                    EditorGUILayout.LabelField(info, EditorStyles.wordWrappedMiniLabel);
            }
            GUILayout.Space(2);

            var fields = FieldsOf(r.GetType());
            float labelW = 90f;
            foreach (var f in fields)
                labelW = Mathf.Max(labelW, EditorStyles.label.CalcSize(new GUIContent(ObjectNames.NicifyVariableName(f.Name))).x);
            labelW = Mathf.Min(labelW + 12f, 260f);
            float prevLW = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = labelW;

            EditorGUI.BeginChangeCheck();
            foreach (var f in fields)
                DrawField(r, f);
            if (EditorGUI.EndChangeCheck()) MarkDirty();

            EditorGUIUtility.labelWidth = prevLW;

            GUILayout.Space(2);
            if (GUILayout.Button("Reset to default", EditorStyles.miniButton)) { r.ResetToDefault(); MarkDirty(); }
            GUILayout.EndVertical();
        }

        // ── Reflection field rendering ──────────────────────────────────────────────

        static readonly Dictionary<Type, FieldInfo[]> _fieldCache = new Dictionary<Type, FieldInfo[]>();

        static FieldInfo[] FieldsOf(Type t)
        {
            if (_fieldCache.TryGetValue(t, out var arr)) return arr;
            var list = new List<FieldInfo>();
            var chain = new List<Type>();
            for (Type cur = t; cur != null && cur != typeof(object); cur = cur.BaseType) chain.Add(cur);
            chain.Reverse();
            foreach (var ct in chain)
                foreach (var f in ct.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (f.IsNotSerialized) continue;
                    if (Attribute.IsDefined(f, typeof(HideInInspector))) continue;
                    list.Add(f);
                }
            arr = list.ToArray();
            _fieldCache[t] = arr;
            return arr;
        }

        void DrawField(object owner, FieldInfo f)
        {
            string nice = ObjectNames.NicifyVariableName(f.Name);
            object v = f.GetValue(owner);

            if (f.FieldType == typeof(float))
            {
                var range = (RangeAttribute)Attribute.GetCustomAttribute(f, typeof(RangeAttribute));
                if (range != null) f.SetValue(owner, EditorGUILayout.Slider(nice, (float)v, range.min, range.max));
                else f.SetValue(owner, EditorGUILayout.FloatField(nice, (float)v));
            }
            else if (f.FieldType == typeof(int))
            {
                var range = (RangeAttribute)Attribute.GetCustomAttribute(f, typeof(RangeAttribute));
                if (range != null) f.SetValue(owner, EditorGUILayout.IntSlider(nice, (int)v, (int)range.min, (int)range.max));
                else f.SetValue(owner, EditorGUILayout.IntField(nice, (int)v));
            }
            else if (f.FieldType == typeof(bool)) f.SetValue(owner, EditorGUILayout.Toggle(nice, (bool)v));
            else if (f.FieldType == typeof(string)) f.SetValue(owner, EditorGUILayout.TextField(nice, (string)v));
            else if (f.FieldType.IsEnum) f.SetValue(owner, EditorGUILayout.EnumPopup(nice, (Enum)v));
            else if (f.FieldType == typeof(Color)) f.SetValue(owner, EditorGUILayout.ColorField(nice, (Color)v));
            else if (f.FieldType == typeof(Vector2)) f.SetValue(owner, EditorGUILayout.Vector2Field(nice, (Vector2)v));
            else if (f.FieldType == typeof(Vector2Int)) f.SetValue(owner, EditorGUILayout.Vector2IntField(nice, (Vector2Int)v));
            else if (typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType))
                f.SetValue(owner, EditorGUILayout.ObjectField(nice, (UnityEngine.Object)v, f.FieldType, true));
            else if (f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(List<>))
                DrawList(owner, f, nice);
        }

        void DrawList(object owner, FieldInfo f, string nice)
        {
            var elemType = f.FieldType.GetGenericArguments()[0];
            var list = f.GetValue(owner) as System.Collections.IList;
            if (list == null)
            {
                list = (System.Collections.IList)Activator.CreateInstance(f.FieldType);
                f.SetValue(owner, list);
            }

            EditorGUILayout.LabelField($"{nice}  ({list.Count})", EditorStyles.boldLabel);
            bool isClass = elemType.IsClass && elemType != typeof(string) && !typeof(UnityEngine.Object).IsAssignableFrom(elemType);

            int removeAt = -1;
            for (int i = 0; i < list.Count; i++)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"[{i}]", GUILayout.Width(28f));
                if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(22f))) removeAt = i;
                GUILayout.EndHorizontal();

                if (isClass)
                {
                    var elem = list[i];
                    if (elem == null) { elem = Activator.CreateInstance(elemType); list[i] = elem; }
                    foreach (var ef in FieldsOf(elemType)) DrawField(elem, ef);
                }
                else if (typeof(UnityEngine.Object).IsAssignableFrom(elemType))
                    list[i] = EditorGUILayout.ObjectField((UnityEngine.Object)list[i], elemType, true);
                else if (elemType.IsEnum) list[i] = EditorGUILayout.EnumPopup((Enum)list[i]);
                else if (elemType == typeof(float)) list[i] = EditorGUILayout.FloatField((float)list[i]);
                else if (elemType == typeof(int)) list[i] = EditorGUILayout.IntField((int)list[i]);
                else if (elemType == typeof(string)) list[i] = EditorGUILayout.TextField((string)(list[i] ?? ""));
                GUILayout.EndVertical();
            }
            if (removeAt >= 0) { list.RemoveAt(removeAt); GUI.changed = true; }

            if (GUILayout.Button("+ Add " + elemType.Name, EditorStyles.miniButton))
            {
                list.Add(isClass || elemType.IsValueType ? Activator.CreateInstance(elemType)
                                                          : (elemType == typeof(string) ? "" : null));
                GUI.changed = true;
            }
        }

        // ── Global Rules tab ──────────────────────────────────────────────────────────
        // Edits the GLOBAL ruleset asset (Resources/Rulesets/Global.asset) — the rules that apply under EVERY
        // ruleset. Same list/detail UI as the Rules tab, always bound to the Global asset (never the live
        // host). A ruleset overrides a global single-instance rule only by ENABLING its own copy of that type
        // (see RulesHost.BuildLive). Rules are listed DISABLED by default here — global rules are opt-in.

        RuleSet _globalSet;

        RuleSet GetGlobalSet()
        {
            if (_globalSet == null)
                _globalSet = AssetDatabase.LoadAssetAtPath<RuleSet>($"{RulesetDir}/{RuleSetManager.GlobalName}.asset");
            return _globalSet;
        }

        // Like EnsureAllRuleTypes, but every newly-added rule is left DISABLED (the Rules-tab variant honours
        // each rule's DefaultActive, which would silently turn rules on globally). Global rules are opt-in.
        static void EnsureAllRuleTypesDisabled(RuleSet set)
        {
            if (set == null) return;
            var present = new HashSet<Type>(set.Rules.Where(r => r != null).Select(r => r.GetType()));
            bool added = false;
            foreach (var t in AllRuleTypes())
            {
                if (present.Contains(t)) continue;
                if (Activator.CreateInstance(t) is GameRule rule)
                {
                    rule.ResetToDefault();
                    rule.Active = false;
                    set.Rules.Add(rule);
                    added = true;
                }
            }
            if (added) EditorUtility.SetDirty(set);
        }

        void DrawGlobalRulesTab()
        {
            EditorGUILayout.LabelField("The GLOBAL ruleset applies under EVERY ruleset. Enable a rule here to make it global. A ruleset overrides a global (single-instance) rule only by ENABLING its own copy of that rule type. Edits save to the asset and take effect on the next RulesHost load.", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(4);

            var set = GetGlobalSet();
            if (set == null)
            {
                EditorGUILayout.HelpBox("No Global ruleset yet. Create one to start adding global rules.", MessageType.Info);
                if (GUILayout.Button("Create Global ruleset")) CreateGlobalSet();
                return;
            }

            EnsureAllRuleTypesDisabled(set); // list every rule type (disabled); toggle the ones you want global
            _owner = set;
            var rules = set.Rules.Where(r => r != null).OrderBy(r => r.GetType().Name).ToArray();

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(210f));
            DrawLeftPanel(rules);
            GUILayout.EndVertical();
            GUILayout.Space(6);
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            DrawRightPanel(rules);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        void CreateGlobalSet()
        {
            Directory.CreateDirectory(RulesetDir);
            var set = ScriptableObject.CreateInstance<RuleSet>();
            EnsureAllRuleTypesDisabled(set); // one of every rule, all disabled
            AssetDatabase.CreateAsset(set, $"{RulesetDir}/{RuleSetManager.GlobalName}.asset");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            _globalSet = set;
        }

        // ── Rulesets tab ────────────────────────────────────────────────────────────

        const string RulesetDir = "Assets/Resources/Rulesets";

        // Per-ruleset list — EXCLUDES the Global ruleset (it isn't a selectable ruleset; edit it in the
        // "Global Rules" tab).
        static List<(string path, RuleSet asset)> AllRuleSets() =>
            AssetDatabase.FindAssets("t:RuleSet")
                .Select(g => AssetDatabase.GUIDToAssetPath(g))
                .Select(p => (p, asset: AssetDatabase.LoadAssetAtPath<RuleSet>(p)))
                .Where(x => x.asset != null && x.asset.name != RuleSetManager.GlobalName)
                .OrderBy(x => x.asset.name)
                .ToList();

        void DrawRulesetsTab()
        {
            var host = UnityEngine.Object.FindAnyObjectByType<RulesHost>(FindObjectsInactive.Include);
            var applied = host != null ? host.SourceSet : null;

            EditorGUILayout.LabelField("Rulesets are RuleSet assets in Resources/Rulesets. \"Apply\" points the scene's RulesHost at one (and hot-swaps it in Play). Edit a ruleset by Applying it, then using the Rules tab.", EditorStyles.wordWrappedMiniLabel);
            if (host == null)
                EditorGUILayout.HelpBox("No RulesHost in the open scene — Apply has nowhere to attach.", MessageType.Info);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("New empty", GUILayout.Width(80))) CreateRuleset(null);
            using (new EditorGUI.DisabledScope(applied == null))
                if (GUILayout.Button("New from current", GUILayout.Width(120))) CreateRuleset(applied);
            if (GUILayout.Button("Save to disk", GUILayout.Width(90))) AssetDatabase.SaveAssets();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            var sets = AllRuleSets();
            if (sets.Count == 0) { EditorGUILayout.HelpBox("No rulesets yet — create one above.", MessageType.Info); return; }

            _rulesetsScroll = GUILayout.BeginScrollView(_rulesetsScroll);
            foreach (var (path, asset) in sets)
            {
                bool isApplied = asset == applied;
                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.BeginHorizontal();

                if (_renamingPath == path)
                {
                    _renameBuffer = EditorGUILayout.TextField(_renameBuffer);
                    if (GUILayout.Button("OK", GUILayout.Width(32))) { RenameRuleset(path, _renameBuffer); _renamingPath = null; }
                    if (GUILayout.Button("✕", GUILayout.Width(24))) _renamingPath = null;
                }
                else
                {
                    int activeN = asset.Rules.Count(r => r != null && r.Active);
                    string label = (isApplied ? "● " : "") + asset.name + $"   ({activeN}/{asset.Rules.Count} on)";
                    EditorGUILayout.LabelField(label, isApplied ? EditorStyles.boldLabel : EditorStyles.label);
                    using (new EditorGUI.DisabledScope(isApplied || host == null))
                        if (GUILayout.Button("Apply", GUILayout.Width(48))) ApplyRuleset(host, asset);
                    if (GUILayout.Button(_peekPath == path ? "Hide" : "Peek", GUILayout.Width(44)))
                        _peekPath = _peekPath == path ? null : path;
                    if (GUILayout.Button("Dup", GUILayout.Width(38))) DuplicateRuleset(path);
                    if (GUILayout.Button("Rename", GUILayout.Width(56))) { _renamingPath = path; _renameBuffer = asset.name; }
                    using (new EditorGUI.DisabledScope(isApplied))
                        if (GUILayout.Button("Del", GUILayout.Width(36))) DeleteRuleset(path);
                }
                GUILayout.EndHorizontal();

                if (_peekPath == path)
                {
                    EditorGUI.indentLevel++;
                    var active = asset.Rules.Where(r => r != null && r.Active).OrderBy(r => r.GetType().Name).ToList();
                    if (active.Count == 0) EditorGUILayout.LabelField("(no active rules)", EditorStyles.miniLabel);
                    foreach (var r in active) EditorGUILayout.LabelField("• " + SafeDescribe(r), EditorStyles.wordWrappedMiniLabel);
                    EditorGUI.indentLevel--;
                }
                GUILayout.EndVertical();
                GUILayout.Space(2);
            }
            GUILayout.EndScrollView();
        }

        void ApplyRuleset(RulesHost host, RuleSet asset)
        {
            if (host == null || asset == null) return;
            host.SourceSet = asset;
            EditorUtility.SetDirty(host);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(host.gameObject.scene);
            if (Application.isPlaying) host.LoadSet(asset);
            _selectedType = null;
        }

        void CreateRuleset(RuleSet source)
        {
            Directory.CreateDirectory(RulesetDir);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{RulesetDir}/RuleSet.asset");
            if (source != null)
            {
                AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path);
            }
            else
            {
                var set = ScriptableObject.CreateInstance<RuleSet>();
                EnsureAllRuleTypes(set);
                AssetDatabase.CreateAsset(set, path);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        void DuplicateRuleset(string path)
        {
            AssetDatabase.CopyAsset(path, AssetDatabase.GenerateUniqueAssetPath(path));
            AssetDatabase.SaveAssets();
        }

        void DeleteRuleset(string path)
        {
            if (EditorUtility.DisplayDialog("Delete ruleset", $"Delete '{Path.GetFileNameWithoutExtension(path)}'? This can't be undone.", "Delete", "Cancel"))
            { AssetDatabase.DeleteAsset(path); if (_peekPath == path) _peekPath = null; }
        }

        void RenameRuleset(string path, string newName)
        {
            if (!string.IsNullOrWhiteSpace(newName)) AssetDatabase.RenameAsset(path, newName.Trim());
            AssetDatabase.SaveAssets();
        }

        // ── helpers ─────────────────────────────────────────────────────────────────
        // In edit mode, persist immediately to disk so a recompile/domain-reload can't revert the tweak.
        // (Play-mode edits act on the live clone — no asset to save, and they reset when you stop.)
        void MarkDirty()
        {
            if (_owner == null) return;
            EditorUtility.SetDirty(_owner);
            if (!Application.isPlaying) AssetDatabase.SaveAssets();
        }
        static string SafeDescribe(GameRule r) { try { return r.Describe(); } catch { return r.GetType().Name; } }
        static string SafeRuntimeInfo(GameRule r) { try { return r.RuntimeInfo(); } catch { return null; } }
        static string SafeBrief(GameRule r) { try { return r.Brief(); } catch { return null; } }
    }
}
