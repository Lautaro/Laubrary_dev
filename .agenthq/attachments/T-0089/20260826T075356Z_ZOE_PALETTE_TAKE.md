# Laubrary — the palette model: my take

**What this is:** round three of the modularity work. Round one diagnosed. Round two designed a system where the character asset decides which reaction fits. **You then pushed back and proposed something different, and this is my answer to it.** **Date:** 2026-08-26.

**How to read it:** written for someone looking at the editor, not the code. **No class, method or file names appear anywhere in this document.** But your proposal is fundamentally about what a programmer types, so *code* is discussed throughout — that is unavoidable and I am not going to pretend otherwise. Where it says "a row", "a chip", "a dropdown", that is literally what you would see on screen.

**One thing you'll have to take on trust:** two short passages are about things with no presence in the editor at all — an abandoned folder, and some stale text inside saved files. Both are flagged where they appear.

**Status:** still talking, as you said. Nothing has been built. But this is no longer an open question in my mind — I have a verdict, and it changes what round two said.

---

## TL;DR

**You are right, and you are right for a better reason than the one you gave.**

You proposed it as a *retreat* — "maybe Laubrary shouldn't take everything, maybe this gets too complex." It isn't a retreat. It is a **cleaner split of who knows what**, and it makes Laubrary's promise *more* keepable, not less.

**The one sentence:** the character asset says **what this character can show**; your game says **which one to show**; and Laubrary *always* carries out **what actually happens to the character** — which is never something your game decides.

**Four things I did not expect to be able to tell you:**

1. **Most of what you proposed already exists and already works.** Asking a character to play a state by name is a finished, working, public feature today. Laubrary itself already uses it — every time a weapon fires, it asks the character to play a state called "Fire", and shrugs harmlessly if that character never declared one. **You have been describing a mechanism that is already running in your project.** What is missing is not the mechanism. It is the handful of things that would make anyone *prefer* it.

2. **Your proposal deletes round two's most dangerous idea.** Round two put "and this one kills" on a card, plus a safety net for when someone forgot to tick it — because forgetting meant a character that plays its death animation and then doesn't die. Under your model that box does not exist and cannot be forgotten. **Which state plays never decides whether something dies.**

3. **It costs dramatically less.** Round two was eight phases plus three toolkit builds before anything visible shipped, and its riskiest phase restructured saved data in five other projects at once. Yours reaches the payoff far sooner, and the phase where the model actually arrives is purely additive to what's saved on disk.

4. **It retires a whole change to the combat system.** Round two required damage types to be stamped onto every blow before the flagship scenario could work at all. Your model doesn't need it — your game already knows the gun is a lazer, so it just says so.

**And one thing that is not good news, which I put here rather than burying it:** the two rounds have now spent a lot of effort arguing about *which* visual plays, and the only unmet need actually demonstrated anywhere in your project is a different question — *with what values*. See "The thing neither design solves".

---

## What I am reversing from round two, out loud

Round two ended on two habits. One I am now reversing, and you should hear it from me rather than notice it.

> *"If it answers **when**, it must be pluggable, never a fixed set of choices."*

Wrong shape for this domain. The new version:

> **If it answers *when*, and the answer depends on something your game invented, Laubrary should not answer it at all — it should ask.**

The other habit — *"if there can ever be two of these, it is a list from day one"* — stands, and your palette is exactly that habit applied.

Round two is not wholly wrong; three things in it survive and I say which below. But its central mechanism — the character asset holding conditions and choosing between them — I am withdrawing.

---

## The one move that makes this work

Three questions keep getting mashed together by the word "modularity". Separate them and everything falls out:

| The question | Who owns it | Why it can't be anyone else |
|---|---|---|
| **What can this character show?** | The character asset — you, in the editor | It's content. It's already a list. It already saves correctly in every project. |
| **Which one right now?** | **Your game, always** | The judgement uses ideas Laubrary must never learn — what a lazer is, what a combo is, what "overheated" means. |
| **What happens to it mechanically?** | **Laubrary always carries it out.** A state may *tune* two details of it — how long the character is stunned, and what becomes of the body — but never *whether* any of it happens. | Switching off collision, stopping it acting, disposing of the body, telling whatever is waiting to respawn it. These are consequences, not choices. |

