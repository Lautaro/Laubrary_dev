# Foundation flow

`ZuiColumnFlow` now resolves optional unitless `--zui-column-flow-column-width` and `--zui-column-flow-gutter` values on `CustomStyleResolvedEvent`. Width falls back to the existing public `ColumnWidth` caller value, while the public getter continues to return that caller value rather than a USS override. Gutter falls back to its frozen 6 px value. Both values reject non-finite input; width has the existing 1 px safety minimum and gutter clamps to zero.

The width adapter changes only the existing width bucket input. It deliberately does not subtract gutters from that calculation, so column-count thresholds and redistribution behaviour remain unchanged. The gutter is still assigned inline to following columns because it is a cached resolved metric shared with the placement calculation; structural row, column, item, and HGroup layout now lives in `ZuiFoundationFlow.uss`.

`ColumnWidth` remains source-compatible: setting it updates the caller fallback, invalidates the same bucket guard, and schedules redistribution. A later custom-style resolution overrides it only while the USS value is present; removing that value restores the caller fallback. There is no public gutter setter to preserve.
