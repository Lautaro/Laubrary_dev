# Laubrary — the circumstance model, a design

**What this is:** round two of the modularity work. Round one diagnosed; this one designs. **Date:** 2026-08-25.

**How to read it:** written for someone looking at the editor, not the code. No programming terms. Where it says "a card", "a dropdown", "a + button", that is literally what you would see on screen once this is built. **One honest exception:** the section about the abandoned half-built system is about something that exists only on disk and is invisible from the editor. You will have to take that section on trust, or have someone show you the folder. I flag it because a cost estimate rests on it.

**Status:** a proposal. Nothing has been built. There are seven decisions at the end that are yours, not mine — the design deliberately stops short of them.

---

## TL;DR

**Your instinct was right, and it is a bigger idea than you pitched it as.**

You suggested that instead of hardcoded Hit, Death and Fire sections, an event should declare which *circumstance* triggers it, and that a circumstance should carry its own data. That is the correct design, and this document works it out.

**The one sentence version:** a character stops having *slots* and gets *one list of cards*, each saying "when THIS happens, and only if THAT is true, do THESE things" — where "this happens" is a pluggable kind that brings its own facts along, and those facts are what both the "only if" and the "do these things" read.

**The three things that make it work, and none are obvious:**

1. **The specialness has to travel with the card, not with the section it sits in.** Today "death" is special because of *where it lives*. That is why a second death, if you could author one, would play its animation and then not actually kill the character — no body disposal, no "can't act any more", and anything waiting on a respawn waiting forever. **This is written down in this project as an observed consequence, not a theory**, and everything else here is downstream of it.
2. **A circumstance must be able to bring data Laubrary has never heard of, and existing effects must still work with it.** This is the genuinely hard part and where my two design passes disagreed most.
3. **You need to be able to ask the editor "what would happen if…" and see it highlight the answer.** Your worry about selection logic is legitimate. The fix is not to avoid selection — it is to make it *visible*.

**And one piece of luck:** a good deal of this was designed and partly written in this project some months ago, then abandoned when it broke, and quarantined rather than deleted. It is not as much of a head start as it first looks — I am reversing two of its decisions — but the thinking is there and the reason it broke is understood.

---

## What changed since round one

You gave six clarifications. Five of them **remove** work. Worth stating plainly, because the report you read was scoped much wider than the project actually needs.

| You said | What it takes off the table |
|---|---|
| Zounds is the only sound engine | Nothing needs to be pluggable about audio. One issue remains but it is a different one: the weapon's fire sound skips the effect list, so you cannot see it, condition it, layer it or vary it. That is an **authoring** problem, not an engine-choice problem, and it stays. |
| Laumination is the only animation renderer, and needn't be as versatile | Drops "bring your own animation player" entirely. Also lowers the priority of the character window's rig section rendering blank for other kinds of character — still worth a message rather than silence, but no longer structural. |
| Meta layer kinds stay closed, extended natively | Drops that item entirely. |
| Pyre legacy shapes and modifiers are being pruned | Drops that item. |
| ZUI is closed to consuming projects | Drops "let a game contribute a shared control". **But it raises the priority of the missing controls**, because they are now the only way anything gets built. See the correction below — most of them turned out to already exist. |
| Events should be circumstance-driven | This is the rest of the document. |

**What remains genuinely open, by your own decision:** characters, agents and combat. That is the right place to spend, and it is where all of this goes.

---

## Four corrections to round one

I re-verified the report. It was wrong in four places, and I would rather you hear it from me.

**1. The "locked decision" is narrower than reported — but not as narrow as I first wrote.** Round one said the description of "what just happened" had been deliberately locked to a fixed set, and that this contradicts pluggable conditions. What was actually rejected, in writing, was a **loose untyped bag of named values** — which you would not want either. What *exists*, though, is a single fixed description shared by hit, death and every custom event alike; a custom event simply gets zeroes. So **nothing declares anything today, and per-kind fact declaration is new construction, not a small reversal.** The good news is only that the thing that was rejected is not the thing you are asking for.

