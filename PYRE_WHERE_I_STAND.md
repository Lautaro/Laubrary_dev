# Pyre — where I stand after your nine points

*The third report for T-0098. It answers your nine points one by one, in your order, under your numbers, and says what your answers changed in the two documents that came before. Read `PYRE_GUG.md` and `PYRE_SHAPE_FILL_BORDER.md` first if you want the reasoning behind what is being revised; this one assumes both and does not repeat them. Like the first two, it contains no code and no code names. Written 2026-08-30.*

---

## 0. Where I stand now — the whole shift in one page

**One sentence: almost every "this cannot be done" in the two earlier reports turned out to be "nobody has done it yet."**

Your points 8 and 9 pushed on the two places both reports said the tool had a limit, and in both places the limit is not real. And when I went looking for the hardest objection in report 2 — the one it called the thing that must be designed for and not discovered, where the colour supposedly decides the shape — **it does not exist.** Not in one generator. Not partly. I had six of the nine big effects and both fire simulations read line by line, specifically hunting for a single number doing both jobs, and there isn't one anywhere — then a second pass independently re-read four of them and confirmed it. **In four of the nine big effects you could swap the entire colour scheme today and the outline would come out pixel-for-pixel identical.** In the remaining five — seven, once you add the two fire simulations — the two decisions are multiplied together at the last moment, which you can pull apart with scissors. *(Scope, so the claim can be judged: eleven generators were examined in this depth. The wider claim that nothing in the library is inseparable rests on the earlier decomposability study, which classified all twenty-nine and found exactly one candidate — the imported-sprite one, which this document retracts below.)*

**That correction is the most important thing in this document**, because report 2 built its caution on it. The exercise you are proposing is significantly safer and significantly cheaper than that report said.

Three more things changed my position.

**Your ruling on assets removed about a fifth of both reports' arguments, and it removed exactly the fifth that was doing the wrong job.** The architecture survives untouched — those parts never used a single usage figure. What died is the reasoning about *what to do first and why*: report 2 ordered its whole plan by "this can't break anything you've saved," which is now meaningless. Four verdicts flip. One whole recommendation ("simplify the mask system rather than extend it") turns out to have had nothing under it but a statistic and should be withdrawn.

**You already ran the strategy you propose in point 4 — five weeks ago, in this project, and it worked.** Pyre as it stands today *is* the output of a parallel rebuild that ran for thirty-three days beside a still-working original. That reframes everything. The risk in what you are proposing is not that a rebuild fails; it demonstrably doesn't. The risk is that a rebuild driven by copying the old code **reproduces the old shape** — and that is not a hypothetical, because the specific tangles both reports diagnose are not ancient legacy. They were built five weeks ago, by that rebuild.

**And you are right about point 3, with a concrete example.** My own headline rename in report 1 — "call them Generators everywhere" — is exactly the pattern-chasing you were warning about. It touches everything, it fixes nothing a person is blocked by, and the actual defect it was standing in for (four hidden mechanisms decide what a layer draws, only one of them is in the picker) is untouched by renaming the word. I'd withdraw it. Two of the four consolidations I proposed fail the same test.

**What I would do now, in order:** ungate the border stage in today's Pyre this week, because it is three edits and it is the cheapest possible test of whether the whole shape/border idea holds up at all. Then fix the one over-loaded value that currently means five different things. Then, and only then, start the parallel build — **in the same project, not a clone** — with a design document that states the target model before a single line is copied. And carry across the nine big ported effects **untouched and shared**, never copied, because those ten and a half thousand lines are the only genuinely irreplaceable thing in the tool.

---

# Your nine points

---

## 1. Nothing that Pyre or 3D Shaper has ever produced needs to survive this refactor — delete it all if it makes the work simpler.

**Accepted, and it helps more than you probably expected.** It doesn't just remove a constraint; it removes a whole category of *bad reasoning* that had crept into both reports.

Here is what it changes concretely.

**The word "migration" leaves the vocabulary.** Report 1 said that retiring the old blast effect needed "a migration note rather than a straight delete", and it stated as a general principle that *retiring something is not finished until the saved files referencing it are migrated*. That principle is now cancelled, and I want it cancelled loudly rather than softened, because it was written as a general engineering rule and it would otherwise keep quietly re-infecting later decisions. Under your ruling it is simply a straight delete, and that particular one is now the single most conclusive deletion in the whole task — not because nothing uses it, but because **its own successor's source says, in writing, that it replaces it, and works through a forty-seven-row list of the differences one by one.** That is the author's own written supersession, and it needs no reference to any asset at all.

**The route's whole organising principle has to be rebuilt.** Report 2 sold its seven-step plan on the promise "six of the seven cannot break a saved effect." That is now a promise about nothing. The right ordering principle instead is **resolve the biggest unknown first, then follow the dependencies, then cost.** Reassuringly, the order barely moves — but one step moves earlier and one changes character entirely. The step where each generator opts into the new model gradually was chosen *only* so that nothing saved had to move; with that gone, a straight cutover is permissible and is materially cheaper, because there is no dual code path to write and maintain. **Keep the gradual approach only for the nine big ported effects**, where fidelity to an outside original is the whole point.

**And it makes the biggest question in this document a live one again** — see point 4.

**Two things the ruling does *not* touch, stated so nobody joins them up wrongly later.** First — and this matters, because "delete all the assets" and "the ported effects are the only irreplaceable thing in the tool" sit two pages apart in this document — **the ruling does not endanger the ported effects at all.** Their hundred-odd fidelity tests assert against an outside original, not against anything you have saved in Pyre, so the two facts are unrelated.

Second, and this one is a genuine question I can't answer for you: your ruling plainly covers *authored Pyre effects*. It is less clear whether it covers the **committed demo scenes**, and the places where Pyre output is consumed by other tools — the debris system, Mirage, the Zoe effect palettes. Those are other tools' demos and tests loading Pyre's output. **If "delete them all" includes those, say so; if it doesn't, they are a real constraint on any generator removal and should go on the list.** I've assumed the narrower reading everywhere in this document.

**One honest cost of the ruling, which you should know about.** Both earlier reports ended by admitting that nothing was ever checked by looking at Pyre running. Usage counts were quietly doing double duty as a sanity check — a crude but real signal that *something* worked well enough to be used. With that withdrawn, **direct observation is now the only empirical evidence left about whether a generator is worth keeping.** Every "keep, it's distinctive" verdict in either report is an inference from how much care went into building it, not from seeing its output. Before anything is cut, a rendered frame per generator, looked at by you, is now genuinely load-bearing rather than a nice-to-have. It is on the list in point 4.

---

## 2. How many saved effects use a particular generator or effect says nothing about whether that tech is any good.

**Accepted, and you are right about the mechanism.** The reports treated usage as a proxy for value, and the proxy was invalid for the reason you give: the library grew far faster than any one project could exercise it, so the counts measure your development speed, not the quality of anything.

Rather than just deleting the statistics, here is **what replaces them** — the evidence a verdict is now allowed to rest on, roughly in order of strength.

1. **Reachability.** Can a person get to this from the window at all, without being told it exists? Is the control even built, or is it gated out of existence before it can be drawn? This is directly inspectable, it is stronger evidence than a usage count ever was, and both reports under-used it.
2. **Correctness.** Is it actually broken — a hook nothing calls any more, a value hardcoded to zero that silently kills a branch, a message pointing at a window that was deleted?
3. **Documented supersession.** Does the source itself say X replaces Y? Cleanest evidence class available, and it applies to exactly one item in the whole tool.
4. **Technical distinctiveness.** Does anything else in the library produce this result? If it were gone, could you get the picture another way?
5. **Redundancy.** Are two picker entries actually one routine, or one data model with two drivers?
6. **Maturity.** Dial count, tooltip coverage, and decisively — does the author's own comment call it a stub or a proof of concept?
7. **Cost of keeping.** Not just its own size, but what it forces on everything else.
8. **Explainability.** Does keeping it force an exception into a rule you'd otherwise state in one sentence?

**Two rules for using that list, both of which the reports broke.**

**Redundancy is never an argument for deletion — only for fusion.** This caught a real mistake. Report 1 said to cut the vortex-field warp because "nothing uses it *and* it duplicates the swirl effect's authoring surface." Remove the first half and the second half points the other way: the two read *the same placed points* with a different driver, one static and one driven by progress through the clip. Deleting it removes a genuine behaviour and saves one class; fusing them removes the duplicate authoring surface, which was the actual complaint. **Verdict reversed: fuse the vortex field into swirl as a second mode.** Report 1 even wrote the right answer as an escape clause and then didn't take it.

**Never infer discoverability from usage, in either direction.** Both reports' "uncomfortable finding" sections did exactly that, and both sections are now void as written. But — and this matters — **the conclusion they were reaching for survives completely, and is stronger stated properly.** It does not need a single number:

> Pyre has **one picker and four different mechanisms** that decide what a layer draws. Three of the four have no entry in that picker at all. Two of its most capable stages are reachable only after making an unrelated choice first — the border only appears if you happened to pick a flat 2D shape, and grouping is an integer you have to make match across two separate layers, inside a panel labelled "Matte". The melt-into-a-blob control is offered on generators where the setting is never read at all. **All of that is verifiable by reading the window's own layout code, with no reference to what anyone has ever saved.**

That is the same conclusion, arrived at without statistics, and it's the better version because it's a fact about the tool rather than a prediction about behaviour. **Any new capability shipped through the same kind of surface — a mode buried in another panel, a number wired across layers, a control built only for the generators that already support it — will be equally unreachable no matter how good the architecture underneath it is.**

**The four verdicts that actually flip once usage is withdrawn:**

| Item | Was | Now | Because |
|---|---|---|---|
| The old blast effect | Cull, with a migration note | **Straight delete. Hardest-backed deletion in the tool.** | Its successor's own source says it replaces it, item by item |
| The vortex-field warp | Cull | **Do not cull — fuse into swirl as a second mode** | Same data, different driver; redundancy argues for fusion |
| The 3D playback viewer | Three-way choice: finish, demote, or cut | **Cut, or move it out of Pyre** | It draws nothing at bake time and calls itself a proof of concept in its own comments; finishing it is now the expensive option chosen for no reason |
| "Simplify the mask system rather than extend it" | Recommended, and labelled report 1's strongest argument | **Withdrawn — unsupported** | The sentence carrying it was a usage count. The *rename* recommendation in that section survives on its own merits and is still the best clarity-per-effort item in either document |

The pin-warp cull **stands**, and it reads better now: it had two arguments, and the technical one alone is sufficient. Its frame hook lost its only caller in the rename, so keyframed pins hold their first position forever — and keyframing is the entire point of the effect. Rebuilding the most elaborate authoring surface in the set, for an effect that would then still not animate, is a bad trade with or without a usage count.

---

## 3. Be careful that deprecating things and making u-turns doesn't become a goal in itself.

**You are right, the reports contain exactly that failure, and here it is by name.** I applied one test throughout: *what would a person have been trying to do, and been unable to do, that this fixes?* If the only answer is "it would read better", it's tidiness wearing a recommendation's clothes.

**Pattern-chasing — withdraw these:**

- **"Call them Generators everywhere."** This was report 1's headline rename and it is the clearest pattern-chase in either document. It's a global rename of one word across the whole tool plus its assets, its docs, and an external repository — and **nobody is blocked by the word "Shape."** The real defect it was standing next to is that four unrelated mechanisms decide what a layer draws and only one is in the picker. That is fixed by unifying the dispatch and the picker, which the rename neither does nor helps do. This project has *just* paid the bill for one package-wide rename, and the residue is still in the tree. Do the unification; call the field whatever you like.
- **Two of the four consolidations.** Merging the gem, box, pyramid and can into one "solid" with a mode was sold on the number of picker entries going down. But those are **four different pictures**, and collapsing four visible pictures behind one control's mode is precisely the structural pathology that makes Pyre's features unfindable in the first place. It contradicts the reports' own strongest finding. Same objection, weaker, for merging the star and the polygon.

  *(Run all four consolidations through gate question 3 below — "does it hide a picture?" — rather than through "is it one engine", because **the four solids are also one engine**, by the code's own comment. That is precisely why "one engine" is the wrong test. On the picture test: the **two height techniques** merge cleanly — same lighting routine, two feeds, one picture, and the choice of feed is exactly what a source selector is for. The **three jets** are the honest borderline: they are one engine with three sets of overrides, but they do produce three recognisably different pictures, so this one has to be argued as the small feature decision it is rather than shipped as tidying. My reading is that it survives — the three are presets of one thing in a way the four solids are not — but that is a judgement, and it's yours to overrule.)*
- **"Guard the word layer" and "fuse means three things."** Vocabulary hygiene. Worth a glossary paragraph and one better label on one control; not a three-way rename project.
- **Deleting the empty leftover folders.** There are three, not two — your project instructions say two and miss one under the test folder. Free, fine to do, buys nothing, and is not evidence of anything. It was listed beside real defects, which inflated it.

**Load-bearing — a person would feel every one of these:**

Ungating the border (a working feature that most generators cannot reach, with the control *not even constructed*, so there is no greyed row, no tooltip, no trace of its existence). Splitting the effect list so effects that cannot act aren't offered ("I added an effect and nothing happened" is the most common failure available). Fixing the one per-pixel value that means five different things in five places, one of which is a hardcoded zero that silently kills an effect. Culling pin warp. Fixing the melt-into-a-blob control being offered on roughly half the generator list where it is never read. Deleting the old blast effect. Promoting the three hidden techniques into the picker. And renaming the two masks to say what they actually read.

**The asymmetry is the useful part, and it generalises: every load-bearing item is a reachability or correctness defect — something a user hits. Every pattern-chase is a naming or counting item — something a reader notices.**

**So here is the gate. Five questions, and any future change to Pyre has to pass them before it removes or renames anything.**

1. **Name the person and the moment.** Who was blocked, and what were they doing when they hit this? If the only answer is "a future reader of the code", it's a comment, not a change.
2. **Would the old name still be true?** If the thing does what its name says and the complaint is that the word is used elsewhere too, don't rename — disambiguate where it's used. Renaming to relieve a collision costs a sweep of everything and buys a reader's convenience.
3. **Does it remove or hide a picture?** If a person could produce a rendered frame before and couldn't afterwards — *or could only produce it after one extra decision* — that's a capability change, not a cleanup, and it has to be argued as one. **Consolidation is subject to this test**, which is how two of my four fusions fail it.
4. **What's the residue budget, and who declares it paid?** Name the finish line before starting. Measured precedent: the last package-wide rename moved 366 files and left 96 stale identifiers, 7 misnamed test files and 3 empty folders behind. A change without a named finisher does not finish.
5. **Is the evidence a fact about the code, or a fact about taste?** Admissible: is the control constructed, is the hook called, is the value hardcoded, did the author write that this supersedes that. **Inadmissible: "nothing uses it", "it looks messy", "it doesn't fit the pattern", and "that would be fewer entries."**

A change that can't answer 1 and 5 doesn't get made. A change that trips 3 gets argued on its merits as a feature decision, by you, not shipped as housekeeping.

---

## 4. Clone the project, build the new app beside the old one using today's Pyre as a mold, and drive the whole thing from a real design document.

**The strategy is right. The whole-project clone is wrong. And you have already run this exact strategy once, successfully, in this project.**

That last part is the finding that reframes everything, so take it first.

**Five weeks ago you did precisely this.** On 23 July there is a commit literally titled *"parallel Pyre rework"*. It stood up a complete second Pyre — its own assemblies, its own namespace, its own copies of the big ported effects, its own tests — beside the original, which kept compiling and kept rendering the whole time. It ran **thirty-three days**. It carried an explicit converter whose own header says it never modifies or saves the source, and which reports per-layer what it had to drop or approximate. On 25 August it ended with a package-wide rename and the old one deleted outright. **The Pyre that both of my reports diagnose *is* that rebuild.** It is five weeks old.

Two conclusions follow, and they point in opposite directions.

**In favour of your proposal:** the failure mode everyone warns about with copy-don't-modify — two implementations alive, bugs fixed in one, drift — **did not happen.** Across the entire thirty-three-day window the old Pyre received essentially no feature work: one small layout commit on the first evening, one thumbnail-and-picker addition a few days in, and a handful of cross-cutting syncs. Empirically, in this repository, with you, the strategy works. That deserves saying before any objection.

**Against a naive repeat — and this is stronger than I first wrote it.** Of the four tangles the reports complain about, **two were created by the rebuild** (the border control built for only six generators, and the multi-mechanism dispatch). The other two — grouping expressed as a number matched across two layers inside a panel called "Matte", and the blob control offered where it is never read — **were inherited from the old Pyre and copied across on purpose.** One of the rebuild's own commits is titled, in as many words, *"Matte is per-layer in the layer list (Pyre1-style)."*

**That is the proof, not the counter-example.** Copying code carries its assumptions. A rebuild whose method is "use the old code as a mold" will re-mint the old model unless something external forbids it — and last time nothing did, so half the tangle walked straight across, deliberately, with a commit message saying so.

**That is the single most important sentence in this section, and it is exactly why your instinct about the design document is right.** The document is not paperwork; it is the only mechanism that prevents the copy from re-creating what it was supposed to escape. The rule is: **state the target model as prose and as a contract before any file is copied, and judge the copy against the contract rather than against the original.** Any copied routine that cannot be expressed through the contract is a finding, not a port.

### Why not a whole-project clone

You ran it in the *same* project last time, and that difference is the recommendation.

The clone buys exactly one thing: freedom to break the working project. Since the same-project route leaves the old Pyre compiling, shipping and untouched anyway, that freedom is worth very little. What it costs:

