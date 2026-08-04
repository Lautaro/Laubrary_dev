// SpriteFxFilterEditor — a curated UI Toolkit inspector for the runtime SpriteFxFilter component, so a designer can
// author it without wading through the raw [SerializeReference] modifiers list Unity would otherwise draw.
//
// The layout mirrors the component's own "asset WINS" contract (SpriteFxFilter.EffectiveModifiers/Duration/Seed):
//   • a shared-Stack picker at the top (LauAssetElement — Recall / New / Edit for a SpriteFxSpec, which is already
//     registered for pick/create/edit via SpriteFxStackEditorLink);
//   • when NO Stack asset is assigned, the inline stack IS the recipe — so the embeddable SpriteFxStackView plus a
//     small Timeline group (Duration / Life-remap / Seed) are shown and editable, exactly as the SpriteFx Stack
//     window authors a spec;
//   • when a Stack asset IS assigned it overrides every inline field, so the inline editor is hidden and a short
//     note explains that the asset drives the effect (Edit it via the picker's pen).
//   • Dispatch (inline-vs-Burst) is ALWAYS relevant — it lives on the component, never on the Stack asset — so it
//     shows in both branches, as a radio (ZuiReflect.EnumControl), never a native EnumField dropdown.
//
// Every data edit routes through Dial(...) (Undo.RecordObject BEFORE the mutation, then SetDirty) — the same
// contract SpriteFxStackWindow uses — and a structural change (assigning a Stack, or add/remove/reorder inside the
// inline stack) rebuilds the whole body so the layout tracks the stack's null-ness. No preview here: previewing a
// filtered sprite is the SpriteFx Stack window's job; this inspector just makes the component authorable.
using System;
using System.Collections.Generic;
using Laubrary.AssetKit.Editor;
using Laubrary.SpriteFx;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Laubrary.SpriteFx.Editor
{
    /// <summary>
    /// Custom UI Toolkit inspector for <see cref="SpriteFxFilter"/>: pick/create a shared SpriteFxSpec Stack asset,
    /// or edit the inline stack + timeline when none is assigned. Curated (not the default SerializeReference list).
    /// </summary>
    [CustomEditor(typeof(SpriteFxFilter))]
    public class SpriteFxFilterEditor : UnityEditor.Editor
    {
        const float Wide = 150f;   // reflected value controls / MicroSliders (ui-layout-rules norm)
        const float Num = 70f;     // a bare numeric field

        // Thumbnail cache for the LauAssetElement swatch (kept alive across rebuilds; keyed by asset).
        readonly Dictionary<Object, Texture2D> _thumbCache = new Dictionary<Object, Texture2D>();

        // The rebuilt region: cleared/refilled whenever the Stack null-ness (or inline structure) changes.
        VisualElement _body;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            Z.Attach(root);   // this root isn't owned by a ZuiWindow — attach the shared stylesheet ourselves

            var section = Z.Section("SpriteFx",
                "The colour / mask filter this component plays over its SpriteRenderer on Play(). Assign a shared " +
                "SpriteFx Stack asset to reuse an authored effect, or edit the inline stack here.");
            root.Add(section);

            _body = new VisualElement();
            section.Add(_body);

            Rebuild();
            return root;
        }

        // Record-before + dirty-after — the single Undo contract every edit routes through (matches SpriteFxStackWindow).
        void Dial(string undoLabel, Action apply)
        {
            var f = target as SpriteFxFilter;
            if (f == null) return;
            Undo.RecordObject(f, undoLabel);
            apply();
            EditorUtility.SetDirty(f);
        }

        // Clear + refill the body. The layout depends on whether a Stack asset is assigned (it overrides every inline
        // field), so any structural change (assigning a Stack, or add/remove/reorder inside the inline stack) re-runs this.
        void Rebuild()
        {
            if (_body == null) return;
            _body.Clear();

            var filter = target as SpriteFxFilter;
            if (filter == null) return;

            // 1) Shared-Stack picker. SpriteFxSpec is registered (SpriteFxStackEditorLink) so Recall / New / Edit work.
            const string stackTip = "Optional shared SpriteFx Stack asset. When assigned it WINS — the inline stack, " +
                "duration, life-remap and seed below are ignored — so one authored effect can be reused across many " +
                "entities. Leave it empty to author the effect inline on this component.";
            _body.Add(LauAssetElement.Build(filter.stack, picked =>
            {
                Dial("Assign SpriteFx Stack", () => filter.stack = picked as SpriteFxSpec);
                Rebuild();   // the whole layout below changes with the Stack's null-ness
            }, typeof(SpriteFxSpec), _thumbCache, "New SpriteFx Stack", "Assets/SpriteFx", stackTip));

            _body.Add(Z.VSpace(6f));

            if (filter.stack == null) BuildInline(filter);
            else BuildAssignedNote();

            // 2) Dispatch — ALWAYS relevant (it lives on the component, not the Stack asset). A radio, never a dropdown.
            _body.Add(Z.Divider());
            const string dispTip = "Inline (managed) vs Burst-job dispatch for this component. 'Use project setting' " +
                "reads the SpriteFxSettings asset; 'Force inline' / 'Force burst' override it here (handy for " +
                "profiling or verification). Applies whether the effect is inline or from a Stack asset.";
            _body.Add(Z.Field("Dispatch", dispTip,
                ZuiReflect.EnumControl(filter.dispatch, dispTip,
                    nv => Dial("SpriteFx dispatch", () => filter.dispatch = (SfxDispatch)nv))));
        }

        // No Stack asset assigned → the inline fields ARE the recipe: the embeddable stack editor + a Timeline group.
        void BuildInline(SpriteFxFilter filter)
        {
            _body.Add(Z.Divider("Stack",
                "The colour / mask effects applied in order while the filter plays. Each effect's animatable values " +
                "are resolved at the current life every frame; drag a grip to reorder."));

            var host = new SpriteFxStackView.Host
            {
                OnBeforeChange = () => { var f = target as SpriteFxFilter; if (f != null) Undo.RecordObject(f, "Edit SpriteFx Filter"); },
                OnChanged = () => { var f = target as SpriteFxFilter; if (f != null) EditorUtility.SetDirty(f); },
                // A structural change (add / remove / reorder / enable): the control already rebuilt its own rows;
                // re-run the whole body so anything downstream stays in sync (safe — the control's fold/curve state
                // is keyed per effect instance and survives the rebuild).
                Rebuild = Rebuild,
                ControlWidth = Wide,
            };
            _body.Add(SpriteFxStackView.Build(filter.modifiers, host));

            _body.Add(Z.Divider("Timeline",
                "How long one Play() lasts and how raw progress is remapped into the life value fed to every effect's curves."));

            const string durTip = "How long one Play() lasts, in seconds.";
            _body.Add(Z.MicroSlider("Duration (s)", filter.duration, 0.02f, 2f, durTip,
                v => Dial("SpriteFx duration", () => filter.duration = Mathf.Max(0.001f, v)), Wide));

            // No "Life remap" and no "Step rate" — life is progress and the only clock is the host's. See
            // SpriteFxSpec.SampleEnvelope: a stack-wide remap sat between the host and every effect, so a
            // flattened one pinned life to a constant and silently killed every authored envelope at once.

            const string seedTip = "Seed for any hashing effect (LayerDissolve scatter, AlphaMask noise). Irrelevant " +
                "for a plain Brightness / Tint flash.";
            _body.Add(Z.Field("Seed", seedTip,
                Z.Int(filter.seed, seedTip, v => Dial("SpriteFx seed", () => filter.seed = v), Num)));
        }

        // A Stack asset IS assigned → it overrides every inline field, so hide the inline editor and say so.
        void BuildAssignedNote()
        {
            var note = Z.Text(
                "Driven by the assigned Stack asset — the inline stack, duration, life-remap and seed are ignored. " +
                "Use the pen above to edit the Stack.",
                ZuiText.Subtle,
                "SpriteFxFilter reads its modifiers, duration, envelope and seed from the Stack asset whenever one " +
                "is assigned (see EffectiveModifiers / EffectiveDuration / EffectiveSeed).");
            note.style.whiteSpace = WhiteSpace.Normal;
            _body.Add(note);
        }
    }
}