**2. What travels with a hit is one notch narrower than reported, not wider.** Round one listed six things the description carries. Three of them — who attacked, which side they were on, and whether it was critical — are gathered at the moment of the hit and then **thrown away** before anything can read them. So they are not under-used; they are unavailable. The upside: more about the *victim* is reachable than the report credited — its health, its live sprite, its animation state.

**3. Four of the five "missing UI controls" already exist.** The report repeated a stale list. Text fields, asset pickers, colour fields and pick-one dropdowns are all present and in use. **Only one gap is real: there is no red anywhere in the palette.** Nothing can be shown as dangerous, invalid or shadowed. For a design whose safety story is warning badges, that one gap matters more than the four imaginary ones did.

**4. The abilities recommendation was wrong and I withdraw it.** Round one told you to wire up the existing abilities list first, on the grounds it might be most of the answer. It is an empty shell: nothing in the project implements it, nothing triggers it, both real characters have it empty, and **the editor label already says "Unused today."** There is nothing there to wire. **Recommendation reversed: delete it.** If abilities return, an ability is just another circumstance — a better shape than a parallel mechanism competing with the weapon list that actually works.

---

## The thing I found: this was already half-built

There is a quarantined folder holding an abandoned version of much of what you described: one flat event list instead of fixed slots, pluggable trigger kinds carrying their own data, and the specialness moved onto the event. It was written, never connected to anything, broke, and was moved aside rather than deleted. **You were asked what to do with it. The question was never answered and the task holding it was cancelled**, so it has sat there since.

**Its notes contain the best argument in this document.** The reason a second death could not simply be added to the existing custom-events list is that death behaviour lived on the slot, not on the event — so a second death would look completely correct and then fail to kill. That is recorded as an observed consequence of the shape.

**Why it broke:** it reached for five capabilities that did not exist. Two are on the character — begin dying this way for this long, and be stunned for this long. **Two more are on the animation side** — "which animation is playing right now" and "the animation just reached a painted point". Those two are not incidental: they are exactly what the "reached a painted point" circumstance needs, so they are a real cost, not a footnote. The fifth is the character's event list itself.

**Two places where I am reversing its decisions, and you should know.** Its notes argue that stun, invulnerability and "this kills" must be plain fields rather than pluggable, because consequences have to *combine* and pluggable kinds cannot. **That argument is wrong, and the fix is visible in your own project: the effect list is already a list, and lists combine perfectly.** Its second decision was to hang the filters off the trigger itself; I am splitting them apart, for reasons in the model below. So it is a genuine head start on the *thinking* and on the runtime shape, but a smaller head start on the *code* than "revive it" implies.

---

## The model

Four ideas, deliberately. Every extra concept is one more thing to re-explain to yourself in six months.

### 1. A circumstance — *what happened*

A kind of occasion: "was damaged", "was killed", "fired a weapon", "reached a painted point in an animation", "was spawned", "hit something". Your game can write its own and it appears in the menu automatically, with nothing in Laubrary edited — the same way your own explosion shapes already appear in Pyre's shape picker.

A circumstance **declares the facts it brings**. "Was damaged" brings how much, from where, from which direction, by whom, on which side they were, whether it was critical, and what kind of damage it was. "Reached a painted point" brings the point. Your own "finished a combo" brings the combo count and the style rank. **This is your idea and it is the load-bearing one.**

A circumstance may carry a little *addressing* — which painted point, which signal name — because that is part of naming the occasion. It should not carry judgements like "was it critical". Those belong to the next idea, and the reason matters: **if judgements live on the circumstance, then every new thing you might want to judge means editing the circumstance — which is the bloat engine again, one level down.** This is the point where I part company with the shelved version.

### 2. A filter — *does this instance concern me?*

