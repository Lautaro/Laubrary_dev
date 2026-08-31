# Laubrary — the palette model: my take

**What this is:** round three of the modularity work. Round one diagnosed. Round two designed a system where the character asset decides which reaction fits. **You then pushed back and proposed something different, and this is my answer to it.** **Date:** 2026-08-26.

**How to read it:** written for someone looking at the editor, not the code. Where it says "a row", "a chip", "a dropdown", that is literally what you would see on screen. **Three sections are honest exceptions** and each says so where it starts: the one about names (because what a programmer types is the whole point of your proposal), the one about bypassing (same reason), and the first step of the build order (which is nearly all invisible plumbing). Everywhere else, if you can't see it in the editor, I shouldn't be saying it.

**Status:** still talking, as you said. Nothing has been built. But I have a verdict, and it changes what round two said. **This version has been fact-checked against the project twice, and eight things I told you in earlier rounds turned out to be wrong.** They're listed near the end, and two of them were load-bearing.

---

## TL;DR

**You are right about the split, and right for a better reason than the one you gave.**

You proposed it as a *retreat* — "maybe Laubrary shouldn't take everything." It isn't. It is a **cleaner split of who knows what**.

**The one sentence:** the character asset says **what this character can show**; your game says **which one to show**; and Laubrary *always* decides **what actually happens to the character** — and that third thing is never, ever something you author.

**What holds up:**

1. **The mechanism already exists and already runs.** Asking a character to play a state by name is a finished, working feature today — round one already reported this and I'm repeating it rather than discovering it. What's missing isn't the mechanism, it's the four things that would make anyone *prefer* it.
2. **Your proposal deletes round two's most dangerous idea.** Round two put "this one kills" on an authored card, plus a safety net for when someone forgot to tick it — because forgetting meant a character that plays its death animation and doesn't die. Under your model that box doesn't exist. **Whether something dies is decided by its health reaching zero. Full stop.**
3. **It costs less.** Round two was eight steps, and its riskiest one restructured saved data in **five** other projects at once. This is five steps, the payoff lands in the third, and that step adds settings without moving or reinterpreting anything you've already authored.

**And three things that are not good news, up here rather than buried:**

4. **My headline saving from the first draft was half wrong.** I claimed your model retires damage types from the combat system entirely. It retires them on the *shooting* side — your game holds the gun, it knows it's the lazer. It does **not** retire them on the *getting hit* side, and "Hit by fire looks different" is one of your five example rows. See **"The hole in my own headline"**.
5. **Your central hope — that this stops Claude bypassing Laubrary — is the weakest part of your proposal, and my first draft answered it with a copy button.** It deserved better. See **"The bypass question"**, which is now the longest section here, because it's the one you actually care about.
6. **Two rounds have now argued about *which* visual plays, and the only unmet need actually demonstrated anywhere in your project is a different question — *with what values*.** See **"The thing neither design solves"**.

---

## What I am reversing from round two, out loud

Round two ended on two habits. One I am now reversing, and you should hear it from me rather than notice it.

> *"If it answers **when**, it must be pluggable, never a fixed set of choices."*

Wrong shape for this domain. The new version:

> **If it answers *when*, and the answer depends on something your game invented, Laubrary should not answer it at all — it should ask.**

The other habit — *"if there can ever be two of these, it is a list from day one"* — stands, and your palette is exactly that habit applied.

Round two is not wholly wrong; several things in it survive and I say which below. But its central mechanism — the character asset holding conditions and choosing between them — I am withdrawing.

---

## Your palette, as you'd actually see it

You wrote five rows. Here they are as the editor would show them, because I don't think either of us has pictured it yet — and because **three of the five don't work the way you described, and I'd rather point at that than quietly redesign your example.**

| Your row | What it becomes | Who says "now" |
|---|---|---|
| Shooting gun | a state your game asks for | **your game** |
| Shooting lazer | a state your game asks for | **your game** |
| Hit | an **answer** to a question Laubrary asks | Laubrary asks, your game answers |
| Hit fire damage | an **answer** to the same question | Laubrary asks, your game answers |
| Death | an **answer** to a question Laubrary asks | Laubrary asks, your game answers |

**The change I'm making to your example:** you described all five as things your game fires. Two of them can't be, and the difference matters. Your game never *tells* a character it was hurt — the character finds that out from its own health. What your game gets to do is **answer** "which hurt look?" when it's asked. Same for dying. Only the shooting rows are things your game announces.

That is not a nitpick. It's the reason the model is safe: your game can never make a character die, fail to die, or fail to be hurt. It only ever gets to choose the appearance.

---

## The one move that makes this work

Three questions keep getting mashed together by the word "modularity". Separate them and everything falls out.