**Round two put the second and third in the same box.** That is the error, and it is the whole error. Once a card can both *be chosen* and *decide the character's fate*, choosing wrongly kills the character silently — which is the exact failure this project already has written down as something that was observed, not theorised.

**Your model keeps them apart, and that is why it doesn't need the safety net.**

*(That middle column is deliberately worded to allow two exceptions rather than none. Round two's version allowed no exceptions and then broke its own rule; I'd rather state the exceptions than pretend. The rule that decides which exceptions are allowed is in "What a declared state carries".)*

---

## Which state plays never decides whether something dies

This is the load-bearing rule and it deserves its own heading.

**A character's declared states are a vocabulary of things it can show. "Dies" is not a word in it.** Health reaching zero is what kills, and no state name — right, wrong or misspelled — selects that branch. Your game only ever gets to answer *"and what does it look like while that happens?"*

Everything good follows:

- **A misspelled state name cannot disable dying.** Under round two a mistake here was catastrophic and silent. Here it gives you a character that dies playing the *normal* animation — wrong-looking, obvious, harmless.
- **The safety net, the default rule and the warning badge round two needed all disappear.**
- **The "does a killing blow suppress the hurt reaction?" policy question disappears**, because it stops being a policy and becomes a mechanism: the death claim simply outranks the hurt claim.

**Two honest limits on that, because "nothing you author can affect dying" would be too strong a claim and you'd find the counterexample yourself.** The effect list on any state can already contain an *invulnerability* effect, and invulnerability makes a blow do nothing — so a long invulnerability on a state your game plays a lot really can stop a character dying. There is also an effect that plays an animation directly, which can talk over a death animation from underneath. **Neither is new and neither is caused by this model** — both are in the effects picker today — but the precise claim is *"no state name selects the death branch"*, not *"nothing you author can influence death"*.

**And a correction to round two you should have.** Round two argued the system *must* know a blow is lethal before it plays anything, so it can switch collision off in time. Not true today. **The death animation and the collision switch-off happen in the same instant, in a fixed order — and that order is written down for an entirely unrelated reason**, so nothing actually guards it. There is no window of vulnerability; there is just no rule protecting the arrangement either.

---

## Your proposal actually contains two lists, and separating them is most of the design

You described one thing. It's two, and they behave completely differently.

### List A — states your game can ask for

Open. Per character. Grows by you adding a row. Your game plays any of them by name. **This ships today.** Your game invents "Overheated", you add a row called Overheated, your game asks for it. **Nothing inside Laubrary is edited, ever.** That is the anti-bloat guarantee, and unlike round two's version it needs no new machinery at all.

### List B — moments where Laubrary stops and asks *you*

**Closed. Short. And its shortness is the point** — it is the only surface your game has to learn. Today it is two: **when hurt** and **when killed**. Obvious near-term additions: **when it fires**, **when it spawns**, **when a death animation finishes**. That's the whole list.

Each moment has two faces, and only one needs building:

- **The announcement** — "this character was hurt, here's everything about the blow." **This already exists and is already complete.** How much, from where, from whom, which side they're on — all of it already reaches anyone listening, intact. *(Round two said this data was thrown away. It's thrown away only on the presentation path, which is a much smaller problem than reported.)*
- **The question** — "which state should I show for this?" This is the new bit. It is **one answer slot, not an announcement to everybody**: there is exactly one right answer to "which death does this character show", so if two parts of your game both try to answer, that's a genuine conflict and it should say so out loud rather than one of them silently winning.

**Why this distinction matters practically:** for a death, your game never *asks* for a state — it **answers a question**. Laubrary still runs the entire death sequence exactly as today; it just plays a different animation partway through.

### The trap in this, which you must know about

**If your game instead does the obvious thing — waits for the "it died" announcement and then asks for "Death by fire" by name — you get a broken character.** Verified: both the default death and your chosen one start, the "I've finished dying" signal is lost, and the body is **never disposed of**. You get a permanent corpse with its collision off, and anything waiting to respawn it waits forever.

This is not a flaw invented by your proposal — the ingredients are in the project today and I'd have found the same trap under round two. But your model makes it the *natural thing to try*, which makes it your problem. Two consequences, and I've built both into the plan:

1. **Fix the underlying fault before advertising the feature.** It is the interruption problem described below, and I've moved it earlier in the build order because of this.
2. **Laubrary should refuse the request and say why.** It knows the character is mid-death; asking for a state at that moment should log a clear message pointing you at the answer slot, not quietly break.

---

## What a declared state carries

**One requirement is non-negotiable and not obvious: a state name must resolve to a whole card, never to just an animation name.**

If a state is only "play this animation", you silently lose what makes reactions work — how long the moment lasts, the flash on the body, and every effect timed to fire partway through. There is already a version of this in the toolkit that takes exactly that loss, so it isn't hypothetical. **Ask for a state, get the whole card.**

So a declared state carries what a reaction card already carries — an animation, how long the moment lasts, an optional flash on the body, and the effect list, which needs no redesign. Plus three small things:

- **A role chip** — *nothing special* / *this is a hurt look* / *this is a death look*. This is how "Death by fire" and "Death by crushing" become eligible to be chosen as deaths. It defaults to "nothing special", so nothing you already have changes meaning.
- **Stun.** This field already exists on every reaction, you can already type into it, and **it is read from exactly one place and ignored everywhere else** — a stun you set on a custom event today is saved to disk and never used by anything. Making it read from whichever state actually played fixes a real, live bug.
- **On death states only: what becomes of the body and how long it lingers.** Currently decided once when the character is created and never varies. This is what makes "burn away to nothing" and "leave a smear" both expressible.

**The rule that decides what is *allowed* on a card, which I want written down so nobody drifts back:**

> **Anything whose absence is silent stays off the card. Anything whose absence just falls back to a default may live on it.**

Forget the disposal setting and you get the character's normal disposal — visible, fixable, never wrong about whether it died. Forget round two's "this kills" tick and you get a corpse that walks. A difference in kind, not degree.

**Honest limit on that rule:** it covers a *forgotten* setting, not a *wrong* one. Setting a death state to "leave the body" gives you a body that stays — which is exactly what you asked for, and also what a mistake looks like. That case wants the "nothing dangerous here" checks in the last phase.

### Three things the role chip has to answer, which I am not going to leave vague

1. **If your game answers the death question with a state that isn't chipped as a death look, it plays anyway** — your game is in charge — but it says so in the log, because it almost certainly means someone picked the wrong row.
2. **A death-chipped row that nothing ever chooses is dead content that looks wired.** The "nobody uses this" chip must distinguish *"nothing asks for it"* from *"eligible, but never actually chosen"*.
3. **If both the old death setting and a death-chipped row are filled in, the answer your game gives wins; with no answer, the old setting plays.** That is also what makes the whole change safe to ship — see the build order.

---

## Your own example, checked honestly

You wrote: *a Shoot event comes with direction, ammo left, projectile type, ammo type; native code sees the ammo type is Lazer and fires "Shooting Lazer".* I checked every piece.

| What you assumed | Reality |
|---|---|
| direction | **There.** |
| the projectile | **There.** |
| the ammo | **There**, one step away. |
| the gun | **There**, right where the shot happens. |
| where the muzzle is in the world | **Already handled** — flashes do appear at the muzzle, tracked live, both from the gun's own effect and from the character's Fire state. What *is* dropped is everything else: the shot announces itself with no direction, no strength, and no position of its own, so any effect on a Fire state that wanted to place itself *at the hit point* has nothing to go on. Worth fixing, but it is not the muzzle bug I first thought it was. |
| **ammo left** | **Does not exist anywhere.** No ammo count, no magazine, no reloading in Laubrary at all — weapons only have a cooldown. A new combat feature, not plumbing, and I'm not letting it slide past as though it were. |
| **"the damage type is Lazer"** | **Not expressible in weapon data today.** There's a damage-type asset nothing reads and of which not one has ever been made; a gun's ammo list has a "+" button but only ever uses the first entry, and its own tooltip admits it. A gun that fires *either* lazer *or* bullet ammo cannot currently be described. |

**But here's the good part, and it's the single biggest saving in your proposal:** *under your model it doesn't matter.* Your game holds the gun. Your game knows it's the lazer. It just says so. **Laubrary never has to learn what a lazer is** — and that retires round two's requirement to stamp damage types through weapons, projectiles and melee before anything could work.

*(The ammo-list-only-uses-the-first-entry problem is separate and still real — if you ever want one gun to switch ammo types, that has to be fixed regardless of any of this.)*

