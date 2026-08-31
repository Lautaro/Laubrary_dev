# T-0092 + T-0093 — implementation plan

**What this is:** a ready-to-execute plan for AgentHQ tasks **T-0092** ("make the ask-a-character-to-show-a-named-state mechanism trustworthy") and **T-0093** ("let game code choose which named state a weapon shot asks for"), which are steps 3 and 4 of `ZOE_PALETTE_BUILD_PLAN.md`. Written by the T-0089 Plan subagent, 2026-08-26, against branch `feat/lathe`. **No Unity editor was touched to produce it** — everything below is read from the working tree.

**Baseline.** Every claim here was checked against the **current working tree**, not `HEAD`. Another session's uncommitted T-0090/T-0091/T-0094 work (death linger, revive colliders, interruption arbitration) is already in the tree and this plan builds on it: `ReactionFxPlayer.HurtFinished`/`DeathFinished` now carry `bool interrupted`, `ReactionFxPlayer` has `EventFinished(string,bool)`/`EventRefused(string)`, `AnimationArbiter` is now attached to every Zoe by `ZoeSpawner`, and `TryArmClip` returns a three-valued `ArmResult`. Nothing in this plan changes any of those signatures.

---

## 1. Prior art, ground-truthed

The task descriptions carry claims from the earlier analysis round flagged "verify before relying on". Here is each one against the actual source.

| Claim | Verdict | Evidence |
|---|---|---|
| `ReactionFxPlayer.Raise` / `Zoe.EventNamed` / `NamedReaction` are the existing by-name request path | **HOLDS** | `Runtime/Zoetrope/ReactionFxPlayer.cs` `Raise(string)` / `Raise(string, DamageInfo)` → `Zoe.EventNamed(id)` → `NamedReaction.reaction` (a whole `ReactionFx`). `Raise` runs the identical sequence `OnHit`/`OnDeath` run: `BuildContext` → `EventSecondsOf` → `PlayBodyFx` → `FireImmediate` → `TryArmClip(..., PriorityNamedState, ...)`. |
| `WeaponMuzzleCue.cs` already wires `ProjectileWeapon.Fired` and `HitscanFired` to `Raise(FireEventId)`, with `FireEventId` a hardcoded `"Fire"` | **HOLDS, exactly, including the line numbers** | `Runtime/Zoetrope/WeaponMuzzleCue.cs` line 53 `public const string FireEventId = "Fire";`, lines 140-143 subscribe both `weapon.Fired` and `weapon.HitscanFired` to `…GetComponentInParent<ReactionFxPlayer>()?.Raise(FireEventId)`. Note this wiring is **itself uncommitted** (part of the same working-tree delta), so it is real but not yet in `HEAD`. |
| Something already warns on a missed name; the editor already badges it | **HOLDS, and it is narrower than "there is a warning"** | Exactly ONE runtime warning exists: `Runtime/ZoetropeLaunimator/CueRelay.cs` `Fire()` logs `[Cue] Frame cue tried to raise '…' but this character declares no such event.` Editor badges: `Editor/Zoetrope/ZoetropeWindows.cs` `EventIdIssue` (empty id, duplicate id) and `BuildRaisePicker`'s `! no such event` badge for a cue pointing at an undeclared id. **The gap is exactly as described**: neither message lists what the character *does* declare, and `WeaponMuzzleCue`'s direct `Raise(FireEventId)` discards the returned `bool` entirely — a missed name from code is 100% silent. |
| Case-sensitivity is inconsistent between state names and other authored names | **HOLDS** | `Zoe.EventNamed` compares with `events[i].id == id` (ordinal, case-**sensitive**), and its own doc comment says so. Everything else authored in the same window ignores case: `CueRelay.Matches` and `CueRelay.TryFireLayer` use `StringComparison.OrdinalIgnoreCase` for frame-event names and meta-layer ids; `ZoetropeWindows` builds its sprite/animation/frame/world-position lookups with `StringComparer.OrdinalIgnoreCase` and compares part names and meta-layer ids `OrdinalIgnoreCase`; `MirageWindow` matches clip names `OrdinalIgnoreCase`. Round three's correction #3 is accurate. **Also found:** the editor's own duplicate-id badge (`EventIdIssue`), `DeclaredEventIds`, `UniqueEventId` and `BuildRaisePicker`'s `declared.Contains(v)` / `shown.IndexOf(current)` are ALL case-sensitive too, so flipping only `EventNamed` would leave the editor unable to see a case-duplicate it had just made legal. |
| A raise carries no position/facing (the muzzle-position problem) | **HOLDS** | `Raise(string id)` forwards `default(DamageInfo)`. `BuildContext` then sets `HitPosition = info.point != Vector2.zero ? info.point : (Vector2)transform.position` → the character's anchor, and `HitDirection = Vector2.zero` → `ResolveDirectionDeg` returns `NaN`. So a Fire raise arrives with the anchor and no facing. |
| ProtoGuy dodges it by pinning to a painted muzzle point | **HOLDS** | `Assets/Demos/ProtoGuyDemo/ProtoGuy.asset`: the single `events:` entry is `id: Fire`, `durationMode: 1`, `seconds: 0.15`, with two `FxEntry` both `placement: 3` (`MetaPoint`) `metaLayerId: Muzzle` and `direction: 1` (`None`). ProtoGuy is immune to both the position change and the facing change. |
| `stun` is authorable but ignored | **HOLDS, and it is worse than stated** | `ReactionFx.stunSeconds` is read in exactly one place in the whole package: `ZoeSpawner.SpawnCharacter` line 107, `state.hitStun = def.hit != null ? def.hit.stunSeconds : 0f`. A named event's stun and death's stun are never read at all, and hit's is frozen at spawn — both halves of round three's claim. **Three further findings the task text does not mention:** (a) `stunSeconds` is **not drawn anywhere in the Zoe window** — `BuildReactionFx` draws clip/duration/effects only, so today the field is reachable only through Unity's default inspector; (b) the only thing `ZoeState.CanAct` actually gates is the two animators (`Locomotion.cs` line 94, `MotionPoseAnimator.cs` line 138) — `TopDownMotionDriver` and `ZoeWeaponDriver` never consult it, so the tooltip's "it stops moving and acting" is currently untrue; (c) every authored `stunSeconds` in this whole project is `0`, so the visible blast radius of turning stun on is **nil here** and lives entirely in the five consumer projects. |
| A feature elsewhere in the toolkit resolves a name to a bare clip and loses the card | **HOLDS** | `Runtime/Zoetrope/PlayLauminationEffect.cs` — `Apply(ctx)` is `ctx.View?.PlayClip(clip, loop)`. No duration model, no `bodyFx`, no effect list, **and no `AnimationArbiter` claim**, so it can also stomp a hurt or death reaction the arbiter is holding. The codebase already knows: `ZoetropeWindows.CueRaiseTip` says a cue's Raise picker gets "the WHOLE reaction (its clip, its body SpriteFx, its effect list), **which the Effect slot below cannot express on its own**". `MirageSubject`'s `ClipStep` list is a second bare-clip path, but that one is a preview harness deliberately previewing raw clips and is not the same mistake. |