| The question | Who owns it | Why it can't be anyone else |
|---|---|---|
| **What can this character show?** | The character asset — you, in the editor | It's content. It's already a list. It already saves correctly in every project. |
| **Which one right now?** | **Your game, always** | The judgement uses ideas Laubrary must never learn — what a lazer is, what a combo is, what "overheated" means. |
| **What happens to it mechanically?** | **Laubrary, always** | Switching off its collision, stopping it acting, disposing of the body, telling whatever is waiting to respawn it. These are not choices. They are consequences. |

**Round two put the second and third in the same box.** That is the error, and it is the whole error. Once a card can both *be chosen* and *decide the character's fate*, choosing wrongly kills the character silently.

---

## Death is not a word in the vocabulary

This is the load-bearing rule and it deserves its own heading.

**A character's declared states are a vocabulary of things it can show. "Dies" is not one of them.** Health reaching zero is what kills. Nothing you author, nothing your game says, can make a character die or fail to die.

Everything good follows:

- **A misspelled state name cannot disable dying.** A mistake gives you a character that dies playing the *normal* animation — wrong-looking, obvious, harmless.
- **The safety net, the default rule and the warning badge round two needed all disappear.**

**And a correction to round two, which I got half right in my first draft.** Round two argued the system must know "this kills" before it plays anything, so it can switch collision off in time. That's wrong, and the reason is stronger than I first said: **switching collision off and stopping the character acting are driven by health hitting zero, in the very same instant as the reaction — not one after the other across a frame.** There is no gap to protect and nothing you author can widen one. I previously described this as a lucky ordering nobody wrote down; that was a weaker and slightly wrong version of the same good news.

---

## Your proposal actually contains two lists, and separating them is most of the design

You described one thing. It's two, and they behave completely differently.

### List A — states your game can ask for

Open. Per character. Grows by you adding a row. **This ships today.** Your game invents "Overheated", you add a row called Overheated, your game asks for it. **Nothing inside Laubrary is edited, ever.** That is the anti-bloat guarantee.

### List B — moments where Laubrary stops and asks *you*

**Closed. Short. And its shortness is the point** — it is the only surface your game has to learn. Today it is two: **when hurt** and **when killed**. Obvious near-term additions: **when it spawns**, **when a death animation finishes**. That's the list.

Each moment has two faces, and only one needs building:

- **The announcement.** "This character was hurt, here's everything about the blow." **This already exists and is already complete** — how much, from where, from whom, which side they're on, all of it reaches anything that's listening. It's shouted to the room; anyone can hear it and nobody has to reply. (Round two said this information was thrown away. It's thrown away only on the *appearance* side, which is a much smaller problem than reported.)
- **The question.** "Which state should I show for this?" This is the new bit. It's asked of one specific answerer and needs exactly one reply, because "which death does this character show" can only have one.

**Why this matters practically:** for a death, your game never *asks* for a state — it **answers a question**. Laubrary still runs the entire death sequence exactly as it does today. It just plays a different animation partway through.

### And what if two things both want to answer?

One answer per question is the right rule, but "two answerers is an error" is too blunt — a power-up, a burning status effect and your ordinary game code are all reasonable candidates on the same character at the same time. So the rule is: **whoever answered most recently for this character wins, the one before is remembered and restored when the newcomer goes away, and the editor shows who is currently answering.** Otherwise the first power-up you write silently fights your own game code and you'd never know which won.

### And when the answering is set up, and let go

A character picks up its answerer as it comes into the world and drops it as it leaves — **including when it's being reused rather than freshly made**, which is what happens to everything that respawns in this project. Get this wrong and a recycled enemy keeps answering with the last one's rules. That looks like a haunting, not a bug, and you'd chase it for a week.

There also has to be a defined answer for **"nobody has answered yet"** — a character can be hit on the same instant it appears. The answer is: it does exactly what it does today.

---

## What a declared state has to carry

**One requirement is non-negotiable: a state name must resolve to a whole card, never to just an animation name.** If a state is only "play this animation", you silently lose how long the moment lasts, the flash on the body, and every effect timed to fire partway through. **There is already a feature in the toolkit that takes exactly that loss**, so this isn't hypothetical.

So a declared state carries what a reaction card already carries — an animation, how long the moment lasts, an optional flash on the body, and the effect list. Plus three small things:

- **A role chip** — *nothing special* / *this is a hurt look* / *this is a death look*. This is how "Death by fire" and "Death by crushing" become eligible to be chosen as deaths. It defaults to "nothing special", so nothing you already have changes meaning. **Where it lives, decided:** the chip goes on the rows in your open list, and the two built-in hurt and death rows get their chip *drawn* for them rather than stored. That keeps the promise that nothing already authored is touched.
- **Stun.** That box already exists on every reaction and you can already type into it. **It is only ever obeyed in one situation and silently ignored everywhere else**, so a stun you set on a custom event is saved and never used. Worth knowing: this is round two's finding, not a new one — I'm repeating it because it's still true and still unfixed. It's also frozen when the character is created, so changing it on a character that's already in the world does nothing.
- **On death states only: what becomes of the body and how long it lingers.** Currently decided once when the character is created and never varies. This is what makes "burn away to nothing" and "leave a smear" both expressible.