---

## Names, typos, and the argument already written against you

There's a note sitting in this project, written months ago, arguing against exactly what you propose. It says two things:

> *"Folding hurt and death into a name-keyed list would mean special-casing two entries anyway, and would let a typo silently disable dying."*

**The first half is true and I'm not pretending otherwise.** The role chip *is* that special case. My claim is only that a visible chip on a row is more honest than today's version, where hurt and death are special because of *where they live* and can't be asked for by name at all.

**The second half is answered by removing the coupling it fears.** A typo can't disable dying, because dying isn't name-driven. Worst case: the character dies looking wrong. That note should be rewritten when this is built.

**The four things that make names safe** — three of which this project already does elsewhere, and one sibling tool already does the whole pattern including the honesty check:

1. **A name is typed once, where it's declared.** Already the law here.
2. **Every place that refers to it picks from a list.** The picker already exists and already handles a rename by keeping the old value visible with a warning rather than silently snapping to something else.
3. **Code gets a picker too — a generated one.** Not to stop typos (typing carefully stops typos) but to stop **renames**: rename a state in the editor, regenerate, and every place in your game that used the old name **refuses to build**. That's the loudest possible signal and a hand-written list can't give it. You suggested hand-written; this is the one place I'm arguing with you.
4. **Asking for a state that doesn't exist has to say so.** Three places in Laubrary ask for states by name. **One of them already prints a proper warning naming what you asked for; the other two ignore the answer entirely.** So there's already a good example in your own codebase — the fix is to copy it, keeping Laubrary's deliberate "shrug if absent" cases quiet.

**A related tidy-up, correctly scoped:** there are three different kinds of name in this area — state names, painted-point names, animation-signal names — and all three are matched by different capitalisation rules. Nothing is *contradicting* itself, but three conventions is two too many. Pick one, and keep state names strict, because strictness is what makes a near-miss fail loudly instead of playing the wrong thing.

**And the honesty check, which is the bit I like most.** A sibling tool in your own library already ships a feature whose stated purpose is *"so a tool can honestly report that nothing in this scene reacts to these events yet."* Same idea here: **each row tells you whether anything in your project actually asks for it.** Declared and unused shows an amber chip. That is the answer to "the editor goes blind", and it's a feature with a working precedent rather than an argument.

---

## What this retires from round two

| Round two wanted | Status | Why |
|---|---|---|
| **Conditions on cards** — "only if fire", "only if at least 20", is/is-not/at-least/between, "or means two cards" | **Gone** | No selection left to condition. Your game already made the decision, in its own code, with its own types. A condition re-deriving it inside an asset is the same logic in a place you can't debug. |
| **The named-facts system** — a closed set of fact types, open fact names, a fact picker everywhere | **Gone** | Nothing looks facts up by name any more. Round two called the fact picker load-bearing and warned that shipping without it would make authoring *worse* than what exists. That whole risk goes with it. |
| **Ordering and priority between cards** | **Gone** | Nothing chooses, so nothing to order. **The single largest simplification** — no rule to learn, no "or is two cards" idiom, no drag-to-decide-who-wins. |
| **The "what would happen if…" probe** | **Gone** | Round two called this non-negotiable. It isn't, because the question it answers no longer exists — "what plays when this dies?" now has one answer, visible on the row. |
| **Stamping damage types through combat** | **Gone** | See the table above. Saves a change that touched combat, not just characters. |
| **Power-up overlays as a mechanism** | **Gone — but read the next paragraph** | A power-up doesn't overlay anything; it makes your game ask for a different name while it's active. Round two spent two of its seven decisions on machinery this model doesn't need. |
| **"This one kills" as a tickbox, plus its safety net** | **Gone** | See above. |
| **Restructuring hurt and death into one saved list** | **Gone — and this is where the real risk was** | Hurt and death stay exactly as they're saved today; they're just *drawn* as the top two rows of the same list. You get the uniform surface you asked for, at near-zero risk to five other projects. |
| **"Red" in the toolkit as a blocker** | **Downgraded** | Still worth doing once — the asset-locking work wants the same thing — but off the critical path. |