- **The compiler and the test suite stop seeing both implementations.** In one project, every change to a shared contract is caught the instant it's made. In a clone, drift is discovered at merge — the worst possible time. There are around 165 tests, of which roughly 104 exist specifically to guard the ported effects. In one project a single test run covers both renderers and can assert the new against the old frame by frame. In a clone the old one is in another process.
- **You can't put both windows on screen side by side on the same effect.** That's the fastest feedback loop available and the clone deletes it.
- **The merge back becomes an identity-collision problem instead of a rename.** Copying a folder *within* one project makes Unity mint fresh identities automatically. Copying between two clones of the same project carries the *originals*, so merging collides with the copies still sitting in the host. There's no clean fix: you either hand-regenerate dozens of identities or delete the host's Pyre first, which defeats the entire point of keeping the reference.
- **The reference copy freezes against a moving world.** Pyre depends on ZUI, the effect library, Chunks and the asset kit, all under active development right now. In one project both copies must compile against the same ZUI, so they stay in step for free. In a clone they diverge silently.
- **Practicalities.** A second copy is roughly 950 MB of project plus an 8.8 GB cache that has to be regenerated by a full reimport — hours, and again after every large sync. Two editors share one machine's compile pipeline. And the editor bridge is per-session and stateful, so with two Pyre-bearing projects open an agent that mis-points it edits the wrong tree and both editors recompile, with nothing guarding against it.

**Recommendation: build the new app in a new assembly and namespace inside this project, with old Pyre still compiling and runnable beside it.** Same strategy, same discipline, same converter pattern, none of the clone's costs. And the time to seeing something working is short: the *first* commit of the last parallel rebuild was already "shape section plus live renderer", and a completed three-slice prototype landed the next day. Call it a week to be safe; the precedent says less.

**Three amendments that make it strictly better than last time:**

1. **Do the cheap in-place fixes first, in today's Pyre, before starting anything parallel.** Ungate the border. Fix the five-meanings value. Split the effect list. These are days of work, immediately visible in the existing window — and critically, **they are how you find out whether the diagnosis is even right.** Ungating the border exercises the real border implementation against 23 generators that have never fed it. If that goes badly, the entire shape/border premise needs revisiting, and you'll have learned it for a week instead of a quarter.
2. **Do not copy the nine big ported effects — share them.** They are about 10,700 lines, already in their own assembly, already behind one small contract, and they are the only genuinely irreplaceable thing in the tool: their value is fidelity to an outside original, verified by a comparison harness against a source that still lives on this machine. Pull that contract out into its own tiny assembly that both the old Pyre and the new one reference, and those lines are **shared, never copied, never diverged, and never need a compatibility attribute.** One file, and it converts report 2's strongest surviving objection into a non-issue.
3. **Write the finish checklist on day one, and do the final switch-over as its own reviewed commit.** Last time the switch-over was a 366-file, ±180,000-line rename executed *inside* a fifteen-commit housekeeping batch. A change that size gets no review in a batch like that, which is exactly why the residue survived.

### What the design document must contain

One page. Everything else it does matters less than preventing the copy from re-minting the old model.

- **The one-sentence model.** What a layer *is*, in the new world, written before any code. If it can't be said in one sentence it isn't ready. Working draft: *a layer is one generator producing a substance, plus operations on that substance, plus a fill consuming it.*
- **Locked before a line is written:** (a) whether the existing plug-in contract is unchanged — verify by checking that two of the big effects satisfy the new contract *unmodified*, because this gates everything; (b) the one dispatch path, and what happens to the other three; (c) the capability contract — every generator declares what it produces and consumes, and **every control is always constructed and greyed out with a written reason when it can't act**; (d) the assembly, namespace, window name and a *dated end condition* for the parallel period, written down on day one; (e) a written list of what is deliberately **not** carried across, with a reason each — silence here is how things get carried by accident.
- **Measured before it's designed:** whether the border actually works on the generators that have never fed it; which generators refuse whole-buffer treatment; **a rendered frame per generator from today's Pyre as the visual baseline** (see point 1 — this is now the only empirical evidence left); and the current fidelity-comparison pass state, so a later regression is attributable.
- **Deliberately left open:** which fills become pluggable and in what order; extrusion; the final picker taxonomy — consolidation decisions are feature decisions you make by looking at pictures, not architecture to lock up front.

### Two things to fix before any of this starts

**Your project instructions are stale in two places, and one will actively mislead an agent.** They say the Pyre rename is "staged but uncommitted, ~209 staged renames." Nothing is staged; the rename was committed on 25 August. The caution is still correct — the tree carries around 129 modified and 72 untracked files — but for a different reason, and an agent obeying the stated reason may try to reconstruct a staged state that does not exist.

**The second stale line matters for point 4 specifically.** The instructions record an "open packaging gap: ZUI lives outside the package, so package tools referencing it only work in this dev host." That is no longer true — the folder outside the package now holds exactly two files, and all of ZUI's code moved inside the package some time ago. Worth correcting for its own sake, and worth knowing here because the clone-versus-same-project argument leans on ZUI being a shared dependency both copies must compile against.

**And the serious one: the branch this all lives on has never been pushed anywhere.** The entire current Pyre — the whole parallel rebuild — exists only on this disk. That is unrelated to your question and it is the largest single risk in the repository. **Push it before starting a second large rework.**

---

## 5. Is AgentHQ the right harness for a project this size — its own board, a group of tasks, or one huge to-do list — and should a persistent board overwatcher be built?

**Short answers: a group of tasks under one node. Not its own board. Not one giant to-do list. And no, don't build the overwatcher — the problem it's aimed at has a much cheaper fix that's already built and simply unused.**

**On the structure.** A board in this system is just a pointer at a project folder, so a separate board buys nothing and is the wrong unit besides — Pyre isn't a separate codebase. A node that groups tasks is the right fit, and **this project has already run exactly that pattern at this scale**: the port work was seventeen tasks under one node with phases and numbering, and it worked. One task with an enormous to-do list is what this very task organically became — thirty-one to-dos, three sessions, eleven agents — and it is visibly straining: a to-do has no room for a real sub-discussion, and everything bottlenecks through one task's read state. It's fine for a design conversation like this one; it's wrong for a build.

So: keep this task as it is until the plan settles, then file **one task per shippable slice** under the node. *(One exception to my own advice: if you do end up cloning the project after all despite point 4, register the clone as its own board at that time — that is genuinely what a board is for.)*

**On the context problem, which is the real thing you're pointing at.** It is real and it's now measurable. By the end of this round, a freshly dispatched session would have to read two reports and fifteen evidence files — comfortably over half a megabyte — before it could safely add anything. The dispatch brief supplies almost none of that; it hands over a task URL and generic protocol text.

**And the fix already exists and isn't being used.** The planning-document feature — the tabs where both reports are already filed — is never mentioned by the dispatch brief, so an agent only finds it if it happens to go looking. Two conventions close most of the gap for free:

1. **Add a "State" tab to the planning document, overwritten rather than appended each session**, holding a single page of current-best-understanding. That is the compaction point. The full evidence trail stays underneath it as the citation layer, not as required reading.
2. **Every task's description names which planning tabs to read first**, and whoever ends a work session refreshes the State tab, not just the handover.

**If you build one thing in AgentHQ, build this:** automatically inline a node's planning document into the dispatch brief when one exists. It's a small change — a few lines where the prompt is assembled, and the read function already exists — and it converts a convention that depends on the task-filer remembering into something structural.

**On the overwatcher: no, and the reasons are structural rather than a matter of taste.** The system has no primitive for a long-lived agent at all — every dispatched process is a fresh one-shot subprocess and is explicitly told so. A long-lived overseer would still hit compaction, so it does not actually escape the context problem; it just relocates it, and adds a stale mental model of a codebase changing underneath it. It is a single point of failure for a multi-month project. And it collides head-on with this project's standing rule that **two agents must never drive one Unity editor at once** — there is a real incident on record — which means an overwatcher could never safely run in parallel with you working in the editor yourself, which is most of when you'd want it.

**Try the two conventions and the one small feature first. Revisit the overwatcher only if reading overhead is still the bottleneck afterwards** — and it very likely won't be, because the overhead is caused by *nobody having written the digest*, not by the absence of a persistent process.

---

## 5b. Drawing outside the shape, and outside the frame, is allowed — the user is responsible for framing, though a warning preview would be useful.

**This ruling is worth more than you probably intended. It dissolves what report 2 called one of its four hardest objections, and it dissolves it more cheaply than that report proposed.**

Report 2 §7.3 worried about glows: several generators deliberately paint light where there is no shape, so if colour is confined inside a cut-out, all of that gets deleted. Its proposed fix was to add a second channel called *emission*, meaning "paint here even though there is nothing here."

**That was the wrong fix, and your ruling makes the right one obvious.** A channel that means "paint where there is no coverage" **is coverage under another name.** Adding it costs a second combining rule, a second thing every border has to decide whether to trace, and a permanent "which one do I use?" question in the interface — and buys nothing that the much cheaper blend-mode flag below doesn't buy better. The correct fix is one sentence:

> **Coverage is a soft field of unbounded extent, not a stencil.** It is allowed to be 2% a long way from anything you'd call the body — that's a halo — and it is allowed to be non-zero off the edge of the frame.

