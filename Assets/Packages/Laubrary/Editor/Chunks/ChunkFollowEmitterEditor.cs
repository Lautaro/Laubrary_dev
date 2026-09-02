using System.Collections.Generic;
using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    /// <summary>
    /// Inspector for <see cref="ChunkFollowEmitter"/> (AgentHQ T-0080). Fully ZUI, no default inspector block:
    /// every value on this component is either one of the two sanctioned raw islands (an object reference) or
    /// has a real Z control, so falling back to FillDefaultInspector would only re-render the same fields as
    /// native checkboxes and bare numbers next to the ZUI ones.
    ///
    /// Its non-obvious job is the three ways this component can silently do nothing. All three are surfaced as
    /// STATE on the control they belong to (enabled-ness + a tooltip that reads for the current state — carried
    /// by an enclosing ENABLED element wherever the control itself is disabled, since a disabled UITK element
    /// does not reliably receive the pointer events a tooltip resolves from) plus one
    /// permanently-reserved status line whose TEXT changes — never by adding or removing elements, which would
    /// reflow the inspector under the user's cursor mid-edit (ui-layout-rules.md → "Stable workspace").
    /// </summary>
    [CustomEditor(typeof(ChunkFollowEmitter))]
    public class ChunkFollowEmitterEditor : UnityEditor.Editor
    {
        const float Wide = 150f;
        const float Ref = 200f;
        const float Num = 70f;

        ChunkFollowEmitter _e;

        Label _status;
        ZuiToggleButton _sprayToggle, _spawnToggle, _aimsIndicator, _onlyMovingToggle, _playOnAwakeToggle;
        VisualElement _splashInterval, _spawnInterval, _aim;
        // The enabled carrier for _aimsIndicator's tooltip — a disabled control cannot show one itself.
        VisualElement _aimsRow;
        Button _playButton, _finishButton, _stopButton;

        // Thumbnail cache for the spec's LauAsset chip — kept alive across the periodic Refresh() so the
        // picker doesn't re-fetch a thumbnail twelve times a second (matches ChunkSpecEditor/ChunkWindow.Recipe.cs).
        readonly Dictionary<UnityEngine.Object, Texture2D> _thumbCache = new Dictionary<UnityEngine.Object, Texture2D>();

        public override VisualElement CreateInspectorGUI()
        {
            _e = (ChunkFollowEmitter)target;

            var root = new VisualElement();
            // MANDATORY first line for any root ZUI does not own: without it every Z control renders unstyled
            // (MicroSliders collapse to zero height and pile up), and no headless probe would catch it.
            Z.Attach(root);

            root.Add(BuildSource());
            root.Add(BuildEmission());
            root.Add(BuildTravel());
            root.Add(BuildPlayback());

            Refresh();
            // The two dependencies live in ANOTHER asset (the spec's own modules, edited in the Chunks window),
            // and play state changes with no inspector event at all — so the state readouts are re-derived on a
            // slow poll rather than only on this inspector's own edits.
            root.schedule.Execute(Refresh).Every(250);
            return root;
        }

        // ── boxes ───────────────────────────────────────────────────────────────

        VisualElement BuildSource()
        {
            var box = Z.Box("Source", "What this emitter fires, and what it chases while firing.");

            const string specTip = "The burst recipe this emitter repeatedly fires. It drives exactly two of the " +
                "recipe's capabilities — Palette Splash and Pyre Blast — each on its own interval.";
            // A reference is always a picker, never a raw ObjectField — the same LauAssetElement chip every
            // other ChunkSpec reference in the package uses (ChunkWindow.Recipe.cs's AssetPicker helper).
            // Picking through it doesn't rebuild this inspector's tree (nothing here rebuilds mid-edit), so —
            // exactly like ChunkWindow.PyreBlastCard's own plain-Dial asset pickers — the chip's own label
            // catches up on the next Refresh() rather than instantly; that is the established convention,
            // not a shortcut taken here.
            box.Add(Z.Field("Chunk Spec", specTip,
                LauAssetElement.Build(_e.spec,
                    v => Dial("Follow emitter spec", () => _e.spec = v as ChunkSpec),
                    typeof(ChunkSpec), _thumbCache, "Follow Emitter Spec", "Assets/Chunks", specTip)));

            const string targetTip = "The transform this emitter follows every frame. Leave empty to follow the " +
                "emitter's own transform. A target destroyed mid-flight stops the emitter cleanly.";
            box.Add(Z.Field("Target", targetTip,
                Z.Object<Transform>(_e.target, targetTip, v => Dial("Follow emitter target", () => _e.target = v),
                    Ref, allowSceneObjects: true)));

            // Permanently reserved single line: fixed height, no wrap, truncate rather than grow. Only its text
            // ever changes, so nothing below it can move while the user is aiming at a control.
            _status = Z.Text("", ZuiText.Subtle,
                "What this emitter will actually do right now — including the reasons it would do nothing.");
            _status.style.height = 16f;
            _status.style.whiteSpace = WhiteSpace.NoWrap;
            _status.style.overflow = Overflow.Hidden;
            box.Add(_status);
            return box;
        }

        VisualElement BuildEmission()
        {
            var box = Z.Box("Emission", "The two things this emitter repeats, each on its own interval.");

            const string sprayTip = "Spray the recipe's Palette Splash on an interval, at the tracked position.";
            const string sprayEveryTip = "Seconds between sprays. Small values read as a continuous trail, " +
                "larger ones as separate puffs.";
            _sprayToggle = Z.Toggle("Spray Splash", sprayTip, _e.spraySplash,
                v => { Dial("Spray splash", () => _e.spraySplash = v); Refresh(); });
            _splashInterval = Z.MicroSlider("Splash Every (s)", _e.splashInterval, 0.01f, 1f, sprayEveryTip,
                v => Dial("Splash interval", () => _e.splashInterval = v), Wide, showValue: true);
            box.Add(Z.Row(_sprayToggle, Z.HSpace(), _splashInterval));

            const string spawnTip = "Re-fire the recipe's Pyre Blast on an interval — the flare-up behind " +
                "the target.";
            const string spawnEveryTip = "Seconds between spawns. This is the rate the emitter flares up.";
            _spawnToggle = Z.Toggle("Repeat Spawn", spawnTip, _e.repeatSpawn,
                v => { Dial("Repeat spawn", () => _e.repeatSpawn = v); Refresh(); });
            _spawnInterval = Z.MicroSlider("Spawn Every (s)", _e.spawnInterval, 0.02f, 10f, spawnEveryTip,
                v => Dial("Spawn interval", () => _e.spawnInterval = v), Wide, showValue: true);
            box.Add(Z.Row(_spawnToggle, Z.HSpace(), _spawnInterval));

            const string aimTip = "Which way the burst direction points each tick: opposite the target's travel " +
                "(debris thrown out behind it), along its travel (a thrust reading), or the spec's own fixed " +
                "direction with travel ignored.";
            _aim = Z.Segmented((int)_e.aim, new[] { "Behind", "Ahead", "Spec" }, aimTip,
                i => { Dial("Follow emitter aim", () => _e.aim = (ChunkFollowAim)i); Refresh(); });
            box.Add(Z.Field("Aim", aimTip, _aim));

            // A READ-ONLY mirror of the spec's own splash setting, permanently disabled: this is the third
            // silent failure (spray backwards computed correctly, splash ignores it), and flipping that flag
            // from here would be editing a module this inspector was never pointed at. Its latched state
            // carries the fact; the explanation of the fix is a tooltip.
            //
            // That tooltip is the ONLY carrier for the fix, so it cannot live on the disabled toggle: a
            // disabled UITK element does not reliably receive the pointer events a tooltip resolves from, so
            // the text would simply never appear. It goes on an ENABLED wrapper row instead (see Refresh),
            // and the toggle's OWN tooltip is left empty so it can never win the lookup and swallow it. The
            // row exists for that reason alone — not to constrain the toggle's width.
            _aimsIndicator = Z.Toggle("Splash Follows Burst", "", _e.SplashWillAim, v => { });
            _aimsIndicator.SetEnabled(false);
            _aimsRow = Z.Row(_aimsIndicator);
            box.Add(_aimsRow);
            return box;
        }

        VisualElement BuildTravel()
        {
            var box = Z.Box("Travel", "How the target's direction of travel is measured — the aim is only as " +
                                     "steady as this.");

            const string smoothTip = "How long the measured direction takes to catch up with a change of course. " +
                "Higher is steadier but laggier; 0 uses the raw per-frame movement, which jitters badly on a " +
                "near-stationary target.";
            const string minSpeedTip = "Speed below which the target counts as standing still. Under it the last " +
                "good direction is held instead of recomputed from meaningless movement — never set it to 0.";
            box.Add(Z.Row(
                Z.MicroSlider("Smoothing (s)", _e.directionSmoothing, 0f, 0.5f, smoothTip,
                    v => Dial("Direction smoothing", () => _e.directionSmoothing = v), Wide, showValue: true),
                Z.HSpace(),
                Z.MicroSlider("Min Speed", _e.minTravelSpeed, 0f, 2f, minSpeedTip,
                    v => Dial("Min travel speed", () => _e.minTravelSpeed = v), Wide, showValue: true)));

            const string onlyMovingTip = "Only emit while the target is moving faster than Min Speed. Off keeps " +
                "emitting from a standing target, aimed along the last direction it travelled.";
            _onlyMovingToggle = Z.Toggle("Only While Moving", onlyMovingTip, _e.emitOnlyWhileMoving,
                v => { Dial("Emit only while moving", () => _e.emitOnlyWhileMoving = v); Refresh(); });
            box.Add(_onlyMovingToggle);
            return box;
        }

        VisualElement BuildPlayback()
        {
            var box = Z.Box("Playback", "When emission starts, how long it lasts, and how deep it draws.");

            const string awakeTip = "Start emitting as soon as this component wakes up. Off means nothing " +
                "happens until game code calls Play().";
            const string durationTip = "Total emission time in seconds. 0 keeps going until Stop() is called — " +
                "left as a number rather than a slider because there is no honest ceiling to cap it at.";
            const string orderTip = "Sorting order every spawned particle and blast falls back to when the spec " +
                "has no layer stack configured. The layer stack wins whenever it names the module's slot.";
            _playOnAwakeToggle = Z.Toggle("Play On Awake", awakeTip, _e.playOnAwake,
                v => Dial("Play on awake", () => _e.playOnAwake = v));
            box.Add(Z.Row(
                _playOnAwakeToggle,
                Z.HSpace(),
                Z.Field("Duration (s)", durationTip,
                    Z.Float(_e.duration, durationTip, v => Dial("Follow emitter duration", () => _e.duration = Mathf.Max(0f, v)), Num)),
                Z.HSpace(),
                Z.Field("Sorting Order", orderTip,
                    Z.Int(_e.sortingOrder, orderTip, v => Dial("Follow emitter sorting order", () => _e.sortingOrder = v), Num))));

            _playButton = Z.Button("Play", "Start emitting now. Only available while the game is playing.",
                () => { _e.Play(); Refresh(); });
            // Two distinct stops, both surfaced: StopEmitting lets whatever is already in flight finish
            // naturally (a grace period before the container is destroyed); Stop tears everything down
            // immediately. Collapsing them into one button would hide a real behavioural difference.
            _finishButton = Z.Button("Finish",
                "Stop emitting but let everything already in flight finish naturally, instead of cutting it " +
                "off mid-air.",
                () => { _e.StopEmitting(); Refresh(); });
            _stopButton = Z.Button("Stop",
                "Stop emitting and tear down everything this emitter spawned immediately.",
                () => { _e.Stop(); Refresh(); });
            _playButton.style.width = 70f;
            _finishButton.style.width = 70f;
            _stopButton.style.width = 70f;
            box.Add(Z.Row(_playButton, Z.HSpace(), _finishButton, Z.HSpace(), _stopButton));
            return box;
        }

        // ── the three silent failures, surfaced ─────────────────────────────────

        void Refresh()
        {
            if (_e == null || _status == null) return;

            var spec = _e.spec;
            bool hasSpec = spec != null;
            bool splashOn = hasSpec && spec.Has<PaletteSplash>();
            bool spawnOn = hasSpec && spec.Has<PyreBlast>();
            bool aimsAtTravel = _e.aim != ChunkFollowAim.SpecDirection;

            // (a) + (b): a control for a module that is not there, or is switched off in the spec, is not a
            // control the user can usefully touch — so it greys out and says why, instead of accepting an edit
            // that changes nothing observable.
            _sprayToggle.SetEnabled(splashOn);
            _sprayToggle.tooltip = !hasSpec
                ? "Assign a Chunk Spec first — there is no Palette Splash to spray."
                : splashOn
                    ? "Spray the recipe's Palette Splash on an interval, at the tracked position."
                    : "The assigned recipe has no Palette Splash, so nothing would be sprayed. " +
                      "Add one in the Chunks window.";
            _splashInterval.SetEnabled(splashOn && _e.spraySplash);

            _spawnToggle.SetEnabled(spawnOn);
            _spawnToggle.tooltip = !hasSpec
                ? "Assign a Chunk Spec first — there is no Pyre Blast to fire."
                : spawnOn
                    ? "Re-fire the recipe's Pyre Blast on an interval — the flare-up behind the target."
                    : "The assigned recipe has no Pyre Blast, so nothing would be spawned. " +
                      "Add one in the Chunks window.";
            _spawnInterval.SetEnabled(spawnOn && _e.repeatSpawn);

            _aim.SetEnabled(_e.CanEmit);

            // (c) the trap: the aim is computed correctly and the splash ignores it, because aiming is the
            // SPLASH's opt-in. Shown as its own latched state — never flipped for the user.
            _aimsIndicator.SetValueWithoutNotify(_e.SplashWillAim);
            // On the ENABLED wrapper, never on the disabled toggle — see BuildEmission. Composed per state,
            // so it never reads as "when X is set…" while X is off.
            _aimsRow.tooltip = !splashOn
                ? "Whether the recipe's Palette Splash aims itself at this emitter's direction. There is no " +
                  "splash enabled to aim right now."
                : _e.SplashWillAim
                    ? "On: the recipe's Palette Splash has 'Follow Burst Direction' enabled, so the spray aims " +
                      "where this emitter points it."
                    : "OFF: the recipe's Palette Splash uses its own fixed angle, so 'Behind'/'Ahead' will NOT " +
                      "aim the spray. Turn on 'Follow Burst Direction' in the recipe's Palette Splash section " +
                      "(the Chunks window) — this inspector will not change another module's setting for you.";

            bool playing = Application.isPlaying;
            _playButton.SetEnabled(playing && _e.CanEmit);
            _finishButton.SetEnabled(playing && _e.IsPlaying);
            _stopButton.SetEnabled(playing && _e.IsPlaying);

            _status.text = StatusText(hasSpec, splashOn, spawnOn, aimsAtTravel, playing);
        }

        string StatusText(bool hasSpec, bool splashOn, bool spawnOn, bool aimsAtTravel, bool playing)
        {
            if (!hasSpec) return "No Chunk Spec assigned — this emitter will do nothing.";
            if (!splashOn && !spawnOn)
                return "The recipe has neither a Palette Splash nor a Pyre Blast — nothing to emit.";
            if (!_e.spraySplash && !_e.repeatSpawn)
                return "Both repeats are switched off above — nothing to emit.";
            if (splashOn && _e.spraySplash && aimsAtTravel && !_e.SplashWillAim)
                return "Spray will NOT aim: the spec's splash has 'Follow Burst Direction' off.";

            if (playing && _e.IsPlaying)
            {
                var t = _e.Travel;
                string dir = t.HasDirection ? Mathf.RoundToInt(t.DirectionDeg) + "°" : "no travel yet";
                return "Playing · travel " + dir + (t.IsMoving ? " (moving)" : " (held)");
            }

            string what = splashOn && _e.spraySplash
                ? (spawnOn && _e.repeatSpawn ? "splash + spawn" : "splash")
                : "spawn";
            return playing ? "Stopped · would emit " + what : "Ready · emits " + what + " on play.";
        }

        // ── undo ────────────────────────────────────────────────────────────────

        void Dial(string label, System.Action apply)
        {
            Undo.RecordObject(_e, label);
            apply();
            EditorUtility.SetDirty(_e);
        }
    }
}
