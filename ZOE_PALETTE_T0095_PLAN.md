# T-0095 — Build the palette model: implementation plan

**What this is:** a ready-to-execute build plan for AgentHQ task T-0095 ("Build the palette model — role chip, body-part targeting, unified hurt/death list, default+override effects"), the centrepiece of the T-0089 design effort. Written after reading `ZOE_PALETTE_TAKE.md` (round 3) and `ZOE_PALETTE_BUILD_PLAN.md` (round 4) in full, `CLAUDE.md`, `D:\Unity\UNITY_DEV_GUIDE.md`, and the ZUI layout rulebook, and after tracing the CURRENT working-tree source (which contains uncommitted T-0090/T-0091/T-0094 work from another session). **Date:** 2026-08-26. **Branch:** `feat/lathe`.

**How to read it:** Section 1 states the invariants every step is measured against. Section 2 is the ground truth I found in the code, including three things that change the shape of the work and are in neither design doc. Section 3 is the step list, split into waves, with per-step mechanism, rejected alternatives, compat story and an in-editor verification recipe. Section 4 is the parallelisation table. Section 5 is the decisions I am flagging, each with my recommended answer already applied to the plan above it.

---

## 1. The invariants — every step is judged against these

1. **No authored data moves, is renamed, or is reinterpreted.** Every existing `.asset` must load and behave EXACTLY as before, gaining only new optional settings. Enforced concretely, not by intention: after each wave, open a Zoe in its editor, save, and `git diff` the asset — the diff must consist ONLY of ADDED lines carrying default values. Any changed or removed line is a bug in the step, not an acceptable migration.
2. **No default answerer, ever.** A state must never play because Laubrary picked it — not even when the character declares exactly one candidate. This plan adds zero fallback / default-picking behaviour. (Owner's decision, T-0089; restated in `CLAUDE.md` → "Zoe palette".)
3. **The role chip lives on the data for custom rows only.** `Zoe.hit` and `Zoe.death` gain no new field whatsoever; their chip is DRAWN by the editor, not stored.
4. **Menus:** no new `[MenuItem]`, no new editor window. Everything lands inside the existing `Laubrary/Zoetrope/Zoes` window.
5. **Undo:** every data edit goes through `ZoetropeDefWindow.Commit(path, write)` (which does `So.Update()` then the write then `So.ApplyModifiedProperties()`, and `ApplyModifiedProperties` registers the Undo entry). No new control may write a field directly.
6. **ZUI for all UI**, specifically: enum → `Z.Segmented` (3 or fewer options) / `Z.MiniRadio`, never a dropdown; bool → `Z.Toggle`; **never a text field for a REFERENCE** (a part name, a state name being referenced) — the owner is always known, so the picker is always derivable; a text field is correct ONLY at the DECLARATION site.
7. **Nothing in this task touches the Unity editor from a planning session.** Implementers do; the orchestrator serialises them onto the one editor.

---

## 2. Ground truth — what the code actually does today

Read from the working tree (not HEAD). The uncommitted T-0090/T-0091/T-0094 work is already present and this plan builds on it.

### 2.1 The data model as it stands

- `Zoe.hit` and `Zoe.death` are two plain `ReactionFx` fields. `Zoe.events` is a `List<NamedReaction>`, each a `{ string id; ReactionFx reaction; }`. `Zoe.EventNamed(id)` matches **case-sensitively and exactly**; `Zoe.EventIds` is the picker source.
- `ReactionFx` carries `clip`, `durationMode`, `loops`, `seconds`, `stunSeconds`, `bodyFx`, `List<FxEntry> fx`.
- `FxEntry` carries `enabled`, `trigger`, `frame`, `placement`, `metaLayerId`, `direction`, `scalar`, `follow`, and `[SerializeReference] IEffect fx` — **exactly one effect**, which is the "required default" round 4's override proposal wants to keep.
- `Zoe.deathDisposal` / `Zoe.deathLinger` exist and are copied onto `ZoeState` at spawn.

### 2.2 Three findings that change the shape of the work

**(a) `Zoe.deathDisposal` and `Zoe.deathLinger` are drawn NOWHERE in the Zoe editor.** Grepping the whole Editor tree finds no reference. They are reachable only through the raw Inspector. This matters directly: scope point 5 (the linger timer) is **unverifiable and unauthorable in-editor** until those two controls exist. Adding them is not unrequested surface — it is the affordance point 5's own feature requires, and it is the exact "reachability, per step" failure the handover walk exists to catch.

**(b) A composite Zoe's root GameObject has NO `IAnimatedView`, so ProtoGuy can currently play no reaction clip at all.** `ZonedLauminaryView.Build` adds `AnimatedViewRelay` to the GameObject it builds; for a composite, `CompositeZonedPlayer.Build` creates one CHILD GameObject per part and builds each part's view into THAT child. So `ReactionFxPlayer._view = GetComponent<IAnimatedView>()` on the root resolves to **null** for ProtoGuy, and `TryArmClip` returns `NoClip` for hit, death and every named event. `ZoeSpawner` adds a root `AnimationArbiter` unconditionally and its own comment already concedes it is a "harmless no-op ... with no view to play through, every claim is simply refused". Body-part targeting is therefore not a nicety layered on a working system — **it is the mechanism that makes a composite Zoe able to play a reaction clip at all.** It also means the fan-out design below has no reachable regression surface: there is nothing working today for it to break.

**(c) The clip picker silently degrades to a free TEXT FIELD for composite Zoes.** `GetClipNameOptions(owner)` reflects a field named `version` off the view; `CompositeLauminaryView` has no `version` field (it has `parts`), so `BuildReactionFx` falls into its `Z.TextInput` branch. That is a live violation of the "never make the user type a reference string" rule, sitting in the shipped Zoe window today. The part picker fixes it for free: once a row names a part, the clip options come from THAT part's view, which does have a `version`.

### 2.3 The interruption/arbitration layer (T-0094, in the tree)

`AnimationArbiter` is the traffic warden: `Play(owner, priority, clip, loop, durationSeconds, onComplete, onInterrupted)` with strict-greater preemption, a `WouldAccept(owner, priority)` pre-check that exists precisely so a claimant can learn it is outranked *before* tearing down its own bookkeeping, `Release(owner)`, `HasControl(owner)`, and a `Reassert` event. The ladder is `PriorityLocomotion 0 < PriorityNamedState 100 < PriorityHurt 200 < PriorityDeath 1000`. `ReactionFxPlayer` mints a **fresh token object per claim**, retires the outgoing reaction while its own state is still live, flushes unfired OnFrame entries on interruption, and reports completion as `Action<bool> HurtFinished` / `Action<bool> DeathFinished` / `Action<string,bool> EventFinished` where the bool is `interrupted`. **Build on all of this; do not re-derive it.**

Note the arbiter reaches a PART GameObject today only as a side effect of `AnimatedViewRelay.BindMotionPose` when a `MotionPose` is authored. Both of ProtoGuy's parts have one, so both have arbiters — but a part with no authored MotionPose would not, and step A4 must not assume one.

### 2.4 The death path (T-0090, in the tree)

`ZoeState.OnDied` disables and RECORDS colliders (T-0091), then switches on `disposal`:

- `Immediate` → `Dispose()`.
- `AfterDelay` → `Invoke(Dispose, deathLinger)` **at the moment of death**. This is scope point 5's bug.
- `Leave` → nothing.
- `WhenDeathClipEnds` → sets `_awaitingDeathClip = _reactions.DeathClipArmed` (asks whether a clip ACTUALLY started, not whether one was configured), and if nothing armed, falls back to `Invoke(Dispose, deathLinger)`. This is T-0090's landed fix.

`OnDeathClipFinished(bool interrupted)` disposes when `disposal == WhenDeathClipEnds && _awaitingDeathClip`. `ReactionFxPlayer.DeathClipArmed` is the honest "a death clip genuinely started" signal, and `ReactionFxPlayer.OnDeath` fires `DeathFinished(false)` synchronously when nothing armed. `ZoeSpawner` adds `ReactionFxPlayer` **before** `ZoeState`, and `Health.Died` is multicast in subscription order, so ReactionFxPlayer's handler always runs first — `ZoeState`'s own comment documents and relies on this. Step B4 relies on it too.

### 2.5 ProtoGuy, concretely

`ProtoGuy.asset` is a `CompositeLauminaryView` with two parts: **`Legs`** (root, `ProtoGuyLegs` version, `motionPose` rules `LegsIdle16` / `LegsWalk3`, sortingOrder 0) and **`Upper`** (`parentPartName: Legs`, `ProtoGuyUpper` version, `motionPose` rule `UpperAim16`, sortingOrder 1, Pivot anchors). One weapon slot attached to part `Upper` with `muzzleLayerId: Muzzle`. One custom event **`Fire`** — `durationMode: FixedSeconds`, `seconds: 0.15`, **no clip**, two Immediate MetaPoint effects at the `Muzzle` layer. `hit` and `death` are both empty. `ProtoGuyUpper` declares 16 directional animations `Upper_N` through `Upper_NNW` plus the `UpperAim16` set — **so a death / hurt / fire look can be authored for verification using existing clips, with no new art.**

**Compat proof, measured:** across all four Zoe assets in the project (`ProtoGuy`, `Hero`, `PreviewShooterZoe`, `PreviewTargetZoe`) there is **not one authored `hit` or `death` clip**, and not one clip on any custom event. Every behaviour change proposed below is gated behind "a clip is authored on a reaction", so the observable behaviour of every existing asset is unchanged by construction, not by hope.

---

## 3. The steps

Grouped into three waves. Wave A is five independently-shippable, file-disjoint data / runtime pieces. Wave B is the `ReactionFxPlayer` integration, internally serial because it is one file. Wave C is the editor, disjoint from B and running concurrently with it.

---

### WAVE A — data model and standalone runtime (five parallel steps)

---

#### A1 — The role chip, the part target, and the per-death-row disposal override

**Files:** `Assets/Packages/Laubrary/Runtime/Zoetrope/NamedReaction.cs`, `Assets/Packages/Laubrary/Runtime/Zoetrope/Zoe.cs`.

**Mechanism.** Add to `NamedReaction.cs` a three-way tag and the row-level fields:

```csharp
/// Which of Laubrary's two built-in questions this row is a legal ANSWER to. It never decides WHETHER or WHEN
/// anything happens — dying is health reaching zero, full stop. It only narrows a menu.
public enum StateRole { None = 0, Hurt = 1, Death = 2 }
```

and on `NamedReaction`:

```csharp
public StateRole role = StateRole.None;
public string partName = "";                                   // "" = the whole body
public bool overrideDisposal = false;                          // Death rows only
public DeathDisposal disposal = DeathDisposal.WhenDeathClipEnds;
[Min(0f)] public float linger = 1.5f;
```

Add to `Zoe.cs` purely additive query helpers. `EventNamed` and `EventIds` keep their exact current signatures and behaviour, because `CueRelay`, `WeaponMuzzleCue` and the editor all depend on them:

```csharp
public NamedReaction RowNamed(string id, StateRole requiredRole);   // null when absent OR the chip does not match
public IEnumerable<NamedReaction> RowsWithRole(StateRole role);
public IEnumerable<string> StateIdsWithRole(StateRole role);        // the editor picker's source
```

**Why the chip lives on `NamedReaction` and not on `ReactionFx`.** `ReactionFx` is the CARD (clip, duration, flash, effects) and is shared by `hit`, `death` and every custom row. Putting `role` there would (i) add a stored field to `Zoe.hit` / `Zoe.death`, breaking invariant 3 outright, (ii) create an authorable nonsense state ("the built-in hit slot is a death look"), and (iii) poison the shared-content future round 3 recommends (two characters pointing at one card), because a role is a property of the ROW in a character's list, not of the content behind it. `NamedReaction` is the row. The same argument, verbatim, applies to `partName` and to the disposal override.

**Why `overrideDisposal` is a bool gate and not "just use the enum".** A newly-serialized enum deserialises as 0, and `DeathDisposal`'s 0 is `Immediate` — "vanish the instant it dies". A new field must never default to a meaningful, destructive value. The bool defaults to `false` = inherit the Zoe's own `deathDisposal` / `deathLinger`, which is the only safe default.

**Alternatives rejected.** (i) A parallel `List<StateRole>` on `Zoe`, index-matched to `events` — silently corrupts on reorder, and reorder is one grip-drag away in the current UI. (ii) A `hurtLooks` / `deathLooks` pair of lists on `Zoe` — this is round two's data restructuring, retired explicitly by round 3 ("Hurt and death stay exactly where they are; they're just *drawn* as the top two rows of the same list"), and it is where round two's real risk lived. (iii) An enum `BodyPart { WholeBody, Upper, Legs }` for the part target — Laubrary would be inventing a fixed vocabulary no Zoe is obliged to match (ProtoGuy's parts happen to be named `Legs` and `Upper`, but a three-part character breaks it immediately). A string PICKED from the view's own declared part names follows the same "declare once, pick everywhere" rule as every other name in this window.

**Migration / compat.** Four new fields on a `[Serializable]` class inside a list. Unity's YAML deserialiser supplies the C# field initialiser for any key absent from the file, so every existing `NamedReaction` reads back as `role: None`, `partName: ""`, `overrideDisposal: false` — exactly today's meaning. **No `[MovedFrom]` is needed anywhere in this task:** nothing is renamed, no type moves namespace or assembly, and no field changes type. `[MovedFrom]` stays reserved for the class of change the Pyre rename needed (see `CLAUDE.md`); applying it speculatively here would be noise.

**Also in this step:** rewrite `NamedReaction`'s class doc comment. It currently argues *against* this whole model ("Folding them into a string-keyed list would mean special-casing two entries anyway, and would let a typo silently disable dying"). Round 3 answered it and asked for it to be rewritten. The replacement should say plainly: hurt and death still carry their typed combat information and still gate `ZoeState.CanAct` and disposal, because those things stay on Laubrary's side and are never name-driven; what became name-driven is only the APPEARANCE; a typo can therefore produce a character that dies looking wrong, never one that fails to die; and the role chip IS the honest special case, visible on the row, rather than today's invisible special case of "these two are different because of where they live".

**Verification recipe.** (1) `git status` first — confirm the tree state before touching anything. (2) Open `Laubrary/Zoetrope/Zoes`, select ProtoGuy, click through every section; nothing should look different yet. (3) Touch one unrelated field (retype Display Name to the same value) to force a save, then `git diff Assets/Demos/ProtoGuyDemo/ProtoGuy.asset`. **Pass condition: the diff contains only ADDED lines — `role: 0`, `partName:`, `overrideDisposal: 0`, `disposal: 1`, `linger: 1.5` under the `Fire` event — and no changed or deleted line anywhere.** (4) Repeat on `Assets/Zoetrope/Hero.asset`. (5) Enter Play mode on `ProtoGuyDemo.unity`, confirm ProtoGuy still spawns, walks, aims and fires exactly as before, and `unity command console --json` shows no new warnings.

---

#### A2 — Default-plus-override effects

**Files:** `Assets/Packages/Laubrary/Runtime/Zoetrope/ReactionFx.cs`.

**Mechanism.** Add a small serializable slot type and a list on `FxEntry`:

```csharp
[System.Serializable]
public class FxOverride
{
    /// The name a request carries to select this slot instead of the entry's default effect. Typed ONCE, here,
    /// where the slot is DECLARED; gameplay code addresses it by that name.
    public string name = "";
    [SerializeReference] public IEffect fx;
}
```

Leave `FxEntry.fx` **exactly** as it is (same name, same type, same `[SerializeReference]`) — it is now documented as "the required default" — and add:

```csharp
public List<FxOverride> overrides = new List<FxOverride>();

/// The effect this entry plays for `overrideName`: a matching, non-empty override slot, otherwise the default.
/// An unmatched name deliberately yields the DEFAULT, never nothing — per round 4, an override is a swap, not a gate.
public IEffect Resolve(string overrideName);
```

`ReactionFxPlayer` then calls `entry.Resolve(overrideName)` where it currently reads `entry.fx` — three sites: the `IsEmpty` guard, the `Apply`, and the `is ICombatFx` follow branch. That rewiring is step B1, in wave B.

**Why per-ENTRY and not per-CARD.** Round 4 is explicit: "a state's card keeps exactly one required default effect (as today), plus any number of optional named override slots ... that effect plays instead of the default". A card-level alternate would be a second `ReactionFx` — a duplicated animation, duration and flash — which is precisely the "declared states drifting independently" cost this mechanism exists to avoid. The trade-off is real and worth stating: a powerup that must swap TWO effects on one card needs the same slot name typed on both entries. That is acceptable (one name, two declarations, both visible on the same card) and strictly better than a card-level swap that also duplicates everything else.

**Alternatives rejected.** (i) Replacing `fx` with a name-keyed list where the default is the entry named `""` — every existing `rid:` reference in every authored asset would have to be rewritten, which is exactly the silent data restructuring invariant 1 forbids. (ii) A `[SerializeReference] IEffect[] fxByName` parallel array — same reorder fragility as A1's rejected parallel list. (iii) Carrying the swap in the "parcel" — round 4 spends a whole section explaining why it cannot: the parcel carries VALUES through to an effect that already exists; this swaps WHICH effect asset is used, which is not a value.

**Migration / compat.** `overrides` is a new `List<>` field, absent from every existing asset's YAML, so it deserialises empty. `Resolve(null)` and `Resolve("")` both return `fx` immediately, and every existing call path passes no override name. **Every authored effect is byte-identically unchanged, including its `[SerializeReference]` `rid`, because the field it lives in is untouched.**

**Verification recipe.** Same asset-diff proof as A1: the only added lines under each existing `fx` entry must be `overrides: []`. Then, in the Zoe window, add an override slot named `Flame` to one of ProtoGuy's two `Fire` effects and give it a different Pyre asset; save; diff — the existing entry's `fx: rid:` must be unchanged, with the new slot appended after it. Play `ProtoGuyDemo` and fire: the ORDINARY flash must still appear, since nothing passes an override name yet, proving the default path is untouched.

---

#### A3 — The request, the answerer interface, and the answer register

**Files (all new):** `Assets/Packages/Laubrary/Runtime/Zoetrope/ZoeStateRequest.cs`, `Assets/Packages/Laubrary/Runtime/Zoetrope/IZoeStateAnswerer.cs`, `Assets/Packages/Laubrary/Runtime/Zoetrope/ZoeAnswers.cs`.

**Mechanism.**

```csharp
/// What gameplay code says when it asks for — or answers with — a state. A readonly struct passed by `in`, so
/// later additions (round 3's presentation values and opaque parcel) are a source-compatible extension rather
/// than a signature break at every call site.
public readonly struct ZoeStateRequest
{
    public readonly string state;         // the declared row name to show
    public readonly string overrideName;  // optional; selects an FxOverride slot. null/empty = each entry's default
    public ZoeStateRequest(string state, string overrideName = null);
    public bool IsValid => !string.IsNullOrEmpty(state);
}

/// Something that can answer one of Laubrary's two built-in questions for a character. Returning false is a
/// first-class answer meaning "not mine" — a burning-status answerer that only cares about fire hits declines
/// the rest and lets ordinary game code answer.
public interface IZoeStateAnswerer
{
    bool TryAnswer(StateRole role, in DamageInfo info, out ZoeStateRequest answer);
}

/// The per-character register of answerers. Added unconditionally by ZoeSpawner, same rule as ZoeState — every
/// Zoe has one so a consumer can rely on it being there, and there is exactly one way to get one.
public class ZoeAnswers : MonoBehaviour
{
    public void Push(IZoeStateAnswerer answerer, UnityEngine.Object owner = null);
    public void Remove(IZoeStateAnswerer answerer);
    public bool TryAsk(StateRole role, in DamageInfo info, out ZoeStateRequest answer);
    public IZoeStateAnswerer Current { get; }        // for T-0096's "who is answering" readout
    public int Count { get; }
}
```

Semantics, stated so they cannot drift:

- Entries are a **stack**: the most recently pushed is asked first. This is round 3's "whoever answered most recently for this character wins, the one before is remembered and restored when the newcomer goes away" — `Remove` restores the previous top automatically because it is a stack, with no extra bookkeeping.
- Asking walks **top-down and takes the FIRST `true`**. A declining answerer does not consume the question.
- An entry whose `owner` has been destroyed (Unity-null) is pruned on the next ask, so a component-based answerer is self-cleaning.
- `OnDisable` clears the register — a pooled character must not answer with the last occupant's rules, which round 3 calls "a haunting, not a bug". `Health.Revive()` deliberately does NOT clear it: Mirage's target practice revives the same live object and its registration must survive.
- **`TryAsk` returning false is not a failure and has no fallback.** There is no built-in answerer, no "if there is only one candidate use it", nothing. `ReactionFxPlayer` treats a declined question as "play the built-in slot exactly as today" — see B2, and decision D1 for why that is not a hidden default-picker.

**Alternatives rejected.** (i) A plain C# `event Func<...>` on `ReactionFxPlayer` — multicast delegates return only the last subscriber's value, there is no ordering guarantee, no "who is currently answering" for the editor, and no way to decline. (ii) A single settable `IZoeStateAnswerer answerer` property — round 3 explicitly rejects it: a powerup, a burning status and ordinary game code are all reasonable simultaneous candidates, and last-writer-wins with no restore is how "the first power-up you write silently fights your own game code and you'd never know which won" happens. (iii) A ScriptableObject answerer asset — that is the default-answerer round 4 declined, wearing a different coat.

**Migration / compat.** Three new files, zero edits to existing ones, no serialized data. Nothing can regress.

**Verification recipe.** This step ships no observable behaviour on its own — say so rather than implying otherwise. Verify by compile plus a scratch probe: `unity command eval_file` creating a bare GameObject, `AddComponent<ZoeAnswers>()`, pushing two throwaway answerers, and asserting the ask order, the decline-passes-through behaviour, the owner-pruning, and that an empty register returns false. Delete the probe when done (UNITY_DEV_GUIDE, Debugging #2).

---

#### A4 — Part routing

**Files:** new `Assets/Packages/Laubrary/Runtime/Zoetrope/IPartRoster.cs`, new `Assets/Packages/Laubrary/Runtime/Zoetrope/ZoePartRouter.cs`, edit `Assets/Packages/Laubrary/Runtime/ZoetropeLaunimator/CompositeZonedPlayer.cs`.

**Mechanism.**

```csharp
/// Optional capability a composite host MAY provide (discovered via GetComponent, same pattern as IPartLookup /
/// ICueSink / IMotionPoseHost) — the part names this character actually has, in DECLARED order, so the whole-body
/// target can fan out and so a mis-typed part name can be reported by listing what does exist.
public interface IPartRoster
{
    System.Collections.Generic.IReadOnlyList<string> PartNames { get; }
}
```

`CompositeZonedPlayer` implements it — it already owns `_byName`; keep a declared-order `List<string>` alongside, populated in `Build`. `ZoePartRouter` is a small `MonoBehaviour` on the character root, added by `ZoeSpawner`:

```csharp
public readonly struct PartTarget { public readonly IAnimatedView view; public readonly AnimationArbiter arbiter; }

/// Resolve a row's part target to the animation surfaces it speaks for. into[0] is the PRIMARY — the one that
/// supplies clip length, frame events and the completion signal. Returns the count.
public int Resolve(string partName, List<PartTarget> into);
```

Resolution rules, exhaustively:

1. `partName` non-empty → the single part found via the root's `IPartLookup.FindPartTransform(partName)`; its `IAnimatedView` (from that child GameObject) and an `AnimationArbiter` **ensured** on that same child (`GetComponent` else `AddComponent`, because a part with no authored `MotionPose` has none today). An unknown name → 0 targets and **one** warning naming the parts that DO exist (from `IPartRoster`), matching `CompositeZonedPlayer.WarnOnce`'s existing once-per-key discipline.
2. `partName` empty AND the root has an `IAnimatedView` → **exactly one target: the root, with the root's `AnimationArbiter`.** This is byte-identical to what `ReactionFxPlayer` does today for every single-body Zoe.
3. `partName` empty AND the root has NO `IAnimatedView` AND an `IPartRoster` is present → every part that provides an `IAnimatedView`, in declared order; the primary is the first. This is the composite whole-body case — ProtoGuy's flinch claiming both `Legs` and `Upper`.
4. Otherwise → 0 targets, which `ReactionFxPlayer` maps to `ArmResult.NoClip`, exactly as today.

**Why a NEW interface rather than adding a member to `IPartLookup`.** `IPartLookup` has one implementer and one consumer in this repo, so extending it is trivially safe HERE — but round 3's one unclosable risk is the five consumer projects that could not be read, and adding a member to a public interface is a hard compile break for any of them that implements it. A second optional capability interface is additive, costs one tiny file, and is the idiom this codebase already uses everywhere ("optional capability a view MAY provide").

**Why whole-body on a composite fans out.** Round 3's requirement is that "a flinch that claims the whole body cleanly wins over both" legs and torso. Rule 2 above guarantees single-body Zoes see zero change; rule 3 only ever fires on a composite root, which has no view and therefore plays nothing today (finding 2.2b). Combined with the measured fact that no composite Zoe in the project has a clip on any reaction, the fan-out has **no reachable regression surface**. See decision D2 for the smaller fallback if this is judged too ambitious.

**Alternatives rejected.** (i) One `ReactionFxPlayer` per part — multiplies the hit/death subscription (every part would react to `Health.Damaged`), and death disposal, stun and the `DeathClipArmed` handshake are character-wide, not per-part. (ii) Putting the routing inside `AnimationArbiter` as a "multi-arbiter" — the arbiter's whole value is being one small, well-tested, single-claim authority; teaching it about parts puts composite-body knowledge into core arbitration. (iii) Having `ReactionFxPlayer` reach into `CompositeZonedPlayer` directly — core Zoetrope must not reference the Launimator bridge; that is the layering rule the whole `IPartLookup` / `ICueSink` family exists to preserve.

**Migration / compat.** Two new files plus one interface implementation and one bookkeeping list on `CompositeZonedPlayer`. No serialized data changes. `ZoePartRouter` is not wired into anything until B3.

**Verification recipe.** Play `ProtoGuyDemo`, then `unity command eval_file`: find the spawned ProtoGuy, `GetComponent<ZoePartRouter>()`, and assert — `Resolve("", list)` returns 2 with `list[0]` being the `Legs` view (declared first); `Resolve("Upper", list)` returns 1 and that target's arbiter is on the `Upper` child, not the root; `Resolve("Torso", list)` returns 0 and logs one warning naming `Legs, Upper`. Then repeat against a single-body Zoe (`Hero.asset`): `Resolve("", list)` must return 1 and that target must be the ROOT — the byte-identical-to-today branch.

---

#### A5 — The death-linger timer starts when the death animation ENDS (scope point 5)

**Files:** `Assets/Packages/Laubrary/Runtime/Zoetrope/ZoeState.cs`.

**The bug, precisely.** `DeathDisposal.AfterDelay`'s own doc comment reads "Play the death clip, **then** lie there for `deathLinger` seconds before vanishing." The code does `Invoke(nameof(Dispose), deathLinger)` inside `OnDied` — at the moment of death, not at the end of the clip. With a 3 s burn-away death animation and the default 1.5 s linger, the body is destroyed halfway through the animation. Round 3 calls this "a real bug on the showcase path".

**How this reconciles with T-0090's just-landed fix — stated explicitly, because they touch the same method.** T-0090's fix lives entirely inside the `WhenDeathClipEnds` branch: it introduced `_awaitingDeathClip`, sourced it from `ReactionFxPlayer.DeathClipArmed` (which answers "did a clip ACTUALLY start", not "was one configured"), and made the no-clip case fall back to `Invoke(Dispose, deathLinger)`. **Point 5 is not a competing fix — it is the generalisation of T-0090's own mechanism from one disposal mode to two.** `AfterDelay` today ignores `_awaitingDeathClip` entirely; the change is to make it arm the same flag, and to make the finish handler branch on the disposal mode. T-0090's `WhenDeathClipEnds` branch is left **literally unchanged, character for character**, including its no-clip linger fallback. The new shape:

```csharp
void OnDied(DamageInfo _)
{
    // ... colliders off + recorded, unchanged (T-0091) ...

    // One question, asked once, for both modes that can wait: did a death clip genuinely start? (T-0090's own
    // signal, generalised — AfterDelay previously ignored it and started its timer at the moment of death,
    // which cut a long burn-away in half.)
    _awaitingDeathClip = (disposal == DeathDisposal.WhenDeathClipEnds || disposal == DeathDisposal.AfterDelay)
                         && _reactions != null && _reactions.DeathClipArmed;

    switch (disposal)
    {
        case DeathDisposal.Immediate: Dispose(); break;
        case DeathDisposal.Leave: break;
        case DeathDisposal.WhenDeathClipEnds:
        case DeathDisposal.AfterDelay:
            if (!_awaitingDeathClip) Invoke(nameof(Dispose), Mathf.Max(0f, deathLinger));
            break;
    }
}

void OnDeathClipFinished(bool interrupted)
{
    if (!_awaitingDeathClip) return;
    _awaitingDeathClip = false;
    if (disposal == DeathDisposal.AfterDelay) Invoke(nameof(Dispose), Mathf.Max(0f, deathLinger));
    else Dispose();   // WhenDeathClipEnds — exactly what T-0090 left here
}
```

**Behaviour delta, exhaustively.** `Immediate`, `Leave` and `WhenDeathClipEnds`: identical in every case. `AfterDelay` **with no death clip armed**: identical, because nothing armed means the timer still starts at the moment of death, which is the only thing it can mean. `AfterDelay` **with a death clip armed**: the timer now starts at the clip's end. **No asset in this project authors a death clip, so nothing observable changes today** — this is the fix landing before the content that needs it, which is exactly why it belongs in this task rather than after it.

**Also in this step** (needed by B4, shippable now because it is inert until called): a public entry point for a chosen death row's disposal override, plus its restore-on-revive.

```csharp
/// Apply a chosen death row's disposal override for THIS death. Called by ReactionFxPlayer from its own
/// Health.Died handler, which ZoeSpawner guarantees runs before this component's (it adds ReactionFxPlayer
/// first, and Health.Died is multicast in subscription order — the same ordering OnDied's DeathClipArmed
/// read already depends on).
public void SetDeathDisposal(DeathDisposal d, float linger);
```

It captures the spawn-time `disposal` / `deathLinger` into `_baseDisposal` / `_baseLinger` on first use, guarded by a bool, because `ZoeSpawner` assigns those fields AFTER `AddComponent` and therefore after `Awake`. `OnRevived` restores them and clears the guard — otherwise a revived character permanently inherits the last death's override, which is the same class of pooled-object haunting round 3 warns about for answerers.

**Alternatives rejected.** (i) Making `WhenDeathClipEnds` also linger — that would change the behaviour of the default disposal, which every character in the project uses, for no requested reason; the two modes mean different things and the docs already say so. (ii) A coroutine instead of `Invoke` — `OnRevived` already does `CancelInvoke(nameof(Dispose))` and that line is load-bearing (its own comment says so); switching mechanism means re-deriving the cancellation and gains nothing.

**Verification recipe (this is the one that proves point 5).** (1) Set ProtoGuy `deathDisposal = After Delay`, `deathLinger = 3` — via the Zoe window once C1 lands, or the raw Inspector before that. (2) Play `ProtoGuyDemo`. (3) `unity command eval_file`: find the spawned ProtoGuy, record `Time.time`, call `Health.Kill()`, poll on each subsequent call until the GameObject is null, report elapsed. **Expect about 3.0 s** — the no-clip case, unchanged. (4) Now author a death look: a row targeting part `Upper` with clip `Upper_S`, `durationMode = Fixed seconds`, `seconds = 2`, chipped `Death`; register a scratch answerer that returns it (needs B2). (5) Repeat step 3. **Expect about 5.0 s — 2 s of clip, then 3 s of linger.** Before this fix the same setup gives about 3.0 s with the animation cut off. (6) Set `deathDisposal = When Death Clip Ends` and repeat: **expect about 2.0 s**, proving T-0090's branch is untouched. (7) `git status` on `Assets/Demos/ProtoGuyDemo/` after the authoring — only `ProtoGuy.asset` may be modified; any atlas / sprite / meta churn means a rebuild fired and must be investigated (UNITY_DEV_GUIDE, "Never exercise a SAVE/REBUILD path against real user data").

---

### WAVE B — the runtime integration (serial: one file)

All of B1 through B4 edit `ReactionFxPlayer.cs`; B4 also edits `ZoeSpawner.cs`. Give the whole of B to ONE implementer. B5 is a different file and runs alongside.

---

#### B1 — Thread the override name through effect firing

**Files:** `Assets/Packages/Laubrary/Runtime/Zoetrope/ReactionFxPlayer.cs`.

**Mechanism.** Carry the request's `overrideName` alongside the armed reaction (a `string _armedOverride` field, cleared by `Disarm()`), and pass it to `Fire`. `Fire(FxEntry entry, EventContext ctx, string overrideName)` resolves once at the top — `var effect = entry.Resolve(overrideName);` — and uses `effect` for the `IsEmpty` guard, the `Apply`, and the `is ICombatFx` follow branch. `FireImmediate` and `FlushUnfiredFrameEntries` take the same parameter. `ReactionFx.Play(worldPos, dir)` (the bare point-play used by callers with no player) keeps its exact signature and passes `null`, so `Resolve` returns the default and that path is untouched.

**Compat.** `Resolve(null)` returns `entry.fx`. Every existing call site passes null: `OnHit`, `OnDeath`, `Raise(string)`, `ReactionFx.Play`. Byte-identical behaviour until something deliberately supplies a name.

**Verification recipe.** With A2's `Flame` slot authored on ProtoGuy's `Fire` row: `eval_file` calling `player.Raise("Fire")` shows the ordinary flash; `player.Raise(new ZoeStateRequest("Fire", "Flame"))` shows the flame effect; `player.Raise(new ZoeStateRequest("Fire", "Nonsense"))` shows the ORDINARY flash — an unmatched name yields the default, never nothing. Capture the game view for each and compare.

---

#### B2 — Ask the answerer; play the chosen row instead of the built-in slot

**Files:** `Assets/Packages/Laubrary/Runtime/Zoetrope/ReactionFxPlayer.cs`.

**Mechanism.** Add a lazily-resolved `ZoeAnswers` property, using the same lazy pattern the `Arbiter` property already uses and documents, because component add-order varies between `ZoeSpawner` and a Mirage-added player. Then in `OnHit`:

```csharp
var r = def != null ? def.hit : null;          // the built-in row — the floor, unchanged
string overrideName = null, partName = "";
NamedReaction chosen = null;

if (Answers != null && Answers.TryAsk(StateRole.Hurt, info, out var ans) && ans.IsValid)
{
    chosen = def != null ? def.RowNamed(ans.state, StateRole.Hurt) : null;
    if (chosen != null) { r = chosen.reaction; partName = chosen.partName; overrideName = ans.overrideName; }
    else WarnAnswerMissed(ans.state, StateRole.Hurt);   // names the state AND lists this character's hurt looks
}
if (r == null) return;
// ... unchanged sequence: BuildContext, EventSecondsOf, PlayBodyFx, FireImmediate, TryArmClip ...
```

`OnDeath` gets the mirror at `StateRole.Death`, and additionally does B4's disposal stamp. `Raise(in ZoeStateRequest, DamageInfo)` is added as a NEW overload; `Raise(string)` and `Raise(string, DamageInfo)` keep their exact signatures and semantics, including `Raise` returning "this character declares a row by that name", which `CueRelay` warns off and `WeaponMuzzleCue` relies on.

**The critical compat property, stated so a reviewer can check it in one glance:** when nothing answers, `r == def.hit`, `partName == ""`, `overrideName == null`, and every subsequent line is the code that exists today. There is no new branch on the unanswered path.

**Why an unmatched or unchipped answer falls back to the built-in slot rather than playing nothing.** Round 3: "what the character does when a name misses: nothing. It carries on with whatever it was doing, unchanged, and complains once. A missing state must never freeze a character or blank it." The built-in slot is what a hit does anyway, answered or not — declining to play it because gameplay code typo'd a name would make a typo suppress the flinch, which is strictly worse and is a new silent failure. The warning must **list what the character DOES have** (round 3's step 1 requirement; `Zoe.StateIdsWithRole` exists for exactly this).

**Also in this step:** expose the row that actually played, so downstream consumers stop reading `def.death` directly — `public NamedReaction LastDeathRow { get; }`, null meaning the built-in slot. B5 consumes it (or `DeathClipArmed`; see B5).

**Verification recipe (this is the one that proves the no-default-answerer rule).** (1) Author a SECOND row on ProtoGuy chipped `Death`, named `DeathBurn`, clip `Upper_S`, part `Upper`. (2) Play; `Health.Kill()` with **no answerer registered**. **Pass condition: nothing new plays — the built-in empty `death` slot, i.e. exactly today's behaviour.** A single declared death look must NOT be auto-selected. (3) Write an uncommitted scratch `MonoBehaviour` under `Assets/Scratch/` implementing `IZoeStateAnswerer` and returning `new ZoeStateRequest("DeathBurn")` for `StateRole.Death`; `AddComponent` it and `Push` it via `eval_file`. Revive, kill again. **Pass: `Upper_S` plays on the Upper part.** (4) `Remove` the answerer; kill again — back to nothing. (5) Register an answerer returning `"NoSuchRow"`; kill. **Pass: one warning naming `NoSuchRow` and listing `DeathBurn`, and the built-in slot runs — the character still dies, on time.** (6) Delete `Assets/Scratch/` afterwards.

---

#### B3 — Route arming through the part router

**Files:** `Assets/Packages/Laubrary/Runtime/Zoetrope/ReactionFxPlayer.cs`.

**Mechanism.** `TryArmClip(ReactionFx r, EventContext ctx, float priority, Action<bool> onFinished)` gains a `string partName` and resolves targets through `ZoePartRouter` instead of using the single cached `_view` / `Arbiter`. The existing single-claim logic generalises with its invariants intact:

- **One shared claim token** — the existing fresh `new object()` per claim — submitted to every target arbiter, so `HasControl` / `Release` work per-arbiter under one identity.
- **Pre-check first, all-or-nothing on PRIORITY.** Call `WouldAccept(claim, priority)` on EVERY target before touching anything; any refusal returns `ArmResult.Refused` with nothing torn down. This preserves the exact property the existing comment protects: "a refused reaction leaves the running one completely intact". Partial acceptance would give a half-claimed body — legs flinching while the torso keeps aiming — which is worse than a clean refusal.
- **Best-effort on CLIP KNOWLEDGE.** A target whose view returns false from `PlayClip` (a name that part's lauminary does not have) is simply skipped. Zero targets played means roll back and return `Refused`. This matches the convention already used throughout the codebase for unpainted meta-layers: an unknown name costs accuracy, not the whole effect.
- **The PRIMARY (`targets[0]`) owns the timebase.** `ClipSecondsOf`, the `OnFrameEntered` subscription, `PlayPass` re-looping and the completion callback all hang off the primary only. One clock, one frame stream, one `end(interrupted)`.
- **`end(interrupted)` releases every target** that still returns `HasControl(claim)`, keeping the existing "a death keeps the body forever; everything else hands it back" rule (`handsBack = priority < PriorityDeath`).
- `_view` stays as the field `BuildContext` and `PlayBodyFx` use for meta-points and the body renderer. For a composite whole-body reaction it should resolve to the primary's view, so meta-point placement finally works on a composite — additive, since it is null today.

**Compat.** For a single-body Zoe the router returns exactly one target, the root, with the root arbiter — the same two objects the code caches today. The pre-check, play and release sequence over a one-element list IS the current code path.

**Verification recipe (the headline expressibility proof).** (1) Author on ProtoGuy a row `Fire` — the existing one — with `partName = Upper` and clip `Upper_S`, duration Fixed 0.4 s. (2) Play `ProtoGuyDemo`, hold a movement direction so the legs are visibly walking (`LegsWalk3`), and `eval_file` `Raise("Fire")`. **Pass: the legs keep walking without a hitch while the upper body plays `Upper_S`, then the upper returns to `UpperAim16`.** Capture the game view mid-play. This is the case round 3 says is "inexpressible" without part targeting, and it is scope point 2's acceptance test. (3) Author a `Hurt`-chipped row with `partName` empty (whole body) and a clip that exists on both parts; register an answerer; damage ProtoGuy while walking. **Pass: BOTH parts switch, and the flinch outranks the walk (PriorityHurt 200 beats PriorityLocomotion 0).** (4) `Raise("Fire")` DURING the flinch. **Pass: refused (PriorityNamedState 100 is below 200), the flinch is not cut, and `EventRefused` fires** — the existing T-0094 contract, now across two parts. (5) Regression: repeat the whole hit / death / named-event set on single-body `Hero.asset` and confirm no behavioural difference from before the wave.

---

#### B4 — Stamp the chosen death row's disposal; register the new components at spawn

**Files:** `Assets/Packages/Laubrary/Runtime/Zoetrope/ReactionFxPlayer.cs`, `Assets/Packages/Laubrary/Runtime/Zoetrope/ZoeSpawner.cs`.

**Mechanism.** In `OnDeath`, after the row is chosen and before anything else: `if (chosen != null && chosen.overrideDisposal) GetComponent<ZoeState>()?.SetDeathDisposal(chosen.disposal, chosen.linger);`. This works because `ZoeSpawner` adds `ReactionFxPlayer` before `ZoeState`, so its `Health.Died` handler runs first — the same ordering guarantee `ZoeState.OnDied`'s `DeathClipArmed` read already documents and depends on. In `ZoeSpawner.SpawnCharacter`, add `ZoeAnswers` and `ZoePartRouter` immediately after the `AnimationArbiter` line and **before** `ReactionFxPlayer`, so both are already present when the player's `OnEnable` runs.

**Compat.** Two more no-op components on every character: an empty list, and a resolver that returns the root. The stamp only fires for a row that both exists and has `overrideDisposal` ticked, which no asset has.

**Verification recipe.** Author two death rows: `DeathBurn` (`overrideDisposal` on, `AfterDelay`, `linger 4`) and `DeathDrop` (no override). Answer with each in turn and time the disposal via `eval_file` as in A5. **Pass: `DeathBurn` gives clip + 4 s; `DeathDrop` gives the Zoe's own `deathDisposal`.** Then revive the character (target practice) and kill it again answering `DeathDrop`: **pass condition — it must use the Zoe's own setting, not the 4 s left behind by the previous death.** That is the restore-on-revive check and it is the one most likely to be got wrong.

---

#### B5 — `TargetPracticeController` must ask which death actually played

**Files:** `Assets/Packages/Laubrary/Runtime/Zoetrope/TargetPracticeController.cs`. **Disjoint from B1 through B4 — runs in parallel.**

**Mechanism.** `DeathClipEmpty()` currently reads `reaction.def.death` directly to decide whether to blank the body on a clip-less death. Once a death ROW can be chosen, that answer is wrong whenever an answerer supplied one. Replace it with a question to the player: use `ReactionFxPlayer.DeathClipArmed`, which is already the honest "did a clip actually start" signal and needs no new API at all. (`LastDeathRow` from B2 also works but is more indirect; recommend `DeathClipArmed`, which additionally makes B5 unblocked from the very start of the wave.)

Also flag, without necessarily fixing here: `TargetPracticeController.PlayIdle()` calls `view.PlayClip` directly, bypassing the arbiter — that is T-0094's own "route plain movement and named states through it" scope. If T-0094 has already routed it, leave it; if not, note it in the handover rather than silently widening this task.

**Verification recipe.** Mirage, target practice on ProtoGuy: with a death row answered, the body must show the death clip's last frame and then respawn; with no answer and no built-in death clip, the body must blank (today's behaviour) and respawn. Do both **twice in one session** (the stale-captured-state rule) and once more after a forced domain reload.

---

### WAVE C — the editor (one file, parallel with wave B)

---

#### C1 — Hurt and death as the top two rows of one unified list

**Files:** `Assets/Packages/Laubrary/Editor/Zoetrope/ZoetropeWindows.cs` **only**. (ZUI itself lives at `Assets/Packages/Laubrary/Zui/`. `CLAUDE.md` still says `Assets/ZUI/`, which is stale — do not waste time hunting.)

**Mechanism.** `BuildReactionsSection` becomes one list of uniformly-shaped rows over three unchanged storage locations:

- **Row 0, "Hurt"**, bound to `So.FindProperty("hit")`. No grip, no remove button, no id field. A **drawn, static** role chip (a `Z.Text` badge reading `Hurt`, with a tooltip explaining it is built in and cannot be changed) and a static `Whole body` part chip. Body: the existing `BuildReactionFx`, unchanged.
- **Row 1, "Death"**, bound to `So.FindProperty("death")`. Same shape, chip reads `Death`. **Plus the disposal pair**, which is currently drawn nowhere at all (finding 2.2a): `deathDisposal` as a `Z.MiniRadio` over its four options and `deathLinger` as a `NumField`.
- **Rows 2 onward, the custom rows**, bound to `So.FindProperty("events")` elements. Unchanged grip, id field, duplicate/empty badge and remove button, plus: an **editable role chip** (`EnumPicker(roleProp, "Role", tip)` — three choices, so `Z.Segmented` is chosen automatically by the existing helper), an **editable part picker**, and, visible only when the chip is `Death`, an `overrideDisposal` `Z.Toggle` gating the same disposal pair.

**The role chip must be a static badge on the built-in rows, not a disabled control.** A control that cannot change lies about what pressing it does (the "Label = action" rule). A text badge is honest, costs less width, and makes the asymmetry visible, which is exactly round 3's point: the chip IS a special case, and a visible one is more honest than today's invisible one.

**The part picker.** Sourced from the view: when `zoe.view is CompositeLauminaryView c`, the options are `["Whole body"]` plus `c.parts.Select(p => p.name)`, rendered with the same `choices.Length <= 3 ? Z.Segmented : Z.MiniRadio(wrap:true)` idiom `EnumPicker` already uses (ProtoGuy gives three options, so Segmented). A non-composite view gets a static `Z.Text("Whole body")` chip whose tooltip says this character is a single part and points at Look/Rig for adding more. **Never a text field**, and never a text field as an "empty state" degradation — that is the failure mode the ZUI rule names explicitly.

**The clip picker becomes correct for composites.** `BuildReactionFx` takes the resolved view object for its clip options: a row targeting part `Upper` uses `GetClipNameOptions(thatPart.view)`; whole-body on a composite uses the UNION of all parts' clip names, matching the runtime's best-effort per-part play, with a warning badge on the row when the chosen clip is missing from at least one targeted part; a single-body Zoe keeps `zoe.view` exactly as today. This closes finding 2.2c.

**Header layout (card-layout rule, item 1).** `grip | role chip | part chip | name or id | flexible gap | remove`, and **the header must not wrap** — a flexible gap in a wrapping row is precisely what pushes the remove button onto a line of its own. The two built-in rows omit the grip and the remove button, so their header is shorter, not differently shaped. Chips are short, so they belong on the header beside the name, not on rows of their own: vertical space is the scarce resource, and this section already stacks a whole effect list under every row.

**Override slots on an effect card.** Inside `BuildFxEntry`, under the effect's own fields: a `Z.Text($"Overrides ({n})", ZuiText.Subtle, tip)` line, one row per slot (a `Z.TextInput` for the name — this is the DECLARATION site, so a text field is correct and required — plus that slot's own effect via the existing managed-ref machinery), and a `+ Add override` button reusing `ShowAddEffectMenu`. Zero overrides must render as nothing but the Add button, so an unused card costs no vertical space.