Nothing in the tool ever forced it to be a stencil. Coverage is already a smooth number in every one of these generators, and in most of them it's smooth *precisely because* the field was blurred before opacity was decided. The soft wide fringe you were worried about losing is already there. **Drop the emission channel from the model. That part of report 2 is superseded.**

Same ruling kills a second objection. Report 1 found that four effects deliberately draw outside the silhouette and get **clipped inside Pyre but not in a standalone stack** — the same effect, same settings, different picture depending on where you use it. Every conclusion that gated an effect on "it must stay inside the shape" should be dropped.

**Two things survive honestly, and neither is what report 2 named.**

**The frame edge is real code, and your ruling doesn't repeal it.** Every generator writes into a fixed-size buffer. Letting a shape's field genuinely extend past the frame needs either a padded working buffer or an accepted clip. That's required work, not a free consequence — **and it is a precondition for your own red-pixels idea**, because the renderer cannot paint out-of-frame pixels red if it never computed them in the first place. I'd note the warning preview is a genuinely good idea and cheap *once padding exists*; it is not cheap before.

**Additive light isn't expressible by coverage alone.** A halo is often *added* light rather than a translucent skin. Two overlapping half-covered haloes composited over each other give 75%; added they give 100%. This doesn't bite inside a generator's own field, where the accumulation already happens correctly upstream. It bites when a layer is composited, and the honest fix is small: **a fill declares whether it blends over or adds.**

---

## 6. The Shaper primitive engine should be the only way Pyre makes primitives, and its complexity must stay out of the way until it's needed.

**Feasible, and dramatically cheaper than either of my earlier reports implied. Four carve-outs, and one UI rule that is genuinely the load-bearing half of the deliverable.**

**The size, measured.** Shaper's entire silhouette engine — eight primitives, the bulge warp, the transform maths, all four combine modes including both soft ones, and the nested-tree compiler with its depth and complexity guards — is **98 lines**. Extrusion and bevel add 26 more. It's pure arithmetic: no browser, no canvas, no Unity objects. Report 1 said "re-implementing Shaper's whole renderer", and it is meaningfully smaller than that, because the genuinely big part — Shaper's own rasteriser, materials and lighting, comfortably larger than the silhouette engine — is exactly the part you do *not* want, since Pyre has its own fill system.

**Two honest qualifiers on that line count**, because it is a raw count and not a measure of the work. Shaper is a single-file web app and this block is extraordinarily dense — around 75 characters a line, 92 statement separators across 98 lines, one line running to 311 characters — so a conventionally formatted port in a normal language is materially larger. And it's a contiguous range, not a dependency closure: it pulls in at least one helper from elsewhere and includes a few lines that aren't silhouette work at all. It is still small. It is not 124 lines of typing.

**The seam is already there.** Almost every one of Pyre's flat shapes is already an inverse-transformed point test — "put this pixel into the shape's own frame, then ask if it's inside." Shaper's evaluator has exactly that signature. **Pyre's disc and Shaper's ellipse are literally the same function written two ways.** So the case you insist must stay cheap — a plain disc — costs the same arithmetic it costs today. *(Three don't fit the pattern: the disc has a separate fast path that does the same thing by different code, the imported sprite is a texture fetch, and sparkle isn't a point test at all — which is the carve-out below.)* There is no quality or speed argument against routing the disc through the general engine, and there are two free wins: proper anti-aliasing, which Pyre's built-in shapes currently do not have at all, and one honest definition of "how far across this shape am I" — which today is assigned at fourteen places in seven different forms, several of them genuinely incompatible with each other.

**Four carve-outs the ruling must name explicitly, or it deletes things by accident:**

- **Sparkle is not a primitive.** It's a per-frame stochastic twinkle keyed to the particle's own age. Shaper's shape stage has no time dimension whatsoever. Read literally, "Shaper handles all primitives" deletes a shipped effect. **It belongs on the fill side**, which is a promotion, not a demotion.
- **Streak is a rectangle plus a placement policy** — anchor bias, an axis that faces the swarm's direction, two independent envelopes, and the only per-shape special case inside the swarm loop. The rectangle ports trivially; the policy stays on the layer. One specific loss to watch: today the swarm can scale a streak's *length only*, not its width, and a general shape has one uniform scale.
- **The star and the polygon get worse, not better.** Shaper's star is a hardcoded five-lobed shape with no parameters at all, against Pyre's four animatable dials, and there's no arbitrary N-gon — its hexagon and octagon are hand-fitted approximations. The fix is a handful of lines each, but it has to be **scheduled**, not assumed.
- **The lit 3D solids must survive separately.** Shaper's "3D" is a height map rendered from **one fixed, unrotatable viewpoint** — its *lighting* rig is fully movable, with several placeable lights and real shadows; it's the camera that doesn't exist. Pyre's solids do real 3D rotation of real geometry with proper back-face culling. That's a structural gap, not a quality gap, and no amount of Shaper work closes it.

**How the shape should be stored.** Not as a baked picture — that path loses sub-pixel edges under the swarm, loses *all* per-particle shape animation (a star whose arm length changes over its own life, a crescent whose bite opens), and Pyre already has a cautionary example of importing external content as a shape: the 3D playback viewer, which was given a full editor preview and then never drew anything at bake time. **Port the evaluator into the runtime as pure arithmetic**, with the shape stored on the layer itself. This is forced anyway: Pyre renders every frame at runtime in C#, so whatever the primitive engine is, it has to exist there. Reusable named shapes as a shared asset are a good later addition, but shipping that first adds a load path, a picker and a broken-reference failure mode to *the simplest case*, which is precisely what your ruling forbids.

**And so: export is off the critical path, and I'm contradicting report 1 directly.** That report said "until export exists, no integration of any shape is possible." That's true for importing a finished artefact — the model it recommended and you've now reversed. Under "Shaper's engine becomes Pyre's primitive engine", **nothing crosses a process boundary at all**, at author time or run time. What moves is a small, dense block of arithmetic, transcribed once, by a person — no file format, no importer, no runtime dependency on the browser or its server. Shaper survives as its own tool and a design source, and never needs an exporter.

### The UI rule you asked for

You said the disclosure design "must be a UI guideline." Agreed — and I checked: **the project's UI guide has no progressive-disclosure rule today**, though it has four rules that constrain one, and the codebase already ships this exact pattern three times over (the fill control that's a plain colour swatch until you change its mode; the box that hides opt-in controls behind a gear; the attribute that hides a dial while the swarm is off, so the card never shows a dead control).

Concretely, what you described works like this. **A new layer is one ellipse with equal radii and one flat colour — a disc, by identical arithmetic.** The shape box shows what it shows today and not one row more. The existing picker's meaning changes from "pick one of eighteen forms" to "pick one of eight primitives", and the primitive's *own* parameters — a star's arm count, a polygon's sides, a rectangle's rounding — **stay right there at the top level**, because they are what makes it that shape. One thing is added: the picker row gains a trailing **"Edit shape…"**, and the picker's caption reads the resolved name — `Disc` for one part, `Disc + 2` for three. **Nothing on screen ever says "Shaper", "CSG" or "component".** Opening it reveals a component list *below* the existing rows, which with one component is a single collapsed card header plus an add button — two rows, one of which is a button, never a panel. It lives inline in the window's left control pane, so it structurally cannot move the preview.

The rule, phrased for pasting into the guide:

> ### Progressive disclosure — complexity must earn its space
>
> When a general mechanism replaces a specific one, the general mechanism starts in the specific one's clothes.
>
> 1. **A generator's default state is its simplest useful one, and at rest it costs ZERO extra rows versus the control it replaced.** If a general shape engine's fresh layer shows one more row than the old dedicated disc did, the disclosure design has failed and the replacement is not ready to ship.
> 2. **The next level opens from the control that IS the simple value — never from a separate "Advanced" toggle or a mode switch.** There is no simple-versus-advanced mode; there is a thing, and it can be opened.
> 3. **Each level adds ONE row at rest, not a panel.** *(This is the existing Card-layout rule 5 — decide by counting fields, so new one-field kinds get it free.)*
> 4. **The collapsed control must NAME what is hidden inside it** — `Disc`, `Disc + 2`, `Fill: gradient`. A disclosure that hides its own existence is not disclosure; it is a lost feature, indistinguishable from the feature never having been built.
> 5. **Opening a level must not move the workspace.** Grow the control pane, never the canvas side. *(Existing "Stable workspace" rule, applied — reserve the opener as a fixed-height, non-wrapping row.)*
> 6. **The opener is either the inline choice itself, or is named for its destination with a trailing ellipsis.** *(Existing "Label = action" rule, applied — never a bare action verb that merely opens a place to act.)*
> 7. **A parameter that identifies the thing stays at the top level; only parameters that COMBINE things go deeper.** A star's arm count is what makes it a star — it stays where the star is chosen. How that star is fused with a second shape is composition, and that goes one level down.

