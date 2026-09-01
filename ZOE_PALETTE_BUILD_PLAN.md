# Laubrary — round four: closing the loop and a build plan

**What this is:** round four, answering your notes on round three. Round three (the palette model — asset declares vocabulary, your game picks which item to show, Laubrary alone decides what mechanically happens) is accepted and stands as written except where this document says otherwise. **Date:** 2026-08-26.

**How to read it:** same rule as round three — written for someone looking at the editor, not the code. Nowhere here should you need to have seen a script. **Everything in round three that this document calls "proposed" or "not yet built" really is exactly that — nothing from round three has been built.** Round three itself was talk, not construction, and this round doesn't change that; it only answers a few open points and turns the result into an ordered list of things to build.

---

## 1. What changed since round three

Five things came out of your notes, and each changes or confirms something specific in round three:

1. **The product question is answered, not deferred.** Round three ended by declining to force an answer to "is Laubrary a no-code authoring tool, or a library that games drive?" and kept both doors open. You've now closed one of them: **Laubrary is not a no-code editor.** It should remove from your code only as much as is genuinely reusable, and native game code stays the thing that decides what's specific to a given game. This doesn't undo anything already designed — the palette itself, and the "ask by name" mechanism, are exactly as useful either way — but it does settle which direction future work should default toward when there's a choice.

2. **No state ever plays without gameplay code deciding it should — no exceptions, not even a list of one.** This is a real reversal of one specific thing round three recommended, and it should be said out loud rather than quietly dropped: round three's closing section proposed keeping one door open — an optional built-in "default answerer," an asset that would answer the hurt-look and death-look questions on its own using a simple visible rule, used only when your own game supplied no answer. You've declined that. The reasoning you gave is consistency: if a hurt-look question or a death-look question can go unanswered by your game and still get answered by *something*, that's a hidden exception to the rule that gameplay code always decides. So: that built-in default-answerer is not going into the build list below. If a designer wants "if there's only one death, just play it," that has to be written as ordinary gameplay code that explicitly answers the question — never a Laubrary behaviour that fires on its own.

3. **Movement and aiming are not gameplay-code decisions — they stay Laubrary-owned, swappable modules. Hit detection needs a real look before the same can be said.** Round three's split was "the game decides *which* named state to show, Laubrary decides *what happens mechanically*." Your notes add a boundary that split didn't spell out: for movement, aiming, and hit detection specifically, you don't want the *game* choosing behaviour at all — you want to keep picking from a shelf of Laubrary-provided modules, and when a project needs a kind of movement or aiming or hit detection that doesn't exist yet, the answer is a new module added to Laubrary, not bespoke logic written into that one game. This is not a contradiction of round three's table, it's a clarification of the third row ("what happens to it mechanically — Laubrary, always") extended to whole systems, not just reaction consequences.

   **Checked against the project, and it's a mixed result — worth being honest about rather than a clean "already fine."** Movement and aiming really are already built as two separate, independently swappable pieces, documented as reusable across any 2D perspective without touching one when you replace the other. Weapon-firing is a third, similarly swappable piece. **Hit detection is not shaped the same way today.** It currently lives as one shared central piece that everything routes through, with a single extension point for special cases, rather than a shelf of interchangeable modules the way movement and aiming are. That may well be the right shape for hit detection specifically — a single shared funnel is a reasonable design, not an accident — but it is a *different* shape from "swap the whole module," and this document isn't the place to decide which shape hit detection should have. **That's an open decision, listed at the end, not settled here.**

4. **A concrete pattern for weapon fire specifically:** a Laubrary-owned module watches the fire button, and announces "the trigger was just pulled" — game code listens for that announcement and *decides* which named state to ask the character to show (so it can check what weapon is equipped, whether a powerup is still active, and so on), rather than Laubrary or the weapon itself always deciding that on its own. Section 3 below checks this against what's actually built — and it turns out more of the pipe already exists than either of us expected, with the gap being narrower and more specific than "nothing is wired up."