**Stable workspace.** Toggling `overrideDisposal`, or switching the role chip, must not reflow the row under the cursor: reserve the disposal pair's slot permanently and drive it with `visibility` (Hidden keeps its layout space), exactly as `BuildEventDuration` already does for its Loops/Seconds pair. Copy that idiom rather than inventing one.

**Section naming.** Keep the section title and the toggle-bar label as **"Reactions"**. Renaming it to "States" would orphan every saved `ZuiViewBar` view (a `Z.Box` / `Z.Section` falls back to keying saved-view state by its title), it is churn the user did not ask for, and the model is fully expressible without it. Same for the `+ New event` button — keep the label.

**Alternatives rejected.** (i) A real reorderable list over a synthesised array containing hit, death and the events — that IS restructuring; it needs an index translation layer between "row 3 on screen" and "events[1]" on every commit path, and one off-by-one silently edits the wrong card. Drawing three storage locations into one visual list costs a few lines and cannot corrupt anything. (ii) Giving the built-in rows a real editable chip stored on a hidden field — breaks invariant 3. (iii) A separate "Palette" section beside "Reactions" — two places to author the same kind of thing, which is precisely the split this whole design exists to close.

**Verification recipe: the handover walk, written down. Mandatory; do not skip it.** (1) **State the goal as the user's sentence:** "I want ProtoGuy to burn away when fire kills him and drop where he stands otherwise, and I want his legs to keep walking while he fires." (2) **Walk it cold**, from "they have the editor open"; for every step record what they SEE, what they CLICK, and how they knew to click it. (3) **The empty state is a first-class screen:** do the walk on a Zoe with zero custom rows and a non-composite view — the part chip, the role chip and the disposal controls must all read sensibly with nothing authored. (4) **Do it twice in one session**, then close and reopen the window, then force a domain reload — rename a row's id both times, because the rename-worked-once-then-silently-failed bug is real and lives exactly here. (5) **Undo:** set a role chip, set a part, add an override slot, set the disposal — Ctrl+Z each, one at a time, and confirm the asset returns to its prior state. (6) **`ZuiAudit`** via `Laubrary/Audit Focused Editor Window` with the window OPEN and laid out at real size; the result is authoritative **only when `foldedSkipped == 0`**, so expand all first. (7) **Follow the data to something that matters:** every control authored above must be visibly consumed by wave B's runtime recipes — and if wave B has not landed yet, say so in those words rather than implying the authoring works. (8) Report in three buckets — verified by probe, verified by eye, not verified — and never let the first be reported as the second.

