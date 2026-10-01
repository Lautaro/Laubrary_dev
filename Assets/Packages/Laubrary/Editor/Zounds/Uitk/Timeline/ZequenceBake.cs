using System;
using System.Collections.Generic;
using System.IO;
using Laubrary.Zounds.Dsp;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Laubrary.Zui;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// Bake a Zequence, or a time range of it, to one new audio file and a new Klip that plays it (T-0567). Only ever on
    /// request: the Zequence stays exactly as it is, so the edit stays reversible.
    ///
    /// Rendered offline, faster than real time and sample-exact: each piece is its original audio through its own effect
    /// chain by the same voice code a play uses, placed at its moment, with its tail (a reverb still ringing) included.
    /// The randomness a play would draw is settled one way or the other: <b>resting values</b> (the middle of every range,
    /// every track plays -- the default) or <b>one random run</b> from a seed, which the same seed repeats exactly.
    /// </summary>
    internal static class ZequenceBake {

        public struct Options {
            public bool range; public float from, to;
            public bool random; public int seed;
            public string name;
        }

        public sealed class Mix { public float[] left, right; public int sampleRate; public int pieces; public float seconds; }

        /// <summary>Renders the mix. Main thread; allocates freely (it is an explicit, one-off action).</summary>
        public static Mix Render(Zequence zeq, in Options o) {
            int sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            var rnd = o.random ? new System.Random(o.seed) : null;
            var pieces = new List<(float start, float[] l, float[] r, float gain, Envelope env, float envLen)>();
            float end = 0f;
            Gather(zeq, 0f, ZequenceTimeline.Mid(zeq) * (rnd != null ? Draw(rnd, zeq.minPitch, zeq.maxPitch) / ZequenceTimeline.Mid(zeq) : 1f), 1f, rnd, sr, pieces, ref end);
            float from = o.range ? Mathf.Max(0f, o.from) : 0f;
            float to = end;
            if (o.range) {
                // The range, with the tails of what sounds in it allowed to ring out.
                float last = o.to;
                foreach (var p in pieces) if (p.start < o.to) last = Mathf.Max(last, p.start + p.l.Length / (float)sr);
                to = Mathf.Max(o.to, Mathf.Min(last, o.to + 10f));
            }
            int frames = Mathf.Max(1, Mathf.CeilToInt((to - from) * sr));
            var L = new float[frames]; var R = new float[frames];
            int used = 0;
            foreach (var p in pieces) {
                if (o.range && (p.start >= o.to || p.start + p.l.Length / (float)sr <= from)) continue;
                used++;
                int at = Mathf.RoundToInt((p.start - from) * sr);
                for (int i = 0; i < p.l.Length; i++) {
                    int k = at + i; if (k < 0) continue; if (k >= frames) break;
                    float g = p.gain;
                    if (p.env != null && p.envLen > 0f) g *= p.env.Evaluate(Mathf.Clamp01(i / (float)sr / p.envLen));
                    L[k] += p.l[i] * g; R[k] += p.r[i] * g;
                }
            }
            // The Zequence's master volume curve, over its whole length.
            if (zeq.masterVolumeEnvelope != null && zeq.masterVolumeEnvelope.enabled && end > 0f)
                for (int k = 0; k < frames; k++) { float g = zeq.masterVolumeEnvelope.Evaluate(Mathf.Clamp01((from + k / (float)sr) / end)); L[k] *= g; R[k] *= g; }
            return new Mix { left = L, right = R, sampleRate = sr, pieces = used, seconds = frames / (float)sr };
        }

        static float Draw(System.Random rnd, float lo, float hi) => lo + (float)rnd.NextDouble() * (hi - lo);

        static void Gather(CompositeZound c, float start, float pitch, float volume, System.Random rnd, int sr,
                           List<(float, float[], float[], float, Envelope, float)> pieces, ref float end) {
            bool anySolo = c.zoundEntries.Exists(e => e.solo);
            // A Zequence that picks one track per play: the first playable one at rest, or a seeded pick.
            int only = -1;
            if (c.mode != CompositeZound.Mode.Parallel) {
                var ok = new List<int>();
                for (int i = 0; i < c.zoundEntries.Count; i++) { var e = c.zoundEntries[i]; if (!e.mute && (!anySolo || e.solo) && c.TryGetEntryZound(e, out _)) ok.Add(i); }
                if (ok.Count == 0) return;
                if (rnd == null) only = ok[0];
                else { int total = 0; foreach (var i in ok) total += Mathf.Max(1, c.zoundEntries[i].chanceWeight); int pick = rnd.Next(total); foreach (var i in ok) { pick -= Mathf.Max(1, c.zoundEntries[i].chanceWeight); if (pick < 0) { only = i; break; } } }
            }
            for (int i = 0; i < c.zoundEntries.Count; i++) {
                var e = c.zoundEntries[i];
                if (only >= 0 && i != only) continue;
                if (e.mute || (anySolo && !e.solo)) continue;
                if (!c.TryGetEntryZound(e, out var z)) continue;
                if (rnd != null) {
                    float chance = e.overrideChance ? e.chance : e.chance * z.chance;
                    if (rnd.NextDouble() > chance) continue;
                }
                float pf = e.overridePitch ? e.pitch : e.pitch * (rnd != null ? Draw(rnd, z.minPitch, z.maxPitch) : ZequenceTimeline.Mid(z));
                float vf = e.overrideVolume ? e.volume : e.volume * (rnd != null ? Draw(rnd, z.minVolume, z.maxVolume) : 0.5f * (z.minVolume + z.maxVolume));
                float s = start + e.delay / Mathf.Max(pitch, 0.01f);
                float p = pitch * pf, v = volume * vf;
                if (z is Klip k) {
                    var clip = ZoundSapPlayback.LoadSourceClip(k, out bool pre);
                    if (clip == null || !string.IsNullOrEmpty(ZoundPcmCache.Validate(clip))) continue;
                    var pcm = ZoundPcmCache.Get(clip);
                    if (pcm == null || !pcm.valid) continue;
                    var ex = e.ownTrim && e.trimEnd > e.trimStart ? ZoundSapPlayback.Excerpt.Of(e.trimStart, e.trimEnd) : default;
                    // The chain exactly as a play of this excerpt uses it (old-style curves read on a converted copy).
                    float shift = pre && k.trimEnabled ? k.trimStart : 0f;
                    float a, b;
                    if (ex.on) { a = ex.start - shift; b = ex.end - shift; }
                    else if (!pre && k.trimEnabled) { a = k.trimStart; b = k.trimEnd > k.trimStart ? k.trimEnd : pcm.LengthSeconds; }
                    else { a = 0f; b = pcm.LengthSeconds; }
                    a = Mathf.Clamp(a, 0f, pcm.LengthSeconds); b = Mathf.Clamp(b, a, pcm.LengthSeconds);
                    if (b <= a) continue;
                    CurveAnchor.Axis? own = ex.on && !pre ? CurveAnchor.Axis.Of(k, pcm.LengthSeconds) : (CurveAnchor.Axis?)null;
                    var plan = ZoundSapPlayback.Plan(k, a * pcm.frequency, b * pcm.frequency, pcm.frequency, pcm.frames, own);
                    ZoundSapPlayback.TryGetPlayLength(k, ex, out float len);
                    var layout = plan.chain != null && !plan.chain.IsEmpty ? ChainLayout.Build(plan.chain, sr) : null;
                    float tail = layout != null ? layout.tailSeconds : 0f;
                    float seconds = len / Mathf.Max(p, 0.01f) + tail;
                    var r = ZoundDspOffline.Render(pcm.samples, pcm.channels, pcm.frequency, sr, plan.chain, p, 1f, seconds, a, b);
                    if (r == null || r.left == null) continue;
                    var env = e.volumeEnvelope != null && e.volumeEnvelope.enabled ? e.volumeEnvelope : null;
                    pieces.Add((s, r.left, r.right ?? r.left, v, env, len / Mathf.Max(p, 0.01f)));
                    end = Mathf.Max(end, s + r.left.Length / (float)sr);
                }
                else if (z is CompositeZound cz && !ReferenceEquals(cz, c) && !ZequenceHandler.CheckRecursiveness(cz, c)) {
                    Gather(cz, s, p, v, rnd, sr, pieces, ref end);
                }
            }
        }

        /// <summary>Writes the mix to a new audio file (imported so the engine can read its samples) and adds a Klip that
        /// plays it to the library. One undo step for the Klip; the file itself is a new asset.</summary>
        public static Klip Write(Zequence zeq, Mix mix, string name) {
            string folder = ZoundsProject.Instance.projectSettings.workFolderPath;
            if (string.IsNullOrEmpty(folder)) folder = "Assets/ZoundsData/Sources";
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            string path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, Safe(name) + ".wav").Replace('\\', '/'));
            WriteWav(path, mix.left, mix.right, mix.sampleRate);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is AudioImporter imp) {
                var s = imp.defaultSampleSettings;
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.PCM;
                imp.defaultSampleSettings = s;
                imp.SaveAndReimport();
            }
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            var audioRef = AudioRenderUtility.GetAudioReference(clip);
            Klip made = null;
            ZoundsWindow.ModifyZoundsProject("bake zequence to klip", () => {
                var k = new Klip(ZoundLibrary.GetUniqueZoundId());
                k.audioClipRef = audioRef;
                k.name = ZoundDictionary.EnsureUniqueZoundName(name);
                k.trimStart = 0f; k.trimEnd = clip != null ? clip.length : mix.seconds;
                k.volumeEnvelope = new Envelope(Zound.MinVolumeRange, Zound.MaxVolumeRange);
                k.pitchEnvelope = new Envelope(Zound.MinPitchRange, Zound.MaxPitchRange);
                k.tags = new List<int>(zeq.tags);
                if (ZoundEngine.IsInitialized()) ZoundDictionary.ValidateZoundRuntime(k);
                var lib = ZoundsProject.Instance.zoundLibrary;
                lib.klips.Add(k);
                lib.klips.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
                ZoundsWindowProperties.Instance.zoundTabProperties[0].dirty = true;
                made = k;
            }, true);
            return made;
        }

        static string Safe(string n) {
            foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            return string.IsNullOrWhiteSpace(n) ? "Baked" : n.Trim();
        }

        /// <summary>16-bit stereo PCM WAV.</summary>
        static void WriteWav(string path, float[] l, float[] r, int sr) {
            int frames = l.Length;
            using (var fs = new FileStream(path, FileMode.Create))
            using (var w = new BinaryWriter(fs)) {
                int dataBytes = frames * 2 * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + dataBytes); w.Write(new[] { 'W', 'A', 'V', 'E' });
                w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(sr); w.Write(sr * 4); w.Write((short)4); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(dataBytes);
                for (int i = 0; i < frames; i++) {
                    w.Write((short)Mathf.Clamp(Mathf.RoundToInt(l[i] * 32767f), -32768, 32767));
                    w.Write((short)Mathf.Clamp(Mathf.RoundToInt(r[i] * 32767f), -32768, 32767));
                }
            }
        }
    }

    /// <summary>The Bake card (T-0567): what to bake, how its randomness is settled, and the new Klip's name.</summary>
    internal sealed class ZequenceBakePopup : PopupWindowContent {

        Zequence zeq; ZequenceTimeline tl;
        bool range, random; int seed;
        string name;
        Label info;

        public static void Show(Vector2 at, Zequence zeq, ZequenceTimeline tl, bool isLocal) {
            if (zeq == null) return;
            var p = new ZequenceBakePopup { zeq = zeq, tl = tl, name = zeq.name + " (baked)", seed = UnityEngine.Random.Range(1, 99999), range = tl != null && tl.hasSel && tl.selB > tl.selA };
            UnityEditor.PopupWindow.Show(new Rect(at, Vector2.zero), p);
        }

        public override Vector2 GetWindowSize() => new Vector2(360f, 118f);

        public override void OnGUI(Rect rect) { }

        public override VisualElement CreateGUI() {
            var root = new VisualElement();
            ZS.Attach(root);
            root.style.paddingLeft = 6; root.style.paddingRight = 6; root.style.paddingTop = 6;
            float h = EditorGUIUtility.singleLineHeight;
            bool canRange = tl != null && tl.hasSel && tl.selB > tl.selA;
            VisualElement Row() { var r = new VisualElement(); r.style.flexDirection = FlexDirection.Row; r.style.marginBottom = 4; root.Add(r); return r; }

            var r1 = Row();
            ZuiToggleButton whole = null, sel = null, rest = null, rnd = null;
            IntegerField seedField = null;
            whole = ZS.Toggle("Whole Zequence", "Bake everything, from its start to where the last tail stops ringing.", !range, v => { range = !v; Sync(); }, "ZoundBtnFlatToggle", ZUICornerMask.Left, 120f, h, new Color(0.22f, 0.45f, 0.75f, 1f));
            sel = ZS.Toggle("Selected range", canRange ? "Bake only " + ZequenceTimeline.Seconds(tl.selA) + " – " + ZequenceTimeline.Seconds(tl.selB) + " of the timeline; what sounds in it may ring out past its end."
                                                       : "Select a time range on the ruler or a track first.", range, v => { range = v; Sync(); }, "ZoundBtnFlatToggle", ZUICornerMask.Right, 120f, h, new Color(0.22f, 0.45f, 0.75f, 1f));
            sel.SetEnabled(canRange);
            r1.Add(whole); r1.Add(sel);

            var r2 = Row();
            rest = ZS.Toggle("Resting values", "No randomness: every range at its middle, every track plays, game-code values at rest.", !random, v => { random = !v; Sync(); }, "ZoundBtnFlatToggle", ZUICornerMask.Left, 120f, h, new Color(0.22f, 0.45f, 0.75f, 1f));
            rnd = ZS.Toggle("Random run", "One play's worth of random choices (pitch and volume ranges, chances, which track a picking Zequence plays), drawn from the seed beside it: the same seed bakes the same result.", random, v => { random = v; Sync(); }, "ZoundBtnFlatToggle", ZUICornerMask.Right, 120f, h, new Color(0.22f, 0.45f, 0.75f, 1f));
            seedField = new IntegerField { value = seed, tooltip = "The random run's seed. The same seed repeats the same bake." };
            seedField.AddToClassList("zs-imgui-field"); seedField.style.width = 70; seedField.style.marginLeft = 6;
            seedField.RegisterValueChangedCallback(e => seed = e.newValue);
            r2.Add(rest); r2.Add(rnd); r2.Add(seedField);

            var r3 = Row();
            var nameField = new TextField { value = name, tooltip = "The new Klip's name (and its audio file's)." };
            nameField.AddToClassList("zs-imgui-field"); nameField.style.width = 250;
            nameField.RegisterValueChangedCallback(e => name = e.newValue);
            r3.Add(nameField);
            var bake = ZS.Button("Bake", "Render now and add the new Klip to the library. The Zequence is not changed.", "RichButton", DoBake, ZUICornerMask.All, 70f, h);
            bake.style.marginLeft = 6;
            r3.Add(bake);

            info = new Label(); info.AddToClassList("zs-lbl"); info.AddToClassList("zs-greymini");
            info.style.height = h; info.style.whiteSpace = WhiteSpace.NoWrap; info.style.overflow = Overflow.Hidden;
            root.Add(info);

            void Sync() {
                whole.SetValueWithoutNotify(!range); sel.SetValueWithoutNotify(range);
                rest.SetValueWithoutNotify(!random); rnd.SetValueWithoutNotify(random);
                foreach (var t in new[] { whole, sel, rest, rnd }) ZS.ApplyOnColor(t, new Color(0.22f, 0.45f, 0.75f, 1f));
                seedField.SetEnabled(random);
            }
            Sync();
            return root;
        }

        void DoBake() {
            var o = new ZequenceBake.Options { range = range && tl != null && tl.hasSel, from = tl != null ? tl.selA : 0f, to = tl != null ? tl.selB : 0f, random = random, seed = seed, name = name };
            var mix = ZequenceBake.Render(zeq, o);
            if (mix == null || mix.pieces == 0) { info.text = "Nothing sounds there to bake."; return; }
            var k = ZequenceBake.Write(zeq, mix, name);
            info.text = k != null ? "Baked " + mix.seconds.ToString("0.00") + " s into '" + k.name + "'." : "The bake could not be written.";
            if (k != null) { editorWindow?.Close(); KlipEditorWindowTK.Open(k, false); }
        }
    }
}