**And the rule that decides what is *allowed* on a card:**

> **Anything whose absence is silent stays off the card. Anything whose absence is just a default may live on it.**

Forget the disposal setting and you get the character's normal disposal — visible, fixable, never wrong about whether it died. Forget round two's "this kills" tick and you get a corpse that walks. Difference in kind, not degree.

**One warning that comes with keeping the effect list as it is.** The effects on hurt and death rows are placed by **frame number**, not by a moment you painted on the animation — the rest of the toolkit does it the safer, painted way. That's survivable with one death animation. With three death rows of different lengths, an effect authored for frame 8 lands somewhere completely different in each, and nothing tells you. Round one flagged this as a silent-breakage trap; round two and my own first draft both said the effect list needs no work, which quietly contradicted it. **It does need this one thing**, and I should have said so.

---

## What about two things at once?

**This is the biggest thing my first draft missed entirely, and it lands directly on ProtoGuy.**

A character often has to show more than one thing at the same moment — legs walking, arms firing, and then a flinch that takes over everything. ProtoGuy is *already built this way*: separate legs and upper body. A state as described so far is one thing at a time, and that is not enough.

The fix is small and has to be in from the start: **a row says which part of the character it speaks for** — the whole body, or just the top half, or just the legs. Then "walking" and "Shooting lazer" can be true together, and a flinch that claims the whole body cleanly wins over both. Leave it out and your own headline example — firing a lazer while walking — cannot be expressed at all.

---

## Your own example, checked honestly

You wrote: *a Shoot event comes with direction, ammo left, projectile type, ammo type; native code sees the ammo type is Lazer and fires "Shooting Lazer".* I checked every piece. **Two rows of this table were wrong in my first draft and are corrected here.**

| What you assumed | Reality |
|---|---|
| direction | **There.** |
| the projectile | **There.** |
| the ammo | **There**, one step away. |
| the gun | **There**, right where the shot happens. |
| where the muzzle actually is in the world | **There, and mostly fine — I overstated this.** The weapon's own muzzle flash already appears at the muzzle correctly. What's wrong is narrower: when the character is asked to play its "Fire" state, that request arrives carrying nothing — no position, no direction, no amount — so any effect on that row left on its default position setting lands at the character's anchor point instead. ProtoGuy dodges it by pinning its fire effects to a painted muzzle point. Worth fixing, but small, and not the visible bug I described. |
| **ammo left** | **Does not exist anywhere.** No ammo count, no magazine, no reloading in Laubrary at all — weapons only have a cooldown. That's a new combat feature, not plumbing. |
| **"the damage type is Lazer"** | **Not expressible in weapon data today.** There's a damage-type asset nothing reads and of which not one has ever been made; a gun's ammo list has a "+" button but only ever uses the first entry, and its own tooltip admits it. **A gun that fires either lazer or bullet ammo cannot currently be described** — which means the "which ammo am I firing" model has to live in your game, outside Laubrary. |

**On the shooting side, that last one doesn't matter:** your game holds the gun, your game knows it's the lazer, it just says so. Laubrary never has to learn what a lazer is. That is a real saving.

---

## The hole in my own headline

**My first draft claimed your model retires damage types from the combat system entirely, and called that "worth more than anything else on this page". That was half wrong and I'm correcting it prominently rather than in a footnote.**

The saving is real **going out**. It is not there **coming in**.

Walk your own row "Hit fire damage". A character is hit by something it didn't author, fired by someone else. Laubrary asks "which hurt look?" and your game has to answer. **To answer, your game must know the blow was fire.** What arrives with the announcement is how much, from where, from whom and which side — **not what kind**. So your game's answering code has to work fire-ness out for itself: find the attacker, find its weapon, find its ammo, decide.

**That is a per-game bespoke model that isn't reusable — the exact thing you said you were afraid of, produced by the saving I was cheering about.**

Three ways out, and you should pick one rather than discover this later:

1. **Stamp a kind onto every blow after all.** Round two's plan, unretired. Most reusable, most work, touches combat.
2. **Let the answer come with the shot.** Whoever fires attaches its own note to the blow, and that note comes back with the announcement untouched. This is the parcel idea below, pointed at the incoming side, and it's the cheapest thing that actually works.
3. **Accept it.** Every game writes its own "was that fire?" helper. Fine for one game. Bad for a library shipping to five.

**My recommendation is 2**, and it means the parcel is not an optional extra — it's load-bearing.

---

## Names, typos, and the argument already written against you

