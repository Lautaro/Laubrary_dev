# Laubrary.Rulesets

A game-agnostic **rules-as-data** system: every fundamental or tweakable mechanic is a small serializable
data object (a *rule*) with a description, category, tags, and live-editable fields. Rules are collected into
*rulesets* (preset assets), edited in a dedicated editor window, and driven at runtime by a single host.

No Odin, no ZUI — plain `EditorWindow` + reflection. Concrete rules live in **your game's** assembly; the
plugin only ships the generic base types and the editor.

## Modules

- **Runtime** (`com.Lautaro-Arino.Laubrary.Rulesets`, namespace `Laubrary.Rulesets`)
  `GameRule`, `RuleSet`, `RulesHost`, `RuleSetManager`, the `[RuleCategory]`/`[RuleTags]` attributes, and
  `RuleTaxonomy`.
- **Editor** (`com.Lautaro-Arino.Laubrary.Rulesets.Editor`, namespace `Laubrary.Rulesets.Editor`)
  `RulesEditorWindow` — the browse/tune window. References the runtime asmdef.

## How to add a rule

Subclass `GameRule` in your **game's** assembly (any assembly that references the runtime asmdef). A rule is a
plain `[Serializable]` data class — no MonoBehaviour. Override the lifecycle hooks you need and implement the
mandatory `Describe()`:

```csharp
using Laubrary.Rulesets;
using UnityEngine;

[System.Serializable]
[RuleCategory("Pieces/Timing")]          // optional — drives the editor's category tree
[RuleTags("piece", "speed", "fall")]     // optional — drives the editor's filter chips
public class FallSpeedRule : GameRule
{
    [Range(0.1f, 10f)] public float CellsPerSecond = 2f;

    protected override void OnResetToDefault() => CellsPerSecond = 2f;   // strict-default literal

    public override string Describe() => $"Pieces fall {CellsPerSecond:0.#} cells/sec.";

    // Optional lifecycle hooks (override only what you need):
    //   OnLoad / OnUnload  — run for EVERY rule regardless of Active (registry singletons).
    //   OnActivate         — once when the rule starts running.
    //   Tick(dt)           — every frame while running.
    //   OnDeactivate       — once when the rule stops running.
}
```

Field types the editor renders by reflection: `float`, `int` (with `[Range]` → sliders), `bool`, `string`,
enums, `Color`, `Vector2`, `Vector2Int`, `UnityEngine.Object` references, and `List<>` of any of those (or of
nested `[Serializable]` classes). Mark a field `[System.NonSerialized]` or `[HideInInspector]` to hide it.

### Categories & tags

Two ways, attributes win:

1. **Attributes** (above): `[RuleCategory("A/B")]` and `[RuleTags("x","y")]` on the rule class.
2. **Central registration** (optional) — if you'd rather keep one table than annotate each class, call this at
   startup (e.g. from a `[RuntimeInitializeOnLoadMethod]` or editor `[InitializeOnLoadMethod]`):

   ```csharp
   RuleTaxonomy.Register("FallSpeedRule", "Pieces/Timing", "piece", "speed", "fall");
   ```

A rule with neither lands under **"Misc"** with no tags.

## How to open the editor

**Tools ▸ Laubrary ▸ Rules Editor.**

- The window discovers rule types across **all loaded assemblies**, so it finds the concrete rules in your
  game even though `GameRule` lives in the plugin. The scan is cached per domain reload.
- **Rules tab** — left: search, category tree, state filter (All/Enabled/Disabled), tag chips. Right: the
  filtered rules, each with an on/off toggle and an expandable field editor. "Reset to default" restores a
  rule's strict defaults.
- The window operates on the **live** rules: in Play mode it edits the running `RulesHost`; in edit mode it
  edits the authored `RulesHost.SourceSet` asset. Edit-mode tweaks are saved to disk immediately (survive a
  recompile/domain reload); play-mode tweaks act on the live clone and reset on Stop.

## How rulesets save / load

A **ruleset** is one `RuleSet` ScriptableObject asset — a `[SerializeReference]` polymorphic list of
`GameRule` instances. Create rulesets via **Assets ▸ Create ▸ Laubrary ▸ Rule Set**, or from the window's
**Rulesets tab** ("New empty" / "New from current"). The tab lists every `RuleSet` in the project, lets you
**Apply** one to the scene's `RulesHost` (hot-swaps it in Play), and Peek / Duplicate / Rename / Delete.

### Wiring it into a scene

1. Add a `RulesHost` MonoBehaviour to a boot GameObject (it runs at `DefaultExecutionOrder(-2000)`).
2. Assign its `SourceSet` to a `RuleSet` asset (or Apply one from the Rulesets tab).

At runtime `RulesHost` clones `SourceSet`, drives each rule's lifecycle (activating/deactivating as the
`Active` flag flips, ticking the running ones), and exposes lookups: `RulesHost.Rule<T>()`,
`host.Get<T>()`, `GameRule.ActiveRules()`.

For runtime preset switching, place ruleset assets under **`Resources/Rulesets/`** and call
`RuleSetManager.Load("Standard")` — it loads the named asset and swaps the live host over to it.

> Runtime persistence is currently Resources-only. SaveGame integration (a player's tuned ruleset surviving
> across sessions) is deferred.

## Global rules

A single optional **Global ruleset** (`Resources/Rulesets/Global.asset`) holds rules that apply under **every**
ruleset, so you don't repeat them in each preset. At boot/load, `RulesHost.BuildLive` merges the chosen
ruleset with the Global one:

- **Multi-instance rules** (`AllowMultiple == true`) from both sets always coexist.
- **Single-instance rules** (`AllowMultiple == false`) end up with exactly one live instance per type, with
  precedence: the chosen ruleset's **enabled** copy → the Global copy → the chosen ruleset's disabled copy.
  In other words, a ruleset overrides a global rule **only by enabling its own copy of that rule type**. A
  merely-present-but-disabled copy (e.g. an editor placeholder the Rules tab auto-adds) does **not** override —
  the global still applies.

Edit the Global ruleset in the **Global Rules tab** of the Rules Editor (it's listed separately from the
per-preset Rulesets, and its rules are listed disabled by default — global rules are opt-in). If no
`Global.asset` exists, `BuildLive` is just the chosen ruleset, so the feature is zero-cost when unused.
