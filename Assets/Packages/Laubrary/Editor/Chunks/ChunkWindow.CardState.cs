// ChunkWindow.CardState — two small things a card says about itself that the stage alone cannot.
//
// A source state line: a Fragment Fracture with nothing to cut, or a Palette Splash that will spray plain white,
// says so under its source pickers, in the same words the stage's tooltip uses, read from the same caches the
// stage draws from — so the card and the picture cannot disagree. It is a state, not an instruction, and it is
// absent while there is nothing to say.
//
// A seed field whose tooltip reads for the value it holds: 0 is not "no seed", it is "a different burst every
// time", and the preview can only ever show one of those.
using System;
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        // One refresher per card that shows a state line, keyed by capability id so a rebuilt card simply
        // replaces its own. Run on every edit, because a splash's state depends on OTHER cards (the fracture it
        // borrows a picture from, a debris scatter's sample source).
        readonly Dictionary<string, Action> cardStates = new Dictionary<string, Action>();

        void RefreshCardStates()
        {
            foreach (var refresh in cardStates.Values) refresh();
        }

        /// The state line for a Fragment Fracture or Palette Splash card. Hidden while the source is fine.
        VisualElement SourceStateLine(ChunkSpec c, ChunkCapability cap)
        {
            var line = Z.Text("", ZuiText.Subtle);
            line.style.whiteSpace = WhiteSpace.NoWrap;
            line.style.overflow = Overflow.Hidden;
            line.style.textOverflow = TextOverflow.Ellipsis;
            line.style.flexShrink = 1f;

            void Refresh()
            {
                var spec = Current != null ? Current : c;
                string state = ChunkPreviewSim.SourceState(spec, cap);
                line.text = state ?? "";
                line.tooltip = state == null
                    ? ""
                    : state + " The preview draws exactly what this card does when a burst is fired on its own.";
                line.style.display = state == null ? DisplayStyle.None : DisplayStyle.Flex;
            }

            Refresh();
            cardStates[cap.EnsureId()] = Refresh;
            return line;
        }

        /// A Seed field. <paramref name="pins"/> names what the seed fixes, as a plural noun phrase ("the cut and
        /// the pieces' flight"); the tooltip is rewritten whenever the value changes so it always describes the value shown.
        VisualElement SeedField(int value, string pins, string undoLabel, Action<int> apply)
        {
            var input = Z.Int(value, SeedTooltip(value, pins), null, 70f);
            var field = Z.Field("Seed", SeedTooltip(value, pins), input);
            var label = field.Q<Label>(className: "zui-field__label");
            input.RegisterValueChangedCallback(e =>
            {
                Dial(undoLabel, () => apply(e.newValue));
                string tip = SeedTooltip(e.newValue, pins);
                input.tooltip = tip;
                if (label != null) label.tooltip = tip;
            });
            return field;
        }

        static string SeedTooltip(int seed, string pins) => seed == 0
            ? $"0 = a new roll on every burst: {pins} change each time it plays, so no two plays match. " +
              "The preview shows one example roll. Any other number pins it."
            : $"Pins {pins}, so every play comes out the same. 0 = a new roll on every burst (the preview " +
              "then shows one example).";
    }
}