An optional line on a card: "only if the damage type is Fire", "only if the amount is at least 20", "only if it was critical". Filters read the facts the circumstance declared. Several filters on one card must all pass — **and there is deliberately no "or", no nesting and no brackets.** A filter list you cannot build a puzzle out of is not hidden logic; it is a sentence.

**"Or" is expressed as two cards**, and that is precisely why order is the rule. Say it out loud in the editor, because it is the one idiom someone has to learn.

Each fact kind needs its full set of comparisons including the negative ones — is / is not, at least / below / between, is / is not set. "Not fire" is a real authoring need and it must not require inverting the whole card.

Because filters read *facts by name* rather than from a fixed menu of things they can ask about, a game that invents a circumstance carrying a "style rank" gets that rank filterable immediately, with no new filter kind and nothing in Laubrary changed. **That property is what turns growth into growth instead of bloat**, and it is the specific thing round one found missing everywhere.

### 3. Consequences — *what to do about it*

You already have this and it is the best-built part of the domain: a stackable list of effect cards with an "add" menu that grows on its own. It does not need redesigning. It needs three additions:

- **Each entry can carry its own filter.** Today an entry can only be muted by hand. "The same death, but the gore only on a critical" currently costs a whole duplicated card; it should cost one line.
- **Where an effect appears stops being a fixed list of four and becomes a pick from the facts actually available** — including every point you have painted on the animation, which today are reachable only as a special case.
- **The things that skip the list get pulled into it**: the weapon's fire sound, the weapon's muzzle flash, and the bullet's impact. All three happen off to one side today where you cannot see, condition or duplicate them.

### 4. The card itself — and where "specialness" lives

One card binds it all: a name, a circumstance, optional filters, what animation plays and for how long, the effect list, and — the important bit — **whether this event ends the character, and if so how the body is disposed of and how long it lingers.**

Those three stop being properties of the *character* and become properties of *this particular way of dying*. That is what makes a burn-death that leaves a smear and a crush-death that vanishes both expressible. Today all three are decided once, when the character is created, and can never differ.

**Everything else that changes the character — stun, invulnerability, knockback — should be an ordinary effect card.** Two of those three **already exist as effect cards**; only stun does not. And a sound card exists too, which means pulling the fire sound onto the list is plumbing rather than building. The general reason: if state changes are special fields, every future one (rooted, silenced, set alight, phase-changed) is a new field on a class every project inherits — the exact bloat you are escaping. As effect cards, your game ships its own and Laubrary does not change.

**"Ends the character" is the one exception**, because the system must know the answer *before* it plays anything, so it can switch the colliders off and stop the character acting while the death animation still runs.

**And it needs a hard rule, because otherwise this design reintroduces the bug it exists to kill.** If a killing blow matches no card at all, the character must still die, by a sensible default. Death is a fact, not a presentation choice. A death card that forgets to tick "ends the character" should end it anyway when it finishes and show a warning on the card. Making a non-lethal death *impossible to author* is better than making it *expressible*, because the failure is silent and the project's own history says it bites.

**A live example of the whole thesis, already on disk:** a stun value already exists on every reaction, including custom events — and it is only ever read off the hit slot, once, when the character is created. So a stun authored on a custom event is saved to disk and never read by anything. That is specialness-living-on-the-slot as a one-line demonstrable bug, today.

---

## The hard part: how a circumstance brings its data

My two design passes disagreed here, so I will show the disagreement rather than hide it.

**The cheap answer:** stop throwing away the attacker, the faction and the critical flag, carry the whole hit record through, and let effects read whatever they like off it. Nearly free, fixes the immediate gap.

**Why I did not take it.** It works perfectly for damage and not at all for anything else. Your own invented circumstance — "finished a combo", "overheated", "entered water" — would have nowhere to put its data except an untyped escape hatch only your own effects could read. So a Laubrary effect could never place itself using your data, and a filter could never ask about it. **That fails the actual vision**: your solutions grow what Laubrary offers only if Laubrary's existing pieces can consume what your solutions provide.

