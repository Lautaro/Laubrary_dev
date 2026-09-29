using System;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// A Zequence track's ZPOC id (T-0495): the name game code can reach this track by through a play's token
    /// (token.Track("id")), as well as by its number. Declared here, so typed here. One row; a click elsewhere closes it; all
    /// changes while it is open are one Undo step.
    /// </summary>
    public class TrackIdPopup : PopupWindowContent {

        const float RowH = 20f;
        readonly CompositeZound parent;
        readonly CompositeZound.ZoundEntry entry;
        readonly Action changed;
        bool begun;

        TrackIdPopup(CompositeZound parent, CompositeZound.ZoundEntry entry, Action changed) { this.parent = parent; this.entry = entry; this.changed = changed; }

        public static void Show(Rect anchorWorld, CompositeZound parent, CompositeZound.ZoundEntry entry, Action changed) {
            if (entry == null) return;
            UnityEditor.PopupWindow.Show(anchorWorld, new TrackIdPopup(parent, entry, changed));
        }

        public override Vector2 GetWindowSize() => new Vector2(6f + 32f + 130f + 14f + 6f, RowH + 12f);   // the same id row as the modifier ZPOC popover
        public override void OnGUI(Rect rect) { }

        public override void OnOpen() {
            var root = editorWindow.rootVisualElement;
            ZS.Attach(root);
            root.style.paddingLeft = 6f; root.style.paddingTop = 6f;
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row; r.style.height = RowH; r.style.flexShrink = 0;
            string tip = "A name game code can reach this track by: token.Track(\"" + (string.IsNullOrEmpty(entry.zpocId) ? "name" : entry.zpocId) + "\"), as well as by its number. Case, spaces, underscores and hyphens are ignored, as for Zound names. Only has to be unique within this Zequence; a Zequence playing this one reaches it by the same name. Empty: reached by number only.";
            var l = new Label("⚡ Id") { tooltip = tip };
            l.AddToClassList("zs-zpocmark");
            l.style.width = 32f; l.style.flexShrink = 0; l.style.unityTextAlign = TextAnchor.MiddleLeft;
            r.Add(l);
            var id = new TextField { value = entry.zpocId ?? "", tooltip = tip };
            id.AddToClassList("zs-namefield");
            id.style.width = 130f; id.style.height = RowH - 2f; id.style.flexShrink = 0;
            var dup = new Label("⚠") { tooltip = "Another track of this Zequence already has this id, so one call reaches both. Give one a different id unless that is what you want." };
            dup.AddToClassList("zs-warnmark");
            dup.style.width = 14f; dup.style.flexShrink = 0;
            dup.style.visibility = Duplicate() ? Visibility.Visible : Visibility.Hidden;
            id.RegisterValueChangedCallback(e => {
                if (!begun) { begun = true; ZoundsWindow.BeginDragUndo("track id"); }
                entry.zpocId = e.newValue.Trim();
                EditorUtility.SetDirty(ZoundsProject.Instance);
                dup.style.visibility = Duplicate() ? Visibility.Visible : Visibility.Hidden;
                changed?.Invoke();
            });
            r.Add(id); r.Add(dup);
            root.Add(r);
            id.schedule.Execute(() => id.Focus());
        }

        public override void OnClose() { if (begun) ZoundsWindow.EndDragUndo(); }

        bool Duplicate() {
            var key = ZpocKeys.Key(entry.zpocId);
            if (key == null || parent?.zoundEntries == null) return false;
            foreach (var e in parent.zoundEntries) if (e != entry && ZpocKeys.Key(e.zpocId) == key) return true;
            return false;
        }
    }
}
