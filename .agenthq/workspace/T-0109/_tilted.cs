// T-0109 — regenerate `tilted-conformance.png`, the HS-9.5 conformance frame (discharging BC-2.7 / BC-4.4).
//
// KEEP THIS SCRIPT. BC-2.7 requires the tilted frame to be "re-rendered whenever the resolve changes", so
// this file is the mechanism for doing that: RE-RUN IT after ANY edit to ShaperResolve.cs, ShaperHeight.cs
// or ShaperHeightCompiler.cs, and look at the PNG again. A conformance artefact nobody can regenerate stops
// being a conformance artefact the first time the code underneath it moves.
//
// Run with:
//   unity command --project-path "D:/UNITY/Laubrary Dev - Shaper" --timeout 900 eval_file --file <this>
//
// The render must satisfy three things, all of which the returned report states as numbers rather than
// asserting: EVERY ray takes the GENERAL branch (straight-down count must be 0 - a frame that silently fell
// back to the closed form would look identical and prove nothing), the frame must contain a VISIBLE SIDE
// WALL (HS-6.5 - straight down a wall has zero screen area, so this is the only thing in Wave 2 that
// exercises HS-0.2's ruling at all), and both layers' Z bases must come from HS-7.2's ordering base.
//
// NOTE (T-0105's gotcha): the CLI's eval_file wraps this in a method body, so `using` aliases at the top
// silently fail. Every type is fully qualified.
string path = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\tilted-conformance.png";
return Laubrary.Shaper.Editor.ShaperHeightAudit.TiltedConformance(path);
