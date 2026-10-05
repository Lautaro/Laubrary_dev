# ZTracker add-on changes

## [Unreleased]

- Introduced an optional sibling package for tracker song data, Windows x64 playback, and a compact Laubrary preview window using canonical ZUI. Creating a song provides saved synth content; playing remains explicit.
- Sequenced notes now respect channel gain and pan, with channel gain retained across volume commands and slides without replacing instrument gain.
- Added a native ABI check, platform capability result, preallocated rendering buffers, owned stop/reload/device-change cleanup, and silent idle components. This managed host does not preserve Zounds SAP garbage-collection immunity during simultaneous playback.