**So the design is named facts**, and the discipline that stops it becoming the loose untyped bag that was rightly rejected before is this split:

- **The *kinds* of fact are a small closed set** — a point, a direction, a number, a yes/no, a reference to an asset, a reference to an object. Six or so. Adding a seventh is a genuine language change and should be rare. This is what keeps everything typed.
- **The *names* of facts are open.** Anyone adds one. That is Tuesday.

The payoff on screen: a filter's value control **draws itself from what the fact actually is** — a damage-type fact gives you an asset picker, a number fact gives you a number and a greater-than/less-than choice, a yes/no fact gives you a checkbox. **You cannot author a mismatch, because the editor cannot offer you one.**

And the compatibility story, which is the part I am most pleased with: an effect that wants a position does not name a specific fact by default — it asks for **"the main position of whatever happened"**. A hit answers with the hit point; a cue answers with the painted point; your combo finisher answers with wherever you said it landed. So an effect authored today, dropped onto a circumstance invented in three years, still places itself sensibly with no re-authoring. **That single default is why this scales.**

---

## Choosing between cards, and your worry about it

You said: *"it doesn't make sense to have hardcoded events if we have to make lists for them and have logic that decides which one it will use."*

**You are half right, and it is the important half.** Selection is not the problem. *Invisible* selection is. The proof that ordered lists are fine when the rule is visible is in your own project: movement poses are exactly a first-match-wins list, with three conditions and a tooltip that states the rule — and nobody finds them confusing.

So the design keeps an ordered list and spends the effort on making the rule impossible to miss:

- **Storage is one flat list. Presentation is sections.** The editor groups cards by circumstance: "When killed (3)", "When damaged (2)", "When cued (1)". **You get the sectioned view you asked for at no structural cost, because the sections are a view rather than a schema.** This matters: the moment sections are real, code starts reaching into them by name, and that is exactly how the current death bug happened.
- **Order is priority within a section.** Fire-death above generic-death wins. The last card with no filter is the catch-all.
- **Each section header states the rule in words**, and says so when it is not satisfied — *"No unfiltered card: a crush death will play nothing and, unless the default kicks in, will not die."*
- **And the thing that settles the argument: a "what would happen if…" probe at the top of the section.** Pick a circumstance, type a damage type and an amount, tick critical, and the editor highlights the card that would fire. **Selection stops being logic you trust and becomes an answer you look at.** This is a deliverable, not a nicety; if it is cut, your objection stands unanswered.

**Two honest limits on that probe**, both created by other parts of this design: once weapons carry their own cards, the probe must let you say "with this weapon equipped", and the merge order has to be stated rather than discovered. And a power-up's runtime cards exist only while the game is running, so the probe cannot show them at all. Say both on screen rather than letting someone find out.

**One more rule that has to be decided rather than discovered:** a killing blow currently raises *both* "was damaged" and "was killed" from the same moment. Today that is invisible because there is exactly one of each. Under this model a lethal fire hit matches a "damaged by fire" card *and* a "killed by fire" card, and you get a flinch animation starting on the same frame as the death. **Does a killing blow suppress the damaged circumstance?** I recommend yes.

**And an interruption rule.** A second hit while a reaction is still playing currently cancels and restarts it, silently dropping any later-frame effect that had not fired yet. With several cards per circumstance this stops being an edge case and becomes an everyday one. It needs a stated answer, per card: restart, ignore-while-playing, or layer.

**On "three deaths, pick one at random"** — this needs one more concept than pure first-match-wins, and I have deliberately not chosen its shape. See decision 2.

---

## The four scenarios, authored by hand