Rules 3, 5 and 6 restate rules already in the guide and should be written as cross-references rather than as new text. Rules 1, 2, 4 and 7 are new. *(Rule 4 was originally proposed with a justification built on usage counts; I've rewritten it to stand on its own, per your point 2.)*

**The honest counter-argument, at its strongest.** Rule 7 is the single decision most likely to be got wrong, and getting it wrong makes authoring *slower* than today for every shape except the disc — because if a primitive's own dials go one level down, choosing a star becomes two clicks deeper than it is now, and "as easy as it is now" would be true only for the disc.

The other real risk is cost, and it's a cost class Pyre has never had. Pyre evaluates every shape dial per particle at that particle's own age, so a composed shape whose parameters vary per particle can't be compiled once — the tree would be rebuilt per particle per frame. An eight-part shape across 200 particles is roughly 640,000 primitive evaluations plus 200 tree builds, per layer per frame, where a disc today is about 80,000 square roots. **The caching that rescues the static case does nothing for that one, and there is currently no budget dial, no complexity cap surfaced to the user, and no warning.** Shaper itself needed two guards for the same reason and its own code says why. There is a plausible mitigation — separating the static structure from the per-particle parameters, so only the numbers change — but **it has not been prototyped, and it is the load-bearing assumption behind "the simple case is free."** Prove it early.

---

## 7. Some generators can't fit the shape/fill/border split because the fill and the shape are calculated in reference to each other — unless there's a way to actually split them.

**There is. And the premise turns out to be false: not one generator is genuinely fused. Report 2 §7.1 — the passage you couldn't follow — was wrong, and I'd rather replace it than refine it.**

### What is actually going on

A big Pyre effect does not draw a picture and then colour it in. **It makes weather.**

Before a single colour exists, it has built an invisible cloud of numbers over the canvas. At every pixel it knows things like *how much stuff is here* and *how hot it is*. That cloud is real; it sits in memory as its own array, complete, before any paint exists.

Then, right at the end, in one loop, it does two things to that cloud — and this is the only place the alleged trouble lives:

1. **It decides how solid each pixel is.** Not from a colour — from the numbers. *"Below this much heat: nothing. Above this much: fully solid. Fade between."* Call that the **edge rule**.
2. **It decides what colour each pixel is.** It turns a heat number into a position along a ramp and looks up the colour there.

Two decisions, one loop, one pile of numbers — **and they never read each other.** The edge rule never asks what colour came out. The colour lookup never asks how solid the pixel is. They are two people reading the same thermometer and writing down different things.

**That is the entire "entanglement". It isn't that the shape is made of the colour. It's that the two rules were written on the same sheet of paper.**

In **four** of the nine big effects it is *only* that: you could replace the whole palette with a random one today and the outline would come out bit-identical. In one of them the two rules don't even read the same number — the transparency comes from the raw heat, the colour from a separate cooled copy. Two sheets, two rules, zero contact.

In the remaining **five** — **seven**, once you add the two fire simulations — there's one real complication, and it's worth understanding because it's the good news in disguise. In those, the ramp you author has a **transparency slider on each colour stop**, and the generator multiplies that into the edge rule's answer. So yes — changing the ramp genuinely can change the silhouette. But look at the *shape* of that: it's a **multiplication of two independent factors**, not a blend of two things that have become one. You pull it apart with scissors and put it back with nothing lost.

**Mathematically fused: zero of eleven.** I went looking for one specifically.

**One caveat, so the claim isn't read wider than it was tested.** All of the above is about the eleven big field-based generators. The simple stamped shapes have their own, smaller shape-to-colour coupling that this doesn't cover: the swarm shades a particle by its depth, multiplying its colour directly. That's a shape-derived number driving paint, and under the split it has to become a proper *depth* sheet or the effect quietly vanishes. It's small and it's solvable — but it's a real item on the list, not a nothing.

### The design — "the shape keeps the edge, the fill gets a veil"

Three parts. That's the whole thing.

**Part one — the shape publishes its weather.** When a generator finishes, it doesn't hand over a cut-out. It hands over a small stack of transparent sheets laid over the canvas, one number per pixel, each with a name everyone understands: *how much stuff is here*, *how hot it is*, *how sooty it is*, *how far this pixel is from the edge*, *how old this pixel's stuff is*, *which way the surface faces*. Not every generator has every sheet — each declares what it has, and a fill wanting a sheet the shape hasn't got is **greyed out with the reason shown**, not hidden and not silently broken.

**Part of this is already built, and it's worth being exact about which part.** Eight of the nine big effects already hand out sheets through machinery that works today — built so the fidelity-testing harness could compare their numbers against the originals'. Nobody except that harness is allowed to look at it. But what they publish is a **subset**: how much stuff, how hot, how sooty, and where on the ramp. **Distance-from-edge exists on one generator only, and as a blend weight rather than an actual distance. Age and surface-direction are published by nobody at all and would have to be built.** And only four of them currently publish coverage itself.

So: **exposing the mechanism is promotion, not construction — but two of the six sheets are still construction.** No prior report noticed the mechanism existed at all, which is the good news; it is less finished than "it already exists" implies.

**Part two — the shape keeps the edge rule.** This is the part report 2 got right and it's essential not to give away. The edge rule sounds like a technicality; it isn't. It's what makes a jet read as a **tongue** rather than a smear, and a lightning bolt read as a **bolt** rather than a bright fog. It's tuned against numbers only that generator knows the range of. It is as much that generator's identity as its geometry.

So the shape keeps it, and can therefore always answer, **by itself, with no paint anywhere in sight**: *here is how solid I am at every pixel.* Everything downstream needs that answer to exist before paint does — a border needs an edge to trace, a group needs to know what each member contributes, a preview needs to show you something before you've picked paint, a swarm needs a rule for how two overlapping copies combine. And per your ruling in 5b, that coverage is deliberately **soft and unbounded**: allowed to be a faint trace far from the body, allowed to run off the frame. **A fog, not a stencil.**

**Part three — the fill is a paint recipe plus an optional veil.** A fill says two things. *"Which sheet do I read, and how do I turn its numbers into colour?"* — read the energy sheet, curve it, look it up in this gradient; or read the edge-distance sheet and make a rim; or ignore every sheet and churn my own plasma. And: *"Do I want to thin the paint anywhere?"* — that's the **veil**, a transparency the fill varies per pixel which **multiplies** the shape's coverage and never overrides it.

A fill with no veil is saying *"I have no opinion about the silhouette; the shape is in charge."* That's the default and it's what most fills should do. A fill *with* one can make a solid shape wispy or lacy — and that matters, because **seven of the eleven generators already do exactly that today** (eight, if you count the imported-sprite one when tinting is on), via the transparency column on the ramp you author. The veil isn't a new power. It's the existing power, named and made deliberate.

> **The picture to remember: the shape is fabric cut with a soft, frayed edge. The fraying belongs to the shape and nobody may take it away. The fill is what the fabric is dyed with, plus an optional gauze you view it through. Re-dye it any colour, print any churning pattern on it, and the frayed edge is still exactly the edge that generator is famous for.**

**Why not report 2's proposal.** It said the opacity rule travels with the shape, full stop — and then admitted its own cost: *"a plasma fill cannot make a solid shape wispy."* That isn't a limitation, it's a **regression**, because seven generators can do that today. The veil gives report 2 everything it wanted (the shape can always speak for itself) at no cost at all. And since its stated motivation is false for four of the nine big effects it claimed to cover, it should be superseded rather than refined.

**Why not the other obvious option** — the shape hands over raw numbers and the fill decides everything including transparency. Two reasons. A shape would then have **no silhouette until paint is chosen**, so groups, borders and previews all break, and "what will this do?" becomes "it depends what you put on it" — which is the exact failure this whole exercise exists to abolish. And more damningly: the edge rule is **calibrated to number ranges only that generator knows.** Hand a generic plasma fill the raw energy of any of them and you get a hard-edged blob or a blank canvas, every time, until you've hand-tuned four numbers you have no intuition for. That option makes every fill swap a calibration exercise. The veil keeps swaps free by default and calibration optional.

### Your headline example: a fireball wearing churning plasma

**Two halves work fully. The third half works but shouldn't be the default. And there's one way to be disappointed, which is worth knowing in advance.**

**The paint churns on its own clock — works, and it's among the cheapest things in this document, though not quite as cheap as report 2 claimed.** Here is the exact state of it. Pyre's fill type takes a clock *and a spatial sample point*, and **every single Pyre call site passes that spatial point as zero.** That's why every spatial and textured fill mode — including the noise texture that is precisely a churn — collapses to a single sample and cannot churn at all. Feeding it a real point lights the whole family up, **and the number it needs is the same shape-local coordinate that point 9 needs** — so this is one change serving two purposes rather than two independent cheap wins.

The clock half is genuinely separable in the ramp type, which already distinguishes "where am I on the ramp" from "what is the ramp doing over time". **One honest catch:** in the plain over-life mode those two are currently the same single value, and prising them apart means changing a type that other Laubrary tools also use — so that part is a small API change, not purely a call-site change. With those done, the plasma churns at its own speed inside the fireball's silhouette while the silhouette boils at its own. **That is the picture you described.**

**The silhouette keeps its identity — works, fully.** Because the veil defaults to fully opaque, the fireball's boiling edge, its puffs, its rim falloff are untouched by the paint. Swapping to plasma does not turn your fireball into a soft ball.

**The churn eating into the edge — works, one checkbox, don't default to it.** That's the veil set to the plasma's own density. Available. But the result is a silhouette that's the product of *two* turbulences, and in practice that reads as noisier rather than more alive.

**The way to be disappointed:** a plasma fill that reads **only its own noise** and ignores the shape's energy sheet gives you **a fireball-shaped hole with plasma behind it.** Flat. A decal, not a burning thing — because all the internal structure (hot core, cooling rim, soot) was in the sheet the fill declined to read. So the rule has to be on the tin from day one: **a good material fill reads the shape's energy and uses its own pattern as a modulation, not as the whole signal.** A fill that reads no sheets at all is a *sticker*, and the interface should call it that.

### Does it actually make them more valuable? Yes — but unevenly, and the unevenness is useful

Your claim was that flexibility would rocket. It's true, **and it's concentrated**: it rockets for the generators that have a **second physically meaningful sheet**, and merely improves for the single-sheet ones. That's a sorting rule for the build order.

- **The explosive jet family — the single best argument for the whole exercise.** It publishes two physically meaningful sheets, heat and soot, and its fracture, chunk, dust and spark populations all deposit into those *same two sheets* with a per-piece soot tint. So a fill that crossfades two materials by soot gets **shrapnel that is molten at the core and cooling at the rim, for free, with no new generator.** Metal, glass, ice, ash — all reachable. Your "a jet's field driving a metal fill" instinct is right, and the sheet that makes it work already exists and is already published.
- **The arc effect** already deposits coverage and energy as two genuinely separate, separately-blurred sheets. "Where the bolt is" and "how hot the bolt is" are already independent authored quantities. A chrome bolt, an ink bolt, a crack-in-glass — immediately reachable, and it's nearly free because it's already the model.
- **The plasma bloom** is the only one already crossfading two palettes by a *geometric* sheet, so a fill reading edge-distance gets edge-aware paint for free. Notably, "coverage plus distance-from-edge" is exactly the input pair Shaper's own interior-fill library wants. Two projects arrived at the same two sheets independently.
- **The torch** publishes both raw and height-cooled heat, so a fill can choose. "Flame-shaped anything, anchored to a ground plane" becomes a real nameable look.
- **The inferno** is the one whose split *fixes something already broken*: its smoke grey is **hardcoded** today and only the fire half goes through the fill at all. Splitting makes the smoke paintable for the first time. It also has a pseudo-surface-direction, so a fill could put a genuine material on a boiling cloud.
- **The energy projectile: modest.** Any fill is largely the re-skin you already get by editing its ramp. What's genuinely new is spatial and textured fills, which the ramp can't do, plus the second clock.
- **The fire simulation: real but cost-bound.** "Anything that behaves like fire" — flowing lava, moving liquid, smoke that's actually water. The ceiling is that reaching a frame means replaying every frame before it.
- **Honest lows: the old blast effect and the fireball gain almost nothing**, and in both cases for the same documented reason — a better generator already publishes the same sheets. Split them for uniformity, not for value.

### Nothing is irreducible — retract that claim

Report 2 nominated exactly one generator as genuinely inseparable: the imported-sprite one. **It's wrong, and it's wrong in a way that matters.** That generator's coverage is the artwork's transparency and its colour is the artwork's colour — an all-but-trivial separation, and one of the easiest in the library. *(With one extra term: when tinting is on, the fill colour's own transparency multiplies the coverage — which is exactly the same product form as the seven above, making this an eighth generator whose fill can already thin it.)* What's actually true about it is different:

> It is the only generator whose **fill half is authored data rather than a rule.** Every other one's colour half is a *function*, and a function can be replaced by another function. Its colour half is a *picture*, and pictures don't parameterise.

That's a statement about **replaceability**, not separability, and the two got conflated. The distinction is worth insisting on: if "some generators just cannot do this" stands as a precedent, it will be cited by the next awkward case. Report 2's *conclusion* about that generator — promote it to a shape provider, land Shaper imports there — is right and should be kept. Its reasoning should be dropped.

### Your escape hatch: yes, with a one-line contract

You said you're open to keeping generators that simply aren't shape/fill/border. **Agreed — but only with a contract, and without it, it's exactly the hole you're worried about.**

The failure mode: if a monolithic generator publishes *nothing*, then every downstream stage — border, group, swarm, effects, preview, badges — needs an "…unless it's monolithic" branch, and each branch is a place where "what will this do?" becomes *"it depends"*. That isn't a valve, it's a second undocumented model growing inside the first. Within a year, monolithic is where anything hard goes.

**The contract is one line: a monolithic generator MUST publish coverage, and MAY publish nothing else.** That single obligation is nearly free, because coverage is the only thing anything downstream actually requires — the border traces it, the group unions and carves it, the swarm composites it, the preview shows it. **All eleven already compute coverage internally; four already hand it out.** So for most of them it is a matter of exposing a number they already have, not deriving a new one — but it is not literally zero work, and I'd rather say so than sell it as free. With that, a monolithic generator keeps everything: all buffer-running effects, group membership, swarming, borders. It loses exactly one thing — its fill picker shows one named entry ("the artwork", "its own palette") rather than a choice.

**The second half of the policy matters as much.** Monolithic must be a *declared reason*, never a *declared exemption*, and there are exactly two legitimate reasons: **its default fill is authored data rather than a rule** (permanent, and fine), or **it hasn't been split yet** (temporary, and it must say so, so a compliance pass can count them). Any third reason is a design bug, and the review question is always: *"which sheet would a fill have wanted that you decided not to publish?"* If there's an answer, it isn't monolithic — it's unfinished.

### One trap, and it's bigger than report 2 said

Report 2 warned that two generators anti-alias *after* colouring — evaluating at higher resolution, pushing through a hard-banded palette, then filtering down — so colouring a downsampled field would give you soft band edges where the original had crisp steps. **Verified: it's three, not two, and the one it missed is one of the two most affected** (five hard colour bands, and it was omitted; meanwhile one of the two it named blends continuous ramps and is the *least* sensitive of the three).

**The design handles all three completely, at a known price:** a shape declares its native resolution, the fill is evaluated at that resolution, and the single downsample is the very last thing the pipeline does. All three then come out bit-identical, because the only thing that changes is *who* writes the colour — and all three already exit through the same shared downsampling function, which was written specifically to defer quantisation to one point. It stops being each generator's last line and becomes the pipeline's last line. *(One caveat for whoever builds it: one of the three only takes that shared path on a small canvas. Above about 128 pixels it stops supersampling and writes directly through a hand-written branch of its own, which will need the same treatment.)* **The honest price is memory: those buffers get allocated at 4× to 9× the size, paid only by the three generators that ask for it.**

---

## 8. Why can't swarming be applied to every generator — if a thing can be generated once, it can be generated many times with different parameters?

**You're right, and the reports were sloppy with the word. "Run the generator N times with per-instance offsets" isn't a proposal — it is already the implementation.** All nine big effects loop over the swarm; seven of them do it by running the generator once per instance with its own origin, scale and seed, accumulating into one shared plane, and the other two loop it to build event descriptors instead of drawing directly. *(At the code level there are fewer loops than that — five, since three of the effects share one engine — which is itself a small argument that the pattern is already general.)*

**No generator makes it impossible.** Three come close, each for a fixable reason:

- **The fireball** hardcodes its source at the canvas centre, and its parameter block **has no position field at all**. That's the entire blocker: **two missing numbers**, not a limit.
- **Fire without its emitter option** uses its own fixed emitters instead.
- **The 3D playback viewer** renders nothing at bake time, so there's nothing to swarm.

**Your alternative — "many seeds that interact" — is also already built, several times over.** Fire can take N emitters into *one* fluid grid. One of the blast effects places blasts that **know they are siblings** and bias each other's fire-versus-smoke character across the sequence; a second is sibling-aware for scale. And the melt-into-a-blob modes read the whole cloud as one field. So the two behaviours you intuited both exist; nobody has ever named them as two things.

**The buckets, counted:** of 29 generator behaviours, **21 (72%) already comply** with your model; **5 (17%) have a better native interacting-seeds path**; **2 need real work** (the two stateful simulations); 1 is out of scope. So your model is already how roughly three quarters of the library works, has a strictly better alternative for a sixth of it, and needs genuine work on exactly two — which are the same two, for the same reason, as everything else awkward in this tool.

### The real defect isn't a limit — it's an under-specified contract, and it hides a probable bug

**Every instance gets an index, but not a seed.** So each big effect invents its own seed from that index — five of them share one arbitrary constant, one uses a different one, and one invents no seed at all. That's why "different parameters per instance" is folk practice rather than a contract. Adding a seed field breaks nothing — each can keep its current expression until migrated.