5. **A new proposal: a "variation" mechanism** — a state has one required default effect, and a game can optionally pass along a name at the moment it asks for that state, swapping in a different effect instead (your own example: a muzzle effect defaults to an ordinary flash, but passing "FlamethrowerPowerup" swaps it for a flame-thrower effect). Section 4 below checks this against round three's opaque parcel proposal, which was aimed at a related but different problem.

---

## 2. Should events be categorized by anything other than their name?

**Verdict: yes for everything your game asks for, and round three already agrees with you there — nothing changes. But one small, honest exception is proposed to survive, for a narrower reason than "categorizing" the event, and it should stay in the design.**

For everything in list A — states your game explicitly asks a character to show, "Shooting gun," "Shooting lazer," anything you invent — you're already right and round three already designed it that way. The name is nothing but an address. Laubrary never looks inside it, never asks *why* it was requested, never treats one name differently from another.

The one place a small marker is proposed to exist is the **role chip** — the three-way tag (nothing special / this is a hurt look / this is a death look) that round three put on rows so they'd be eligible to answer Laubrary's own two built-in questions ("which hurt look?" and "which death look?"). **This chip does not exist yet — it's part of round three's unbuilt model, same as everything else in this section.** It's worth being precise about what it actually is, because it's easy to mistake for exactly the thing you're objecting to:

- **It is not a category on the event.** It would never decide *whether* or *when* anything happens — that stays entirely health reaching zero, entirely mechanical, entirely untouched by anything you author. A "Shooting gun" state doesn't need one and never will.
- **It is a proposed filter on which answers would be legal for a question Laubrary itself asks — nothing else.** Right now, hurt and death can't be asked for by name at all — there's no "legal answer list" to worry about yet, because the mechanism itself doesn't exist. Once it does, the hurt-look and death-look questions would each need exactly one answer, chosen from a list. Without any marker at all, that list would be *every state the character has*, including a walking cycle. If your game (or, in principle, some future opt-in helper) picked the wrong name for "which death look," nothing would catch it — the character would die exactly correctly and simply look wrong while doing it.