**One correction to my own table, because you'd catch it.** *Your specific muzzle-flash scenario is not delivered by the palette alone.* The flash is raised by Laubrary itself, using a fixed name the gun hardcodes — so your game has no name to substitute. Making that work needs weapons to get their own state lists, which is a later phase below. **The overlay machinery is genuinely retired; the scenario is deferred, not solved.** I'd rather say so than score it as a win.

**What survives from round two, each earning it with a live defect rather than an argument:**

- **The effect list is untouched.** Its three suggested additions — pull the weapon's fire sound, the muzzle flash and the bullet impact onto visible lists — are still worth doing and depend on neither model.
- **A killing blow currently plays part of the hurt reaction and then stomps it.** Round two framed this as a policy question. It isn't — it's happening right now, with almost nothing authored, and the hurt reaction's later effects and its "I'm finished" signal are lost every time. **Fix it either way.**
- **Interruption needs a rule.** Round two wanted one per card; one rule for everything is enough. But see the build order — this is **more** work than I first thought, not less.
- **Stop narrowing what a reaction is told.** Still right, and smaller than reported.
- **Delete the abilities shell.** Unchanged, still right.
- **Round one's four leftovers are all still open**, and one of them gets *worse* under your model rather than better: effects still fire on numbered animation frames rather than on painted moments, so re-timing an animation silently breaks them. More declared states means more frame-numbered effects. Round two partly absorbed this; this design doesn't, and I'm flagging that rather than losing it.
- **Auditing every "+" button that only uses its first entry** is *more* urgent now, because it's the reason your own example can't be written.

---

## The thing neither design solves

**This is the finding I'd most like you to take away, because two rounds of design have now walked past it.**

Round two claimed — and I repeated — that your project contains demos that "chose to bypass the character system so they could pick their own death effects", and that this proved your point. **That claim is wrong and I'm withdrawing it.** Those demos say in their own notes that they're placeholders waiting to be converted, and they mark the exact spots where a real character would take over. They're unfinished, not rebellious.

**But looking at what they actually do is far more useful.** They don't want to choose between three deaths. They want to hand *values* to a visual: *this instance's* colour for the debris, an explosion size driven by a live slider on screen, and a "this one died" signal carrying which instance it was.

**No character asset can express any of that, and neither model fixes it.** A character asset is shared by every copy of that character — it can't say "tint this one blue". Round two would have you author cards; your model would have you name states; **neither gives you a way to pass a value at the moment it happens.**

**So the palette needs one more thing than you proposed:** when your game announces something, it can hand over **a parcel of its own** — a colour, a size, whatever it likes — which Laubrary carries through untouched to your own effects without ever looking inside. Laubrary keeps reading the facts it owns; your parcel is yours. This doesn't reopen the "loose bag of unlabelled values" that was rightly rejected before, because it isn't a bag of loose values — it's one thing your game defined, and Laubrary never inspects it.

**Ship that and those demos can finally become real characters.** Don't, and they'll stay placeholders no matter which model wins. It has its own phase below; it is not a nice-to-have.

---

## Where your model genuinely loses

Three, and I'd rather state them than have you find them.

**1. The character asset stops describing the character.** Round two's promise was that you open a character and read every way it can die and exactly when. Under yours, the asset holds the *vocabulary* and your game holds the *grammar* — and the grammar is in code you don't read. The chips recover most of it: you can see which states exist, which are automatic, which are yours, and which nothing calls. **What you cannot see is *why* your game chose one.** Round two couldn't show that either for anything your game invented, and its probe explicitly couldn't show a power-up's states at all — but for the narrow case of "several deaths that differ by damage type", round two really does put the whole rule on screen and yours really doesn't.

**2. "The same death, but the gore only on a critical hit."** Under round two, one line on one effect. Under yours, two rows with duplicated effect lists. **Round two is simply better here and it's not a contrived example.** Partial mitigations: keep the mute checkbox on individual effects, and note that a condition on an effect *inside a card your game already chose* could be added later as a small optional extra — it doesn't drag the whole apparatus back with it.

**3. The migration risk changes shape rather than disappearing.** Round two's risk was *loud* — restructured saved data either loads or doesn't, and you find out immediately. Yours is *quiet* — the interruption fix means effects start firing that never fired before, and a scene can look subtly wrong for weeks.

---

## The question underneath all of this, which only you can answer

