T-0195 — preview resize bar, verification notes

Commits: 3ef8ded5 (feature), 69374b10 (overlap fix, requested by the PM after T-0199's walk).

Screenshots (all in this folder): small.png (previewHeight=140), large.png (previewHeight=900),
large-scrolled.png (previewHeight=900, right pane scrolled to the bottom — confirms the Bake box
stays reachable), after-reload.png (previewHeight=900, captured after a forced domain reload via
EditorUtility.RequestScriptReload()).

Driven through a probe on Assets/Temp/T0195_ScratchDoc.asset (a throwaway document, deleted after
the pass; ShaperDemoDoc.asset was never opened or touched) by setting previewHeight and
stage.style.height the same way BuildPreviewResizeBar's own PointerMove handler does, then
Repaint(). previewHeight was reset to its 320 default and the scratch doc was unbound afterward,
leaving the window in a clean state.
