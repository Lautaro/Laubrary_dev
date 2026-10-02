**Measured live in the Shaper worktree editor, T-0318 (round 10), HEAD 455d8c6d plus that round's fixes.**

`PyreWindow.ClampedLeftPaneWidth()` (`Editor/Pyre/PyreWindow.cs:465-469`) caps the dial pane at `Mathf.Min(4f*360f + 3f*6f, Mathf.Max(360f, position.width - 260f))` — subtracting only the right pane's own 260px `minWidth` (`PyreWindow.cs:401`). It does not subtract the root row's horizontal padding (4 + 4 = 8px) nor the 6.2px divider between the two panes. That is 14.2px, and 14.2px is exactly what spills.

Measured with `workspace/T-0318/probes/d-pyreset.cs` + `d-pyremeas.cs` (the width written through `leftPaneWidth`, which is what the drag handler writes, then read in a later eval):

| persisted `leftPaneWidth` intent | window | preview pane | root content ends | spill | drawn elements past the window |
|---|---|---|---|---|---|
| 360 (the default) | 820 / 1400 / 1800 | fits | - | 0.0px | 0 |
| 1200 | 1400 | x=1150.2..1410.2 | 1396.0 | **14.2px** | **23** |
| 9999 (divider dragged fully right) | 820 | x=570.2..830.2 | 816.0 | **14.2px** | **23** |
| 9999 | 1800 | fits | - | 0.0px | 0 |

It bites whenever the cap is the binding constraint - any window narrower than about 1718px, once the divider has been dragged to or past the cap. `leftPaneWidth` is a `[SerializeField]` the drag handler writes (`PyreWindow.cs:481-485`), so a divider dragged wide in a big window keeps pushing the preview off every time the window is narrower afterwards. T-0313 established that dragging that divider is how a user reaches Pyre's 2- and 4-column dial stack, so this is a path people take.

At Pyre's own declared minimum (820) with every section open the window is otherwise clean (T-0318: 0 captions short, 0 overflow, 0 off-window, 0 missing tooltips) - this pane spill is the only finding there, and it hides 23 drawn elements including the transport's right edge and a '?' help label.

**The remedy needs no invented number:** subtract the two constants the layout actually spends - the root's horizontal padding and the divider width - from the cap, in `ClampedLeftPaneWidth()` and in the drag handler, which currently spells the same expression out a second time inline. Those two copies should not exist separately.

Not fixed in T-0318: this is arithmetic in a Pyre window file with a drag path behind it, not the caption/padding trivia that round fixed. No mouse has dragged the divider by hand.
