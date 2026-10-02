# Foundation asset-window shell — T-0534

`ZuiAssetWindow<T>` now adds the neutral `lau-asset-browser` root class. It identifies shared asset-window chrome only; a concrete tool applies its own root class separately and overrides this shell through ordinary descendant selectors.

## Extracted fixed presentation

`ZuiFoundationAssetBrowser.uss` owns the fixed tag-island, editor-host, folder-label, folder-row, library, scroll, grid, and selected-cell presentation. The existing `zui-cell` classes and selectors remain in place for legacy compatibility. The new semantic selected state mirrors the existing selected thumbnail appearance without changing the old selector.

## Intentional inline exceptions

- `CellSize`, `ThumbSize`, image inset, and name maximum width remain C# writes because subclasses supply these geometry parameters.
- Thumbnail hover events remain C# because they drive animated-preview selection and repaint scheduling, not a cosmetic colour or border. There was no inline hover cosmetic write to replace; a USS `:hover` rule would change the existing appearance.
- The create-folder character budget remains C# because it is measured from the live window width.

The reference source at `Documentation/UISeparation/Reference/ZuiAssetWindow.cs.txt` is the frozen pre-extraction implementation for the coordinator's comparison harness. No legacy IMGUI picker or thumbnail-grid algorithm changed.