Round two and round three aren't really two architectures. They're two answers to one product question:

> **Is Laubrary a no-code character-authoring tool, or a library that games drive?**

If a designer with no programmer must be able to author "burn deaths look different from crush deaths" entirely in the editor, **round two is the right design and this one isn't.**

If the people building on Laubrary write code — and here the author, the programmer and the AI are the same team — **yours is right, and by a wide margin.**

**My read: it's the second, and the evidence is your own content.** Across six characters there is exactly one custom event authored, zero cues, zero death reactions, and seven effect entries in total. There's no body of no-code authoring here to protect. **Say which one you're building, though, because it settles this permanently rather than round by round.**

---

## Build order

Six phases. Each leaves this project working. Each has its line about what the five other projects see on the next sync — they're all updated by wholesale folder replacement with no staged rollout, which is why these notes matter.

**Phase 1 — make the thing that already exists trustworthy.** Generate the code-side name list. Make "no such state" say so, copying the one place that already does it properly, while keeping Laubrary's own harmless shrugs quiet. Settle on one capitalisation convention. Pass the metadata that's already sitting at the fire site instead of dropping it. Make the stun field actually get read. Editor: warn when the generated names are out of date.

> **Others see:** one visible change — a stun set on a custom event or on a death starts working, having been silently ignored. It's a fix, but it *is* a change; name it in the sync note so nobody thinks something broke.

**Phase 2 — the interruption debt, and the death bugs sitting next to it.** Give reactions a proper claim-and-priority rule so death always wins and the newest thing shows. Stop dropping an interrupted reaction's pending effects. Stop losing its "I'm finished" signal. And fix the death-disposal faults in the corrections section while you're in there.

> **Others see: real behaviour changes — the only phase that has them.** Effects that never fired now fire. **Sync this alone, with nothing else in the drop.**
>
> **Two warnings.** First, this is **more work than it looks.** There *is* an existing component that does claim-and-priority — but it's only attached to characters that use a particular kind of animated view *and* have movement poses authored, and **not one of your six characters currently qualifies**. So this is building, not just connecting. Second, **why this comes before the headline feature and not after:** it's what closes the trap described earlier, and today the whole thing is invisible for one accidental reason — your one custom event has no animation on it. **Type an animation name into that row and it flips from invisible to everyday, with no code written and no new feature.**

**Phase 3 — the "ask me" direction. This is the phase after which you have the thing.** The role chip. The two answer slots, hurt and death. Hurt and death drawn as the top two rows of one list, with their chips. Death states may override what becomes of the body. Rewrite that old objecting note.

> **Others see: nothing.** Three new settings appear with sensible defaults, and **no existing saved value changes meaning or moves** — these are ordinary settings on an ordinary saved type, not the kind of restructuring that can lose data. With no answer given, every character behaves exactly as it does today. **The model lands and the migration risk is at its floor — that combination is the entire argument against round two's plan.**

**Phase 4 — the parcel.** Your game hands its own values through to its own effects. **This is what lets those two placeholder demos finally become real characters**, and I'd fund it before the next phase.

**Phase 5 — the honesty surface.** Each row reports whether anything asks for it, and whether a death-eligible row is ever actually chosen. A project-wide check for names asked for that no character declares. A copy-the-name button on each row — a few lines of work, and the most direct possible answer to your "path of least resistance" goal: the exact text to paste sits one click from where the name was declared.

**Phase 6 — weapons and ammo get their own state lists.** Cheap after phase 3, and **this is the phase that actually delivers your power-up-changes-the-muzzle-flash scenario.** It also stops you authoring a gun's flash onto every character that can hold it.

**Named as out of scope, so it isn't quietly assumed:** ammo counts; damage types on blows; absorbing the cue list; the frame-number problem in the effect list.

**Two things round two had that this plan drops, said out loud rather than lost:** a version stamp on the character asset, which was round two's only proposed way of making the other projects' upgrades automatic rather than manual; and grouped headings in the "add a kind" menus, which round two justified by a cross-tool win — your effect pickers already overflow the screen on long lists. Both are still worth doing; neither is in the six phases.

**The one risk I can't close:** the five other projects couldn't be read from here. How much bespoke character-driving code they already contain is the biggest unknown in either design. Phases 1, 3, 4 and 5 barely change behaviour; phase 2 concentrates all of it into one droppable batch.

