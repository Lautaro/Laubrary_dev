# Playback3D reference rig (dev host only)

`Playback3DVfxTest.unity` is the rig the PyrePlus **Playback 3D** shape's preview was matched against: one VFX prefab, a black-background HDR camera, and the VFX pack's own Bloom + ACES Tonemapping volume profile. Press play on nothing — just scrub the particle systems in the editor and look. If the Playback 3D preview inside Pyre Plus ever stops looking like this scene, the preview's grade (`PyrePlusPlayback3DPost.shader` + `PyrePlusPlayback3DPreview.cs`) has drifted.

**External dependency:** the scene references the *Vefects Fire VFX (URP)* Asset Store pack under `Assets/Vefects/`, which is **not committed** (third-party licensing). Without that pack imported the camera/volume rig still opens fine, but the `VFX Subject` object will be a missing reference — drop any particle-based fire/explosion prefab in its place.

This folder is deliberately outside `Assets/Demos/` and outside the package: it is a development reference rig, not a shipped Laubrary demo.
