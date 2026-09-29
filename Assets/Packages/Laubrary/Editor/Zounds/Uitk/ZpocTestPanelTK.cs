using System.Collections.Generic;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// "Test as game code" (T-0492): pretends to be the game, so the ZPOC display can be seen working without writing any.
    /// It lists every ZPOC id this sound's chain declares, with a value and a pattern each, and while Drive is on it sends
    /// those values to every play of this sound in the editor — its own (Play ×N, spaced a little apart so they overlap)
    /// and the ordinary Play button's alike — through the real tokens, exactly as game code would.
    ///
    /// Patterns: Hold sends the value to every instance; Ramp sweeps each instance slowly around it, each on its own phase;
    /// Jitter jumps each instance to a new value near it every 0.3 s. The last two make several instances disagree, which is
    /// what the spread display is for. Project-wide sends the value once for everything instead, the global path.
    ///
    /// Test state, not saved data: nothing here is recorded for Undo, and turning Drive off clears every value it set.
    /// Off by default, so in Play mode it never fights the real game unless asked to.
    /// </summary>
    public class ZpocTestPanelTK : VisualElement {

        enum Pattern { Hold = 0, Ramp = 1, Jitter = 2 }

        class Row {
            public string id;
            public float value = 0.5f;
            public Pattern pattern;
            public bool global;
        }

        const float RowH = 20f;
        readonly Zound zound;
        readonly List<Row> rows = new List<Row>();
        readonly List<ZoundToken> ownPlays = new List<ZoundToken>();
        readonly HashSet<string> globalsSet = new HashSet<string>();
        bool drive;
        int instances = 3;
        string builtIds;

        public ZpocTestPanelTK(Zound zound) {
            this.zound = zound;
            style.flexShrink = 0;
            schedule.Execute(Tick).Every(33);
            RegisterCallback<DetachFromPanelEvent>(_ => StopDriving());
        }

        /// <summary>The distinct ZPOC ids this sound's chain declares, in order, as display strings.</summary>
        List<string> DeclaredIds() {
            var ids = new List<string>();
            var seen = new HashSet<string>();
            var chain = Dsp.ZoundDspPlayback.ResolveChain(zound, out _);
            if (chain == null) return ids;
            foreach (var m in chain.modifiers) {
                if (!m.HasZpoc) continue;
                var key = ZpocKeys.Key(m.zpocId);
                if (key != null && seen.Add(key)) ids.Add(m.zpocId);
            }
            return ids;
        }

        void Rebuild(List<string> ids) {
            Clear();
            // Keep each id's settings across a rebuild; drop ids that are gone.
            var old = new Dictionary<string, Row>();
            foreach (var r in rows) old[ZpocKeys.Key(r.id) ?? ""] = r;
            rows.Clear();
            foreach (var id in ids) rows.Add(old.TryGetValue(ZpocKeys.Key(id) ?? "", out var r) ? Rename(r, id) : new Row { id = id });
            if (rows.Count == 0) { style.display = DisplayStyle.None; return; }
            style.display = DisplayStyle.Flex;

            Add(Header());
            foreach (var r in rows) Add(IdRow(r));
        }

        static Row Rename(Row r, string id) { r.id = id; return r; }

        VisualElement HRow() {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row; r.style.height = RowH; r.style.flexShrink = 0; r.style.marginBottom = 1f;
            return r;
        }

        static VisualElement Gap(float w) { var e = new VisualElement(); e.style.width = w; e.style.flexShrink = 0; return e; }

        VisualElement Header() {
            var r = HRow();
            var title = new Label("Code test") {
                tooltip = "Pretends to be the game: while Drive is on, every play of this sound in the editor takes the values below, sent through its token exactly as game code would (token.SetZpoc). Watch the amber on the chain above while it plays. Nothing here is saved."
            };
            title.AddToClassList("zs-text-subheader"); title.AddToClassList("zs-subheader");
            title.style.width = 128f; title.style.flexShrink = 0;
            r.Add(title);
            ZuiToggleButton driveT = null;
            driveT = ZS.Toggle("Drive", "On: every play of this sound in the editor follows the values below. Off: plays go back to where each ZPOC rests (or to the game's values in Play mode).",
                drive, v => { if (v) drive = true; else StopDriving(); driveT.SetValueWithoutNotify(drive); }, "RichToggle", ZUICornerMask.All, 52f, RowH - 2f);
            r.Add(driveT);
            r.Add(Gap(10f));
            var counts = new[] { 1, 3, 8 };
            var tg = new ZuiToggleButton[counts.Length];
            for (int i = 0; i < counts.Length; i++) {
                int n = counts[i], idx = i;
                var corner = i == 0 ? ZUICornerMask.Left : i == counts.Length - 1 ? ZUICornerMask.Right : ZUICornerMask.None;
                tg[i] = ZS.Toggle("×" + n, "How many overlapping instances Play starts: " + n + (n == 1 ? "." : ", a little apart, so several are alive at once and the chain shows their spread."),
                    instances == n, v => { instances = n; for (int k = 0; k < tg.Length; k++) tg[k].SetValueWithoutNotify(k == idx); }, "RichToggle", corner, 30f, RowH - 2f);
                r.Add(tg[i]);
            }
            r.Add(Gap(4f));
            r.Add(ZS.Button("Play", "Starts the chosen number of instances of this sound, 0.15 s apart, and turns Drive on.", "RichButton",
                () => { drive = true; PlayInstances(); Rebuild(DeclaredIds()); }, ZUICornerMask.All, 44f, RowH - 2f));
            r.Add(Gap(2f));
            r.Add(ZS.Button("Stop", "Stops the instances Play started (a Looper keeps going until stopped).", "RichButton",
                StopOwnPlays, ZUICornerMask.All, 44f, RowH - 2f));
            return r;
        }

        VisualElement IdRow(Row row) {
            var r = HRow();
            var label = new Label("⚡ " + row.id) { tooltip = "What game code would send as '" + row.id + "'." };
            label.AddToClassList("zs-zpocmark");
            label.style.width = 128f; label.style.flexShrink = 0; label.style.unityTextAlign = TextAnchor.MiddleLeft;
            r.Add(label);
            ZuiSkinSlider s = null;
            s = ZS.Slider("Value  " + row.value.ToString("0.00"), row.value, 0f, 1f, "",
                v => { row.value = v; s.text = "Value  " + v.ToString("0.00"); }, ZuiSkinSlider.LabelMode.LabelOnly, 0.5f, "Default", 200f, RowH - 2f);
            r.Add(s);
            r.Add(Gap(6f));
            string[] names = { "Hold", "Ramp", "Jitter" };
            string[] tips = {
                "Hold: every instance gets exactly this value.",
                "Ramp: each instance sweeps slowly around the value (±0.3, one cycle in 4 s), each on its own phase, so their values spread.",
                "Jitter: each instance jumps to a new value near this one (±0.3) every 0.3 s, like a jumpy game signal."
            };
            var pt = new ZuiToggleButton[3];
            ZuiToggleButton globalT = null;
            // Tooltips say what happens in the CURRENT state, and patterns are off while the value goes project-wide.
            void Sync() {
                s.tooltip = row.global ? "The value sent once, project-wide, to every play that has not set this id itself. 0..1, as game code sends it."
                          : row.pattern == Pattern.Hold ? "The value every instance is sent. 0..1, as game code sends it."
                          : row.pattern == Pattern.Ramp ? "The middle each instance sweeps around (±0.3). 0..1, as game code sends it."
                          : "The middle each instance jumps around (±0.3). 0..1, as game code sends it.";
                for (int k = 0; k < 3; k++) pt[k]?.SetEnabled(!row.global);
                if (globalT != null) globalT.tooltip = row.global
                    ? "On: the value is sent once for everything (ZoundEngine.SetGlobalZpoc); patterns do not apply. Click to send it to each play instead, with its pattern."
                    : "Off: each play is sent its own value, with the pattern chosen. Click to send one value for everything instead (ZoundEngine.SetGlobalZpoc): every play that has not set this id itself follows it.";
            }
            for (int i = 0; i < 3; i++) {
                int idx = i;
                var corner = i == 0 ? ZUICornerMask.Left : i == 2 ? ZUICornerMask.Right : ZUICornerMask.None;
                pt[i] = ZS.Toggle(names[i], tips[i], (int)row.pattern == i,
                    v => { row.pattern = (Pattern)idx; for (int k = 0; k < 3; k++) pt[k].SetValueWithoutNotify(k == idx); Sync(); }, "RichToggle", corner, 48f, RowH - 2f);
                r.Add(pt[i]);
            }
            r.Add(Gap(6f));
            globalT = ZS.Toggle("Project-wide", "", row.global, v => { row.global = v; if (!v) ClearGlobal(row.id); Sync(); }, "RichToggle", ZUICornerMask.All, 86f, RowH - 2f);
            r.Add(globalT);
            Sync();
            return r;
        }

        void PlayInstances() {
            PruneOwn();
            for (int i = 0; i < instances; i++) {
                int delayMs = i * 150;
                schedule.Execute(() => {
                    var t = ZoundEngine.PlayZound(zound, new ZoundArgs {
                        startImmediately = true, delay = 0f, chanceOverride = 1f, ignoreCooldown = true, bypassGlobalSolo = true,
                    });
                    if (t != null) { ownPlays.Add(t); Send(t, 0f); }
                }).StartingIn(delayMs);
            }
        }

        void StopOwnPlays() {
            foreach (var t in ownPlays) { try { if (t != null && t.state != ZoundToken.State.Killed) t.Kill(); } catch { } }
            ownPlays.Clear();
        }

        void PruneOwn() { ownPlays.RemoveAll(t => t == null || t.state == ZoundToken.State.Killed); }

        void StopDriving() {
            if (!drive) return;
            drive = false;
            var tokens = ZoundEngine.LiveTokens;
            if (tokens != null)
                foreach (var t in tokens)
                    if (t != null && ReferenceEquals(t.zound, zound))
                        foreach (var r in rows) t.ClearZpoc(r.id);
            foreach (var id in globalsSet) ZoundEngine.ClearGlobalZpoc(id);
            globalsSet.Clear();
        }

        void ClearGlobal(string id) {
            if (globalsSet.Remove(id)) ZoundEngine.ClearGlobalZpoc(id);
        }

        void Tick() {
            if (panel == null) return;
            var ids = DeclaredIds();
            var sig = string.Join("\u0001", ids);
            if (sig != builtIds) { builtIds = sig; Rebuild(ids); }
            if (!drive) return;
            PruneOwn();

            foreach (var r in rows) {
                if (r.global) { ZoundEngine.SetGlobalZpoc(r.id, r.value); globalsSet.Add(r.id); }
                else ClearGlobal(r.id);
            }
            var tokens = ZoundEngine.LiveTokens;
            if (tokens == null) return;
            float now = Time.realtimeSinceStartup;
            int index = 0;
            for (int i = 0; i < tokens.Count; i++) {
                var t = tokens[i];
                if (t == null || !ReferenceEquals(t.zound, zound) || t.state == ZoundToken.State.Killed) continue;
                Send(t, now, index++);
            }
        }

        void Send(ZoundToken t, float now, int index = 0) {
            foreach (var r in rows) {
                if (r.global) { t.ClearZpoc(r.id); continue; }
                float v = r.value;
                if (r.pattern == Pattern.Ramp) v += 0.3f * Mathf.Sin(now * Mathf.PI * 0.5f + index * 1.3f);
                else if (r.pattern == Pattern.Jitter) {
                    uint h = (uint)(Mathf.FloorToInt(now / 0.3f) * 73856093) ^ (uint)((index + 1) * 19349663);
                    h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                    v += ((h & 0xFFFF) / 65535f * 2f - 1f) * 0.3f;
                }
                t.SetZpoc(r.id, Mathf.Clamp01(v));
            }
        }
    }
}
