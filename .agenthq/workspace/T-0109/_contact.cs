// T-0109 — regenerate `height-contact-sheet.png`: every extrusion profile x a representative bevel,
// rendered and lit through the real pipeline (ShaperEvaluator -> ShaperHeight.FillTile ->
// ShaperNormals(Profile) -> ShaperLightLaw.Shade). T-0105's contact-sheet precedent.
//
// Re-run after any change to the profile catalogue, the normal provider or the light rig.
//   unity command --project-path "D:/UNITY/Laubrary Dev - Shaper" --timeout 900 eval_file --file <this>
string path = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\height-contact-sheet.png";
return Laubrary.Shaper.Editor.ShaperHeightAudit.HeightContactSheet(path);
