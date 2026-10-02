# Presentation context

`ZuiPresentationContext` carries presentation into a root that is not a descendant of its owner, such as a future popup window, a standalone panel, or a bare inspector root. Capture it from the owner before opening the surface, then apply it to the new root.

The snapshot walks the owner ancestor chain from outer to inner. It copies deduplicated stylesheet references in that order and classes whose names begin with `lau-tool-`. That prefix is the presentation-context convention: tool roots opt into sharing a class such as `lau-tool-zounds`. A caller can explicitly include a legacy skin root with `Capture(anchor, "zs-root")`; only classes actually present on the owner chain are captured. Arbitrary state, interaction, and structural classes are otherwise excluded.

`ApplyTo` first calls `Z.Attach(root)`, then adds the captured sheets and tool classes without duplicates. It also ensures `zui-root` is present. This keeps the toolkit’s normal default attachment while making a detached root receive the same selected semantic presentation as its owner without a mutable global “active skin.”

The context is immutable and does not live-sync. Changing an owner’s sheets or `lau-tool-*` classes after capture does not change an already-open detached surface. Recapture into a fresh root when reopening if the current owner presentation is needed. Applying is additive, so reusing a root does not remove earlier classes or sheets. It does not copy custom properties, inline styles, arbitrary classes, or other inherited state; represent those through stylesheet rules keyed by selected root classes when needed. Selectors that depend on the original ancestor structure are not reconstructed by flattening root classes.

`ZuiPopover` remains an in-window overlay when it finds a `zui-root` host, preserving its existing placement and interaction path. Its bare-host fallback now uses a captured context, so it attaches the normal ZUI sheets plus any owner-local presentation sheets and `lau-tool-*` root class.
