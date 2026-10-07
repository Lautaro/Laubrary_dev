using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Laubrary.Zounds.Uitk;
using Laubrary.Zounds.Dsp;

namespace Laubrary.Zounds {
    // No menu, saved assets, scene changes or global audio stop. Invoked by the canonical Unity CLI.
    // Run starts short phases on the editor update loop; LastReport returns the finished result.
    public static class ZoundsPreviewOwnershipCheck {
        public static string LastReport { get; private set; } = "not run";
        static EditorApplication.CallbackFunction update;

        public static string Run(string reportPath = null) {
            if (update != null) throw new InvalidOperationException("The ownership check is already running.");
            var report = new StringBuilder("T-0019 scoped preview ownership\n");
            int failures = 0;
            var windows = new List<EditorWindow>();
            var own = new List<ZoundToken>();
            var sounds = new HashSet<Zound>();
            var generators = new HashSet<ZoundSapVoiceGenerator>();
            var raw = new List<AudioClip>();
            void Check(string name, bool pass) { if (!pass) failures++; report.Append(pass ? "PASS " : "FAIL ").AppendLine(name); }
            void Track(ZoundToken t) {
                if (t == null || !sounds.Contains(t.zound)) return;
                own.Add(t);
                var g = t.audioSource != null ? t.audioSource.GetComponent<ZoundSapVoiceGenerator>() : null;
                if (g != null) generators.Add(g);
            }
            EditorWindow NewWindow(bool legacy = false) {
                EditorWindow w = legacy ? ScriptableObject.CreateInstance<ZoundsWindow>() : ScriptableObject.CreateInstance<ZoundsWindowTK>();
                windows.Add(w); return w;
            }
            Klip Sound(int id, bool loop = false, bool repeats = false) {
                var library = ZoundsProject.Instance.zoundLibrary;
                var source = library.klips.Concat(library.zequences.SelectMany(z => z.localKlips))
                    .FirstOrDefault(k => k.audioClipRef != null && k.audioClipRef.editorAsset is AudioClip clip && clip.length >= 5f);
                if (source == null) throw new InvalidOperationException("No existing source audio to borrow read-only.");
                var k = new Klip(id) { name = "(preview ownership check)", audioClipRef = source.audioClipRef, audioClipPath = source.audioClipPath,
                    effectChain = new ZoundEffectChain(), trimEnabled = true, trimStart = 0f, trimEnd = .35f,
                    loop = new ZoundLoop { enabled = loop }, retriggerEnabled = repeats, retriggerCount = 4,
                    retriggerGap = .18f, retriggerGapMode = Zound.RetriggerGap.FromStart };
                sounds.Add(k); return k;
            }
            var args = new ZoundArgs { startImmediately = true, volumeOverride = .01f, pitchOverride = 1f, chanceOverride = 1f,
                ignoreCooldown = true, bypassGlobalSolo = true };
            void Finish() {
                EditorApplication.update -= update; update = null;
                ZoundEngine.onNewTokenCreated -= Track;
                foreach (var w in windows) if (w != null) { ZoundPreviewPlayback.Dispose(w); UnityEngine.Object.DestroyImmediate(w); }
                foreach (var t in own) if (t != null && t.state != ZoundToken.State.Killed) t.Kill();
                foreach (var clip in raw) if (clip != null) UnityEngine.Object.DestroyImmediate(clip);
                report.AppendLine("RESULT " + (failures == 0 ? "PASS" : "FAIL") + ": " + failures + " failures");
                LastReport = report.ToString();
                if (!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath, LastReport);
            }
            try {
                LastReport = "running";
                ZoundEngine.onNewTokenCreated += Track;
                var a = NewWindow(); var b = NewWindow(true);
                var loop = Sound(-19001, true); var shot = Sound(-19002); var repeat = Sound(-19003, false, true);
                var gameRepeat = Sound(-19004, false, true); var stoppedRepeat = Sound(-19005, false, true);
                var ta = ZoundPreviewPlayback.Play(a, loop, args);
                var tb = ZoundPreviewPlayback.Play(b, loop, args);
                var game = ZoundEngine.PlayZound(loop, args);
                Check("two windows and game start distinct live loops", ta != null && tb != null && game != null && ta != tb && ta != game && ta.state != ZoundToken.State.Killed);
                Check("loop has latched Stop loop tooltip", ZoundPreviewPlayback.IsLoopPlaying(a, loop) && ZoundPreviewPlayback.Tooltip(a, loop) == "Stop loop");
                Check("second click stops only the matching window loop", ZoundPreviewPlayback.Play(a, loop, args) == null && ta.state == ZoundToken.State.Killed && tb.state != ZoundToken.State.Killed && game.state != ZoundToken.State.Killed);
                ta = ZoundPreviewPlayback.Play(a, loop, args); ta.Kill();
                Check("external stop clears loop latch", !ZoundPreviewPlayback.IsLoopPlaying(a, loop));
                var s1 = ZoundPreviewPlayback.Play(a, shot, args); var s2 = ZoundPreviewPlayback.Play(a, shot, args);
                Check("repeated one-shot clicks overlap", s1 != null && s2 != null && s1 != s2 && s1.state != ZoundToken.State.Killed && s2.state != ZoundToken.State.Killed);
                Check("preview leaves caller arguments reusable for game playback", args.editorPreviewAlive == null && args.editorPreviewStarted == null);
                ZoundAudition main = null;
                main = new ZoundAudition(-19006, () => a != null, () => ZoundPreviewPlayback.Play(a, loop, args, main, false), () => true);
                ZoundPreviewPlayback.Register(a, main);
                var mainLoop = main.PlayOnce();
                Check("nested main Play wiring starts one loop without toggling twice", mainLoop != null && main.IsLoopPlaying(main));
                Check("nested main Play wiring second click stops its loop", main.PlayOnce() == null && mainLoop.state == ZoundToken.State.Killed);
                var finite = new Zequence(-19007); finite.localKlips.Add(loop);
                finite.zoundEntries.Add(new CompositeZound.ZoundEntry { local = true, zoundId = loop.id }); sounds.Add(finite);
                var finiteArgs = args; finiteArgs.overrideDuration = .4f;
                var bounded = ZoundPreviewPlayback.Play(a, finite, finiteArgs);
                Check("composite latch follows actual runtime duration", bounded != null && ZoundPreviewPlayback.IsLoopPlaying(a, finite) == float.IsPositiveInfinity(bounded.duration));
                report.AppendLine("NOTE finite override on this looping composite resolves to " + bounded.duration + "; existing runtime semantics preserved.");
                var ra = ZoundPreviewPlayback.Play(a, repeat, args);
                var stopped = ZoundPreviewPlayback.Play(b, stoppedRepeat, args);
                ZoundPreviewPlayback.StopControl(b, stoppedRepeat);
                var gameBurst = ZoundEngine.PlayZound(gameRepeat, args);
                var tailSound = Sound(-19008);
                var delay = new ZoundEffectNode(ZoundEffectType.Delay); delay.p[0] = 250f; delay.p[1] = .5f; delay.p[2] = 1f; delay.p[3] = 300f;
                tailSound.effectChain.nodes.Add(delay);
                var tail = ZoundPreviewPlayback.Play(b, tailSound, args);
                Check("effect tail is included in the token lifetime", tail.duration > .35f);
                var popup = NewWindow();
                var popupVoice = ZoundPreviewPlayback.Play(b, loop, args, new object(), secondaryOwner: popup);
                UnityEngine.Object.DestroyImmediate(popup);
                Check("closing preview popup stops its own sound while launcher survives", popupVoice.state == ZoundToken.State.Killed && tb.state != ZoundToken.State.Killed);
                var clipA = AudioClip.Create("(owned raw A)", 48000 * 3, 1, 48000, false);
                var clipB = AudioClip.Create("(owned raw B)", 48000 * 3, 1, 48000, false);
                raw.Add(clipA); raw.Add(clipB);
                AudioPreviewUtility.PlayPreviewClip(clipA, a); AudioPreviewUtility.PlayPreviewClip(clipB, b);
                foreach (var t in own) { var g = t.audioSource != null ? t.audioSource.GetComponent<ZoundSapVoiceGenerator>() : null; if (g != null) generators.Add(g); }
                var rawPopup = NewWindow();
                AudioPreviewUtility.PlayPreviewClip(clipA, rawPopup, b);
                UnityEngine.Object.DestroyImmediate(rawPopup);
                Check("raw popup close preserves launcher's own raw preview", Resources.FindObjectsOfTypeAll<AudioSource>().Any(s => s.clip == clipB && s.isPlaying));
                var launchedPopup = NewWindow();
                AudioPreviewUtility.PlayPreviewClip(clipB, launchedPopup, a);
                var launchedVoice = ZoundPreviewPlayback.Play(a, loop, args, new object(), secondaryOwner: launchedPopup);
                // Register above replaced the first session, so establish fresh live voices at the actual close.
                // Keep these alive through fixture construction and a busy editor frame; short shots may naturally
                // finish before the close assertion, which tests ownership rather than scheduler latency.
                var closeSound = Sound(-19010); closeSound.trimEnd = 5f;
                var closeShot1 = ZoundPreviewPlayback.Play(a, closeSound, args);
                var closeShot2 = ZoundPreviewPlayback.Play(a, closeSound, args);
                var closeLoop = ZoundPreviewPlayback.Play(a, loop, args, new object());
                var closeTokens = new[] { closeShot1, closeShot2, closeLoop };
                int repeatAtClose = 0;
                var aTokens = own.Where(t => ReferenceEquals(t.zound, repeat) || closeTokens.Contains(t) || ReferenceEquals(t, bounded)).ToArray();
                double until = EditorApplication.timeSinceStartup + .1;
                var pruningWindow = NewWindow();
                var pruningSession = ZoundPreviewPlayback.Session(pruningWindow);
                for (int i = 0; i < 16; i++) ZoundPreviewPlayback.Play(pruningWindow, shot, args);
                ZoundToken ringing = null;
                int phase = -1;
                update = () => {
                    try {
                        EditorApplication.QueuePlayerLoopUpdate();
                        if (EditorApplication.timeSinceStartup < until) return;
                        if (phase == -1) {
                Check("actual close begins with two live one-shots and a loop", closeTokens.All(t => t != null && t.state != ZoundToken.State.Killed && t.audioSource != null && t.audioSource.isPlaying));
                report.AppendLine("MEASURE close-live carriers=" + closeTokens.Count(t => t.audioSource.isPlaying) + " generators=" + closeTokens.Count(t => t.audioSource.GetComponent<ZoundSapVoiceGenerator>() != null));
                repeatAtClose = own.Count(t => ReferenceEquals(t.zound, repeat));
                UnityEngine.Object.DestroyImmediate(a);
                Check("UITK window disable stops every owned overlapping token", aTokens.All(t => t.state == ZoundToken.State.Killed));
                Check("actual close silences all three live carriers", closeTokens.All(t => !t.audioSource.isPlaying));
                Check("closing one window preserves legacy-window and game loops", tb.state != ZoundToken.State.Killed && game.state != ZoundToken.State.Killed);
                Check("launching window close also stops surviving popup previews", launchedVoice.state == ZoundToken.State.Killed && Resources.FindObjectsOfTypeAll<AudioSource>().Count(s => s.clip == clipB) == 1);
                Check("raw clip close stops only the owning window source", !Resources.FindObjectsOfTypeAll<AudioSource>().Any(s => s.clip == clipA) && Resources.FindObjectsOfTypeAll<AudioSource>().Any(s => s.clip == clipB && s.isPlaying));
                until = EditorApplication.timeSinceStartup + Math.Max(1.3, tail.duration + .2);
                            phase = 0;
                            return;
                        }
                        if (phase++ == 0) {
                            Check("no authored repeat starts after owner closes", own.Count(t => ReferenceEquals(t.zound, repeat)) == repeatAtClose);
                            Check("per-control stop cancels its delayed authored repeats", own.Count(t => ReferenceEquals(t.zound, stoppedRepeat)) == 1);
                            Check("game-owned authored repeat remains unaffected", own.Count(t => ReferenceEquals(t.zound, gameRepeat)) == 4);
                            Check("naturally ended effect tail leaves no ringing carrier before next close", tail.state == ZoundToken.State.Killed && !tail.audioSource.isPlaying);
                            var retained = (Dictionary<object, List<ZoundToken>>)typeof(ZoundAudition).GetField("previewPlays", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(pruningSession);
                            Check("ended repeated shots are removed from per-control ownership", retained.Count == 0 && !pruningSession.AnyLive);
                            UnityEngine.Object.DestroyImmediate(pruningWindow);
                            tailSound.trimEnd = .1f;
                            ringing = ZoundPreviewPlayback.Play(b, tailSound, args);
                            until = EditorApplication.timeSinceStartup + .3;
                            return;
                        }
                        if (phase == 2) {
                            report.AppendLine("MEASURE ringing state=" + ringing.state + " carrier=" + ringing.audioSource.isPlaying + " time=" + ringing.time + " duration=" + ringing.duration);
                            Check("tail close begins after source end while effect still rings", ringing.state != ZoundToken.State.Killed && ringing.audioSource.isPlaying && ringing.time > .1f && ringing.duration > ringing.time);
                            UnityEngine.Object.DestroyImmediate(b);
                            Check("close silences ringing effect carrier", ringing.state == ZoundToken.State.Killed && !ringing.audioSource.isPlaying);
                            Check("legacy window disable stops owned loop and raw source", tb.state == ZoundToken.State.Killed && !Resources.FindObjectsOfTypeAll<AudioSource>().Any(s => s.clip == clipB));
                            Check("game loop survives both window closes", game.state != ZoundToken.State.Killed && game.audioSource.isPlaying);
                            game.Kill(); until = EditorApplication.timeSinceStartup + .4;
                            return;
                        }
                        Check("owned SAP generators are quiet after normal per-token stop", generators.All(g => g == null || (!g.IsPlaying && !g.RenderInProgress)));
                        Check("all temporary sound tokens ended", own.All(t => t.state == ZoundToken.State.Killed));
                        Finish();
                    }
                    catch (Exception e) { Check("exception: " + e, false); Finish(); }
                };
                EditorApplication.update += update;
                return "running; read LastReport after editor progress (~2 seconds)";
            }
            catch (Exception e) { Check("exception: " + e, false); Finish(); return LastReport; }
        }
    }
}