**And here's the one that matters: only two of the seven give an instance its own clock.** The rest read the *shared layer clock* and treat an instance's own lifetime purely as an alive-or-not filter. So instances **pop in mid-animation instead of starting their own lifetime** — which is exactly the opposite of what you assumed swarm did, and what you'd want. **That is arguably a latent bug, not a missing feature**, and fixing it is about one line each — genuinely one line for most of them, and one small conversion for the one that counts in whole frames rather than fractions. It does change output, so it's a change worth making deliberately rather than by accident.

### The honest counter-argument, and it's narrow

**For the two stateful simulations, "swarm as a wrapper around N full renders" is asymptotically wrong and no tuning fixes it.** They reach a frame only by replaying every frame from zero, and any edit forces a cold replay. So one bake is already quadratic in frame count; N instances makes it N times that. At a typical canvas with 24 frames and 200 particles that's roughly **236 million cell updates against 1.2 million** — and every twiddle of a dial pays it again, with no checkpointing in place. **For those two, the wrapper must be refused and "many emitters into one grid" offered instead** — which costs the same regardless of N and is already built for one of them.

**A smaller one:** forcing the simple stamped shapes into a merged-field model would make them worse. Per-particle transparent compositing is correct for discrete objects and no field-accumulation rule reproduces it. So "one swarm model for everything" has to mean **one contract with two combining rules** — stack on top for discrete stamps, merge for fields — not one rule.

**What is *not* a valid objection, and shouldn't be recycled from my earlier reports:** that the big effects "can't take the shared controls" (half wrong — they do receive a per-instance size multiplier, which seven of them use, and an orientation, which four of them use; what they don't get is the *layer's* size and spin, which is a different and much smaller gap); that "simulations have essentially no swarm" (wrong for fire, which has a full native multi-emitter path); and that swarm is three roles (it's five distinct implemented behaviours).

**The right framing, which nobody has written down:** the question isn't *"can every generator be swarmed?"* — essentially all already are. It's **"what does an instance receive, and does it merge with its siblings or stack on top of them?"** Two axes, both already present in the code, neither currently named.

---

## 9. Same question for effects: if every effect touches pixels and every generator makes pixels, why can't they always be used?

**You're right again, and the code proves it harder than my earlier reports did. The tool already contains the proof.**

**Six effects read *nothing* from the generator at all, and three more read only trivia** — contrast, brightness, saturation, posterise, colour tint and colour replace take no per-pixel context whatsoever; colour remap reads only the age; wipe reads only a per-shape seed, and only in one of its nine patterns. All nine are dead on the two fire simulations, the playback viewer and the height consumer **for exactly one reason: the recolour step is a callback fired inside each generator's own drawing loop, rather than a pass over the finished picture.** That's wiring, not a requirement. **This is your point, and it's the cheapest win in the whole task.**

**The tool already does this correctly one stage over.** Whole-frame effects run *outside* the generator dispatch entirely — for every layer, including the viewer that draws nothing at all. **Universality already works. It just stopped one stage short.**

**The 41 effects, sorted honestly:**

| | Count | What it takes |
|---|---|---|
| **Universal today, correctly advertised** | 11 | Nothing — with one exception: one of the eleven is offered in the standalone effect stack's menu and that stack has no handling for it at all, so it silently does nothing there |
| **Universal-capable today, mis-advertised** | 9 | **Give the four generators that lack a per-pixel stage a buffer fallback** — and leave the existing in-loop stage exactly where it is (see below) |
| **Universal via a buffer fallback, at a named cost** | 7 | Quantisation and one resample per chained warp — mitigable by folding the chain into a single map, which the tool already knows how to do in one place |
| **Universal once the generator publishes two extra sheets** | 13 | *(below — the highest-leverage change in the report)* |
| **Genuinely stuck** | 1 | Edge warp |

**One important correction to how that second bucket gets fixed.** An earlier draft of this section said to *move* the recolour stage out of the draw loop, at no behavioural cost. **That is wrong and it would change output on every layer where particles overlap.** Today the stage runs on each particle's colour *before* that particle is composited, and it is allowed to drop a particle's contribution entirely. A pass over the finished picture is not the same thing: non-linear operations like posterise and contrast give different answers applied once to the composite than applied per particle, even linear ones differ once anything lies underneath, and "drop this pixel" stops meaning "drop this particle's contribution" and starts meaning "drop the whole layer's". **Add a buffer fallback for the four generators that have no per-pixel stage; leave the existing in-loop stage untouched.** Same result for your question, none of the damage.

**The 13 need two numbers, and both are already computed and thrown away.** They read a per-pixel **shape-local coordinate** (where am I within *this* shape, and how big is it) and a **stable per-shape seed**. Both exist today, per pixel, and are discarded at the end of the draw loop. Publish them as sheets alongside the picture — and the mechanism for carrying a sheet through a warp already exists — and **all 13 become buffer-runnable with their present per-shape meaning intact, swarms included.**

**There is a third precondition, and it belongs here rather than only under 5b:** the buffer those effects run on has to be **padded** by however much room each effect asks for. Pyre never asks — it never even tells an effect what rectangle it is working within — which is the same missing call that makes four effects get clipped in Pyre but not in a standalone stack. So "universal effects" and "let things draw outside the frame" are not two projects. They are one.

### The one thing that genuinely changes, and it's about swarms

There's a real cost to universality and nobody had costed it. Report 1 said a warp means two different things in Pyre versus a standalone stack — inside Pyre it bends the shape as it's drawn, centred on *that shape*; in a stack it resamples the whole canvas, centred on the canvas.

**Verified, and worse than reported.** On a **single centred shape the pivot is already identical** and only the reach differs — by the ratio of the canvas's half-width to the shape's radius, so roughly double for a big shape and eight times or more for a small particle. But on a **swarm, N pivots collapse into 1**, and no dial recovers it. For a 200-ember explosion, "each ember wobbles" and "the whole cloud wobbles" are different effects, and only one of them is what the dial says. **Publishing the shape-local sheet is precisely the answer to this** — but if that isn't built, universality for those ten warps is a downgrade dressed as a feature.

### Three corrections, two live bugs

**There are four live attachment stages, not five.** The "edge" stage described in report 1 has **no consumer at all** — nothing calls it. Report 1's five-stage framing should be corrected to four.

**A live bug in the standalone effect stack.** It pivots shape-centred warps at the canvas's **top-right corner** — a value that is used everywhere else as an offset *from* the canvas centre is being passed as an absolute position. It affects ten of the seventeen warps. Report 1's "centred on the canvas" understates it: it isn't centred on anything sensible. Found by reading, not reproduced — **confirm with one rendered frame before touching it**, because the fix changes saved stacks.

**A second live bug, and it's the one that gates everything above.** Pyre never tells an effect what picture rectangle it is working within, so any effect that normalises a position falls back to measuring against the raw buffer. Harmless today, because Pyre never pads its buffer either — and immediately live the moment it does, which is exactly what universal effects and your out-of-frame ruling both require. Fix it in the same change, not afterwards.

**And one of report 2's claims needs withdrawing.** It cited one generator's opt-out flag as the codebase's own proof that resampling the finished picture is not equivalent to warping sample by sample. Read properly, that flag is a **double-application guard** — it stops the same warp being applied twice — not an aesthetic verdict. So **that equivalence question is still open, not settled.** It should be tested rather than assumed in either direction.

### The honest counter-argument

**One effect would be *useless* universally rather than merely impossible, and this is the strongest single reason not to treat universality as an unconditional goal.** The edge warp exists precisely to roughen the rim *without* touching what reads the shape's true geometry — the fill, the gradient core, the hole sizing — giving a jagged-but-smoothly-shaded look that a whole-frame warp cannot, because a whole-frame warp drags the paint along with the outline. A buffer version can't separate outline from paint, so it would produce exactly what an existing wobble effect already produces. **Making it universal would ship a duplicate under a name promising something else — worse than admitting it's generator-specific.** Give it a real host, or delete it. Don't universalise it.

**And one objection I checked and will not make.** Report 2 warned that each warp becomes three effects (bend the shape, bend the fill, bend both). That explosion comes from the **shape/fill split**, not from universality. A universal buffer pass bends shape and paint together — exactly what happens today — and adds no dials at all.

**One genuine tension you should know about before adopting both proposals.** Universality *fixes* the ordering problem; report 2's "split the one effect list into four typed lists" *creates* one, because the standalone stack keeps strict authored order and deliberately flushes so later effects see what earlier ones produced. **Don't adopt both without deciding which wins.** My inclination: keep one ordered list, and use the badges to grey out what can't act — you get the predictability that the four-list split was for, without losing expressiveness.

---

# What this changes in the two earlier reports

Rather than leave you to find these, here is every place a prior report should now be read with a correction attached.