**Two things the task text does not say that this plan acts on.** First, `ProjectileWeapon.FireInternal` raises `Fired` **once per projectile**, so a weapon with `projectilesPerShot: 3` currently raises the character's "Fire" state three times in one frame; with the new arbiter in the tree, two of those three are refused (`WouldAccept` rejects an equal-priority newcomer) while all three still fire the reaction's Immediate effects. Second, `ProjectileWeapon.TryHitscanAt` has **no caller anywhere in Laubrary** — hitscan is driven entirely by consumer game code, which is why the light-gun path cannot be traced from inside this project.

---

## 2. Decisions the owner must make (none of these block the work)

1. **What carries position/facing into a raise: a new `ReactionRequest` struct, or reuse of `DamageInfo`?** *Recommendation: a new `ReactionRequest`.* `DamageInfo` lives in Combat2D and means "one damage event" (amount, source, faction, crit); a fire pose is not a blow, and Raise's own doc comment already apologises for the mismatch. More practically, the build plan's steps 6 and 8 both need a raise to carry MORE later (an override name for the default-plus-override capability; the opaque parcel), and cutting that seam once now is cheaper than cutting it twice. Rejected alternative: widening `DamageInfo` — wrong module, and it changes a struct five projects pass around.
2. **Where the generated state-name file is written, and what the class is called.** *Recommendation: a sibling file next to the Zoe asset, `<AssetName>.states.cs`, holding `namespace ZoeStates { public static class <SanitizedName>States { … } }`.* A sibling file automatically joins whatever asmdef the asset already lives beside (`Assets/Demos/ProtoGuyDemo/` joins `Laubrary.Demos.asmdef`), which is exactly the assembly the code that uses that Zoe lives in. Rejected alternative: one central generated file, which would land in `Assembly-CSharp` and be invisible to any game asmdef.
3. **What happens to a generated file when its Zoe is deleted or renamed.** *Recommendation: delete/move it automatically, with a one-line log.* CLAUDE.md's confirm-dialog-before-asset-delete rule exists to protect **authored** data; a generated artifact whose source is gone is not authored data, and leaving it means code referencing a deleted character still compiles, which is precisely the lie the generator exists to prevent. A modal dialog raised from an asset postprocessor is also a wedged-editor hazard.
4. **How far "obeyed" goes for stun.** *Recommendation: this task makes stun be READ from whichever reaction actually played and applied live, and stops there.* Widening what `ZoeState.CanAct` gates (so a stunned character's mover and trigger also stop, not just its walk cycle) is a genuinely bigger behaviour change, it lands on `TopDownMotionDriver` which another session just rewrote, and it deserves its own task. Do correct the `ReactionFx.stunSeconds` tooltip in this task so it stops promising something it does not do.
5. **Whether `PlayLauminationEffect` gets fixed here or just documented.** *Recommendation: fix it minimally — route it through the `AnimationArbiter` at `PriorityNamedState` — and leave "replace it with a raise-a-state effect" to step 6.* Right now it is the one remaining way to make a Zoe animate without going through the arbiter, which quietly holes the T-0094 interruption work that just landed.
6. **Whether the fire-state chooser is a delegate or a discovered interface.** *Recommendation: a discovered interface (`IFireStateSource`), matching `ICueSink` / `IVectorAimSource` / `IActiveWeaponSource` / `IFlippableView` — the established capability-discovery pattern in this exact module.* A game that prefers a delegate wraps it in a three-line component.
7. **Whether an `IFireStateSource` that is present but answers nothing falls back to `"Fire"`.** *Recommendation: no.* If game code installed a chooser, game code is in charge; a hidden fallback is exactly the "something answered on its own" that the CLAUDE.md Zoe-palette rule forbids. The hardcoded `"Fire"` survives only as the no-chooser-present default, which is what keeps every existing project working untouched.

---

## 3. T-0092 — the steps

### S1 — Make name matching case-insensitive, everywhere at once

**Files and members.** `Runtime/Zoetrope/Zoe.cs` → `EventNamed(string id)`: replace `events[i].id == id` with `string.Equals(events[i].id, id, System.StringComparison.OrdinalIgnoreCase)`, and rewrite its doc comment (it currently advertises the opposite). `Editor/Zoetrope/ZoetropeWindows.cs` → `EventIdIssue` (both duplicate loops), `UniqueEventId` (`new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)`), `DeclaredEventIds` (case-insensitive de-dupe), `BuildRaisePicker` (both `declared.Contains(v)` and `shown.IndexOf(current)` must become case-insensitive, or the picker will show a correctly-resolving "fire" as "fire  (undeclared)"), and the `EventIdTip` constant, which literally says "Case-sensitive".

**Mechanism and why.** Ordinal-ignore-case, matching the comparison every other authored name in the same window already uses. Rejected alternative: making everything else case-**sensitive** instead — a strictly narrowing change that could silently stop an existing cue or meta-layer from resolving in a consumer project, which is the opposite of the loud failure this task is for.

**Byte-compatibility.** Nothing on disk changes. This is a strictly **widening** change to resolution: every lookup that matched before still matches, and only previously-failing near-misses start matching. No authored id is moved, renamed or reinterpreted.

**Do it first**, because S2 generated constants and S3 duplicate detection both have to agree with the matching rule.

### S2 — Generate a per-character code-side state-name list, regenerated on save

**New file.** `Editor/Zoetrope/ZoeStateNamesGenerator.cs` (asmdef `ZoetropeEditor`, which already references the Zoetrope runtime).

**Shape.** An `AssetPostprocessor` with `OnPostprocessAllAssets(imported, deleted, moved, movedFrom)`. For each imported path, test `AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(Zoe)` first (never `LoadAssetAtPath` on everything), then load, build the text, and write `<same folder>/<asset file name>.states.cs` **only if the text differs from what is already there** — an unconditional write recompiles the whole project on every save of anything.

**Generated content.** An auto-generated header naming the source asset and its GUID, then `namespace ZoeStates { public static class <Sanitized>States { public const string Fire = "Fire"; … public static readonly string[] All = { … }; } }`. Member names are the id sanitised to a C# identifier ("Shooting Lazer" becomes `ShootingLazer`); the string VALUE is always the id verbatim, so the constant is a safe substitute for the literal.

**Collisions.** Two ids that sanitise to the same member name, or (after S1) that differ only in case, are a duplicate: `Debug.LogError` naming the asset and both ids, emit the member once and put both strings in `All`. Two Zoe assets with the same file name in the same assembly would produce two identical class names: detect it during the generate pass (`AssetDatabase.FindAssets("t:Zoe")`), log an error naming both paths, and skip the second rather than emitting a file that cannot compile.

**Deletes and moves.** On a deleted Zoe, delete its orphaned `.states.cs`; on a move, delete the old one and write the new one (decision 3).

**Staleness is a hard error.** Add `[InitializeOnLoadMethod] static void VerifyAll()` in the same file: after every assembly reload, walk every Zoe, compare its generated text, and on any mismatch `Debug.LogError` naming the asset and regenerate. That covers a hand-edited or merge-mangled generated file, and it terminates (regenerating writes a `.cs`, the postprocessor ignores `.cs`, the recompile VerifyAll then matches and writes nothing).

**Two guards that matter.** Skip generation entirely while `EditorApplication.isPlayingOrWillChangePlaymode` and re-run on exiting play mode — writing a script during play kills the session. And note that the Zoe window `Commit` helper does `SerializedObject.ApplyModifiedProperties` only (no `AssetDatabase.SaveAssets`), so **the postprocessor fires on Ctrl+S, not on every keystroke** — which is exactly the "regenerated when the character asset is saved" the design asks for, and it is why this does not recompile while you are typing an id.

**Byte-compatibility.** The generated `.cs` files are new files in the host project. No authored asset is read-modify-written by this step at all.

### S3 — Make a missed name report loudly, and list the alternatives

**Files and members.** `Runtime/Zoetrope/ReactionFxPlayer.cs` → split `Raise` into a loud form and a quiet one over one shared private implementation; `Runtime/ZoetropeLaunimator/CueRelay.cs` → `Fire()` drops its own `[Cue]` warning and calls the loud form.

**Mechanism.** `public bool Raise(string id, …)` warns when the name misses; `public bool TryRaise(string id, …)` returns the same bool and never warns. Both call `RaiseInternal(id, req, warn)`. The warning is emitted once per id per component (a `readonly HashSet<string> _warnedMissing`, never cleared, so a full-auto weapon does not produce sixty lines a second) with `this` as the log context so clicking it selects the character, and it reads roughly: `[Zoe] 'ProtoGuy' was asked to show state "Shooting Lazer" but declares no such state. It declares: Fire. (Add a row under Reactions > Custom events, or ask for one of those.)` — with "It declares: (none)" and "has no Zoe asset" as the two honest degenerate cases.

**Why a split rather than always warning.** The current automatic `Raise(FireEventId)` in `WeaponMuzzleCue` is documented as a harmless no-op for any Zoe that has not declared one — if plain `Raise` started warning, every character in every project that has not authored a Fire reaction would log on its first shot. The rule that comes out of it is the right one and is what ties this task to T-0093: **a name nobody asked for may miss quietly; a name game code deliberately chose must never miss quietly.** Rejected alternative: a `bool warnIfMissing = true` parameter — same behaviour, but it makes the call sites read as configuration rather than as two different intents.

**Why CueRelay stops warning.** One message, one implementation, and the cue path gains the "here is what it does have" list for free. The `[Cue]` prefix is lost; acceptable, because the log context now points at the character, which is more useful.

**Byte-compatibility.** No data touched. The `Raise` return value keeps its original meaning — "this character declares a state by that name" — because `CueRelay` and `WeaponMuzzleCue` both branch on it.

### S4 — Carry position and facing through a state request

**New file.** `Runtime/Zoetrope/ReactionRequest.cs` — a `readonly struct` with `Vector2? Position` (null = the character's own anchor), `Vector2 Direction` (zero = no facing, omni-directional), `float Amount`, `GameObject Source`, and a `static ReactionRequest From(in DamageInfo info)`.

**Files and members.** `Runtime/Zoetrope/ReactionFxPlayer.cs` → `BuildContext(in DamageInfo)` becomes `BuildContext(in ReactionRequest)`; `OnHit` and `OnDeath` call `BuildContext(ReactionRequest.From(info))`; add `public bool Raise(string id, in ReactionRequest req)` and `public bool TryRaise(string id, in ReactionRequest req)`, keeping `Raise(string)` and `Raise(string, DamageInfo)` as forwarding overloads so no existing call site breaks.

**The one subtle correctness requirement.** `ReactionRequest.From(info)` must set `Position = info.point != Vector2.zero ? info.point : (Vector2?)null` — reproducing the current fallback exactly — so hit and death effects keep spawning at byte-identical points. The nullable is what lets a deliberate request at world origin work, which the old zero sentinel could not express.

**Where the values come from for a fire raise.** Position is the muzzle (`WeaponMuzzleCue.muzzle`, already kept live-tracked by `MuzzleTracker`/`MuzzleVectorTracker`, falling back to `WeaponDef.muzzleOffset`). Facing is the shot aim: make `ProjectileWeapon.ResolvedAimDirection()` public (a one-word change, no serialized field) rather than reading the private `dir` on `Projectile`, because the SHOT aim is what a fire pose should face and a shotgun five-pellet volley has a different `dir` per pellet. For a hitscan shot, facing is the normalised vector from the muzzle to `worldTarget`. Rejected alternative: adding a public `Direction` accessor to `Projectile` — also workable, but it answers a per-pellet question when we want a per-shot one.

**Byte-compatibility.** `ReactionRequest` is a runtime-only struct, never serialized. `DamageInfo` is untouched. The public fields of `EventContext` are untouched; only who fills them changes.

### S5 — Make stun get read and obeyed

**Files and members.** `Runtime/Zoetrope/ZoeState.cs` → add `public void ApplyStun(float seconds)` doing `if (seconds > 0f) _stunnedUntil = Mathf.Max(_stunnedUntil, Time.time + seconds);`, and leave `hitStun` and `OnDamaged` in place so nothing that sets `hitStun` directly breaks. `Runtime/Zoetrope/ReactionFxPlayer.cs` → in `RaiseInternal`, `OnHit` and `OnDeath`, once the reaction resolves, call `State?.ApplyStun(r.stunSeconds)`, resolving `ZoeState` through a lazy property in the same style as the existing `Arbiter` property, because `ZoeSpawner` adds `ReactionFxPlayer` **before** `ZoeState` (lines 99-110) so an Awake-cached reference would be null. `Runtime/Zoetrope/ZoeSpawner.cs` line 107 → delete the spawn-time `state.hitStun` freeze, since a hit stun now comes from the live reaction like everything else. `Runtime/Zoetrope/ReactionFx.cs` → correct the `stunSeconds` tooltip so it describes what stun actually gates today.

**Editor half, and it is not optional.** `Editor/Zoetrope/ZoetropeWindows.cs` → `BuildEventDuration` gains a `Stun` field beside Clip / Lasts / Loops|Seconds in the same wrapping row, built with the window's existing `NumField` helper and clamped at zero, exactly how Seconds is already drawn. A value that starts being obeyed but cannot be seen or set in the tool that owns the card fails the handover walk on requirement-to-code grounds. ZUI rules for it: `NumField` resolves to `Z.Float`, which is correct here because a stun has no stable natural cap and the layout rulebook says a scrub-draggable numeric input beats inventing a ceiling for a slider; it is a short field joining an existing row rather than starting one; and it commits through `Commit`, which is already Undo-safe.

**Byte-compatibility.** `ReactionFx.stunSeconds` already exists on every serialized reaction in every asset — confirmed in the ProtoGuy asset YAML on `hit`, `death` and the `Fire` event. Nothing is added, removed or reordered; a field that was written and ignored starts being read.

### S6 — Keep ask-by-name resolving to a whole card, and stop the one place that does not

**The ask-by-name path already resolves to a whole card** (`EventNamed` gives `NamedReaction.reaction`, i.e. clip + duration model + `bodyFx` + `fx` list), and S3/S4 keep it that way. The requirement to serve here is "do not repeat the mistake", and the concrete way to serve it is to make the mistake harder to reach.

**Files and members.** `Runtime/Zoetrope/PlayLauminationEffect.cs` → `Apply(EventContext ctx)` currently calls `ctx.View?.PlayClip(clip, loop)` directly. Route it through the arbiter instead: resolve `ctx.Transform?.GetComponent<AnimationArbiter>()` and, when one exists, `arbiter.Play(this, AnimationArbiter.PriorityNamedState, clip, loop)`; fall back to the direct `PlayClip` only when there is no arbiter (a Zoe built by something other than `ZoeSpawner`). Update its tooltip and doc comment to say plainly that it plays a bare clip with no duration, no body flash and no effect list, and that raising a named state is the way to get those.

**Also binding on T-0093 (S9):** the naive implementation of "let game code pick the name" is explicitly forbidden — no `view.PlayClip(chosenName)` and no `arbiter.Play(…, chosenName, …)` from the firing path. The chosen name goes to `ReactionFxPlayer.Raise`, which resolves the card. That is the whole of "do not repeat the mistake" for the new code.

**Byte-compatibility.** The serialized fields of `PlayLauminationEffect` (`clip`, `loop`) and its `[MovedFrom]` attribute are untouched, so every `SerializeReference` instance already authored keeps deserialising.

### S7 — Changelog

`Assets/Packages/Laubrary/CHANGELOG.md`, under `## [Unreleased]`, in the existing prose style: one **Changed** entry for the visible behaviour changes (see section 6) and one **Added** entry for the generated state-name lists. This is the artefact the five consumer projects read on the next wholesale folder sync, so it is where the "check your stun values first" warning has to live.

---

## 4. T-0093 — the steps

### S8 — Expose the shot's own aim

`Runtime/Combat2D/ProjectileWeapon.cs` → make `ResolvedAimDirection()` public. It already prefers a sibling `IVectorAimSource` animation-drawn barrel angle, then the owning `Combatant.aimDirection`, then its own field — exactly the right answer, currently private. Purely additive; nothing else changes.

### S9 — Let game code choose the name

**New file.** `Runtime/Zoetrope/IFireStateSource.cs` — a `readonly struct FireShot` carrying `ProjectileWeapon weapon`, `WeaponDef def`, `Vector2 origin` (the live muzzle), `Vector2 direction` (the shot aim) and `bool hitscan`, plus `public interface IFireStateSource { string FireStateFor(in FireShot shot); }` whose doc comment states that null or empty means "ask for nothing".

**File and members changed.** `Runtime/Zoetrope/WeaponMuzzleCue.cs` → replace the two lambdas at lines 140-141 with one shared `RaiseFireState(bool hitscan, Vector3 hitscanTarget)` helper, plus a lazily-resolved `IFireStateSource _fireState` found via `GetComponentInParent<IFireStateSource>()` and re-resolved while null — the same pattern `_reactions` and `_sink` already use, and it must be lazy because `ZoeSpawner` adds this component during weapon equipping, before the game has had any chance to add something of its own.

**The resolution rule, stated once.** No `IFireStateSource` present → ask for `FireEventId` ("Fire") exactly as today, through `TryRaise` (quiet — nobody asked for it, so a character that has not authored a Fire reaction must stay silent). An `IFireStateSource` present → its answer is the name; an empty or null answer means **ask for nothing at all** and no fallback fires; a non-empty answer goes through `Raise` (loud — game code deliberately chose it, so a miss must say so and list the alternatives). This pairing is what makes the S3 split earn its keep.

**The request it carries** is a `ReactionRequest` from S4 built with the live muzzle position and the resolved shot aim, so the same change that lets a lazer weapon pick its own name also lands its effects at the muzzle.

**One shot, one raise.** Guard `RaiseFireState` with a `_lastFireStateFrame == Time.frameCount` early-out, because `ProjectileWeapon.FireInternal` raises `Fired` once per projectile and a three-pellet weapon otherwise raises the state three times in one frame — of which the arbiter refuses two, while all three still fire the reaction Immediate effects. Scope the guard to the state raise only: the muzzle VFX and the fire zound are also per-projectile today, and changing those is a separate visible behaviour change nobody asked for.

**Rejected alternatives.** A `Func<>` field on `WeaponMuzzleCue` — the component is created at runtime by `ZoeSpawner` per weapon slot, so game code has no clean place to reach it, and a delegate cannot be seen in the Inspector. A chooser on `ReactionFxPlayer` — wrong seat; that component is the answerer, not the asker. A field on `WeaponDef` or `ZoeWeaponSlot` — that is authoring, and the design says game code decides. A static or global event — untestable, and wrong for a per-character decision.

### S10 — Confirm coverage across control schemes

A verification step with an outcome to write down, not a code change, and the answer is **stronger than the round-4 document assumed**.

The hook is at the **weapon**, not at the input: `WeaponMuzzleCue` subscribes to `ProjectileWeapon.Fired` and `ProjectileWeapon.HitscanFired`, and those are raised inside `FireInternal`, `TryFireAt` and `TryHitscanAt` regardless of who pulled the trigger. Every trigger source is therefore covered at once — `ZoeWeaponDriver` (player input; its own doc comment states it is perspective-agnostic and reused verbatim by platformer, side-scroller, twin-stick and light-gun alike, and it resolves neither direction nor position), `ProjectileWeapon.autoFire`, a Daemon brain, the `fireWeapon` preview steps in `MirageSubject`, and `TargetPracticeController`. The statement to record is: *coverage does not depend on the perspective, because the perspective-specific component is the MOTION driver and fire sits downstream of `Combatant.aimDirection`.*

**The one honest gap to write down:** `ProjectileWeapon.TryHitscanAt` has no caller anywhere in Laubrary — hitscan is driven entirely from consumer game code. `WeaponMuzzleCue` subscribes to `HitscanFired` so the mechanism covers it, but no in-project scene exercises it, so it can only be verified by a synthetic call (V8 below).

---

## 5. Ordering

- **S1 (case-insensitive)** first — the S2 generated constants and the S3 duplicate detection both have to agree with the matching rule.
- **S2 (codegen)** next, independent of everything after it. Landing it early lets every later verification step use the generated constants.
- **S4 (`ReactionRequest`)** and **S3 (loud warning)** together if you want one pass over the `Raise` signature; they touch the same three methods and doing them jointly avoids editing `Raise` twice.
- **S5 (stun)** is independent of S1-S4 and can run in parallel; its editor half must land in the same change as its runtime half.
- **S6 (`PlayLauminationEffect`)** is independent; do it last within T-0092 so it is easy to drop if the owner declines decision 5.
- **S8** before **S9**. **S9 depends on S3 and S4** and on nothing else. **S10** is verification, after S9.
- **S7 (changelog)** last, describing whatever actually landed.

Commit boundaries: (a) S1+S2, (b) S3+S4, (c) S5, (d) S6, (e) S8+S9+S10, (f) S7. Each leaves the project compiling and the demos working.

---

## 6. What existing projects will see, and what to check before it ships

**Change 1 — effects on a fire state move to the muzzle.** Any `FxEntry` on a "Fire" (or any game-raised) reaction left on the DEFAULT `placement: HitPosition` currently spawns at the character anchor and will start spawning at the weapon's live muzzle. Entries on `MetaPoint`, `TargetPosition` or `TargetOrigin` are unaffected. **In this project the blast radius is zero** — the only authored custom event anywhere is the ProtoGuy `Fire` event, and both its entries are `placement: 3` (MetaPoint/Muzzle). Before syncing to a consumer project, grep its Zoe assets for `events:` blocks containing `placement: 0` and eyeball those effects.

**Change 2 — facing arrives where there was none.** The same fire raise now carries a direction, so an entry left on the DEFAULT `direction: HitDirection` goes from omni-directional (NaN) to aimed along the shot. The two ProtoGuy entries are `direction: 1` (None) and are unaffected. Same pre-sync check: `direction: 0` inside an `events:` block.

**Change 3 — stun starts being obeyed.** A `stunSeconds` typed onto a custom event or a death reaction has never done anything and has therefore never been vetted. **Every authored `stunSeconds` in this project is `0`**, so nothing changes here; the entire risk is in the five consumer projects. Before syncing, grep their Zoe assets for `stunSeconds:` values greater than zero and confirm each one was intended. Note the second-order effect: a stun on a Fire state freezes the character's walk/idle animation for that long (that is all `CanAct` gates today — see decision 4), so a 0.5 s stun on a full-auto weapon's fire state would leave the character permanently frozen in its idle pose.

**Change 4 — a warning appears where there was silence.** Any game code calling `Raise` with a name the character does not declare now logs once. That is the point, but it will surface latent bugs in consumer projects the first time they run, which is worth saying in the changelog rather than letting someone discover it as "the new version spams the console".

**Change 5 — one raise per shot instead of one per projectile.** A multi-projectile weapon's fire reaction currently fires its Immediate effects N times per shot and will fire them once. This reads as a fix, but it IS a visible difference for anyone who tuned an effect against the duplicated version.

**Not a change:** no serialized field on `Zoe`, `NamedReaction`, `ReactionFx`, `FxEntry` or `CueBinding` is added, removed, renamed, retyped or reordered by any step in this plan. Every existing asset loads byte-identically. The only new files in a host project are the generated `<Zoe>.states.cs` siblings.

---

## 7. Verification — executable steps for the orchestrator

**Ground rules.** Never invoke the Unity Test Runner (`UNITY_DEV_GUIDE.md` hard rule — it raises an invisible modal and wedges the bridge). Edit-mode checks go through `& "C:\Users\Lauta\AppData\Local\Unity\bin\unity.exe" command eval_file "<path>.cs" --json`, which resolves `Laubrary.*` types and returns the value in `.data.result.result`. Play-mode checks use an ephemeral probe MonoBehaviour in `Assets/Demos/` (the pattern `_T0089Probe.cs` already establishes) plus Coplay for the enter-play loop, because entering Play rotates the CLI bearer token. Delete any probe when done. Never exercise a save path against a real authored asset — duplicate it first.

**V1 — codegen writes a correct file (S2).** In the Project window duplicate `Assets/Demos/ProtoGuyDemo/ProtoGuy.asset` to `ProtoGuy T0092 Probe.asset`. Open it in the Zoe window, Ctrl+S. **Proof:** `Assets/Demos/ProtoGuyDemo/ProtoGuy T0092 Probe.states.cs` exists and contains `public const string Fire = "Fire";` and an `All` array with one entry.

**V2 — a rename cannot go stale (S2).** On that duplicate, rename the event Fire to "Fire Gun", click away, Ctrl+S, wait for the recompile to settle (`unity command recompile_status`, or wait out the domain reload per the UNITY_DEV_GUIDE note). **Proof:** the `.states.cs` now contains `FireGun = "Fire Gun"` and no `Fire` member. Then hand-edit the generated file to insert a bogus `Bogus = "Bogus"` const and force a domain reload (`unity command recompile`). **Proof:** the console carries a `Debug.LogError` naming the asset, and the file has been rewritten back to correct content. Delete the duplicate asset and its generated file.

**V3 — case-insensitive matching (S1).** `eval_file` body: load `Assets/Demos/ProtoGuyDemo/ProtoGuy.asset` as `Laubrary.Zoetrope.Zoe` and return the four results of `EventNamed("fire") != null`, `EventNamed("FIRE") != null`, `EventNamed("Fire") != null`, `EventNamed("Fires") != null` joined by slashes. **Proof:** `True/True/True/False`. No play mode, no asset write.

**V4 — the editor agrees about case (S1).** Open the Zoe window on a DUPLICATE of ProtoGuy, add a second event and name it "fire". **Proof:** a duplicate-id badge appears on one of the two cards. Delete the duplicate asset.

**V5 — a missed name is loud and lists alternatives (S3).** Probe in ProtoGuyDemo: after `ZoeSpawner.SpawnCharacter`, call `Raise("Shooting Lazer")` on the spawned `ReactionFxPlayer` ten times in a loop. **Proof:** `unity command console --json` shows **exactly one** warning, it names Shooting Lazer, and its "It declares:" list contains Fire. Then call `TryRaise("Shooting Lazer")` ten times on a freshly spawned character. **Proof:** zero warnings.

**V6 — position and facing arrive (S4 + S9).** Probe: `var copy = Object.Instantiate(zoeDef);` — a runtime copy, never the authored asset — then set the first `FxEntry` of the Fire event to `placement = HitPosition` and `direction = HitDirection`, and swap its `fx` for a one-line `IEffect` that logs `ctx.Position` and `ctx.DirectionDeg`. Spawn `copy`, let its weapon fire once, and capture `GetComponentInChildren<WeaponMuzzleCue>().muzzle.position` on the same frame. **Proof:** the logged `ctx.Position` equals the muzzle position within 0.001, and `ctx.DirectionDeg` is finite and equals the atan2 of the weapon's `ResolvedAimDirection()`. Run this probe once BEFORE implementing S4 as well — it should then log the character `transform.position` and `NaN`, which proves the delta rather than assuming it.

**V7 — stun is read from whichever reaction played, and is live (S5).** Probe: on a runtime `Instantiate` copy set the Fire event `stunSeconds` to 0.5, spawn, `Raise("Fire")`. **Proof:** `ZoeState.IsStunned` is true immediately after and false again after 0.6 s. Then mutate `copy.hit.stunSeconds` on an ALREADY-SPAWNED character and damage it. **Proof:** the new value takes effect — this is the "frozen at creation" half, which before the change would keep using the spawn-time value.

**V8 — hitscan coverage (S9/S10).** Probe: on the spawned ProtoGuy `ProjectileWeapon`, call `TryHitscanAt(transform.position + Vector3.right * 3f)` directly — nothing in Laubrary calls it, so this is the only way to exercise it. **Proof:** the Fire state raises exactly once and its effect lands at the muzzle, same assertion as V6.

**V9 — game code steers the name (S9).** Probe: add a component to the spawned character implementing `IFireStateSource` and returning "Shooting Lazer". Fire once. **Proof:** one warning naming Shooting Lazer and listing Fire. Now add a "Shooting Lazer" event to the runtime `Instantiate` copy, respawn, fire once. **Proof:** `EventFinished` fires with id "Shooting Lazer" and there is no warning. Then make the source return the empty string. **Proof:** nothing is raised at all — specifically `EventFinished` does not fire with "Fire", confirming there is no hidden fallback (decision 7).

**V10 — one raise per shot (S9).** Probe: set `projectilesPerShot = 3` on the spawned weapon, subscribe to `EventFinished`, fire once. **Proof:** exactly one `EventFinished` for that shot, and the reaction Immediate effect spawns once rather than three times.

**V11 — the card is not reduced to a clip (S6).** Probe: on a runtime copy give the Fire event a `bodyFx` stack and `durationMode: FixedSeconds, seconds: 0.4`. Raise it. **Proof:** a `SpriteFxFilter` appears on the body `SpriteRenderer` with `stack` equal to the reaction `bodyFx`, and `EventFinished` arrives about 0.4 s later rather than immediately.

**V12 — nothing regressed on hit/death.** Re-run whatever probe the T-0090/T-0091/T-0094 session used (`Assets/Demos/_T0089Probe.cs` is still in the tree). **Proof:** death linger, revive colliders and the interrupted-flag behaviour are unchanged — S4 in particular must not move a hit effect spawn point by so much as a pixel.

**V13 — layout sanity for the one UI change (S5).** With the Zoe window open on ProtoGuy and laid out at real size, run `Laubrary/Audit Focused Editor Window` and confirm `foldedSkipped == 0` and no new width/overlap findings from the added Stun field.

---

## 8. Loose ends worth flagging, not fixing here

- `Assets/Demos/_T0089Probe.cs` is another session's declared-ephemeral probe and its own comment says to delete it after the verification run. It is not mine to remove; hand it back to whoever owns T-0090/T-0091.
- `ZoeState.CanAct` gates only the two animators. `TopDownMotionDriver` and `ZoeWeaponDriver` never consult it, so a dead or stunned character can still be walked and fired. That is a real defect, it is bigger than this task, and it is decision 4 above.
- The `WeaponDef` muzzle VFX and `fireZoundName` both play once per PROJECTILE, so a shotgun currently flashes and bangs N times per trigger pull. Left alone deliberately (see S9), but it should get its own decision.
- `ReactionFx.stunSeconds` will still be the only reaction field whose meaning depends on what `CanAct` happens to gate. When the step-6 role chip lands, it is worth revisiting whether stun belongs on the card at all or on the character.
