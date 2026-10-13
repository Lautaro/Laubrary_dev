# Runtime control conveniences

The Trackeroids API demo uses `ZuiStack.Button(label, fontSize, tooltip)`, `Toggle(label, value, fontSize, tooltip)` and `SegmentedContents(selected, fontSize, params GUIContent[])`. The existing string-based `Segmented` call remains compatible. Tooltips are exposed through Unity's `GUI.tooltip`; the host draws the transient tooltip surface.

`Slider(label, value, min, max, fontSize, format, defaultValue, tooltip)` accepts an optional nullable default. When supplied, it draws a default tick and returns that value on a double-click inside the slider. Assign the return value through the same setter used for dragging. The tooltip includes the reset value. These controls do not persist settings or mutate assets themselves; an asset-editing host remains responsible for Undo and dirty state.

Runtime toggles use a visibly latched button. Selection and enabled state can be separate, as in the demo's combined-case selector. Keep host labels and tooltip explanations specific to the behavior the player is changing.
