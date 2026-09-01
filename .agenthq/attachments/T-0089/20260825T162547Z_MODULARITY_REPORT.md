# Laubrary modularity report — characters, combat and their visuals

**Scope:** Zoe (characters), Launimator (animation), the combat system, and Pyre/Chunks (the visuals). **Date:** 2026-08-25.

**How to read this:** written for someone looking at the editor, not the code. Where it says "a box", "a dropdown" or "a + button", that is literally what you see or don't see on screen. Nothing here required you to open a script.

---

## TL;DR

**The good news is bigger than expected, and the bad news is narrower and sharper than expected.**

Laubrary is genuinely good at letting you plug in **new things**. If a game writes its own character look, its own AI, its own player controller, its own visual effect, or its own explosion shape, it shows up in the right dropdown by itself, with nothing in Laubrary edited. That is the exact property you asked for, and it is real, not aspirational.

Laubrary is currently bad at letting you express **when** something applies. Every "under what circumstances does this happen?" question in the whole character/combat domain is answered by a **fixed list you cannot add to**. That is the bloat engine you are worried about: as Laubrary grows, those lists grow, and every game pays for every entry.

And there is one concrete, specific hole that is exactly the scenario you described: **a character has precisely one way to be hurt and precisely one way to die.** Not "one by default" — one, full stop. There is no + button, no condition, no variant list. The editor is honest about this: you can see there is nowhere to put a second one.

**Your muzzle-flash worry, specifically, is already solved.** The flash lives on the gun, not baked into the character. Two guns already give two different flashes with zero work. The half of it that is *not* solved is a power-up changing the flash without changing the gun.

**And the recurring theme, which surprised me: you keep building the right mechanism and then not plugging it in.** A damage-type asset that would let a character react differently to fire than to bullets — shipped, used zero times. An abilities list with a + button that already accepts anything, which is most of the answer for melee and beams — attached to every character, never triggered. A shared foundation for debris behaviours — built, then fronted by five fixed slots and no + button. **Several of the fixes below are wiring, not building.**

---

## The one idea that explains the whole report

There are three separate questions, and they are in very different health:

| The question | Health | What it means in the editor |
|---|---|---|
| **1. WHAT can be plugged in?** | 🟢 **Excellent** | Dropdowns list every option that exists in the project, including ones your game wrote. Nothing to register, no list to edit. |
| **2. WHEN does it apply?** | 🔴 **Poor** | Every "when" is a fixed dropdown with a handful of entries. You cannot add one. Adding one is a Laubrary change that every game then carries. |
| **3. HOW is a character assembled?** | 🔴 **Poor** | There is a single fixed recipe for building a character in the world. It has no hooks and no options. |

Almost every complaint in this report is question 2 or question 3. Almost every compliment is question 1.

This matters because **question 1 was the hard one and it is done.** Questions 2 and 3 are smaller, and fixing them does not require redesigning anything — it requires applying a pattern Laubrary already uses elsewhere, in two more places.

---

## Scorecard

🟢 modular · 🟡 partly · 🔴 hardcoded

### Characters (Zoe)

| Thing | Verdict | Plain English |
|---|---|---|
| What a character looks like | 🟢 | A "View" dropdown listing every look-kind in the project, including your own. |
| Its AI brain | 🟢 | Same shape. Your own AI appears in the dropdown. Best-built area in the whole domain. |
| Which device the player uses | 🟢 | A real swap point, with two options already shipped. A replay, a network feed or an AI could drive a character with nothing else changing. |
| Its player-control rig as a whole | 🟢 | A dropdown; your own rig appears automatically. |
| The mover itself (how it walks) | 🔴 | Only one exists and there is no slot for a second. To change how movement works you must replace the whole control rig above it. |
| Aiming policy (e.g. lock-on-nearest) | 🔴 | Same. Aiming is one fixed component; there is no "aiming" slot. |
| Trigger style (follow-the-weapon / press / hold / release) | 🔴 | A fixed dropdown of four. A fifth (charge-up, burst) is a Laubrary change. |
| The kind of weapon | 🟡 | Instant-hit firing already ships and works. But the character's trigger only ever drives the projectile path, so from a character's point of view every weapon is a projectile weapon. Melee and beam have no finished route — though see the abilities list below. |
| Abilities (dash, shield, area attack) | 🟡 | **There is already a list with a + button that accepts anything your game writes.** It gets attached when the character spawns and then nothing ever triggers it. A finished mechanism, half-wired. |
| Its stats and health | 🔴 | A plain number. There is a full stat system shipped in Laubrary (modifiers, buffs, priorities) that combat does not use at all. |
| How the character is assembled in the world | 🔴 | One fixed recipe, no hooks. **This is the real reason movement and firing "feel hardcoded" even though the data is pluggable.** |

