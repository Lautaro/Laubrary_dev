# Shared foundations acceptance record

Reference commit: `e52505a`. Scope: AHQ T-0530–T-0536, first phase-2 foundation rollout. See `Shared Foundations Report.md` for the human-readable outcome and limits.

## Reference integrity

`FoundationBaseline` freezes eight pre-change source files under a separate namespace; imports and aliases are adapted, with aliases inside the nested namespace so they actually win over the parent toolkit's `Z` type. Comparing the normalized sources against `e52505a` after undoing those adaptations produces exact matches. The frozen factory is a full pre-change copy of `Zui.cs` under `FoundationFactoryBaseline`.

The consumer reference freezes the old generic asset-window shell and unchanged BackSplash window, renaming types, removing the duplicate menu attribute and aliasing the frozen factories/enum. It shares unchanged lower-level services and the current window base. This is isolation of the changed shell and factories, not an independent implementation of every shared service. The old shell does not receive the new shell's semantic classes.

## Reproduction

1. Open only the isolated project's targeted editor. Run `run_probe.ps1 -Name FoundationCompileProbe` and check its actual loaded API version and project path.
2. Run `verify_foundation_cases.ps1`; it opens independent windows, checks resolved metrics and writes five exact comparisons, including deliberate override and reset cases.
3. Run `FoundationInteractionProbe`. It checks eight edit/fold/popup/compatibility outcomes. Run `FoundationContextSetup`, `FoundationContextProbe`, then `FoundationContextFinish` in order after layout has settled; each command round trip allows the editor to update.
4. Run `OpenFoundationConsumer`, capture, `FoundationBrowserOpen`, capture the library, `FoundationBrowserSelect`, `FoundationConsumerEdit`, capture the visible edited preview, `FoundationConsumerUndo`, then `FoundationConsumerReopen`. Repeat the browser-to-edit walk from empty selection. The fixture is under `Assets/Editor/UISeparationPilot/Fixtures` and all changes stay in the clone.
5. Reopen the old pilot before visual checks so a candidate's prior pointer-hover state is not compared against an untouched reference. Normal and Colorful captures match at 620 logical pixels; `InteractionSuite` passes 14 assertions.
6. Run `python check_presentation.py` and `python check_presentation.py --self-test` from this directory. The manifest permits specific reviewed occurrences, including compatibility defaults/fallbacks; it is not a blanket exception for any file.

## Results and limits

Foundation 420/720/900 default and 720 reset captures have zero changed UI pixels. The 720 parent override deliberately differs by 160,674 pixels in the final fixture. Real BackSplash library/editor and the original pilot normal/Colorful captures also match exactly. All comparisons use crop-top 60 and tolerance 0. Resolved layout metrics allow one physical pixel of rounding at the active display scale; screenshot comparisons do not allow a tolerance.

The coordinator inspected the rendered surfaces and the changed preview result. The walkthrough uses generated UI events and control callbacks, not physical hardware input. Native picker internals, cross-platform/display-scale coverage, full downstream packaging and every real consumer remain unverified. Broader tool/runtime migration is still open scope; this record does not retire those tasks.

Source review found no remaining concrete defect in the extracted selectors/values. Context snapshots deliberately capture present classes, never invent a requested class, and are additive when applied; use a fresh root for a newly captured context. Responsive controls require `Z.Attach` through their normal ZUI root, as documented by the authoring contract.
