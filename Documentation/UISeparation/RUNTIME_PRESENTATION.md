# Runtime presentation

Player-facing runtime UI uses `ZuiRuntimeSkin` as its presentation boundary. The asset contains static visual decisions: semantic text recipes, neutral panel and prompt colours, segment and focus treatments, and the controls overlay's fixed reference layout. It contains no live view state, input state, control labels, bindings, scroll position, or device-specific artwork.

A runtime consumer resolves presentation in this order: a skin assigned directly to that consumer, the shipped `Resources/ZUIRuntimeSemanticSkin` asset, then an in-memory compatibility recipe. This allows two roots to use different skins in the same frame; resolving a skin never writes to a global active-skin variable or reads the editor's selected skin. The shipped resource preserves the controls overlay's former appearance. It can be duplicated and edited with Unity's normal asset workflow, and a consumer can instead assign its own asset locally.

The immediate-mode runtime helpers accept semantic roles rather than hard-coded style recipes. Their style cache belongs to the resolved skin and includes the semantic role, widget kind, current GUI skin/template, font, scaled size and the full text recipe. That keeps a change to one skin or a runtime GUI-skin switch from reusing another skin's cached style.

`InputGuideOverlay` is the first migrated consumer. It owns live legend data, rebinding, menu navigation, control placement, scaling and the device-diagram painter. The skin owns the surrounding visual language. Device-specific controller button colours and keyboard anatomy remain painter data because they communicate the controls themselves rather than a reusable UI theme.

Verification is deliberately split. The project compiles the runtime-only skin and InputGuide assemblies without editor code, the default resource resolves in the editor, and two distinct skin instances resolve independently. A full player interaction walk still needs an authored InputGuide scene with a real action catalog and physical mouse, keyboard and gamepad input; do not claim that from static or editor-only probes.
