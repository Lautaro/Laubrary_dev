using System;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// A modifier's ZPOC settings (T-0495), opened from the ⚡ chip on its card: the id game code reaches it by, how the
    /// value acts (Scale or Set; a Code modifier has no choice, its output IS the value), where it rests before code
    /// speaks, and how quickly it follows. One wide row; a click elsewhere closes it; every change made while it is open
    /// is one Undo step. The id is typed here because this is where it is DECLARED; game code refers to it by that name.
    /// </summary>
    public class ZpocPopup : PopupWindowContent {

        const float RowH = 20f;
        readonly Zound zound;
        readonly ZoundModifier mod;
        readonly Action changed;
        bool begun;

        ZpocPopup(Zound zound, ZoundModifier mod, Action changed) { this.zound = zound; this.mod = mod; this.changed = changed; }

        public static void Show(Rect anchorWorld, Zound zound, ZoundModifier mod, Action changed) {
            if (mod == null) return;
            UnityEditor.PopupWindow.Show(anchorWorld, new ZpocPopup(zound, mod, changed));
        }

        // Sized to the row's content: padding, id label, field, warning, gap, [Scale/Set, gap, As authored, gap, Rest, gap], Follow, padding.
        const float CommonW = 6f + 32f + 130f + 14f + 6f + 130f + 6f, ModeW = 48f + 40f + 6f + 84f + 4f + 110f + 6f;
        public override Vector2 GetWindowSize() => new Vector2(mod.type == ZoundModifierType.Code ? CommonW : CommonW + ModeW, RowH + 12f);

        public override void OnGUI(Rect rect) { }

        public override void OnOpen() {
            var root = editorWindow.rootVisualElement;
            ZS.Attach(root);
            root.style.paddingLeft = 6f; root.style.paddingTop = 6f;
            Build(root);
        }

        public override void OnClose() { if (begun) ZoundsWindow.EndDragUndo(); }

        void Change(Action a) {
            if (!begun) { begun = true; ZoundsWindow.BeginDragUndo("ZPOC settings"); }
            a();
            var chain = Dsp.ZoundDspPlayback.ResolveChain(zound, out _);
            chain?.Touch();
            Dsp.ZoundDspPlayback.InvalidateLayout(zound);
            EditorUtility.SetDirty(ZoundsProject.Instance);
            changed?.Invoke();
        }

        /// <summary>Another modifier in the same Zound already answers to this id.</summary>
        bool Duplicate() {
            var key = ZpocKeys.Key(mod.zpocId);
            if (key == null) return false;
            var chain = Dsp.ZoundDspPlayback.ResolveChain(zound, out _);
            if (chain == null) return false;
            foreach (var m in chain.modifiers) if (m != mod && m.HasZpoc && ZpocKeys.Key(m.zpocId) == key) return true;
            return false;
        }

        void Build(VisualElement root) {
            root.Clear();
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row; r.style.height = RowH; r.style.flexShrink = 0;

            var idLabel = new Label("⚡ Id");
            idLabel.AddToClassList("zs-zpocmark");
            idLabel.tooltip = "The name game code reaches this modifier by, through a play's token: token.SetZpoc(\"" + (mod.HasZpoc ? mod.zpocId : "name") + "\", value). Case, spaces, underscores and hyphens are ignored, as for Zound names. It only has to be unique within this Zound; sibling sounds in a Zequence may share it, and one call then reaches them all. Empty: not exposed.";
            idLabel.style.width = 32f; idLabel.style.flexShrink = 0; idLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            r.Add(idLabel);
            var id = new TextField { value = mod.zpocId ?? "", tooltip = idLabel.tooltip };
            id.AddToClassList("zs-namefield");
            id.style.width = 130f; id.style.height = RowH - 2f; id.style.flexShrink = 0;
            var dup = new Label("⚠") { tooltip = "Another modifier in this Zound already has this id, so one call reaches both. Give one of them a different id unless that is what you want." };
            dup.AddToClassList("zs-warnmark");
            dup.style.width = 14f; dup.style.flexShrink = 0;
            dup.style.visibility = Duplicate() ? Visibility.Visible : Visibility.Hidden;
            id.RegisterValueChangedCallback(e => {
                Change(() => mod.zpocId = e.newValue.Trim());
                dup.style.visibility = Duplicate() ? Visibility.Visible : Visibility.Hidden;
            });
            r.Add(id);
            r.Add(dup);
            r.Add(Gap(6f));

            if (mod.type != ZoundModifierType.Code) {
                bool set = mod.zpocMode == ZpocMode.Set;
                ZuiToggleButton scaleT = null, setT = null;
                scaleT = ZS.Toggle("Scale", "Scale: the value scales how strongly this modifier acts, as authored. Nought: it does nothing; one: exactly as you tuned it. It can never go beyond what you tuned.",
                    !set, v => { Change(() => mod.zpocMode = ZpocMode.Scale); scaleT.SetValueWithoutNotify(true); setT.SetValueWithoutNotify(false); },
                    "RichToggle", ZUICornerMask.Left, 48f, RowH - 2f);
                setT = ZS.Toggle("Set", "Set: the value IS how strongly this modifier acts. One: as strongly as it can. The parameter it moves most lands on the value and any others keep their proportion to it, so code can go above what you tuned.",
                    set, v => { Change(() => mod.zpocMode = ZpocMode.Set); setT.SetValueWithoutNotify(true); scaleT.SetValueWithoutNotify(false); },
                    "RichToggle", ZUICornerMask.Right, 40f, RowH - 2f);
                r.Add(scaleT); r.Add(setT);
                r.Add(Gap(6f));

                bool authored = mod.zpocRest < 0f;
                ZuiSkinSlider rest = null;
                r.Add(ZS.Toggle("As authored", authored
                        ? "Until code sends a value this modifier acts exactly as you tuned it. Turn off to choose where it rests instead."
                        : "Rest exactly as you tuned it until code sends a value, instead of at the value beside.",
                    authored, v => { Change(() => mod.zpocRest = v ? -1f : 1f); Build(root); }, "RichToggle", ZUICornerMask.All, 84f, RowH - 2f));
                // Always laid out, only hidden while "As authored" is on, so switching it never shoves the controls after it.
                r.Add(Gap(4f));
                rest = ZS.Slider("Rest", authored ? 1f : mod.zpocRest, 0f, 1f,
                    "Where the value sits before game code sends anything, and what you hear in the editor.",
                    v => Change(() => mod.zpocRest = v), ZuiSkinSlider.LabelMode.LabelAndValue, 1f, "Default", 110f, RowH - 2f);
                rest.style.visibility = authored ? Visibility.Hidden : Visibility.Visible;
                r.Add(rest);
                r.Add(Gap(6f));
            }

            ZuiSkinSlider follow = null;
            follow = ZS.Slider("Follow " + mod.zpocSmoothMs.ToString("0") + " ms", mod.zpocSmoothMs, 0f, 500f,
                "How long a new value from code takes to be reached (about two thirds of the way in this time). Short follows code tightly; long smooths out jumpy values. Nought jumps at once, which can click.",
                v => { Change(() => mod.zpocSmoothMs = Mathf.Round(v)); follow.text = "Follow " + Mathf.Round(v).ToString("0") + " ms"; },
                ZuiSkinSlider.LabelMode.LabelOnly, 30f, "Default", 130f, RowH - 2f);
            r.Add(follow);
            root.Add(r);
        }

        static VisualElement Gap(float w) { var e = new VisualElement(); e.style.width = w; e.style.flexShrink = 0; return e; }
    }
}