**Honest exception #1 — this section is about what a programmer types, which is the whole substance of your proposal.**

There is a note sitting in the project, written months ago, arguing against exactly what you propose. **My first draft quoted it inaccurately and cut off its main argument**, which was unfair to it. What it actually says is that hurt and death aren't merely named reactions: hurt carries proper combat information, and death gates whether the character can still act and decides what becomes of the body — so folding them into a list of names would mean special-casing two entries anyway, and would let a typo silently disable dying.

**The main argument is answered by the split, not dodged:** hurt and death *do* carry those things, and under this model they keep carrying them, because those things stay on Laubrary's side and are never name-driven. What becomes name-driven is only the *appearance*. **The typo half is answered by killing the coupling it fears** — dying isn't name-driven, so worst case is a character that dies looking wrong.

**The special-casing half is true and I won't pretend otherwise.** The role chip *is* that special case. My claim is only that a visible chip on a row is more honest than today's version, where hurt and death are special because of *where they live* and can't be asked for by name at all.

**The four things that make names safe:**

1. **A name is typed once, where it's declared.** Already the law in this project.
2. **Every place that refers to it picks from a list.** The picker already exists and already handles renames by keeping the old value visible with a warning.
3. **Code gets a picker too — a generated one.** Not to stop typos, but to stop **renames**: rename a state, regenerate, and every place in your game using the old name refuses to build. **But this only helps if you actually regenerate** — and my first draft sold it as automatic safety when it depends on you remembering. So: **the list must regenerate itself when the character is saved**, or a stale list must be a hard error, not a warning. Otherwise it's no better than the hand-written list you proposed and I've argued for extra machinery for nothing.
4. **Asking for a state that doesn't exist has to say so.** Half-true today, and I overstated this too: a missing state raised from an animation frame *already* warns by name, and the editor already badges it. What's actually missing is that the warning doesn't list what the character *does* have, and a direct request from game code reports failure in a way that's easy to ignore — so it looks silent even though it isn't.

**And what the character does when a name misses: nothing.** It carries on with whatever it was doing, unchanged, and complains once. A missing state must never freeze a character or blank it.

**The alternative you invited me to offer, since you said you were open to one.** A declared state could be **an asset you pick** rather than a name you match. That kills renames outright, matches this project's own standing rule that a reference is never typed, and — the real prize — **lets two characters share the same death look instead of each holding their own drifting copy**. The catch is that game code can't cheaply hold an asset reference the way it can hold a name.

**So take both:** the **name** is the address your game speaks; the **content** behind it may optionally be a shared asset that several characters point at. You get code-friendly names and editor-friendly reuse, and neither has to lose.

**And the honesty check I like most.** A sibling tool in your own library already ships a feature whose stated purpose is *"so a tool can honestly report that nothing in this scene reacts to these events yet."* Same idea here: **each row tells you whether anything in your project actually asks for it.** Declared-and-unused gets an amber chip.

---

## The bypass question

**Honest exception #2 — this is about what someone writing game code will actually do, and it can't be discussed in editor terms alone. It's also the thing you said you cared about most, and my first draft answered it with a copy button. That wasn't good enough.**

Your hope: this model is intuitive enough that whoever writes the game code — you or Claude — reaches for Laubrary instead of solving Zoe's job natively.

**Here is the case against, honestly:**

1. **This model moves the game one seat closer to bypassing.** Round two's game only ever *reported a fact* and then shut up. Under yours it becomes the **director**: it knows the vocabulary, holds the mapping, issues the instruction. Once game code contains *"if the ammo is lazer, ask for Shooting Lazer"*, the cost of writing *"if the ammo is lazer, spawn the lazer flash myself"* on the next line is **zero**. Same data, same seat, same file already open.
2. **For everything except death, there's no force at all.** The genuine anti-bypass guarantee is narrow: dying is owned by Laubrary, so bypassing it gets you nothing. **But nothing in List A has mechanical consequences.** For exactly your headline example, the only thing between an AI and a direct effect spawn is taste.
3. **The friction runs the wrong way.** The right path: leave the code, open Unity, find the character, add a row, name it, author its card, regenerate, come back, paste. The wrong path: type one line in the file that's already open. A copy button shortens the last step of the long path. It doesn't shorten the path.
4. **Your own project already shows the revealed preference.** The compliant mechanism has existed all along. Across six characters there is **one** custom event, **zero** cues, **zero** death reactions and **seven** effect entries in total. I originally read that as "there's no no-code authoring here to protect". It reads at least as well as *"the authoring surface is the one people already skip"* — which is the bypass problem, measured, in your own project.
5. **The amber chip points the wrong way.** It catches *declared but never asked for*. The failure you fear is *code did it itself and nothing was ever declared* — which produces no row, no chip, nothing to be amber. **The only detection mechanism in my first draft is structurally incapable of seeing the failure it was offered as an answer to.**

