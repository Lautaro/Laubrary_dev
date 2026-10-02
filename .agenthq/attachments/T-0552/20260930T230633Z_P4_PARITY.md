# Phase 4 Pyre and Chunks parity record

This record covers the first dense-surface migration slice: Pyre and Chunks. Stable UI structure and cosmetics that were repeated in code now have semantic names in the shared stylesheet, while live values such as a user-resized dimension, visibility, text, and authored colours stay with the tool.

## What was checked

Both tools were captured at 1400 by 900 pixels with no asset selected. Pyre was also captured with Green Lantern selected at wide and narrow widths and with its second layer selected. Chunks was captured with Ring Blast selected at wide and narrow widths. Each state has a complete resolved-style dump and a client-area image in this folder with the `p4-` prefix.

## Result

Chunks matches every frozen reference state exactly: there are no resolved-style differences and no changed pixels. Pyre’s empty state also matches exactly. The populated Pyre captures preserve every moved style value. Two labels have a small width change between captures because their live text changes while the window is open; a same-build control capture with the `p4c-` prefix reproduces those width changes. One older reference photograph also shows a different button background, consistent with an input-state capture rather than a persistent presentation rule.

## Verification boundary

The project compiles after the migration. Screenshots were inspected by eye. Pointer-held hover, drag, and deliberate error-state checks remain unverified because the Windows automation surface did not expose the Unity editor window in this run. The remaining named dense surfaces are deliberately not claimed by this record; they need their own frozen states before their static presentation is moved.
