// SpriteFxStackWindow — the dedicated browse / create / tag authoring window for a SpriteFxSpec (the "SpriteFx
// Stack" asset). Same AssetKit base every other Laubrary tool uses (ZuiAssetWindow<T>: assign / New / Duplicate /
// Rename / Delete / thumbnail browser for free), so this window only has to lay out the per-asset editor.
//
// It hosts the reusable SpriteFxStackView control (slice 3) for the effect stack itself, then a small Timeline
// section for the play-through duration, life-remap envelope and hashing seed. No input-sprite scrub / play
// PREVIEW here — that is slice 5.
//
// Every dial routes through Dial(...) (Undo.RecordObject BEFORE the mutation, then SetDirty), matching ChunkWindow
// so a whole session's edits don't coalesce into one Undo step.
using Laubrary.AssetKit.Editor;
using Laubrary.SpriteFx;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.SpriteFx.Editor
{
    /// <summary>
    /// Authoring window for a <see cref="SpriteFxSpec"/> — the reusable colour/mask "SpriteFx Stack" asset.
    /// Embeds the host-agnostic <see cref="SpriteFxStackView"/> for the effect stack and adds a Timeline section.
    /// </summary>
    public class SpriteFxStackWindow : ZuiAssetWindow<SpriteFxSpec>
    {
        [MenuItem("Laubrary/SpriteFx Stacks")]
        public static void Open() => GetWindow<SpriteFxStackWindow>("SpriteFx Stacks");

        /// Same entry-point shape as ChunkWindow.OpenFor / PyreWindow.OpenFor — lets a LauAssetField's Edit
        /// button (and the shared LauAssetEditors registry) jump straight into this SpriteFxSpec's own editor.
        public static void OpenFor(SpriteFxSpec spec)
        {
            var w = GetWindow<SpriteFxStackWindow>("SpriteFx Stacks");
            if (spec != null) w.SetAsset(spec);
        }

        protected override string TypeLabel => "SpriteFx Stack";
        protected override string NewAssetName => "New SpriteFx Stack";
        protected override string DefaultFolder => "Assets/SpriteFx";

        const float Num = 70f;
        const float Wide = 150f;

        SpriteFxSpec Spec => Current;

        // ── mutation helper (the Undo contract every dial routes through) ────────────────────
        void Dial(string undoLabel, System.Action apply)
        {
            var s = Spec;
            if (s == null) return;
            Undo.RecordObject(s, undoLabel);
            apply();
            EditorUtility.SetDirty(s);
        }

        protected override void BuildAsset(VisualElement root, SpriteFxSpec spec)
        {
            root.style.flexGrow = 1f;
            root.style.minHeight = 0f;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            var body = scroll.contentContainer;

            BuildTags(body, spec);
            BuildStack(body, spec);
            BuildTimeline(body, spec);

            root.Add(scroll);
        }

        // Tag editing (assigned-tag chips + the Tags… picker). LauTagField is IMGUI-only and the UITK
        // ZuiAssetWindow base — unlike the IMGUI LaubraryAssetWindow base — does NOT surface tags in its
        // toolbar, so this window draws it as a small, width-capped IMGUI island. Same LauTagLibrary GUID
        // side-table every other Laubrary asset window tags through, so a SpriteFx Stack browses/filters
        // by tag identically. (If a UITK tag control lands, or ZuiAssetWindow gains a tag row, drop this.)
        void BuildTags(VisualElement root, SpriteFxSpec spec)
        {
            var tags = new IMGUIContainer(() => LauTagField.Draw(spec));
            tags.style.width = 360f;
            tags.style.flexShrink = 0f;
            root.Add(tags);
        }

        void BuildStack(VisualElement root, SpriteFxSpec spec)
        {
            var s = Z.Section("Stack",
                "The colour / mask effects applied in order while the stack plays. Each effect's animatable " +
                "values are resolved at the current life every frame; drag the grip to reorder.");

            var host = new SpriteFxStackView.Host
            {
                // Fires once per gesture, before the first mutation.
                OnBeforeChange = () => { var sp = Spec; if (sp != null) Undo.RecordObject(sp, "Edit SpriteFx Stack"); },
                // Fires after every value edit — mark the asset dirty (retained controls repaint themselves).
                OnChanged = () => { var sp = Spec; if (sp != null) EditorUtility.SetDirty(sp); },
                // A structural change (add / remove / reorder / enable): the control already rebuilt its own
                // rows in place; re-run the whole panel so anything downstream stays in sync (safe — the
                // control's fold/curve state is keyed per effect instance and survives the rebuild).
                Rebuild = Rebuild,
                ControlWidth = Wide,
            };
            s.Add(SpriteFxStackView.Build(spec.modifiers, host));
            root.Add(s);
        }

        void BuildTimeline(VisualElement root, SpriteFxSpec spec)
        {
            var s = Z.Section("Timeline",
                "How long one play-through lasts and how raw progress is remapped into the life value fed to " +
                "every effect's curves.");

            const string durTip = "How long one play-through lasts, in seconds.";
            s.Add(Z.MicroSlider("Duration (s)", spec.duration, 0.02f, 2f, durTip,
                v => Dial("SpriteFx duration", () => spec.duration = Mathf.Max(0.001f, v)), Wide, showValue: true));

            const string envTip = "Optional easing / remap of raw progress (0→1 over Duration) into the LIFE " +
                "value fed to every effect's curves. Identity by default; a triangle (0→1→0) turns a " +
                "monotonic effect into a pulse, an ease softens the ends.";
            s.Add(Z.Field("Life remap", envTip,
                Z.Curve(spec.envelope, envTip, v => Dial("SpriteFx envelope", () => spec.envelope = v))));

            const string seedTip = "Seed for any hashing effect (LayerDissolve scatter, AlphaMask noise). " +
                "Irrelevant for a plain Brightness / Tint flash.";
            s.Add(Z.Field("Seed", seedTip,
                Z.Int(spec.seed, seedTip, v => Dial("SpriteFx seed", () => spec.seed = v), Num)));

            root.Add(s);
        }
    }
}