**What would actually earn it — four things, all cheap, none in my first draft:**

- **Make the compliant path strictly more capable, and put that on the page.** Only Laubrary-routed visuals get interruption handling for free, attachment to the right body point, facing and mirroring, pooling, and animation-timed effects. Then bypassing costs you five features instead of nothing. **This is the only one of the four that changes the incentive rather than the instructions, and it's the one I'd build first.**
- **A written rule in the project's own instructions**, which an AI reads before writing a line: *a character shows things only through its palette; if the state you need isn't there, add a row — never spawn it yourself.* Your worry is literally about an AI's default behaviour, and the cheapest lever on that is a written instruction. My first draft never mentioned it.
- **A reverse check** for a character showing something that didn't come from its palette. The mirror of the amber chip, and the only thing that can see the real failure.
- **Each character can print its own list of states on demand**, so whoever is writing code can see what already exists without opening Unity.

**Verdict:** your model is still better than round two on this axis, because round two's compliance cost was higher and higher cost means more bypass. But **better than the alternative isn't the same as solving it**, and I shouldn't have implied it was. With those four levers it's genuinely defended. Without them it rests on discipline.

---

## The thing neither design solves

Round two claimed, and I repeated, that your demos "chose to bypass the character system so they could pick their own death effects". **I've withdrawn that** — they say in their own notes that they're placeholders awaiting conversion, and they mark exactly where a real character takes over. Unfinished, not rebellious. (I also can't find that claim in round two's document, so it may be mine alone; either way it's wrong.)

**But what they actually want is far more useful.** They don't want to choose between three deaths. They want to hand *values* to a visual: *this instance's* colour for the debris, an explosion size driven by a live slider, and a "this one died" signal saying which instance.

**No character asset can express any of that.** A character asset is shared by every copy of that character — it can't say "tint this one blue". **Neither round two nor your model fixes it.**

**So the palette needs one more thing than you proposed, and it comes in two halves** — my first draft had only the second, and that version couldn't actually solve the debris case it was introduced for:

- **A few presentation values Laubrary *does* own and *does* read: a tint, a scale, an intensity.** These aren't gameplay concepts, they're universal to visuals, and they're what lets a Laubrary debris effect be tinted per copy. Without this half the example that motivated the whole section still doesn't work.
- **An opaque parcel Laubrary never looks inside**, carried through untouched to *your own* effects. This is not the loose bag of named values that was rightly rejected before — it's one thing your game defined, and Laubrary never inspects it. **This is also the cheapest fix for the damage-kind hole above.**

---

## What this retires from round two

| Round two wanted | Status | Why |
|---|---|---|
| **Conditions on cards** — "only if fire", "at least 20", is/is-not/between | **Gone** | There's no selection left to condition. Your game already made the decision in its own code with its own types. |
| **The named-facts system** — fact types, fact names, a fact picker everywhere | **Gone** | Nothing looks facts up by name any more. Round two called the fact picker load-bearing and warned that shipping without it would make authoring worse than today. That whole risk goes with it. |
| **Ordering and priority between cards** | **Gone** | Nothing chooses, so there's nothing to order. Largest single simplification — but see the losses section, it takes something real with it. |
| **The "what would happen if…" probe** | **Gone** | Round two called it non-negotiable. The question it answers no longer exists. |
| **Stamping damage types through combat** | **Downgraded, not gone** | **Corrected from my first draft.** Retired on the shooting side. Still needed on the receiving side, unless the parcel carries it. |
| **"This one kills" as a tickbox, plus its safety net** | **Gone** | See "Death is not a word in the vocabulary". |
| **Restructuring hurt and death into one saved list** | **Gone — and this is where the real risk was** | Hurt and death stay exactly where they are; they're just *drawn* as the top two rows of the same list. Uniform surface, no rearranging. |
| **"Red" in the toolkit as a blocker** | **Downgraded** | Still worth doing once; no longer on the critical path. |
| **Power-up overlays** | **NOT retired — I had this backwards** | See the losses section. This is one of the model's worst cases, not a win. |

**What survives, each earning it with a live defect rather than an argument:**

- **The effect list stays** — except for the frame-number timing above, which round one was right about.
- **A killing blow will play part of the hurt reaction and then stomp it**, dropping the hurt's remaining effects and its "I'm finished" signal. **Correction: this is latent, not live.** It needs a character with *both* a hurt animation and a death animation, and today none has either. **The moment you author your first death look — which is the entire point of this design — it becomes live.** That changes when it has to be fixed, and I've moved it earlier in the build order because of it.
- **Interruption needs a rule.** There's already a part of your library whose whole job is deciding who gets control when two things want it, and handing it back. **Correction: it is not attached to every character** — only to ones with authored directional movement — and only one thing currently uses it. So this is "attach it always and route three things through it", not "connect one thing".
- **Stop narrowing what a reaction is told.** Still right, and smaller than round two thought.
- **Delete the abilities shell.** Unchanged. Zero implementations, zero callers, and its own label already says it's unused.
- Round one's leftovers — hooks on the character-building recipe, opening up debris behaviours, and **auditing every "+" button that only uses its first entry** — all still open. The last is *more* urgent now, because it's why your own example can't be written.

---

## Where your model genuinely loses

Seven, not three. My first draft listed three and put one of the worst in the wins column.

**1. The character asset stops describing the character.** The asset holds the *vocabulary*, your game holds the *grammar*, and the grammar is in code you don't read. The chips recover most of it — which states exist, which nothing calls. **What you can't see is *why* your game chose one.** For the narrow case of "several deaths differing by damage type", round two really does put the whole rule on screen and this doesn't.

**2. "The same death, but the gore only on a critical hit."** Round two: one line on one effect. Yours: two rows with duplicated effect lists. **Round two is simply better here.** Mitigations: keep the mute checkbox on individual effects, and note that a condition on an effect *inside a card your game already chose* could be added later without dragging the apparatus back. (Worth knowing: nothing in Laubrary ever marks a hit as critical today, so this case is theoretical until something does. A consuming game could.)

**3. Power-ups get combinatorially worse. I had this as a win; it isn't.** "Bigger muzzle flash for 10 seconds" means a duplicate row with a duplicated effect list. A power-up meant to work on *any* character needs that duplicate on **every** character for **every** state it touches, all drifting independently. Round two did it with one overlay card. **The parcel fixes this properly** — hand a scale through instead of authoring a second row — which is another reason it isn't optional.

**4. Sharing a look between characters doesn't exist.** Five characters that all die the same way hold five copies that drift apart. My first draft never mentioned reuse at all, in a document about a *library*. The optional shared-asset half of the names section is the fix.

**5. Variety for its own sake now needs code.** "Three collapse animations, play a different one each time" involves no gameplay judgement whatsoever — but something still has to choose, and under this model that something is your game. Round two got it free. Small remedy: let a row hold several animations and pick one per play, without any of the ordering apparatus coming back.

**6. Replays and networked play get harder.** Today a character's look is decided from data every copy of the game shares. Under this model it's decided by your game's code at the moment it happens — so a replay fed only movement and shooting won't reproduce the lazer flash. Not a blocker, but your game has to send *which state it asked for* alongside everything else, and that's better decided now than the first time two people play.

**7. The migration risk changes shape rather than disappearing.** Round two's risk was *loud* — restructured data either loads or doesn't. Yours is *quiet*: the interruption fix means effects start firing that never fired before, and a scene can look subtly wrong for weeks.

---

## Seven scenarios, walked

Because a design that hasn't been walked is a wish.

| Scenario | Verdict | Where you'd get stuck |
|---|---|---|
| **Lazer instead of bullet, different muzzle** | **Works, with friction** | Add a row, author it, ask for it by name. But the gun can't say it's firing lazer ammo — that model has to live in your game — and this shouldn't be done per-shot until the interruption fix has landed. |
| **Hit by fire shows a burn reaction** | **Works, with the hole above** | Your game must work out that the blow was fire, and nothing helps it. This is the case that makes the parcel load-bearing. |
| **Fire death burns away, bullet death leaves a corpse** | **Works — the showcase** | Two rows, chipped as death looks, each with its own disposal. Genuinely good, and needs no "this kills" tick. One catch: "linger then vanish" starts its timer at the moment of death rather than when the animation ends, so a long burn-away gets cut off. That's a real bug on the showcase path and it's now in the build order. |
| **Power-up: bigger flash for 10 seconds** | **Poor** | Duplicate row per character per state. Fine for one character, bad as a library feature. Fix it with the parcel, not with rows. |
| **Second character reuses ProtoGuy's death** | **Doesn't work today** | Hand-duplicate everything and watch it drift. Needs the shared-asset half. |
| **Renamed a state, forgot to regenerate** | **Works only if regeneration is automatic** | Otherwise the code still holds the old name, builds fine, and fails at runtime the first time that state is needed. |
| **Designer with no programmer adds a second death** | **Doesn't work, by design** | They can add the row and see it's unused. They cannot make anything ask for it. This is the honest cost of the model — see the product question. |

---

## Build order

**Five steps, reordered from my first draft** because the fact-check changed which one is urgent. Each leaves this project working. Each says what the **five** other projects see on the next sync — and note round two said three; it's five, all updated by wholesale folder replacement with no staged rollout.

**Step 1 — make what exists trustworthy.** *(Honest exception #3: almost everything here is invisible plumbing. Two results are visible and they're named below.)* Generate the code-side name list, and make it regenerate when a character is saved. Make a missed state say what the character *does* have. Make name matching consistent — state names are matched exactly today while every other authored name in the same window ignores capitals, which is a trap waiting to happen. Carry the position and direction through when a state is requested. Make the stun box actually get read, from whichever state played. Make asking by name resolve to the whole card, never just an animation. *Rough size: a few days.*

> **Others see:** two visible changes. Effects on a fire state land at the muzzle rather than the character's anchor. **A stun typed into a custom event starts working, having been silently ignored** — and because those values have never been obeyed, nobody has ever vetted them. Check them before syncing rather than after.

**Step 2 — the interruption debt, now rather than later.** *(This was step 3 in my first draft. It moved because authoring a death look is what makes the stomp live, and step 3 is where you author death looks.)* Attach the traffic-warden part to every character. Route reactions, plain movement and named states through it. State the priority ladder out loud — death above hurt, hurt above ordinary states — because "more important wins" isn't enough on its own: as written today an equally-important newcomer takes control, so a fire state would push a flinch aside. Each row says whether it can be pushed aside. **And when a request is refused, whoever asked is told**, rather than assuming it played. Stop dropping an interrupted reaction's pending effects and its finished signal. *Rough size: a day of work, plus however long you want to watch it in five projects.*

> **Others see: a real behaviour change — the only one in the plan. Sync it alone, with nothing else in the drop.** Doing it now is the safest possible moment: seven authored effect entries exist in total across the whole project. Every month you wait, that number grows.

**Step 3 — the model. This is the step after which you have the thing.** The role chip. Which part of the body a row speaks for. The two places Laubrary stops and asks your game a question — when hurt, and when killed. Hurt and death drawn as the top two rows of one list. Death rows may override what becomes of the body and how long it lingers. Fix the linger timer so it starts when the animation ends, as its own description already promises. Rewrite that old objecting note. *Rough size: the real week.*

> **Others see: nothing.** Saved data gains settings; **nothing existing is moved, renamed or reinterpreted**, so every existing asset loads and behaves exactly as before. (My first draft said "no saved data changes shape", which was over-claimed — new settings are still new settings. The safety claim survives; the wording didn't.)

**Step 4 — the honesty surface, and the bypass levers.** Each row reports whether anything asks for it. A project-wide check for names asked for that no character declares — **and the reverse check, for a character showing something that never came from its palette.** Deleting a row warns you if anything still asks for it. A play button on each row, so you can see a state without your game running. A copy button for the name. **The written rule in the project's instructions.** Each character can print its own state list. *Rough size: an afternoon, plus the rule, which is five minutes and matters more than the rest of the step.*

**Step 5 — weapons and ammo get their own state lists.** **This was "out of scope" in my first draft while simultaneously being recommended — which meant your own headline example wasn't in the plan at all.** It's in now. Cheap after step 3, and it stops you authoring a gun's flash onto every character that can hold it.

**Genuinely out of scope, so it isn't quietly assumed:** ammo counts and reloading; absorbing the cue list; stamping damage kinds through combat *if* you take the parcel route instead.

**The one risk I cannot close:** the five other projects couldn't be read from here. How much bespoke character-driving code they already contain is the biggest unknown in either design.

---

## What could make this collapse

**If asking for a state stays quiet when it misses.** Not complexity — *quiet*. A declared state nothing uses looks identical to a working one. **Your project has already had this happen**: three demo assets are still holding onto settings for things that were removed long ago, and nothing ever noticed. That's why step 1 is the loud-failure step and not the fun one.

**If the parcel never gets built.** It is now carrying three jobs, not one: per-instance values, power-up scaling, and the damage-kind hole. Skipping it doesn't cost you a nice-to-have any more.

**If step 2 is skipped.** The first weapon that asks for a state on every shot makes dropped effects an everyday occurrence. Worth knowing precisely: this is invisible today **not** because there's only one custom event, but because that one event has no animation attached. Give it one and the bug appears the same afternoon.

---

## Corrections to what you were told in earlier rounds

**Eight, and two were load-bearing.** Listed because you should hear them from me.

1. **"The muzzle flash spawns at the character's centre; one-line fix."** Overstated. The weapon's own flash is already correct; only effects on the character's fire row are misplaced, and only those left on the default position setting.
2. **"Asking for a missing state fails completely silently."** Not true — one path already warns by name and the editor already badges it. The real gap is what the warning *doesn't* say.
3. **"Two places compare names and disagree about capitals."** Wrong as stated. The real inconsistency is between *kinds* of name: state names are matched exactly, every other authored name in the same window ignores capitals.
4. **"The traffic-warden part is already attached to every character."** No — only to characters with authored directional movement, and only one thing uses it. The step costs more than I said.
5. **"A killing blow stomps the hurt reaction every time, right now."** Latent, not live. It needs both a hurt animation and a death animation, and no character has either yet.
6. **"Round two cited a first-match-wins precedent in the event system."** It didn't — it correctly attributed that to movement poses. I was arguing with something nobody said.
7. **"Your demos deliberately bypass the character system."** They're documented placeholders. And I can't find that claim in round two either, so it's probably mine.
8. **"Round two required knowing a blow was lethal before playing anything, and today's safety is a lucky ordering."** The conclusion was right, the reason was weak — it's safe because both things happen in the same instant, not because of an order someone got lucky with.

**Also corrected:** whether a hit was critical is never set by anything in Laubrary, so from Laubrary's own sources it's always false — though a consuming game could set it, and I can't see those from here. There are **five** consuming projects, not three. The "shoved, not walking" signal has **neither** end wired — nothing sends it and nothing reads it, so it's an unfinished feature rather than a dropped call. Round two's stun finding is round two's, not a new discovery of mine. And the effect list "needs no work" claim quietly contradicted round one about frame-number timing; round one was right.

**One more live bug found on the way, and it's worse than the linger one:** when a character's disposal is "when the death animation ends" — the default, and what every character in the project uses — but it has **no** death animation, the body vanishes instantly instead of lingering. The behaviour that was meant to cover this case can never happen.

---

## Decisions that are yours

1. **The product question below.** Everything else follows.
2. **Does a state name get its own generated code list?** Recommend **yes, generated, and regenerated automatically on save** — the manual version isn't meaningfully safer than the hand-written list you proposed.
3. **The parcel, in both halves** — a few presentation values Laubrary reads, plus an opaque one it never inspects. **Recommend yes, and I've promoted it from optional extra to required**, because it now carries the damage-kind hole and the power-up case as well.
4. **Can two characters share a row's content?** Recommend **yes, straight after step 3**. Not needed to prove the model; needed before the third character exists.
5. **The abandoned folder.** *(This one is about something invisible from the editor.)* You were asked months ago whether to finish it, delete it or leave it; the question expired unanswered and it's still there. Under this model almost none of it is wanted. **Recommend: delete it, keep its notes.** It needs a real answer this time.

---

## The product question — and why I'm no longer asking you to close it

My first draft ended by asking you to settle this permanently:

> **Is Laubrary a no-code character-authoring tool, or a library that games drive?**

**I'm withdrawing the "permanently".** The evidence I used doesn't support it, and the direction I was pushing you is the hard one to reverse.

**The evidence was bad.** I argued: six characters, zero death reactions, seven effect entries — therefore no no-code authoring to protect. But **round one already explained why that number is zero: a character has precisely one way to die, with no "+" button to add a second.** There are zero authored second deaths because the editor has never had anywhere to put one. That measures the tool, not the demand. (And the same emptiness has now been used to argue two opposite cases in two rounds — round two used "zero cues" to argue absorbing cues is free, round three used it to argue nobody wants no-code authoring. When the same empty room proves whatever's being argued that week, it isn't evidence.)

**Two more reasons.** This project is a library development *host*, not a game — thin content is the expected state of a workshop, and the places authored content would actually accumulate are the five consumer projects I couldn't read. And "the author, the programmer and the AI are the same team" is the most temporary fact in this document; it describes a solo project in 2026, not a property of the library.

**And the asymmetry runs the wrong way.** Round two → round three is cheap: the decision logic collapses into code and the asset shrinks. Round three → round two is brutal: once five games hold their own bespoke "which state" logic, pulling that judgement back into the asset means rewriting all five and re-deriving rules nobody wrote down.

**So: sequencing, not identity.** Build the palette, because it's cheap, small, honest and right *first*. Keep exactly one door open: **Laubrary ships a default answerer** — an ordinary asset that answers the hurt and death questions by a simple visible rule, used only when your game supplies no answer of its own. It's one small optional thing on top of everything above. With it, a designer with no programmer can eventually author "burn deaths look different from crush deaths" *inside this same design*, and round two becomes a later optional layer rather than an abandoned road.

**You still get to answer the question. You just don't have to answer it today, and my first draft was wrong to ask you to.**

---

## A closing note

Round one said the modularity policy is followed wherever someone remembered to follow it, and nothing forces it.

Round two tried to fix that by making the library able to express every "when". Your proposal fixes it by making the library **stop trying to answer "when" at all** — and keep a short, honest list of the moments it genuinely owns.

That is the better instinct, and it's the one that scales: a library that answers every question grows forever; a library that asks the right questions doesn't.

**But "stop answering when" is only safe if the library still makes answering *through* it the easiest path.** That's the bypass section, and it's the part of your proposal that isn't finished yet — not because the idea is wrong, but because a good idea with no incentive behind it is a convention, and conventions are what this whole exercise started out trying to replace.