---

## What could make this collapse

**If asking for a state stays silent when it misses.** That's the failure mode of this whole model — not complexity, *quiet*. A declared state nothing uses looks identical to a working one; a drifted name builds fine, saves fine, looks authored, and plays nothing. **This has already happened here** — *(second thing you have to take on trust: it's invisible from the editor)* — three demo assets still hold settings the tools no longer have, and two of them are the more serious kind, where a character's hurt and death content can't be read at all any more and silently falls back to nothing. Nothing noticed. That's why phase 1 is the loud-failure phase and not the fun one.

**If the parcel never gets built**, the demos that need per-instance values stay placeholders and the model looks like it didn't deliver.

**If phase 2 is skipped or reordered after phase 3**, the trap described earlier is wide open and the first person to try the obvious thing gets a permanent corpse.

---

## Corrections to what you were told in earlier rounds

1. **"Demos deliberately bypass the character system to choose their own effects."** Wrong — they're documented placeholders awaiting conversion. Withdrawn, along with the better finding that replaces it.
2. **"The system must know a blow is lethal before it plays anything."** Not true today, and the arrangement that makes it safe is written down for an unrelated reason.
3. **"Whether a hit was critical is gathered and then discarded."** It's never gathered at all — nothing anywhere ever sets it, so it's always false.
4. **"Three other projects consume this library."** Five.
5. **Round two cited a first-match-wins precedent in the event system to justify ordered lists.** It doesn't exist; the only such thing is on the movement side.
6. **"The data reaching a reaction is badly narrowed."** Half right — the full picture already reaches anything listening; only the presentation path narrows it.
7. **My own first draft of this document said muzzle flashes currently appear at the character's centre.** They don't — they're already tracked to the muzzle. Corrected in the table above.

**Four live bugs found on the way, and the first two are worse than anything else on this page:**

- **Every character in your project is destroyed the instant it dies.** Because not one of the six has a death animation, the "wait for the death animation to finish" disposal fires immediately, before the death handling has even run — so the linger time you set is silently ignored, and the code written specifically to catch this case never gets the chance. One of your characters has a 1.5-second linger authored that does nothing.
- **A character's collision is switched off when it dies and never switched back on.** Anything that revives a character in place is therefore permanently unhittable — **including Mirage's own target-practice respawn**, which is a shipped tool, not a demo.
- The "linger for a while then vanish" disposal starts its timer at the moment of death rather than when the animation ends, so a long death animation gets cut off mid-play.
- There's a signal meant to tell movement "this character was shoved, it isn't walking" that nothing ever sends.

**And one editor gap two rounds have now walked past:** the rig section of the character window draws *nothing at all* for a character that isn't built from composed parts. Four of your six are. That's a blank space where an explanation belongs, in the very window this whole design is about.

---

## Decisions that are yours

1. **The product question above.** No-code authoring tool, or a library games drive? Everything else follows.
2. **Does a state name get its own generated code list?** I recommend yes, generated rather than hand-written, for the rename check. You proposed hand-written; this is the one place I'm arguing with you.
3. **Do you want the parcel** — your game handing values through to effects? I recommend yes, and I've given it a phase of its own rather than leaving it as an aspiration.
4. **Should phase 2's behaviour changes ship behind a switch, default off**, so each of the five other projects turns them on deliberately after a look? It costs a setting I'd rather not add. Given that those projects are the biggest unknown here, **I'd take the switch for one release cycle** — but it's your call, not mine.
5. **Weapons and ammo getting their own state lists.** Recommend yes — it's the only route to your power-up scenario.
6. **The abandoned folder.** *(Third thing you take on trust — invisible from the editor.)* You were asked months ago whether to finish it, delete it or leave it; the question expired unanswered and it's still sitting there. Under this model almost none of it is wanted. **Recommend: delete it, keep its notes.** It needs a real answer this time.

---

## A closing note

Round one said the modularity policy is followed wherever someone remembered to follow it, and nothing forces it.

Round two tried to fix that by making the library able to express every "when". Your proposal fixes it by making the library **stop trying to answer "when" at all** — and keep a short, honest list of the moments it genuinely owns.

That's the better instinct, and the one that actually scales: a library that answers every question grows forever, and a library that asks the right questions doesn't.