### Combat and events

| Thing | Verdict | Plain English |
|---|---|---|
| The hit reaction | 🔴 | **Exactly one box.** No +, no condition, no variants. |
| The death reaction | 🔴 | **Exactly one box.** Same. |
| Custom named events | 🟢 | A list with a +, works exactly the way you'd want — and proves the shape is available. Firing a weapon already raises one (called "Fire"), so it is proven in use. Hit and death are the two things deliberately kept out of it. |
| "What kind of damage was that?" | 🔴 | A damage-type asset **ships in the package and is used by literally nothing.** Zero uses, zero assets made. A labelled empty socket. |
| What plays when something happens | 🟢 | A stackable list of effect cards, with an "Add effect" menu that grows on its own as new effect kinds ship. Genuinely excellent. |
| Who owns the muzzle flash | 🟢 | **The gun.** Different guns already look different, for free. |
| Where the flash appears on the body | 🟢 | **The character.** So any character can hold any gun. Careful, correct design. |
| Who owns the bullet impact | 🟢 | The ammo. Same good shape. |
| Weapon fire sound | 🔴 | The one consequence that skips the effect system entirely and calls the audio tool directly. The odd one out. |
| Rejecting a hit (e.g. transparent pixels don't count) | 🟢 | A genuinely pluggable list of vetoes. But it can only *reject* a hit, never *change* one. |

### Animation (Launimator)

| Thing | Verdict | Plain English |
|---|---|---|
| Animation names | 🟢 | Free text, no fixed list. Consuming fields are dropdowns filled from the character's real animations — you never type a name twice. |
| Meta Layer *names* (muzzle, hitbox, trail) | 🟢 | Free text. Launimator has no idea what a "muzzle" is, which is exactly right. |
| Meta Layer *modes* (Shape / Point / Vector) | 🔴 | Exactly three, fixed. A fourth is a change in several places at once. |
| Meta Layer → effect wiring | 🟢 | **There is no hardwired "muzzle spawns a Pyre" anywhere.** It is Meta Layer → cue → whatever effect you picked. This is the part you were most afraid of and it is clean. |
| When an effect fires during an animation | 🟡 | **Split.** Cues, and a weapon's muzzle, fire on a *named moment* you author — correct and safe. But Hit and Death effects fire on a **frame number**: re-time or re-order those animations and they silently land on the wrong instant, with no error. The by-name option used to exist there and was deliberately removed for authoring convenience. |
| Bringing your own animation player | 🟡 | You can bring your own *look* freely. But if you want your own renderer *and* still author animations in Launimator, there is no slot to plug into. |

### Visuals (Pyre / Chunks / SpriteFx)

| Thing | Verdict | Plain English |
|---|---|---|
| New explosion shapes | 🟢 | **The flagship.** A new shape written by a game appears in the shape picker, with its own dials generated automatically, and needs no editor work at all. This is "growth not bloat" done properly. |
| New effect modifiers | 🟢 | Same, and one modifier set is shared by Pyre and SpriteFx rather than duplicated. |
| Chunks debris behaviours | 🔴 | A proper shared foundation exists and the machinery that runs them is written against it — but the debris asset offers **five fixed slots and no + button**, so a behaviour your game writes has nowhere to be stored. **The foundation is built and the door is locked.** |
| Legacy built-in shape list | 🟡 | A frozen list of 19 that still ships beside the extensible system. Harmless, but it's the one a new user meets first. |
| Could a game replace Pyre wholesale? | 🟢 | **Yes.** The "play an effect" contract names no tool. A game's own effect system slots into every same place and is driven by the same events. |
| How effects are hooked up | 🟢 | Entirely by pointing slots at assets in the inspector. Never in code. |

### The toolkit itself (ZUI, asset browsers)

| Thing | Verdict | Plain English |
|---|---|---|
| Asset browsers and pickers | 🟢 | One shared system, adopted consistently, and tools opt in by adding a file rather than by editing a shared list. |
| Building a new editor panel | 🟢 | Inherit the window base and compose from existing controls. |
| Adding a new *shared* UI control | 🟡 | A game can build a control for itself, but cannot add one to the shared vocabulary the rest of the toolkit draws from. The ZUI-first rule is enforced by culture, not by mechanism. |
| Known missing controls | 🟡 | Text field, asset picker, colour field, pick-one-from-a-list dropdown, and there is no red in the palette at all — so nothing can be shown as dangerous or locked. Documented, not yet closed. |

---

## Your four worries, answered directly

### 1. "Different muzzles depending on gun or a power-up"

**Mostly already solved, and better than you think.**

The muzzle flash is a slot on the **weapon asset**, not on the character. Two guns = two flashes, no work. Swap a character's weapon and the flash swaps with it. Better still, the flash's *position on the body* is set by the **character**, not the gun — so the same gun stays usable by any character, and a character can hold any gun. That separation is exactly right and someone thought about it.

**What is not solved:** a gun has exactly **one** flash slot. A power-up that should change the flash without changing the gun has nowhere to go — you'd need a second copy of the whole weapon. And the "I just fired" moment always announces itself under one fixed name, **"Fire"** — you can listen for it in a Custom Event, but you cannot have two differently-named fire beats on one weapon.

**Verdict: the fear was well-founded in general but wrong in this specific case. It's the character-side equivalent that's broken, not the weapon side.**

### 2. "The same Zoe can be hit in different ways and die in different ways"

**This is the real hole, and it is worse than "not done yet" — the assumption that there is only one is baked into four separate places.**

Open a character in the Zoe window and look at Reactions. You see a heading **"Hit"** and one editor. Then a heading **"Death"** and one editor. **Neither has a + button.** Directly beneath them is a "Custom events" list that *does* have a +. So the editor is telling you the truth: a character has one way to be hurt and one way to die, and Laubrary already knows how to do lists — it just doesn't do one here.

Four things assume singularity, not one:

1. The character asset has one hit slot and one death slot, not lists.
2. When a hit happens, that one box is used unconditionally — nothing ever asks "which of these applies?", because there is nothing to ask about.
3. The reaction has **no "when" setting at all**, so even if something did ask, there'd be nothing to answer with.
4. The stun duration is copied out of the hit box **once, when the character is first created** — so a per-hit-type stun is structurally impossible today, not merely unwired.

And the piece that was supposed to solve this **already ships and is used zero times**: a damage-type asset, whose own description says it exists so a target can react differently to being burned than to being shot. There are no assets of it in the project and nothing reads it. The socket is labelled and empty.

**What you have to do today to get a burn-death and a crush-death: duplicate the entire character.** That forks its health, faction, look, weapons, AI, movement rules, cues and events — for one animation difference. That is unacceptable and you were right to flag it.

**The good news:** the fix is small and uses a pattern Laubrary already runs in two other places (see "What good looks like" below).

### 3. "Future optional solutions grow what Laubrary can offer rather than bloat it"

**This is largely working, and the flagship example is excellent.**

A game can add its own explosion shape and it appears in Pyre's shape picker with its own controls generated automatically — no editor work, no registration, no list to edit. Same for effect modifiers. Same for new effect kinds in the "Add effect" menu. Same for AI brains, character looks and player-control rigs. This is real and it is the hard part of what you asked for.

**Three exceptions:**

- **Chunks debris behaviours.** Frustrating rather than missing: the shared foundation a game would build on already exists and is properly used internally, but the debris asset only offers five named slots and no + button, so there is nowhere to put a sixth. Adding one today means six separate edits inside shipped Laubrary. **This is the cheapest big win in the report — the hard part is already done.**
- **Shared UI controls** — a game can make a control for itself but cannot contribute one to the toolkit's shared vocabulary.
- **Every "when" list** — and this is the important one. Because conditions are fixed lists, *the only way to add a new condition is to grow the list for everyone.* **That is not growth, that is exactly the bloat you named.** Left alone, this is the mechanism by which Laubrary gets fat.

### 4. "Bespoke game-native solutions can be used in place"

**True at runtime for the big pieces, false for the small ones, and the editor degrades quietly.**

Works cleanly: your own look, AI, player-control rig, input device, effects, projectile paths, hit-rejection rules, and a total replacement of the visual FX tools.

Doesn't work: your own mover, your own aiming policy, a melee or beam weapon, your own damage maths, and any change to how a character is assembled — all of which need you to replace a bigger thing than the thing you wanted to change, or to fork Laubrary.

**The quiet one worth knowing about:** a game that brings its own visual system gets a **working game and a half-working editor**. Parts of the character window and most of the Mirage preview only light up for Launimator-backed characters. Mirage at least says so — it tells you there are no clips to preview. The character window's rig section does not: it simply renders nothing, with no message. **That silent case is the one to fix**, because an empty space looks like "nothing to configure here" rather than "this tool doesn't support your setup".

---

## The three structural weaknesses, ranked

### 🥇 1. "There is exactly one answer" is baked in

Not just for hit and death. It is the same shape everywhere: one way to walk, one way to aim, one usable weapon kind, one muzzle slot per gun, one flash per fire, one damageable region per character. The system consistently models "the answer" rather than "the answers, and how to choose".

**Why it matters:** this is the assumption that costs the most to remove later, because each instance is baked into several layers at once, and every asset already authored assumes it too.

**The twist: you have already built the way out, twice, and left both unplugged.** Besides the damage-type socket, the character asset carries an **abilities list with a + button that already accepts anything your game writes** — a dash, a shield, an area attack, a melee swing, a beam. It is attached when the character spawns and then nothing ever pulls its trigger, and a second, fixed weapon-slot list beneath it quietly took over the job. So the open, list-shaped, plug-anything mechanism this report says is missing from weapons **is already sitting on the character, finished, one wire short of working.** Before designing anything new for melee or beams, look at that first.

### 🥈 2. Every "when" is a closed list

You can plug in new **things**. You cannot plug in new **circumstances**. "When moving", "when idle", "when always" — that's the entire movement condition vocabulary. Facing sources: three. Corpse fates: four. Trigger styles: four. Where an effect appears: four. Which direction it uses: four. Which number it scales by: two.

**Why it matters:** it is the bloat engine, and it is *silently* the bloat engine. Every future Laubrary feature that needs a new circumstance grows a list that every game then carries and every dropdown then shows. **Fixing this one prevents future damage rather than repairing past damage — which makes it the highest-value thing to do before building more.**

### 🥉 3. Building a character is one fixed recipe

There is a single unhookable procedure that assembles a character in the world: what components it gets, in what order, with what settings. No options, no hooks, no way to say "same as usual but not that part".

**Why it matters:** this is the actual reason movement, aiming and firing *feel* hardcoded even though their data is pluggable. The data being swappable doesn't help if something else decides unconditionally what gets attached. Any game wanting a platformer character, a pooled character or a character without physics has to fork this — and then every Laubrary tool that builds characters, including the preview, still uses the original.

**Secondary issues**, worth fixing but not structural: hit and death effects timed to frame numbers instead of named moments (a silent-breakage trap, and the rest of the toolkit already does it the safe way); Meta Layer modes fixed at three; weapon-fire sound bypassing the effect system; the character window only lighting up its rig section for Launimator-backed looks; a full stat system and a full rules system both shipped and both entirely unused by combat.

**And one trap worth its own line: a list with a + button that only ever uses the first entry.** A weapon's ammo list is exactly this — you can add several, and only the first has any effect. This is *worse* than a single slot, because a single slot honestly tells you there is only one, whereas a list actively invites you to add a second and then silently ignores it. **Anywhere a + button exists, it must either work or not be there.**

---

## What good looks like — and why you already have it

**You do not need to invent the answer. Laubrary already runs the right pattern in two places:**

- **Movement poses** are an *ordered list of rules*: a condition, then what to play. First match wins, last one is the fallback.
- **Custom events** are an *open list you add to with a + button*.

Apply the first shape to hit and death and the problem is gone. In the editor that means: **Hit and Death each become a list with a + button**, each entry has an optional **"when"**, and the first one that matches wins. Leave one entry with no condition and it behaves exactly as today — so nothing you have already authored breaks.

**The one thing that must be got right:** the **"when" itself has to be pluggable** — a picker of condition kinds, the same way effects are a picker of effect kinds — **not a fixed dropdown of conditions Laubrary happens to have thought of.** If it ships as a fixed dropdown, you have moved the bloat, not removed it. This single decision is the difference between fixing weakness 2 and entrenching it.

---

## The scope of flexibility to actually design for

You asked to map out how much flexibility is needed. Here is the honest axis list — the things a reaction realistically needs to vary by. Not all need building now; the *model* needs to be able to express them, because that's the part that's expensive to retrofit.

**What caused it:** damage type or element · which weapon · which attacker · was it a critical hit.

**How much:** the amount · was it a killing blow · overkill beyond zero · what fraction of max health.

**From where:** direction of the hit · position of the hit · which part of the body or which painted region was struck. *(Partly there already: damage can be scaled per region — a headshot doubling it, armour halving it — but the character-building recipe only ever creates **one** region covering the whole body, and there is no authoring surface to add more. The maths exists; the anatomy doesn't.)*

**The victim's own state:** health remaining · already on fire / frozen / stunned · armoured, shielded or blocking · airborne or grounded · which way it is facing relative to the hit.

**Variety for its own sake:** three death animations, pick one at random — this is a legitimate and very common need and today it is impossible.

**Situation:** underwater, in darkness, during a special phase — game-defined circumstances Laubrary should never need to know about, which is precisely why the condition kinds must be pluggable.

**One structural note, and it is the most important sentence in this report.** All of the above needs a richer description of "what just happened" to travel with the hit than exists today. Today that description carries only: how much damage, who dealt it, which side they're on, where it landed, which way it came from, and whether it was a critical. Notably absent: **what kind of damage it was** — which is why the damage-type socket sits empty.

That narrowness is **not an oversight — it is a decision that was made on purpose and written down**: the description was deliberately locked to a fixed set, explicitly rejecting an open, extensible one. **This matters enormously, because it directly contradicts the fix this report recommends.** Pluggable conditions require an open description — a condition your game invents can only ask about things the description can carry. So "make conditions pluggable" and "keep the description fixed" cannot both be true. **This is one decision, not two, and it has already been made the other way.** Reopening it deliberately is the first real piece of work; everything else in the recommendations below is downstream of it.

---

## What is already right — please don't break it

Worth stating plainly, because the fixes above should not disturb any of it:

- **The plug-in machinery.** Anything a game writes appears in the right dropdown automatically. This is the hard part and it is done.
- **The bridge structure.** Combat knows nothing about visuals. Characters know nothing about Pyre. Animation knows nothing about combat. Each connection is a small separate piece you can delete. This is textbook and it is why "swap out Pyre entirely" is genuinely possible.
- **Effects are stacked lists, not single slots.** One hit can already play a flash, a burst, debris, a sound and a knockback together. The list-of-consequences half of your worry is solved — it's the choice-of-which-list half that isn't.
- **Weapon/character separation.** The gun says what plays; the character says where. Deliberate, and correct.
- **No hardwired muzzle → Pyre path.** The thing you specifically feared does not exist anywhere.
- **Names are picked, never typed twice.** Animation and marker fields are dropdowns fed from real data.
- **Pyre's shape system** is the model every other part of the toolkit should copy.

---

## Two corrections to the project's own notes

- **ZUI now ships fully inside the Laubrary package.** The setup notes still warn that it lives outside the package and that tools using it wouldn't work in another project. **That is out of date** — every piece of ZUI's actual code now lives inside the package; what remains outside is a handful of saved settings and demo files, not the toolkit itself. **Tools are self-contained. The warning should be deleted before it sends someone off to do work that is already done.**
- **There are seven leftover empty folders** from earlier renames and from one abandoned system — including one named for character combat that contains nothing whatsoever. Harmless, but they make the toolkit look like it has systems it doesn't have, and anyone auditing it will waste time opening them.

---

## Recommended order

1. **Reopen the one locked decision: make "what just happened" open, and conditions pluggable.** These are a single decision, not two — a condition your game invents can only ask about things the description can carry, so a fixed description forbids pluggable conditions outright. This is the only item here that is a genuine reversal rather than an addition, and it is **by far the most expensive to defer**: every system built against the narrow version raises the cost again.
2. **Turn Hit and Death into lists with a + and an optional "when".** Directly closes your headline worry. Leave one entry with no condition and everything already authored behaves exactly as it does today.
3. **Wire up the abilities list that already exists** before designing anything new for melee, beams or dashes. It may be most of the answer already.
4. **Give the character-building recipe hooks**, so a game can change one part without forking the whole thing.
5. **Open Chunks debris behaviours** — the foundation is already built, it just needs a + button.
6. **Let hit and death effects fire on named animation moments**, the way cues and muzzles already do. This is a silent-breakage bug waiting to happen every time an animation is re-timed.
7. **Audit every + button** for whether it actually uses more than the first entry, and remove the ones that don't.

**Item 1 is the one that gets more expensive every week it waits, and it gates the honest version of item 2.** Items 3–7 cost roughly the same whenever they're done, and 3 and 5 are unusually cheap for what they return.

---

## A closing note on the policy itself

You asked whether the modularity policy is *healthy* around characters and combat. The honest answer: **the policy is being followed where someone remembered to follow it, and there is no mechanism forcing it.** Pyre's shapes, the effect palette, the look/brain/controller dropdowns and the asset browsers all got it right. Chunks' behaviours, hit and death, the character-building recipe and the fire sound all got it wrong — and nothing flagged that, because nothing can. Every one of the wrong ones compiles, runs, and looks finished.

The two habits that would have caught all of them, worth adopting as rules: **"if there can ever be two of these, it is a list from day one"**, and **"if it answers *when*, it must be pluggable, never a fixed set of choices"**. Both are cheap to apply while designing and expensive to retrofit — which is exactly the position several of these systems are in right now.
