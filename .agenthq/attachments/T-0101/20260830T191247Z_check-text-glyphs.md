# T-0101 Text glyph probe

`ShapeForm.Text` with `textString` set as shown, rendered against every TMP font asset in the project.
Images are in `frames/`. Compare what the PNG spells against the `string` column.

| font asset | atlas readable | string | coverage% | png |
|---|---|---|---|---|
| `Assets/Demos/TextSplashDemo/Border Fonts/Splash Demo (LiberationSans SDF) Border Font.asset` | yes | `PYRE` | 7.30 | `textprobe_Splash-Demo--LiberationSans-SDF--Border-Font_PYRE.png` |
| `Assets/Demos/TextSplashDemo/Border Fonts/Splash Demo (LiberationSans SDF) Border Font.asset` | yes | `ABCDEFGH` | 9.36 | `textprobe_Splash-Demo--LiberationSans-SDF--Border-Font_ABCDEFGH.png` |
| `Assets/Plugins/Shapes/Textures/Inconsolata/Inconsolata-SemiBold SDF.asset` | yes | `PYRE` | 8.51 | `textprobe_Inconsolata-SemiBold-SDF_PYRE.png` |
| `Assets/Plugins/Shapes/Textures/Inconsolata/Inconsolata-SemiBold SDF.asset` | yes | `ABCDEFGH` | 9.29 | `textprobe_Inconsolata-SemiBold-SDF_ABCDEFGH.png` |
| `Assets/Plugins/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset` | yes | `PYRE` | 9.36 | `textprobe_LiberationSans-SDF---Fallback_PYRE.png` |
| `Assets/Plugins/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset` | yes | `ABCDEFGH` | 9.35 | `textprobe_LiberationSans-SDF---Fallback_ABCDEFGH.png` |
| `Assets/Plugins/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset` | no | `PYRE` | 16.02 | `textprobe_LiberationSans-SDF_PYRE.png` |
| `Assets/Plugins/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset` | no | `ABCDEFGH` | 16.02 | `textprobe_LiberationSans-SDF_ABCDEFGH.png` |
