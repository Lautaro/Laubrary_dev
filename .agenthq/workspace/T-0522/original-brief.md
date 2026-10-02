### Where this comes from

The owner had a Zounds-specific styling inventory done for designer-handoff purposes: `D:/Claude@GDrive/Zounds UI Styling Inventory for Designer Handoff 2026-09-30.md`. Read it first, in full, as the evidence base and starting point -- do not re-derive its findings from scratch. Its headline numbers: across the Zounds UI Toolkit screens, 980 places set layout/spacing/color directly in C# against only 305 uses of a shared USS rule; the skinned buttons/toggles/sliders are baked images copied out of the old editor rather than systematically styled; 10 named colours exist in the copied skin that nothing actually reads (the same blue is retyped 10 separate times instead); "row height 20" is duplicated across 8 files and an amber colour across 11 places in 3 files.

Reacting to that document, the owner's own words, verbatim: "If I understand correctly we could do more to separate function, layout and styling. ZUI uses a collection of defined controls with functionality. But if they didn't include any layout or styling, just tags and whatever USS uses for id to identify the elements, then that could be up to the USS much more? It would be important for the code to tag everything semantically." -- and then: "I want this refactor to be done on a Laubrary level. This is how Laubrary should work in general."

**This is explicitly a Laubrary-wide architecture change, not a Zounds-only fix.** ZUI is the shared UI toolkit used by every Laubrary tool (Pyre, Chunks, AssetKit, Cabinets, Cartographer, Choreographer, Zoetrope, Launimator, Zounds, and others) -- a change here is meant to benefit all of them, not just the tool that happened to surface the problem.

### The actual architectural target

The target is the same separation UI Toolkit was designed for -- the same split as HTML/CSS/JS: structure and behaviour in code, all presentation in the stylesheet. Concretely:

- **Controls should do only functional work in C#**: track drag state, hold a value, raise change events, run a control's own interaction logic (a slider's drag math, a toggle's on/off state, the band-sliders' multi-handle logic) -- and nothing about how any of it *looks*.
- **Controls should tag themselves semantically instead of styling themselves** -- e.g. `AddToClassList("zui-slider")`, `"zui-slider__track"`, `"zui-slider__handle"` (a BEM-style naming convention, block/element/modifier), rather than ever setting `.style.width`, `.style.marginTop`, a colour, or any other cosmetic value directly in code.
- **USS should own every static visual decision**: spacing, colour, size, corner radius, font, alignment -- targeted purely by class selectors, so a change like "give every row header more padding" becomes one rule in one file instead of a hunt through many C# call sites.

**One honest nuance the research must state plainly, not paper over**: this cannot reach literally zero layout in code. Some layout is genuinely structural/data-driven rather than cosmetic -- e.g. "this row has however many columns as there are items", or "this section lays out side-by-side only if the window is wide enough" (the width-aware wrapping-row logic already built for Zounds). That kind of dynamic, context-dependent structure has to stay in code, because USS cannot know it ahead of time. The real target is: **everything static/cosmetic moves to USS; only genuinely dynamic, data-dependent structure stays in C#.** The research must draw this line explicitly, with real examples from the existing codebase of what falls on each side.

### Hard requirement: the current look must be recreated exactly, not redesigned

The owner was explicit: **"The research should include how to actually take the step and recreate the current look of everything when it is done."** This is a faithful architectural migration, not a redesign -- every tool must look exactly as it does today once the refactor lands, just built the right way underneath.

**There is already a proven methodology for exactly this kind of change on this project, and the research must use it as the model rather than reinventing an approach**: the Zounds UI Toolkit port (T-0456 and its sub-tasks, commits from late September 2026) faced the identical problem -- port a whole UI surface to a new underlying technology while guaranteeing pixel-for-pixel-equivalent visual output. Its proven approach, which this research should study and adapt:

1. **Extract the current visuals into data first, before restructuring anything.** The Zounds port built a one-time skin extractor that copied the existing style sheet's exact values (colours, gradients, borders, corner radii, padding) into USS variables plus the exact fill textures as image assets -- capturing "what it currently looks like" as a static artifact before any code changed.
2. **Build a side-by-side comparison harness.** The Zounds port built a tool that opens the old and new implementations of the same screen at the same size, captures both, and produces a pixel-difference image plus a percentage-different-pixels number -- used at every step of the migration, not just at the end, to prove visual parity rather than assume it.
3. **Migrate incrementally, verifying at each step**, rather than attempting the whole surface at once.

The research must propose an equivalent plan for the *architectural* refactor (function/layout/styling separation), reusing this same extract-then-verify discipline: capture today's exact visual output as ground truth, then restructure the underlying code (moving inline styling into USS, tagging elements semantically) while continuously proving nothing visibly changed.

### Skins / themes: keep Zounds' distinct look, don't force a unification yet