That failure is bounded and honest — round three already established that a wrong pick here would produce a character that dies looking wrong, never a character that fails to die — so the chip isn't protecting against something catastrophic. But it is the difference between that mistake being visible on the row (a picker that can't even offer the wrong name) versus being silent (a free-text or unfiltered picker that would happily accept anything and only fail at the exact moment it's asked for). Given the chip costs one field per row, would default to "nothing special" so nothing already authored changes meaning, and only ever exists to narrow a menu — not to gate behaviour — it earns its keep. **Recommendation: keep it in the design, unchanged from round three, but describe it to yourself and to Claude going forward as "which rows are legal answers to Laubrary's two built-in questions," never as "the category of this event."** That framing keeps it from ever growing back into round two's condition system, which is the thing you were actually worried about.

---

## 3. The fire-controller pattern, formalized

**What you described:** a Laubrary-owned module watches for the player pressing fire and announces that the trigger was pulled. Game code listens for that announcement and decides which named state to ask the character to show, because only game code knows what weapon is equipped and whether a powerup is active.

**Checked against the actual project — and there's already more built than expected, in a place that isn't obvious.**

- A Laubrary-owned module that watches the fire input and decides *when* a shot should go — respecting press/hold/release rules and the currently equipped weapon's own firing mode — already exists and already works, alongside separate, similarly swappable movement and aiming modules, so any 2D perspective can reuse it untouched.
- **A weapon firing, whether by a held trigger or a hitscan shot, already asks a character to show a named state today** — every shot already raises the built-in "Fire" name automatically, through the exact same "ask a character to show this named state" mechanism round three described as List A's finished, working feature. This already happens today, for every weapon, with no extra authoring needed. **This is a real, working piece of prior art that round three didn't mention and that this design should build on rather than reinvent.**
- **The actual gap is narrower than "nothing is wired up."** What's missing is that the name it asks for is *fixed* — always "Fire," chosen automatically, the same for every weapon and every powerup. There is currently no point where *game code* gets to look at what's equipped and choose a *different* name instead.

**What's missing, concretely:** not a whole new announcement bolted onto the fire module, but letting *game code* participate in choosing the name that already gets asked for on every shot — so a lazer weapon can make that automatic "Fire" become "Shooting Lazer" instead, based on what your game knows about the currently equipped weapon and any active powerup.

**Sizing this honestly:** smaller than first thought, precisely because the announcement half already exists. This is a small addition to an already-existing, already-firing pipeline — not a new module and not a new category of thing. It belongs with round three's step 1 work (making the ask-by-name mechanism trustworthy), since it's the same class of "make the already-working plumbing something you can actually rely on" work.

**One honest limit on this verification:** I checked the top-down / twin-stick control path specifically, since that's where the fire-reading module and its documentation live, and confirmed the existing "Fire" wiring covers both a held-trigger weapon and a hitscan (instant-hit) weapon. A different perspective (a platformer, a light-gun game) is documented as bringing its own movement module while reusing the aim and fire modules exactly as they are — so this same pattern should apply everywhere fire is used, not just top-down, but I haven't independently traced every perspective to confirm that.

---

## 4. The variation-override proposal, against the opaque parcel

Round three's opaque parcel was **proposed**, not built, to solve a specific, named problem: "bigger muzzle flash for 10 seconds" and similar power-ups would otherwise turn into a duplicated row, with a duplicated effect list, on every character, for every state they touch — round three's worst-scoring case for the whole model. The parcel's fix is to let a game hand a **value** — a scale, a tint, a per-instance blob it defined itself — through to an effect at the moment it's played, instead of authoring a whole second row.

Your variation proposal is different in kind, not just in size, and the difference is worth being precise about:

- **The parcel would carry continuous or opaque data through to an effect that already exists** — make this one bigger, make this one that color, hand this specific value to a game's own effect. It would never change *which* effect asset a Laubrary-owned card is pointing at. It was never meant to.
- **Your proposal swaps which discrete effect asset gets used at all** — not "this muzzle flash, but bigger," but "not this muzzle flash — a completely different one." That's not a value the parcel could carry; it's a different asset reference entirely, and nothing in round three's design currently lets a card do that without becoming a whole second declared state.

So: **genuinely different, and it fills a real gap the parcel wouldn't cover.** For "the same effect, scaled," the parcel already covers that case (once built) and nothing new is needed. For "an entirely different effect, selected by name, without declaring a whole new state just to change one effect on an existing card," neither the parcel nor plain list-A growth is a clean fit — duplicating an entire row (a new animation, a new duration, a new everything) just to change one muzzle effect is exactly the "declared states drifting independently" problem round three already flagged as a cost of this model.

**Recommendation: adopt it, as a genuine addition alongside the parcel, not instead of it.** Concretely: a state's card keeps exactly one required default effect (as today), plus any number of optional named override slots. When your game asks for the state, it may optionally pass along one override name; if that name matches a slot on the card, that effect plays instead of the default, otherwise the default plays. This is new work — a place on the card to declare the override slots, and a way for the "ask for this state" call to carry an optional override name.

**Where it fits in the build order:** it's a property of what a declared state carries, which is exactly round three's step 3 (the model) — recommend adding the capability there, at the same time the role chip and body-part targeting are added, since it's the same kind of "what does a card carry" decision. The first place you'd actually *use* it is naturally step 5 (weapons get their own state lists), since a muzzle effect is exactly the kind of thing a weapon state would want to override — so the capability is built in step 3, and your flamethrower example becomes the first real thing authored with it in step 5.

**What it does not solve, so it isn't mistaken for more than it is:**
- **Continuous scaling or tinting is still the parcel's job.** If a powerup means "the same effect, but bigger" rather than "a different effect," that's a value passed through the parcel, not an override name.
- **The incoming-hit damage-kind hole is still the parcel's job.** That problem is about your game *answering* a question Laubrary asks ("which hurt look?") using information it doesn't currently receive — a discrete-effect override on a card doesn't touch that at all, because the override is chosen by whoever is *asking* for a state, and for hurt/death your game is the one *answering*, not asking.
- **A whole different reaction — different animation, different timing, different body part — is still a new row in list A.** The override mechanism only swaps one effect on a card that's otherwise the same; it's not a substitute for declaring a genuinely new state.

---

## 5. Build plan

Numbered for a project tracker. Sizes reuse round three's own estimates where a task is one of its five steps. Ordered so nothing depends on a task listed later.

**1. Fix: dying with no death animation authored destroys the character instantly instead of lingering.**
Plain-language: every character in the project today is supposed to have a short pause before its body is cleaned up when it dies — long enough for a death effect to play, for the game to notice, for it to feel like a death rather than a disappearance. But that pause only actually happens if a death animation has been authored, and right now not one character in the project has one, so in practice every death is instant and the pause never runs. This task makes the pause work correctly whether or not a death animation exists, so it behaves consistently once you start authoring real death looks.
Size: small — hours, not days.
Depends on: nothing.

**2. Fix: a character's hit-detection area is never turned back on after it dies and is later revived.**
Plain-language: when a character dies, its ability to be hit again is switched off, which is correct — you don't want to keep shooting a corpse. But nothing currently switches it back on if that same character is brought back to life and reused, which is exactly what happens every time a respawn feature runs. The practical effect is a revived character that can never be hit again. This task makes revival correctly restore hit-detection.
Size: small — hours, not days.
Depends on: nothing.

**3. Make the "ask a character to show this named state by name" mechanism trustworthy.**
Plain-language: the feature that lets your game ask a character to show something by name already exists and already works — this task is about making it safe to lean on. It covers things like: generating an always-up-to-date list of valid names so a rename can't quietly go stale; making a missed request say loudly what the character *does* have instead of failing quietly; making name-matching behave consistently with the rest of the project; making a requested state actually carry position and facing direction with it instead of arriving with nothing; and making the character's stun setting actually get honored instead of being silently ignored, which it currently is. This is mostly invisible plumbing work, with two exceptions you will actually notice: effects on a fire state will start landing at the weapon's muzzle instead of the character's center, and a stun value typed into a custom event will start being obeyed for the first time — worth double-checking those values before they reach every project, since nothing has ever enforced them before.
Size: a few days.
Depends on: nothing.

**4. Let game code choose which name a shot asks for, instead of every shot always asking for the same fixed name.**
Plain-language: every weapon already asks a character to show a named look each time it fires — that part is already built and already working. What it doesn't do yet is let your game code have a say in *which* name gets asked for. This task adds that: your game gets a chance, at the moment a shot is about to go out, to look at what weapon is equipped and what powerup is active, and choose the name — "Shooting Lazer" instead of the ordinary default — before it's asked for.
Size: a few hours to about a day — smaller than it first looked, because the announcement itself already exists; this is about letting game code steer a choice that's currently automatic.
Depends on: task 3 (same class of "make the existing firing pipeline trustworthy" work).

**5. Fix the interruption and stomping bugs.**
Plain-language: right now, if a character is partway through reacting to being hurt and then a stronger reaction (like dying, or a new action) takes over, the hurt reaction just gets cut off — losing whatever effects were still queued and never sending the signal that says "I'm done." This is currently invisible because no character has both a hurt look and a death look authored yet, but the moment you author the first one, this becomes an everyday, visible bug. This task makes sure the part of the toolkit whose whole job is deciding who's allowed to take over is actually attached to every character and actually used for reactions, plain movement, and named states alike, with a clear, stated rule for what's allowed to interrupt what, and makes sure an interrupted reaction is told it was interrupted rather than assuming it played to completion.
Size: about a day of work, plus time watching it play out once it reaches other projects — recommend shipping this on its own, with nothing else bundled into the same update, since it's the one genuine behaviour change in this whole plan. **Timing reason, stated plainly (this is *why* it comes before the model, not because it depends on tasks 1 or 2):** authoring your first real death look is exactly what turns this from a dormant bug into a daily one, and step 6 below is where death looks actually get authored — so this needs to already be fixed by the time step 6 lands.
Depends on: nothing structurally, but must land before task 6.

**6. Build the model: the role chip, which body part a state speaks for, hurt and death as the top two rows of one list, and the default-plus-override capability for a card's effects.**
Plain-language: this is the actual new feature. Every character gets one open, growing list of named looks it can show. Two of those looks are special only in the sense that Laubrary itself asks a question to fill them in ("which hurt look?" and "which death look?") rather than your game announcing them — everything else on the list works exactly like any other look, requested by name, by your game, whenever your game decides to. Each look can also say which part of the character it controls (the whole body, just the top half, just the legs), so a character can show more than one thing at once — walking legs and a firing upper body, for instance. And each look's effects gain the ability to have a single always-used default plus any number of named alternates, so a look like "firing" can swap its default effect for a completely different one when your game says to, without needing a whole separate look just for that one change.
Size: about a real week.
Depends on: tasks 1, 2, 3, and 5 (this is where hurt and death actually become authorable, so everything that makes authoring them safe has to already be true).

**7. Build the honesty and bypass-resistance surface.**
Plain-language: a small set of visibility tools so nobody — including an AI writing code for this project — quietly starts making a character show something without going through its declared list of looks. Each row shows whether anything in the project actually asks for it. A project-wide check flags a name something asks for that no character declares, and the reverse — a character showing something that never came from its own list. Deleting a row warns you if anything still depends on it. Each look gets a way to preview it without the game running. A written rule gets added to this project's own instructions, telling any future contributor — human or AI — that a character shows things only through its declared list, and if the look you need isn't there, you add a row rather than reaching around the system.
Size: an afternoon, plus writing the rule, which takes about five minutes and matters more than the rest of the task.
Depends on: task 6.

**8. Give weapons and ammo their own list of named looks.**
Plain-language: right now, if a gun needs a muzzle flash or a similar look of its own, that look has to be authored onto every character that could ever hold that gun, which doesn't scale and drifts out of sync the moment you have more than one character. This task lets a weapon or a piece of ammo carry its own small list of looks, the same way a character does, so a gun's flash belongs to the gun. This is also where task 4's "game code picks the name" work and task 6's default-plus-override capability get put to real use for the first time — for example, a gun's muzzle look defaulting to an ordinary flash, with a named alternate that plays instead when a specific powerup is active.
Size: cheap, once task 6 exists.
Depends on: tasks 4 and 6.

---

### Explicitly not scheduled, so nothing here is invented scope

- **A built-in default-answerer for the hurt/death questions.** Round three proposed it as an optional bridge; you've declined it (see item 2 above). Not a task. If you want it later, it should come back as its own explicit decision, not be folded quietly into another task.
- **Ammo counts and reloading**, **absorbing the cue list into this model**, and **stamping a damage kind through combat if the parcel route is taken instead** — all already flagged as out of scope in round three, still out of scope here.
- **Whether override names (task 8's "FlamethrowerPowerup"-style keys) should get the same generated, regenerate-on-save, code-checked list treatment that state names get in task 3.** Not decided. Worth deciding before task 8, but not assumed either way here.
- **How many of the five other consuming projects already have their own bespoke fire-input or weapon-selection code that this new capability would need to coexist with.** Round three already flagged this as the one risk that couldn't be closed from inside this project, and it still can't be — it should be checked before task 4 or task 8 ships to those projects.
- **Whether hit detection should become a shelf of swappable modules, stay as one shared central piece with an extension point, or something else.** This is new, raised in section 1 above — checked against the project and found to be a genuinely open question, not something this document settles. It should get its own explicit decision before any task is written for it, rather than being assumed either way.

---

## Decisions still yours

1. **The open hit-detection question above.** Movement and aiming are confirmed already-modular; hit detection is a different shape today and needs its own decision about whether that's right or needs changing.
2. **Everything round three already listed as yours** — whether a state name gets a generated code-side list (recommended: yes), the parcel in both halves (recommended: yes, required not optional), whether two characters can share a row's content (recommended: yes, soon after step 6), and the abandoned quarantined event-system folder (recommended: delete it, keep its notes) — none of these were revisited here and all still need a real answer.
3. **Whether override names get the same generated/checked-list treatment as state names**, flagged above as undecided.
