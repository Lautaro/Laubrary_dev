// RulesEditorWindow.cs
// One window to browse + tune every game rule. Left: a category tree (built from RuleTaxonomy) with a
// search box and tag chips. Right: the filtered rule list and the selected rule's fields, rendered by
// reflection. A Rulesets tab manages the RuleSet assets in Resources/Rulesets and points the scene's
// RulesHost at one (hot-swapping it in Play).
//
// Operates on the live rules: the running RulesHost in Play mode, else the authored host's SourceSet asset.
//
// UI TOOLKIT PORT (ZUI → UI Toolkit migration): fully native, and it closes all three of this file's old
// `// ZUI-GAP:` markers, because UI Toolkit solves natively what IMGUI could not:
//   • "typed object field by runtime Type" → ZuiReflect.ObjectByType (ObjectField.objectType is settable,
//     so no compile-time generic is needed);
//   • "helpBox-framed list row / detail panel / card" → Z.Box;
//   • the whole reflection-driven field renderer now lives in the shared ZuiReflect, so any other
//     data-driven tool gets it too instead of this window hand-rolling it.
//
// CROSS-ASSEMBLY DISCOVERY: GameRule lives in the Laubrary.Rulesets plugin assembly, but concrete rule
// subclasses live in the consuming game's assembly. AllRuleTypes() therefore scans EVERY loaded assembly
// (AppDomain) for non-abstract GameRule subclasses — not just GameRule's own assembly.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Rulesets.Editor
{
    public class RulesEditorWindow : ZuiWindow
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

        string _renamingPath, _renameBuffer, _peekPath;
        UnityEngine.Object _owner; // what to SetDirty after an edit (the RuleSet asset in edit mode)

        // Play mode changes what the window shows (live host vs authored asset) and rules report live
        // info — so refresh on a slow tick rather than the IMGUI original's every-frame Repaint.
        void OnEnable()
        {
            rootVisualElement.schedule.Execute(() =>
            {
                if (Application.isPlaying) Rebuild();
            }).Every(500);
        }

        protected override void BuildUI(VisualElement root)
        {
            root.Add(Z.MiniRadio((int)_tab, new[] { "Rules", "Rulesets", "Global Rules" },
                "Rules = tune the active ruleset. Rulesets = manage RuleSet assets. Global Rules = the rules that apply under EVERY ruleset.",
                v => { _tab = (Tab)v; Rebuild(); }));
            root.Add(Z.VSpace(4f));

            if (_tab == Tab.Rules) BuildRulesTab(root);
            else if (_tab == Tab.Rulesets) BuildRulesetsTab(root);
            else BuildGlobalRulesTab(root);
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

        void BuildRulesTab(VisualElement root)
        {
            var rules = CurrentRules();
            if (rules.Length == 0)
            {
                root.Add(Z.Help("No rules found. In Play mode this shows the live RulesHost; in edit mode it shows " +
                    "the authored RulesHost's RuleSet asset. Open a scene with a RulesHost (and define some GameRule " +
                    "subclasses) to edit rules.", HelpBoxMessageType.Info));
                return;
            }
            BuildTwoPane(root, rules);
        }

        void BuildTwoPane(VisualElement root, GameRule[] rules)
        {
            var split = new VisualElement();
            split.style.flexDirection = FlexDirection.Row;
            split.style.flexGrow = 1f;
            split.style.minHeight = 0f;

            var left = new VisualElement();
            left.style.width = 210f;
            left.style.flexShrink = 0f;
            left.style.minHeight = 0f;
            BuildLeftPanel(left, rules);
            split.Add(left);

            split.Add(Z.HSpace(6f));

            var right = new VisualElement();
            right.style.flexGrow = 1f;
            right.style.minWidth = 0f;
            right.style.minHeight = 0f;
            BuildRightPanel(right, rules);
            split.Add(right);

            root.Add(split);
        }

        void BuildLeftPanel(VisualElement root, GameRule[] rules)
        {
            root.Add(Z.Text("Search", ZuiText.Section, "Filter rules by name or description."));
            root.Add(Z.TextInput(_search, "Filter rules by name or description text.",
                v => { _search = v; RebuildRight(); }, 200f));

            root.Add(Z.VSpace(4f));
            root.Add(Z.Text("Categories", ZuiText.Section,
                "Rule categories from RuleTaxonomy. Right-click a category to isolate it (drops every other filter)."));

            var cats = rules.Select(r => RuleTaxonomy.CategoryOf(r.GetType())).Distinct().OrderBy(c => c).ToList();
            int enabledN = rules.Count(r => r.Active);
            int disabledN = rules.Length - enabledN;

            var catScroll = new ScrollView(ScrollViewMode.Vertical);
            catScroll.style.height = 260f;
            catScroll.Add(CategoryRow("All", _selectedCategory == "All" && _stateFilter == 0, 0,
                "Show every rule (clears all filters).",
                () => { _selectedCategory = "All"; _stateFilter = 0; _activeTags.Clear(); Rebuild(); },
                () => { _selectedCategory = "All"; _stateFilter = 0; _activeTags.Clear(); Rebuild(); }));
            catScroll.Add(CategoryRow($"Enabled ({enabledN})", _stateFilter == 1, 1,
                "Show only rules that are currently switched on.",
                () => { _stateFilter = _stateFilter == 1 ? 0 : 1; Rebuild(); }, null));
            catScroll.Add(CategoryRow($"Disabled ({disabledN})", _stateFilter == 2, 1,
                "Show only rules that are currently switched off.",
                () => { _stateFilter = _stateFilter == 2 ? 0 : 2; Rebuild(); }, null));
            catScroll.Add(Z.VSpace(4f));
            foreach (var cat in cats)
            {
                string captured = cat;
                catScroll.Add(CategoryRow(captured, _selectedCategory == captured, 1,
                    $"Show rules in the '{captured}' category. Right-click to isolate it.",
                    () => { _selectedCategory = captured; Rebuild(); },
                    () => { IsolateCategory(captured); Rebuild(); }));
            }
            root.Add(catScroll);

            root.Add(Z.VSpace(4f));
            root.Add(Z.Text("Tags", ZuiText.Section,
                "Rule tags from RuleTaxonomy. Click to filter; right-click a tag to isolate it."));
            var allTags = rules.SelectMany(r => RuleTaxonomy.TagsOf(r.GetType()))
                               .Concat(new[] { "enabled", "disabled" })
                               .Distinct().OrderBy(t => t).ToArray();
            root.Add(BuildTagChips(allTags));
            if (_activeTags.Count > 0)
                root.Add(Z.Button("clear tags", "Remove every active tag filter.",
                    () => { _activeTags.Clear(); Rebuild(); }));
        }

        /// A selectable list row. Left-click runs `onClick`; right-click runs `onIsolate` when given
        /// (the IMGUI original used ZUI.SelectableRow's out-param for the same two-button behaviour).
        VisualElement CategoryRow(string label, bool selected, int indent, string tooltip,
            Action onClick, Action onIsolate)
        {
            var row = new VisualElement();
            row.AddToClassList("zui-row");
            row.style.paddingLeft = 4f + indent * 12f;
            row.tooltip = tooltip;
            if (selected) row.style.backgroundColor = new Color(0.35f, 0.55f, 0.95f, 0.22f);

            var text = new Label(label) { tooltip = tooltip };
            if (selected) text.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(text);

            row.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 1 && onIsolate != null) { onIsolate(); e.StopPropagation(); }
                else if (e.button == 0) { onClick?.Invoke(); e.StopPropagation(); }
            });
            return row;
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

        VisualElement BuildTagChips(string[] tags)
        {
            var wrap = new VisualElement();
            wrap.style.flexDirection = FlexDirection.Row;
            wrap.style.flexWrap = Wrap.Wrap;
            foreach (var tag in tags)
            {
                string captured = tag;
                bool on = _activeTags.Contains(captured);
                var chip = new Button(() =>
                {
                    if (_activeTags.Contains(captured)) _activeTags.Remove(captured);
                    else _activeTags.Add(captured);
                    Rebuild();
                })
                {
                    text = captured,
                    tooltip = $"Filter by the '{captured}' tag. Right-click to show ONLY this tag.",
                };
                chip.style.marginRight = 2f;
                chip.style.marginBottom = 2f;
                if (on) chip.AddToClassList("zui-radio__on");
                chip.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button != 1) return;
                    IsolateTag(captured);
                    Rebuild();
                    e.StopPropagation();
                });
                wrap.Add(chip);
            }
            return wrap;
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

        VisualElement _rightHost;
        GameRule[] _rightRules = Array.Empty<GameRule>();

        void RebuildRight()
        {
            if (_rightHost == null) { Rebuild(); return; }
            _rightHost.Clear();
            BuildRightList(_rightHost, _rightRules);
        }

        void BuildRightPanel(VisualElement root, GameRule[] rules)
        {
            _rightRules = rules;
            _rightHost = new VisualElement();
            _rightHost.style.flexGrow = 1f;
            _rightHost.style.minHeight = 0f;
            BuildRightList(_rightHost, rules);
            root.Add(_rightHost);
        }

        void BuildRightList(VisualElement root, GameRule[] rules)
        {
            var filtered = rules.Where(PassesFilter).ToArray();
            // When a filter narrows the view to exactly ONE rule, open it automatically (once per new
            // single result; the user can still collapse it, and it won't re-pop every rebuild).
            if (filtered.Length == 1)
            {
                string only = filtered[0].GetType().Name;
                if (_lastSingle != only) { _selectedType = only; _lastSingle = only; }
            }
            else _lastSingle = null;

            string state = _stateFilter == 1 ? " · Enabled" : _stateFilter == 2 ? " · Disabled" : "";
            root.Add(Z.Text($"{filtered.Length} rule(s)  ·  {_selectedCategory}{state}", ZuiText.Small,
                "How many rules match the current filters."));

            bool playing = Application.isPlaying;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;

            foreach (var r in filtered)
            {
                var rule = r;
                string typeName = rule.GetType().Name;
                bool isSel = _selectedType == typeName;

                var card = Z.Box(null, null);
                var header = new VisualElement();
                header.AddToClassList("zui-row");

                header.Add(Z.Toggle("", $"Switch the {typeName} rule on or off.", rule.Active, v =>
                {
                    rule.Active = v;
                    MarkDirty();
                    RebuildRight();
                }));

                var nameButton = new Button(() =>
                {
                    _selectedType = isSel ? null : typeName;
                    RebuildRight();
                })
                {
                    text = typeName,
                    tooltip = SafeDescribe(rule),
                };
                nameButton.style.unityFontStyleAndWeight = isSel ? FontStyle.Bold : FontStyle.Normal;
                header.Add(nameButton);

                if (playing && rule.Active)
                {
                    string info = SafeRuntimeInfo(rule);
                    if (!string.IsNullOrEmpty(info))
                        header.Add(Z.Text(info.Replace("\n", "   "), ZuiText.Small,
                            "Live runtime state reported by this rule."));
                }
                header.Add(Z.Flexible());
                card.Add(header);

                if (isSel) BuildRuleDetail(card, rule);
                scroll.Add(card);
            }
            root.Add(scroll);
        }

        void BuildRuleDetail(VisualElement root, GameRule r)
        {
            var panel = Z.Box(null, null);
            panel.Add(Z.Text(SafeDescribe(r), ZuiText.Small, "What this rule does."));
            var brief = SafeBrief(r);
            if (!string.IsNullOrEmpty(brief))
                panel.Add(Z.Text("Brief: " + brief.Replace("\n", " / "), ZuiText.Small, "The rule's short summary."));
            if (Application.isPlaying)
            {
                string info = SafeRuntimeInfo(r);
                if (!string.IsNullOrEmpty(info))
                    panel.Add(Z.Text(info, ZuiText.Small, "Live runtime state reported by this rule."));
            }

            // Every tunable field, rendered by reflection through the shared ZuiReflect — this is what
            // used to be this file's own DrawField/DrawList pair plus three ZUI-GAP workarounds.
            ZuiReflect.BuildFields(panel, r, new ZuiReflect.Options
            {
                // Undo the OWNING asset: a GameRule is a plain object living inside the RuleSet, so the
                // ScriptableObject is what Unity can restore. In Play mode `_owner` is null (the rules are
                // a live clone with no asset behind them) and there is correctly nothing to record.
                OnBeforeChange = RecordOwner,
                OnChanged = MarkDirty,
                OnStructureChanged = RebuildRight,
                FloatWrapperProperty = RuleParams.StaticValueProp,
                TooltipFor = f => $"{ObjectNames.NicifyVariableName(f.Name)} — a tunable of the {r.GetType().Name} rule.",
            });

            panel.Add(Z.Button("Reset to default", "Restore every field of this rule to its coded default.", () =>
            {
                r.ResetToDefault();
                MarkDirty();
                RebuildRight();
            }));
            root.Add(panel);
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

        void BuildGlobalRulesTab(VisualElement root)
        {
            root.Add(Z.Text("The GLOBAL ruleset applies under EVERY ruleset. Enable a rule here to make it global. " +
                "A ruleset overrides a global (single-instance) rule only by ENABLING its own copy of that rule type. " +
                "Edits save to the asset and take effect on the next RulesHost load.", ZuiText.Subtle,
                "How the global ruleset relates to per-ruleset rules."));
            root.Add(Z.VSpace(4f));

            var set = GetGlobalSet();
            if (set == null)
            {
                root.Add(Z.Help("No Global ruleset yet. Create one to start adding global rules.", HelpBoxMessageType.Info));
                root.Add(Z.Button("Create Global ruleset", "Create the Global RuleSet asset in Resources/Rulesets.",
                    () => { CreateGlobalSet(); Rebuild(); }));
                return;
            }

            EnsureAllRuleTypesDisabled(set); // list every rule type (disabled); toggle the ones you want global
            _owner = set;
            var rules = set.Rules.Where(r => r != null).OrderBy(r => r.GetType().Name).ToArray();
            BuildTwoPane(root, rules);
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

        void BuildRulesetsTab(VisualElement root)
        {
            var host = UnityEngine.Object.FindAnyObjectByType<RulesHost>(FindObjectsInactive.Include);
            var applied = host != null ? host.SourceSet : null;

            root.Add(Z.Text("Rulesets are RuleSet assets in Resources/Rulesets. \"Apply\" points the scene's RulesHost " +
                "at one (and hot-swaps it in Play). Edit a ruleset by Applying it, then using the Rules tab.",
                ZuiText.Subtle, "What a ruleset is and how Apply works."));
            if (host == null)
                root.Add(Z.Help("No RulesHost in the open scene — Apply has nowhere to attach.", HelpBoxMessageType.Warning));

            var newFromCurrent = Z.Button("New from current", "Duplicate the applied ruleset into a new asset.",
                () => { CreateRuleset(applied); Rebuild(); });
            newFromCurrent.SetEnabled(applied != null);
            root.Add(Z.Row(
                Z.Button("New empty", "Create a ruleset containing one of every rule type, at their defaults.",
                    () => { CreateRuleset(null); Rebuild(); }),
                newFromCurrent,
                Z.Button("Save to disk", "Flush every pending ruleset edit to disk.", AssetDatabase.SaveAssets)));
            root.Add(Z.VSpace(4f));

            var sets = AllRuleSets();
            if (sets.Count == 0)
            {
                root.Add(Z.Help("No rulesets yet — create one above.", HelpBoxMessageType.Info));
                return;
            }

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;

            foreach (var (path, asset) in sets)
            {
                string capturedPath = path;
                var capturedAsset = asset;
                bool isApplied = capturedAsset == applied;

                var card = Z.Box(null, null);
                var row = new VisualElement();
                row.AddToClassList("zui-row");

                if (_renamingPath == capturedPath)
                {
                    var nameField = Z.TextInput(_renameBuffer, "New name for this ruleset asset.",
                        v => _renameBuffer = v, 120f);
                    row.Add(nameField);
                    row.Add(Z.Button("OK", "Apply the rename.", () =>
                    {
                        RenameRuleset(capturedPath, _renameBuffer);
                        _renamingPath = null;
                        Rebuild();
                    }).W(32f));
                    row.Add(Z.Button("X", "Cancel renaming.", () => { _renamingPath = null; Rebuild(); }).W(24f));
                }
                else
                {
                    int activeN = capturedAsset.Rules.Count(r => r != null && r.Active);
                    string label = (isApplied ? "● " : "") + capturedAsset.name + $"   ({activeN}/{capturedAsset.Rules.Count} on)";
                    var title = Z.Text(label, isApplied ? ZuiText.Section : ZuiText.Body,
                        isApplied ? "The ruleset currently applied to the scene's RulesHost." : "A ruleset asset.");
                    row.Add(title);
                    row.Add(Z.Flexible());

                    var applyBtn = Z.Button("Apply", "Point the scene's RulesHost at this ruleset (hot-swaps in Play).",
                        () => { ApplyRuleset(host, capturedAsset); Rebuild(); }).W(48f);
                    applyBtn.SetEnabled(!isApplied && host != null);
                    row.Add(applyBtn);

                    row.Add(Z.Button(_peekPath == capturedPath ? "Hide" : "Peek",
                        "Show this ruleset's active rules without applying it.",
                        () => { _peekPath = _peekPath == capturedPath ? null : capturedPath; Rebuild(); }).W(44f));
                    row.Add(Z.Button("Dup", "Duplicate this ruleset asset.",
                        () => { DuplicateRuleset(capturedPath); Rebuild(); }).W(38f));
                    row.Add(Z.Button("Rename", "Rename this ruleset asset.", () =>
                    {
                        _renamingPath = capturedPath;
                        _renameBuffer = capturedAsset.name;
                        Rebuild();
                    }).W(56f));

                    var delBtn = Z.Button("Del", "Delete this ruleset asset (asks first).",
                        () => { DeleteRuleset(capturedPath); Rebuild(); }).W(36f);
                    delBtn.SetEnabled(!isApplied);
                    row.Add(delBtn);
                }
                card.Add(row);

                if (_peekPath == capturedPath)
                {
                    var peek = new VisualElement();
                    peek.style.paddingLeft = 12f;
                    var active = capturedAsset.Rules.Where(r => r != null && r.Active)
                                                    .OrderBy(r => r.GetType().Name).ToList();
                    if (active.Count == 0)
                        peek.Add(Z.Text("(no active rules)", ZuiText.Small, "This ruleset has nothing switched on."));
                    foreach (var r in active)
                        peek.Add(Z.Text("• " + SafeDescribe(r), ZuiText.Small, "An active rule in this ruleset."));
                    card.Add(peek);
                }
                scroll.Add(card);
            }
            root.Add(scroll);
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

        /// Record the owning asset before a rule field changes, so Ctrl+Z restores it. `Undo.RecordObject`
        /// coalesces repeated identical records, so a slider drag stays ONE undo step.
        void RecordOwner()
        {
            if (_owner != null) Undo.RecordObject(_owner, "Edit rule");
        }
        static string SafeDescribe(GameRule r) { try { return r.Describe(); } catch { return r.GetType().Name; } }
        static string SafeRuntimeInfo(GameRule r) { try { return r.RuntimeInfo(); } catch { return null; } }
        static string SafeBrief(GameRule r) { try { return r.Brief(); } catch { return null; } }
    }
}