| Where | What it said | Status |
|---|---|---|
| R1 §10 | "Shape → Generator throughout" | **Withdraw.** Pattern-chasing (point 3) |
| R1 §10 | Fuse the four lit solids; fuse star + polygon | **Withdraw both.** They hide four visible pictures behind a mode (point 3). The jets fusion and the height fusion survive |
| R1 §10 | Cull the vortex field | **Reversed — fuse into swirl** (point 2) |
| R1 §10 | Cull the old blast effect "with a migration note" | **Straight delete**, and it's the best-evidenced deletion in the tool (point 1) |
| R1 §10 | 3D playback: finish, demote or cut | **Cut, or move it out of Pyre** (point 2) |
| R1 §5 | "Simplify the mask system rather than extend it" | **Withdraw — unsupported.** The rename recommendation in that section survives |
| R1 §5 | The two height techniques are "a merge of unequals" | **Struck.** They share one lighting routine; it's a merge of equals |
| R1 §6 | Five attachment stages | **Four.** The edge stage has no consumer (point 9) |
| R1 §6 | A stack warp is "centred on the canvas" | **Understated — it's the top-right corner, and it's a bug** (point 9) |
| R1 §8 | "Until Shaper can export, no integration is possible" | **False under your point 6.** Nothing crosses a process boundary (point 6) |
| R1 §8 | A Shaper model is an imported reusable asset | **Reversed by your ruling.** Its underlying reason — Shaper's parts must never become Pyre layers — survives and still binds |
| R1 §11 | "Retiring isn't finished until saved files are migrated" | **Cancelled** (point 1) |
| R2 §1, §12 | The route is safe because it can't break a saved effect | **Void as a principle.** Re-order by uncertainty first (point 1) |
| R2 §7.1 | "For all nine scene generators the fill decides the shape" | **False.** Zero are fused; four don't even touch the palette (point 7) |
| R2 §7.1 | Opacity travels with the shape, full stop | **Superseded by the veil.** Its own stated cost is a regression (point 7) |
| R2 §7.3 | Substance needs an "emission" channel | **Drop it.** Your 5b ruling replaces it with one sentence (point 5b) |
| R2 §7.8 | Two generators colour before downsampling | **Three, and it missed one of the two most affected** (point 7) |
| R2 §6 | One generator is genuinely inseparable | **Retract.** It's non-parametric, not inseparable (point 7) |
| R2 §12 slice 5 | A generator's opt-out proves resampling isn't equivalent | **Misread — it's a double-application guard.** Question still open (point 9) |
| R2 §1 | "A rewrite is the one route that actually fails here" | **Survives only for the nine ported effects.** For Pyre's own core it is now open (points 1 and 4) |
| Both | The "uncomfortable finding" sections | **Void as written, fully recoverable on structure** (point 2) |

---

# What I'd do next, in order

1. **Push the branch.** The entire current Pyre exists on one disk. Unrelated to everything else and the largest risk in the repository.
2. **Render one frame per generator and look at them — before touching anything.** This is deliberately item 2 and not item 6. Three of the items below change what Pyre outputs, so a baseline captured after them isn't a baseline. And with usage evidence withdrawn, this is now the only empirical evidence left about what's worth keeping: every "keep, it's distinctive" verdict in either report is currently an inference from how much care went into building it, not from seeing it. It is also the cheapest item on the list and has no prerequisites.
3. **Correct the two stale lines in the project instructions** — the rename being unstaged, and ZUI living outside the package.
4. **Ungate the border stage in today's Pyre.** Three edits. The cheapest possible test of whether the shape/border seam is real, against 23 generators that have never fed it. If it goes badly you've learned it for a week rather than a quarter.
5. **Fix the value that means five different things**, including the hardcoded zero that silently kills an effect. Cheap now, expensive after anything is built on top of it.
6. **Give the four generators with no per-pixel stage a buffer fallback** — your point 9, nine effects light up, and *without* moving the existing stage, which would change output everywhere particles overlap.
7. **Write the design document** (point 4), with the plug-in-contract question resolved first, because it decides whether the ported effects can be shared rather than copied.
8. **Then start the parallel build** — new assembly in this project, contract first, ported effects shared not copied, finish checklist written on day one.

---

# Questions only you can answer

Six from the earlier reports remain live. These are the ones your nine points created.

1. **Does a fill's veil default to "no opinion", and can a generator forbid one?** I recommend yes and no respectively — but a generator whose look depends on its own transparency column may want to insist. This is the one decision inside the point-7 design that changes what "any fill on any shape" actually promises.
2. **Do you want the per-instance clock fixed?** Five of the seven big effects currently start instances on the shared clock rather than their own, so they pop in mid-animation. Fixing it is one line each and is probably what you always meant — but it changes what those generators render.
3. **One ordered effect list, or four typed ones?** They are in tension (point 9). I lean to one list plus honest greying-out, which contradicts report 2's slice 2. Your call, and it's cheap to change your mind before it's built.
4. **Is the lit-solids family in or out of the Shaper ruling?** They do real 3D rotation, which Shaper structurally cannot. I recommend they survive as their own technique — but that means "Shaper is the only way to make primitives" has a named exception from day one.
5. **How much resolution memory are you willing to spend?** Three generators need their fill evaluated at 2–3× canvas resolution to stay bit-identical. That's 4–9× the buffer memory, paid only by them. The alternative is accepting softer band edges on those three.
6. **Do you want to keep the effect stack as a separate tool at all?** Several corrections here are really about Pyre and the standalone stack being two hosts for one effect library that disagree about pivots, ordering and canvas growth. Merging them is a bigger question than anything in these three documents, and it keeps surfacing.
7. **Does "all the assets are disposable" include the committed demo scenes and the Pyre output other tools consume?** (Point 1.) I've assumed not. If it does, several removals get easier; if it doesn't, those are the one real constraint left on cutting a generator.

---

# Confidence and limits

**Measured and solid.** The claim that no generator is mathematically fused — six of the nine big effects plus both simulations were read line by line in their pixel-emitting loops, specifically hunting for a counterexample. The existence of the already-implemented sheet-publishing interface. The 41-effect classification and the four attachment stages. The swarm buckets. The 98-line size of Shaper's silhouette engine. The line counts and the git history behind point 4, including the thirty-three-day parallel window, the near-absence of feature work on the old Pyre during it, and the commit that copied the old grouping model across by name. The four UI-guide rules quoted in point 6, which I checked against the guide myself rather than taking on trust.

**Inferred but strong.** Every claim about what a change would *look* like. Those come from arithmetic and code reading, not from a rendered before-and-after. The claim that a resolution-preserving handover is *bit*-identical is derived from reading the shared downsampling routine; it's falsifiable by a comparison dump and should be checked before the split is called done, because bit-exact fidelity is the entire value of the ported effects.

**Not verified at all — the same limit as both earlier reports, and by your ruling it now matters more.** **Nothing here was checked by looking at Pyre running.** No frame rendered, no window opened. With usage counts withdrawn as evidence, direct observation is the only empirical check left, and it hasn't happened. That's why "render one frame per generator" is item 6 on the list above rather than a footnote.

**Two specific things to reproduce before acting on them.** The top-right-corner pivot in the standalone stack — the arithmetic says it's wrong, but the fix changes saved stacks, so look at one frame first. And the assumption behind "the simple case stays free" under a general shape engine: that a composed shape's structure can be compiled once and only its numbers vary per particle. Plausible from the code's shape; **not prototyped**, and it is load-bearing.

**On process.** This document was built from seven fresh investigations on top of the seven behind the first two reports, and was then **attacked by an eighth whose only job was to break it**. Four investigators worked in parallel on your points 1–2, 6, 8 and 9; three more on points 4, 5 and 7. Where an investigation contradicted a prior report, I have gone with the code and named the report.

**The most useful thing that happened is that the investigation aimed at your point 7 went looking for the entanglement report 2 called its hardest problem, and could not find it anywhere** — which is the kind of result that only shows up when someone is sent to look for a specific thing rather than to summarise.

**The second most useful thing is that the attack pass found seventeen factual errors in the draft of this document, all corrected above.** Three of them mattered: the draft said all four of the tangles in point 4 were created by the rebuild when two were inherited and copied across on purpose (correcting it made the argument *stronger*); it said moving one processing stage had "no behavioural cost anywhere" when it would in fact change every layer where particles overlap, and that was on the action list; and it oversold how ready the sheet-publishing machinery is — two of the six sheets the design needs are published by nobody and have to be built. The rest were counts. **The design argument survived the attack unchanged; the numbers around it needed real work — which is the same thing that happened to both previous reports, and is worth remembering when reading any single figure here.**

---

*Detailed evidence with file and line citations for every claim above is in the working notes at `D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0098\` — fifteen files now. The eight written for this round cover the swarm question, the effect-universality question, Shaper as the primitive engine, the usage-blind re-derivation of both prior reports, the rebuild strategy, the AgentHQ harness, the entanglement split, and the verification pass over this document. Those are for whoever implements this. This document, like the first two, deliberately carries no citations. One caveat for whoever reads them: the verification pass found line-number drift throughout the working notes — the findings hold, the exact line numbers often sit a few lines off.*
