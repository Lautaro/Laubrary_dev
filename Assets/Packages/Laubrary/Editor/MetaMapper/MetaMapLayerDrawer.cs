using System;
using Laubrary.Zui;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.MetaMapper.Editor
{
    /// <summary>
    /// The default inspector for a <see cref="MetaMapLayer"/> — because without one it LIES.
    ///
    /// A raw serialized layer shows <c>uniform</c> AND <c>track</c> side by side regardless of the binding,
    /// so half of what is on screen is dead storage the model never reads. Worse, it invites editing the dead
    /// half, which looks like it worked and changes nothing. This drawer shows the LIVE one only, and puts the
    /// binding state where it cannot be missed.
    ///
    /// Two fields are deliberately READ-ONLY here:
    ///   • <b>kind</b> — fixed at creation; changing it with content authored invites nonsense (delete the
    ///     layer and recreate it instead);
    ///   • <b>binding</b> — Amendment 2026-08-03: switching PerFrame → Uniform is a destructive collapse that
    ///     must WARN and make the author name the winning frame. An inspector has no frame context to offer
    ///     that choice with, so the toggle lives in the MetaMapper window, where it can.
    /// </summary>
    [CustomPropertyDrawer(typeof(MetaMapLayer))]
    public class MetaMapLayerDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            Z.Attach(root);   // a drawer's root is NOT one Zui owns — without this every Z.* control is unstyled

            var host = new VisualElement();
            root.Add(host);

            string path = property.propertyPath;
            var so = property.serializedObject;
            Build(host, so, path);

            // The header text and the live/dead split both depend on `binding`; track it so an external
            // change (an undo, the MetaMapper window) redraws instead of showing the wrong half.
            var bindProp = property.FindPropertyRelative("binding");
            if (bindProp != null)
                host.TrackPropertyValue(bindProp, _ => Build(host, so, path));

            return root;
        }

        static void Build(VisualElement host, SerializedObject so, string path)
        {
            host.Clear();
            var prop = so.FindProperty(path);
            if (prop == null) { host.Add(Z.Text("(layer no longer exists)", ZuiText.Subtle)); return; }

            var idP = prop.FindPropertyRelative("id");
            var kindP = prop.FindPropertyRelative("kind");
            var colorP = prop.FindPropertyRelative("color");
            var hintP = prop.FindPropertyRelative("pointHint");
            var bindP = prop.FindPropertyRelative("binding");
            if (idP == null || kindP == null || bindP == null) return;

            var kind = (LayerKind)kindP.enumValueIndex;
            var binding = (FrameBinding)bindP.enumValueIndex;
            string tip = "One named layer of spatial metadata. The ID is the semantic key reading code looks " +
                         "up (case-insensitively); the binding says whether its content is shared across all " +
                         "frames or authored per frame.";

            var fold = new Foldout
            {
                text = $"{idP.stringValue}   —   {kind}  ·  {binding}",
                value = prop.isExpanded,
                tooltip = tip,
            };
            // A Foldout's tooltip does not reach its internal disclosure Toggle — set both, or the header
            // is the one hoverable thing in the row with nothing to say.
            var disclosure = fold.Q<Toggle>();
            if (disclosure != null) disclosure.tooltip = tip;
            fold.RegisterValueChangedCallback(e =>
            {
                if (e.target != fold) return;
                var p = so.FindProperty(path);
                if (p != null) p.isExpanded = e.newValue;
            });
            host.Add(fold);

            var body = fold.contentContainer;

            body.Add(Z.Field("Id", "THE SEMANTIC KEY. `HasLayer(\"LootShelf\")` is how a consumer asks what " +
                                   "this subject IS — renaming this breaks every caller already asking for " +
                                   "the old name.",
                Z.TextInput(idP.stringValue, "The layer's id, compared case-insensitively.",
                    v => Set(so, path, "id", p => p.stringValue = v), 170f)));

            body.Add(Z.Field("Kind", "FIXED AT CREATION — changing it with content already authored invites " +
                                     "nonsense. Delete the layer and recreate it instead.",
                Z.Text(kind.ToString(), ZuiText.Body, "What this layer stores. Read-only by design.")));

            if (colorP != null)
                body.Add(Z.Field("Colour", "The layer's one display colour. A mask cell's 0–10 value ramps " +
                                           "this colour's BRIGHTNESS; it never picks a different hue.",
                    Z.Color(colorP.colorValue, "Display colour for this layer.",
                        v => Set(so, path, "color", p => p.colorValue = v), 70f, false)));

            // pointHint is Mask-only — on any other kind it is dead storage, so it does not appear at all.
            if (kind == LayerKind.Mask && hintP != null)
                body.Add(Z.Field("Single-cell paint", "An AUTHORING hint, never a data rule: the paint tool " +
                                                      "clears the previous cell before painting the next " +
                                                      "(MetaLayerMode.Point, preserved verbatim).",
                    Z.Toggle("On", "Single-cell paint behaviour for this layer.", hintP.boolValue,
                        v => Set(so, path, "pointHint", p => p.boolValue = v))));

            body.Add(Z.Field("Binding", "AUTHORED, never inferred. Uniform = one entry serves every frame; " +
                                        "Per frame = one entry per frame, and an EMPTY frame is a real " +
                                        "authored answer. Change it in the MetaMapper window: collapsing a " +
                                        "per-frame track is destructive and has to ask which frame wins.",
                Z.Text(binding == FrameBinding.PerFrame ? "Per frame" : "Uniform", ZuiText.Body,
                    "This layer's frame binding. Read-only here — see the tooltip.")));

            // THE POINT OF THIS DRAWER: only the live half is shown. The dead one is not merely hidden — it
            // is cleared by the model on every binding switch, so showing it would be showing a ghost.
            var live = binding == FrameBinding.Uniform
                ? prop.FindPropertyRelative("uniform")
                : prop.FindPropertyRelative("track");
            if (live != null)
            {
                var field = new PropertyField(live, binding == FrameBinding.Uniform ? "Entry" : "Frames")
                {
                    tooltip = binding == FrameBinding.Uniform
                        ? "The single entry this layer serves on every frame."
                        : "One entry per frame, index-aligned. A SHORTER track means \"empty beyond the end\"; " +
                          "an empty entry mid-track means \"nothing on this frame\" — both are legal and " +
                          "neither is ever smoothed away.",
                };
                field.Bind(so);
                body.Add(field);
            }
        }

        static void Set(SerializedObject so, string path, string relative, Action<SerializedProperty> apply)
        {
            var p = so.FindProperty(path)?.FindPropertyRelative(relative);
            if (p == null) return;
            apply(p);
            so.ApplyModifiedProperties();
        }
    }
}