The owner was explicit that **Zounds has its own deliberately distinct visual style** (referred to as Zounds' "Colorful" style), different from the rest of Laubrary's tools, and that this must not be lost or forced into uniformity as a side effect of this refactor.

The owner's own priority order, stated explicitly -- **do not reorder it**: "Note that Zounds has its very own visual style. When all is done we can apply Zounds' Colorful style to all of it. But priority is to change the styling to a USS strategy for all of Laubrary." So:

1. **First priority, and the actual scope of this research/build effort**: convert the underlying architecture to a proper USS-driven, semantically-tagged strategy across all of Laubrary -- regardless of which visual skin currently sits on top of any given tool. Every tool keeps its own current look, just re-implemented the right way underneath.
2. **Later, lower priority, explicitly out of scope for now**: once the architecture itself is sound, optionally apply Zounds' Colorful style project-wide as a reskin. The research should note that the architecture it designs must make this kind of later, wholesale reskin realistic (i.e. support multiple distinct named skins/themes as swappable USS sets), but should not attempt or design the Colorful-everywhere reskin itself in this pass.

**One input is still pending from the owner and not yet available**: "I'll list which parts of Laubrary has the styling and layout that is best representing what I want for all of Laubrary." -- i.e. the owner intends to separately name which existing tool(s) in Laubrary currently embody the general visual/layout style he wants to use as the reference baseline for the rest of Laubrary (this is NOT necessarily Zounds' own Colorful style, which is called out as a separate, later, optional step). Whoever picks up this research should check whether that list has since been provided (search recent AgentHQ tasks/memory/conversation for it) before finalizing a baseline-style recommendation; if it hasn't been provided yet, the research should proceed on the architectural question (which is independent of which skin is chosen) and flag the missing input clearly rather than guessing at a baseline style.

### What this research stage should produce

A thorough, plain-language planning document (same standard as this project's other research docs -- no code, no class names as the explanation, written for a reader who understands general UI/software architecture but has not opened this codebase). It should cover:

1. **An audit/inventory methodology** for the rest of Laubrary's ZUI-based tools, extending or reusing the approach already used for the Zounds-specific inventory, so the same "how much is shared-rule-driven vs hardcoded" measurement can be taken project-wide.
2. **A concrete semantic class-naming convention** to adopt (e.g. a BEM-style scheme), with real worked examples against existing ZUI controls (Button, Toggle, Slider, the band-sliders control, the skinned envelope view, etc.) showing exactly what class names each element and its sub-parts should carry.
3. **The migration strategy**, modelled explicitly on the Zounds UI Toolkit port's proven extract-then-verify approach (see above): how to capture today's exact visual output as ground truth, build an equivalent side-by-side comparison harness for the architectural refactor, and migrate incrementally with continuous verification rather than a single big-bang rewrite.
4. **A phasing plan**: which ZUI controls/patterns to convert first (a proof-of-concept slice), how to validate the approach before rolling it out across every control, and a sensible order for tackling the rest of Laubrary's tools afterward (which currently depend on ZUI's existing behaviour and must not regress).
5. **The line between what moves to USS and what must stay in code**, stated explicitly with real examples from the current codebase on each side (static/cosmetic vs dynamic/data-driven layout).
6. **A skin/theme architecture** that supports multiple distinct, swappable visual styles (at minimum: whatever the general Laubrary baseline style ends up being, plus Zounds' own separate Colorful style) as clean, swappable sets of USS rules -- rather than hardcoded per-tool styling -- so that a later "apply Colorful everywhere" pass (explicitly out of scope for this stage) is realistic once wanted.
7. **Risk and cost assessment**: ZUI's blast radius is every Laubrary tool, not just Zounds -- state plainly how large and risky this is, what could regress, and how the incremental/verified approach mitigates that risk, consistent with this project's standing rule to state the size of a big refactor honestly rather than downplay it.

**This is research and planning only -- do not build anything in this stage.** A build task should follow once the owner has reviewed the plan, the same two-stage pattern already used for the Zounds Programmatic Control (ZPOC) work and the non-destructive multitrack editing research on this project (both were fully designed and owner-reviewed before any implementation began).

### Context to read before starting

- `D:/Claude@GDrive/Zounds UI Styling Inventory for Designer Handoff 2026-09-30.md` -- the evidence base, read in full.
- The Zounds UI Toolkit port's own history on this board (search AgentHQ tasks tagged around T-0456 through T-0470, and T-0457 specifically for the skin-extraction approach, T-0460 for the side-by-side comparison harness) -- the proven precedent methodology to adapt for this broader architectural refactor.
- The actual ZUI source under `Assets/Packages/Laubrary/Zui/` (both the older IMGUI-era controls and the newer UI Toolkit layer) to ground the inventory and naming-convention proposals in real code, not assumption.
- This project's `CLAUDE.md` "UI rule -- ZUI for ALL UI" section, and `D:/AgentGuide/ui-rules.md`, for the standing UI conventions this refactor must remain compatible with.

The owner will hand this task to a separate agent session himself; whoever picks it up should claim it via the AgentHQ API in the normal way before starting.