**Burn death versus crush death.** Make two damage-type assets, Fire and Crush. Add a card, circumstance "when killed", one filter — "damage type is Fire" — pick the burn animation, tick "ends the character", pick how the body goes (the four choices are vanish now, when the clip ends, after a delay, or leave it — *a smear on the floor is an effect card, not a disposal setting*). Add a second card, "when killed", no filter, the normal collapse. Drag the fire one above. Two cards, one drag, one dropdown each, no code.

**But there is a prerequisite and it is not a footnote.** Nothing in the project currently says what *kind* of damage anything deals; the damage-type asset ships and is used by nothing, and no asset of it has ever been made. Until weapons, projectiles and melee stamp a type onto the blows they deal, this scenario cannot work at all and the whole apparatus filters on a fact nobody supplies. **Plumbing damage type from the dealer to the blow is a prerequisite.** It is small, but it touches combat, not just characters.

**A power-up changes the muzzle flash without changing the gun.** Two halves. The half that already works: the flash lives on the weapon, so two guns already look different for free, and *where* it appears on the body is the character's business, so any character can hold any gun. The half that needs building: a character asset is shared by every instance, so a power-up cannot edit it. The answer is a small runtime overlay — the power-up hands the character an extra card while active and takes it back when it expires, and overlay cards are considered first.

**This one has a dependency you should know about:** on ProtoGuy the flash is *already* authored as a character card — a custom "Fire" event that the weapon raises on every shot — rather than as the weapon's own flash slot. **A power-up can overlay a character card and cannot overlay a weapon's flash slot.** So decision 4 below is not a "confirm that was deliberate" question; it decides whether the overlay story works at all.

**Three death animations picked at random.** If they differ only in the animation, this is one card holding three animation names with one picked per death — smaller than the alternative, but still a new idea, not free. If they differ in their *effects* too, they must be three cards and the system needs to know they are alternatives rather than a priority order. That is decision 2.

**Reacting to something your game invented.** Two routes, and both should exist. The simple one **works today and needs nothing**: the card declares a name, and your game raises it by that name — and the name is picked from a list rather than typed, which is the project's standing rule. The declarative one: your game writes its own circumstance, it appears in the "add card" menu under its own section, and its facts appear in every filter and every position picker on that card. **Nothing in Laubrary is edited.**

The honest boundary: Laubrary cannot *evaluate* a circumstance it has never heard of, so your game decides when it happened. That is the right division of labour; anything else would be checking every character's every card every frame.

---

## What this absorbs, and what it retires

| Thing | What happens to it | Why |
|---|---|---|
| The Hit and Death slots | Become ordinary cards | They are the source of the silent-death bug. **Cheaper than it sounds:** of the six character assets in the project, only two have any hit content at all, **none has any death content**, and there are seven authored effect entries in total. The death slot in particular is nearly free to retire right now. |
| Custom named events | Absorbed — they *are* the model now | They were always the same thing with a name instead of a trigger. They keep working and gain filters and variants for free. |
| The animation cue list | Absorbed into a "reached a painted point" circumstance | A cue is a trigger carrying a weaker copy of an event's payload — which is why it grew a "raise an event instead" field to escape its own limits. **No character in the project has a single cue authored, so this absorption is free right now and expensive forever after.** |
| The weapon fire sound | Becomes an effect card | Invisible, unconditional and unvaryable today — and the card kind it needs already exists. |
| The weapon muzzle flash and the bullet impact | Become effect cards on the weapon's and ammo's own lists | Which makes impacts conditionable — sparks on armour, blood on flesh — impossible today. |
| The fixed lists of where / which-direction / how-much | Retired into fact picks | The bloat engine round one identified. One of these lists carries a written apology in its own source for having had to append two options at the end to avoid breaking saved data — **the ordering scheme has already cost this project design freedom, twice.** |
| The abilities list | Deleted | Empty shell, see correction 4. |
| The muting checkbox on an effect entry | **Kept** | Muting by hand and "only when X" are different things; you should not have to delete a condition to switch something off for an afternoon. |