---

### WAVE D — documentation

**D1 — CHANGELOG.** `Assets/Packages/Laubrary/CHANGELOG.md`, under `[Unreleased]` → `Added` (plus a `Fixed` entry for the linger). Follow the existing entries' style: a bold headline naming the task, then prose explaining what the system now does and why, never a list of files changed. The sentence from decision D1 below belongs in it verbatim.

**D2 — the objecting note.** Folded into A1 (`NamedReaction`'s class doc comment). Round 3 asked for it by name.

**D3 — no `CLAUDE.md` change.** Its "Zoe palette" section already states the rule this task implements, including the no-default-picker clause. Adding to it would be duplication.

---

## 4. Parallelisation — what can safely run at the same time

| Step | Files touched | Safe to run concurrently with |
|---|---|---|
| **A1** | `Runtime/Zoetrope/NamedReaction.cs`, `Runtime/Zoetrope/Zoe.cs` | A2, A3, A4, A5 |
| **A2** | `Runtime/Zoetrope/ReactionFx.cs` | A1, A3, A4, A5 |
| **A3** | *(new)* `ZoeStateRequest.cs`, `IZoeStateAnswerer.cs`, `ZoeAnswers.cs` | A1, A2, A4, A5 |
| **A4** | *(new)* `IPartRoster.cs`, `ZoePartRouter.cs`; `Runtime/ZoetropeLaunimator/CompositeZonedPlayer.cs` | A1, A2, A3, A5 |
| **A5** | `Runtime/Zoetrope/ZoeState.cs` | A1, A2, A3, A4 |
| **B1–B4** | `Runtime/Zoetrope/ReactionFxPlayer.cs`, `Runtime/Zoetrope/ZoeSpawner.cs` | B5, C1 — **but B1/B2/B3/B4 are serial with each other** |
| **B5** | `Runtime/Zoetrope/TargetPracticeController.cs` | B1–B4, C1 |
| **C1** | `Editor/Zoetrope/ZoetropeWindows.cs` | all of wave B |
| **D1** | `Assets/Packages/Laubrary/CHANGELOG.md` | everything (do it last) |

**All five of wave A are file-disjoint and can run as five concurrent subagents.** Two cross-step TYPE contracts must be handed to them verbatim in their briefs so they compile together on the first try: A3 references `StateRole`, which A1 declares in `NamedReaction.cs`, and A5's `SetDeathDisposal` takes the existing `DeathDisposal` enum (no new type). Nothing in wave A can compile-check individually against a tree where its sibling has not landed — **compile the whole of wave A as one batch**, then run the five verification recipes.

**Wave B must be one implementer.** Four separate agents editing `ReactionFxPlayer.cs` will conflict on `TryArmClip`, `Fire` and `OnDeath`, all of which every one of them touches. Hand B1 → B2 → B3 → B4 to a single agent as an ordered list.

**Wave C runs fully concurrently with wave B**, because the editor needs only wave A's DATA, never wave B's runtime. This is the biggest scheduling win available: the week-sized editor work and the week-sized runtime work overlap completely.

**Ordering constraints, minimal set:** A (all five) → then B (serial) and C, concurrently. B5 is unblocked from the start if it uses `DeathClipArmed` as recommended. D last.

**Editor-serialisation note for the orchestrator:** the verification recipes are the only part of this that needs the single Unity editor. Wave A's five recipes are all short `eval_file` and asset-diff checks and can be batched into one editor visit. B and C each want a longer play-mode session. Plan for roughly three editor windows in total, not eight.

---

## 5. Decisions I am flagging — each with the answer already applied above

**D1 — "No default answerer" versus "existing assets behave exactly as before". These look contradictory and are not, and getting it wrong breaks every asset in every consuming project.** A naive reading of "nothing plays unless gameplay code explicitly asked" would mean `Zoe.hit` stops playing on a hit whenever no answerer is registered — which is every project today, and would silently kill every authored flinch. **Recommended, and planned: the built-in `hit` and `death` slots are the unchanged FLOOR, not a candidate.** The question Laubrary asks is "which hurt look, INSTEAD of the built-in one?"; declining is the normal case and yields exactly today's behaviour. Laubrary never picks among the role-chipped rows — not by rule, not by recency, not when there is exactly one. That is the owner's decision honoured precisely, and round 3's own "nobody has answered yet → it does exactly what it does today" says the same thing from the other side. **This is the single most important sentence in this plan and belongs in the CHANGELOG entry verbatim.**

**D2 — Whole-body on a composite fans out to every part.** Recommended: yes (A4 rule 3). Justification: it is round 3's stated requirement, and it has no reachable regression surface, because a composite root has no `IAnimatedView` and therefore plays nothing today. The smaller fallback, if this is judged too ambitious: whole-body means the root only, and a composite character can then never author a flinch at all — which fails the walk-through in C1's step 1, so I do not recommend it.

**D3 — Case sensitivity of the override-slot name.** Recommended: `OrdinalIgnoreCase`, matching every other authored name in this window (round 3 flagged that state names are the odd one out). **But T-0092 owns "make name matching consistent" and may land case-sensitivity project-wide — whatever T-0092 decides, match it, and do not create a third convention.**

**D4 — Do override names get the generated, regenerate-on-save, code-checked list treatment that state names get?** Recommended: **defer.** Round 4 leaves it explicitly undecided and says it should be decided before task 8 (weapons get their own state lists), which is where the first real override is authored. A1's `Zoe.StateIdsWithRole` establishes the pattern; add an `OverrideNames` collector when T-0098 needs it.

**D5 — The "parcel" (per-instance tint / scale plus the opaque payload).** Recommended: **out of T-0095's scope**, matching both the task description and round 4's build plan, which schedules no task for it at all. `ZoeStateRequest` is deliberately a readonly struct passed by `in` precisely so the parcel becomes an additive field later with no call-site churn. Say so in the handover, so it is not mistaken for forgotten.

**D6 — Per-death-row disposal override (`overrideDisposal` / `disposal` / `linger`).** Recommended: **in scope.** It is not in the task's five numbered points, but round 3's step 3 lists it ("Death rows may override what becomes of the body and how long it lingers"), it is the showcase scenario ("fire death burns away, bullet death leaves a corpse"), and the wiring it needs is the same wiring point 5 forces into `ZoeState` anyway. Cost: one bool, one enum, one float, and about eight lines of runtime.

**D7 — Multiple answerers: top-down first-answer, or strict most-recent-only?** Recommended: **top-down, first `true` wins**, with declining as a first-class answer. Strict most-recent-only means a burning-status answerer that only cares about fire must answer every hit or else suppress the base game's answers entirely. Round 3's "most recent wins, the previous is restored when the newcomer goes away" is satisfied by the stack either way.

**D8 — Drawing `deathDisposal` and `deathLinger` in the Zoe window at all.** Recommended: **yes.** They are drawn nowhere today, so scope point 5's whole feature is unauthorable and unverifiable without them. This is a required affordance, not speculative surface — but it IS two new controls, so it is flagged here rather than assumed.

**D9 — Answerer lifetime across pooling and revival.** Recommended: cleared on `OnDisable`, pruned by destroyed owner, **kept across `Health.Revive()`**. Mirage's target practice revives a live object and its registration must survive that; a pooled object is disabled between uses and must not answer with the last occupant's rules.

**D10 — `ZoeState.hitStun` is frozen from `def.hit.stunSeconds` at spawn, so a CHOSEN hurt row's stun will be inert.** T-0092 owns "make the stun box actually get read, from whichever state played", and this plan deliberately does not duplicate it. Recommended: **do not fix it here.** If T-0092 has not landed when T-0095 ships, state plainly in the handover that a chosen hurt row's stun value is authorable and not yet obeyed, rather than letting it look as though it works.

**D11 — `TargetPracticeController.PlayIdle()` bypasses the arbiter.** That is in T-0094's scope, not this one. If T-0094 left it, flag it in the handover; do not silently widen T-0095 to fix it.

---

## 6. What would make this collapse

- **If the asset-diff check is skipped even once.** Every compat claim in this plan is falsifiable in ten seconds with `git diff` on a Zoe asset, and is unfalsifiable any other way. Run it after every wave, on at least ProtoGuy (composite, has a custom event) and Hero (single-body).
- **If wave B is parallelised.** Four agents in `TryArmClip` is a merge disaster in a 470-line file whose entire subtlety lives in the ordering of six statements.
- **If C1 ships without the handover walk.** The one thing probes structurally cannot see is a missing affordance, and this task adds four new controls to a window that already hides two fields nobody can reach.
- **If a scratch answerer or probe survives into a commit.** Delete `Assets/Scratch/` and every `eval_file` artifact when each recipe ends, whatever the outcome.
- **If someone "helpfully" adds a fallback.** The instant anything plays a hurt or death look that gameplay code did not name, the split this whole design rests on is gone and no test in this plan will catch it, because everything will look like it is working. That is why D1 is written out in full rather than left as a rule to remember.

---

## 7. Addendum — reconciling with the T-0092 plan's `ReactionRequest` (found after drafting; read this before starting A3)

`ZOE_PALETTE_T0092_T0093_PLAN.md`, written in this same session, already introduces a **`ReactionRequest`** readonly struct in `Runtime/Zoetrope/ReactionRequest.cs` — `Vector2? Position`, `Vector2 Direction`, `float Amount`, `GameObject Source`, plus `static ReactionRequest From(in DamageInfo)` — and changes `BuildContext(in DamageInfo)` to `BuildContext(in ReactionRequest)`. Its own decision note says the seam is being cut once *precisely so* that T-0095's override name and the later parcel can be added to it rather than forcing a second signature change. **Shipping my `ZoeStateRequest` as written would create two competing request types on the same call path, which is exactly the outcome that plan is trying to avoid.**

**Resolution — recommended, and it should be applied to A3 before anyone writes it:**

- **Do not create `ZoeStateRequest.cs`.** Instead, **T-0095 adds one field, `string overrideName`, to T-0092's `ReactionRequest`** (and to its constructors, defaulting to null). That is the additive extension T-0092 already designed for, and it keeps ONE thing travelling with a raise: where it happened, how hard, which way, and now which override slot.
- **The ANSWER stays its own tiny type**, because an answer and a payload are different things: the answerer is told what happened and replies with which row to show. Keep a two-field `readonly struct ZoeAnswer { public readonly string state; public readonly string overrideName; }` in `IZoeStateAnswerer.cs`, and make the interface `bool TryAnswer(StateRole role, in ReactionRequest incoming, out ZoeAnswer answer)`. `ZoeAnswers.TryAsk` mirrors that signature.
- **`ReactionFxPlayer` then merges them:** the incoming `ReactionRequest` supplies position/direction/amount, the `ZoeAnswer` supplies the row name and the override name, and B1 threads `answer.overrideName` (or the request's own, for a direct `Raise`) into `Fire`. Everywhere B2 above says `ZoeStateRequest`, read `ZoeAnswer`; everywhere B1 says "the request's `overrideName`", read "`ReactionRequest.overrideName`".
- **Sequencing consequence:** A3 now has a soft dependency on T-0092's S4 landing first (the file it extends must exist). Two ways to run it, in order of preference: (i) land T-0092 S4 before wave A starts, which is the natural order anyway since T-0092 is a declared prerequisite of T-0095; or (ii) if wave A must start first, have A3 create `IZoeStateAnswerer.cs` + `ZoeAnswers.cs` only, typed against `ReactionRequest`, and let the batch compile fail until S4 lands. **Do not create a placeholder `ReactionRequest` in A3** — two definitions of that struct is worse than a red compile.
- **Everything else in this plan is unaffected.** The role chip, the part routing, the override slots on `FxEntry`, the linger fix and the whole editor wave never mention either struct.

Also worth carrying across from that plan into T-0095's briefs: its S1 makes state-name matching **case-insensitive** project-wide, which settles decision D3 above — use `OrdinalIgnoreCase` for override-slot names too, and for `Zoe.RowNamed`.
