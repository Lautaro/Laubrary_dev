// ZuiSwatchControl — the UI Toolkit picker for a ZuiSwatchRef (the CONTENT-colour ref: an inline colour OR a
// named swatch in a SwatchPalette). One row: [resolved preview] [palette ObjectField] [swatch-name dropdown]
// [inline colour]. Pick a palette + a swatch to bind by name (change the swatch → every ref updates); leave the
// palette empty (or pick "(inline)") to use the plain inline colour. The preview always shows Resolve() — the
// exact colour that lands at runtime. Because ZuiSwatchRef is a STRUCT, every edit builds a NEW struct and fires
// OnChanged with it (the owner writes it back); OnBeforeMutate is the Undo.RecordObject hook.
//
// This is #73's swatch picker — the editor half of the ZuiSwatchRef / SwatchPalette runtime foundation.

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    public class ZuiSwatchControl : VisualElement
    {
        ZuiSwatchRef _value;
        readonly float _width;
        readonly VisualElement _preview;

        /// Fires once per gesture before the first mutation — the Undo.RecordObject hook.
        public Action OnBeforeMutate;
        /// Fires after every mutation, carrying the new struct value (write it back to the owning field).
        public Action<ZuiSwatchRef> OnChanged;

        public ZuiSwatchControl(ZuiSwatchRef value, string tooltip, float width = 220f)
        {
            _value = value;
            _width = width;
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            if (!string.IsNullOrEmpty(tooltip)) this.tooltip = tooltip;

            _preview = new VisualElement { tooltip = "Resolved colour — the swatch, or the inline colour when no swatch is bound." };
            _preview.style.width = 16;
            _preview.style.height = 16;
            _preview.style.marginRight = 4;
            _preview.style.borderTopWidth = _preview.style.borderBottomWidth =
                _preview.style.borderLeftWidth = _preview.style.borderRightWidth = 1;
            var edge = new Color(0f, 0f, 0f, 0.4f);
            _preview.style.borderTopColor = _preview.style.borderBottomColor =
                _preview.style.borderLeftColor = _preview.style.borderRightColor = edge;

            Rebuild();
        }

        void Commit(ZuiSwatchRef nv)
        {
            OnBeforeMutate?.Invoke();
            _value = nv;
            OnChanged?.Invoke(_value);
            Rebuild();   // a palette change alters the name dropdown; a swatch/inline change moves the preview
        }

        void Rebuild()
        {
            Clear();
            Add(_preview);
            _preview.style.backgroundColor = _value.Resolve();

            Add(Z.Object<SwatchPalette>(_value.palette,
                "The SwatchPalette this colour draws from (empty = a plain inline colour).",
                nv =>
                {
                    var v = _value;
                    v.palette = nv;
                    if (nv == null) v.key = "";   // no palette → fall back to the inline colour
                    Commit(v);
                }, _width * 0.5f));

            if (_value.palette != null)
            {
                string label = string.IsNullOrEmpty(_value.key) ? "(inline)" : _value.key;
                var drop = Z.Button(label, "Pick a named swatch from the palette, or (inline) for the plain colour.", null).W(_width * 0.32f);
                drop.clicked += () => ShowSwatchMenu(drop);
                Add(drop);
            }

            Add(Z.Color(_value.inline,
                "The inline colour — used directly when no swatch is bound, and the fallback if a bound swatch is missing.",
                nv => { var v = _value; v.inline = nv; Commit(v); }, 46f));
        }

        void ShowSwatchMenu(VisualElement anchor)
        {
            var menu = Z.Menu(anchor);
            menu.Item("(inline)", "Use the inline colour, not a swatch.",
                () => { var v = _value; v.key = ""; v.autoColorRef = ""; Commit(v); },
                @checked: string.IsNullOrEmpty(_value.key));

            var pal = _value.palette;
            if (pal != null && pal.swatches != null && pal.swatches.Count > 0)
            {
                menu.Separator();
                foreach (var s in pal.swatches)
                {
                    if (s == null || string.IsNullOrEmpty(s.name)) continue;
                    string nm = s.name;
                    menu.Item(nm, $"Bind to swatch '{nm}'.",
                        () => { var v = _value; v.key = nm; v.autoColorRef = ""; Commit(v); },
                        @checked: _value.key == nm);
                }
            }
            menu.Show();
        }
    }
}