**One generalisation falls out and deserves its own line.** Once the fire sound moves onto a card, you would otherwise author it on every character that can hold the gun — which is worse. The resolution: **event lists stop being a character feature and become a feature of anything that reacts.** Weapons and ammo get their own, merged with the character's when equipped. Same card, same editor. This is the difference between a character event system and an event system that characters happen to use, and only the second survives.

---

## What has to be built in the toolkit first

Only three, and one is smaller than round one implied.

1. **Red.** There is no danger or warning colour in the palette at all, and this design leans on badges — "this card can never fire", "this filter asks for a fact this circumstance doesn't have", "this death doesn't actually kill". Without a tone, every warning is grey body text and gets ignored. **Note this is now the second design in your queue asking for red** — the asset-locking work specifies the same missing tokens. Fund it once, with one set of names, or you will pay twice and end up with two.
2. **Sections in the "pick a kind" menu.** That menu currently shows one flat list. Sectioned menus already exist elsewhere in the toolkit, so this is wiring — and Pyre and the effect stacks get grouped menus for free out of it.
3. **A fact picker.** A dropdown fed by whatever facts the chosen circumstance offers, grouped into "from this event" / "from the character" / "painted points", showing labels while storing names.

**One thing I am explicitly not asking for:** dragging cards between sections. It sounds necessary and is not, provided cards sharing a circumstance are kept adjacent in storage — then reordering within a section is all you need and the existing drag machinery is untouched. There is still a little work around it: changing a card's circumstance has to quietly move it to its new group. That trade removes the largest toolkit task from the plan.

**And the risk that rides with the fact picker:** facts are named, and this project has a standing rule that a reference is never typed by hand. **If the runtime ships before the picker, authoring degrades to typing names and the model is temporarily worse than the fixed lists it replaces.** The picker is load-bearing, not polish.

---

## Build order

Each phase leaves this project working. See the warning about other projects immediately after.

**Phase 0 — pay the debt that killed the last attempt.** Build the five missing capabilities: begin-dying-this-way-for-this-long and be-stunned-for-this-long on the character; **which animation is playing and the animation-reached-a-painted-point signal on the animation side**; and the character's event list itself. Route today's behaviour through them. Add a version stamp to the character asset. **Nothing behaves differently. This is the phase whose absence shelved the last attempt** — and note the two animation-side items are what the painted-point circumstance later depends on, so they are not optional extras.

**Phase 1 — stop throwing data away, and supply what's missing.** Carry the attacker, the faction and the critical flag through instead of discarding them. Put a damage type on the blow itself and make weapons, projectiles and melee stamp it. **Nothing behaves differently, but the damage-type asset stops being a labelled empty socket and the flagship scenario becomes possible.**

**Phase 2 — the three toolkit items.** Red, sectioned kind menus, fact picker. Other tools benefit immediately.

**Phase 3 — facts.** Replace the fixed where / direction / how-much lists with fact picks and upgrade the seven authored entries. Painted animation points become first-class pickable positions for the first time.

**Phase 4 — circumstances, filters, one flat list, the sectioned view, and the probe.** The old Hit and Death slots stay as a fallback, consulted only if nothing matched. **Existing characters behave identically; new authoring works immediately.** This is the phase after which you have the thing.

**Phase 5 — retire the slots.** Move existing hit and death content into cards, then delete the slots and the values frozen at creation time. Cheap, per the counts above.

**Phase 6 — absorb the strays.** The cue list, the fire sound, the muzzle flash, the impact; event lists on weapons and ammo; delete the abilities shell.

**Phase 7 — the tail this makes cheap.** Several damageable regions per character, so a headshot becomes filterable. Not before.

### The warning about other projects

**"Each phase leaves the project working" is true of this project and not automatically true of the ones that consume Laubrary.** There are three live consumers, and the sync is a wholesale folder replacement whose own instructions warn the operator to expect breaking-change fallout of exactly this kind. **Every phase here is that kind of change.** The version stamp added in phase 0 is the thing that has to make each consumer's upgrade automatic rather than manual, and each phase needs one line saying what a consumer sees on the next sync. That is the single most under-costed thing in this plan.

---

## What could make this collapse

**If damage type never gets stamped at the source**, the headline scenario does not work and the apparatus exists to filter on a fact nobody supplies. That is why it is phase 1.

**If the card list becomes visually dense**, this is *experienced* as worse than what you have, whatever its merits. Three deaths with three effects each is a lot of nested cards in a window that is already large. Collapsed-by-default rows carrying circumstance, filters and outcome as small chips is a requirement, not a preference.

**If the probe gets cut for time**, your original objection stands unanswered and you would be right to distrust the result.

And a boundary rather than a risk: the idea that the character can contribute facts about itself — health, airborne, a power-up active — is what makes filters about *character state* possible at all; without it filters can only ask about the blow. It is a small seam. But pushed one step further it becomes a general-purpose store of game state, which is a much larger system than anyone signed up for. **The line: it exposes things that already exist; it never computes or stores gameplay state of its own.**

---

## Four things round one recommended that this design does not cover

So they are not quietly lost:

- **Hooks on the character-building recipe.** Round one called this the third structural weakness and "the real reason movement and firing feel hardcoded". This design touches that same recipe repeatedly and does not fix it. Still open, still worth doing.
- **Opening up Chunks debris behaviours.** Untouched here, still the cheapest big win in round one.
- **Firing hit and death effects on named animation moments rather than frame numbers.** Partly absorbed — painted points become first-class in phase 3 — but the silent-breakage trap when an animation is re-timed is not specifically addressed.
- **Auditing every + button that only uses its first entry.** Still live: a weapon's ammo list still uses only the first entry and its own tooltip admits it. **And this design adds several new lists**, which makes the audit more urgent, not less.

---

## Seven decisions that are yours

Both of my design passes independently flagged the same ones, which usually means they are real.

1. **When several cards match, does only the first fire, or all of them?** I recommend the first, matching the movement-pose rule you already have — with an explicit "also run this one" opt-in for things that should layer, like a hit sound riding on whichever hit reaction won. Note that the layering opt-in is what makes "or is two cards" stop working, so the two decisions interact.
2. **Do your three random deaths differ only in the animation, or in their effects too?** Animation-only is one card holding three names. Effects too means three cards plus a way to say "these are alternatives, not priorities". **This decides whether a whole concept gets added, and I will not guess it.**
3. **Where does a damage type live — on the weapon, the projectile, or the ammo?** Decides who stamps it and whether combat may be edited to carry it.
4. **Is the muzzle flash a character card or the weapon's own slot?** ProtoGuy already does it as a character card. **This is load-bearing, not a formality: a power-up can overlay a character card and cannot overlay a weapon's slot.**
5. **May a power-up *replace* a character's card, or only add one?** Decides the overlay rules and whether a reusable card-set asset is worth building.
6. **Absorb the cue list into a circumstance, or keep it separate?** Nothing has cues authored, so absorbing is free today and expensive after the first one is authored. I recommend absorbing.
7. **The shelved system — revive, or start fresh?** I recommend revive-for-its-thinking, rewrite-its-two-wrong-decisions, and this design assumes that. You were asked months ago and the question expired unanswered; it needs a real answer this time.

---

## A closing note

Round one ended by saying the modularity policy is followed where someone remembered to follow it, and nothing forces it. This design is an attempt to make one part of that structural rather than cultural — because once "when does this happen" is a pluggable card rather than a fixed list, the next feature *cannot* bloat the library even if nobody is paying attention. That is worth more than any single item on the list.

The two habits from round one still stand, and this design is both of them applied at once: **if there can ever be two of these, it is a list from day one**, and **if it answers *when*, it must be pluggable, never a fixed set of choices.**
