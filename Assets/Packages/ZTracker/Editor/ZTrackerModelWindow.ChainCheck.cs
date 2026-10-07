using System;
using System.Linq;
using System.Reflection;
using Laubrary.Zounds.Uitk;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.ZTracker.Editor {
    public sealed partial class ZTrackerModelWindow {
        static void LayoutChain(ZTrackerModelWindow window) {
            var panel = window.rootVisualElement.panel;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            for (int i = 0; i < 3; i++) {
                panel?.GetType().GetMethod("ValidateLayout", flags)?.Invoke(panel, null);
                foreach (var chain in window.rootVisualElement.Query<ChainEditorTK>().ToList())
                    typeof(ChainEditorTK).GetMethod("Tick", flags).Invoke(chain, null);
            }
        }
        static ZuiSkinSlider ChainDial(ZTrackerModelWindow window, int node = 0, int parameter = 0) {
            LayoutChain(window);
            return window.rootVisualElement.Q("chain-param-" + node + "-" + parameter)?.Q<ZuiSkinSlider>();
        }
        static void SetChainDial(ZuiSkinSlider dial, float value) {
            ((Action<float>)typeof(ZuiSkinSlider).GetField("_onChanged", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dial))(value);
        }
        static void ReleaseChain(VisualElement element) {
            var chain = element as ChainEditorTK ?? element.GetFirstAncestorOfType<ChainEditorTK>();
            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0 })) { up.target = chain; chain.SendEvent(up); }
        }
        static void ReorderChain(ZTrackerModelWindow window, int from, int to) {
            LayoutChain(window);
            var grip = window.rootVisualElement.Q("chain-grip-" + from);
            var row = window.rootVisualElement.Q("chain-node-" + to);
            var end = new Vector2(row.worldBound.center.x, from < to ? row.worldBound.yMax + 1 : row.worldBound.yMin + 1);
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = grip.worldBound.center })) { down.target = grip; grip.SendEvent(down); }
            using (var move = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, button = 0, mousePosition = end })) { move.target = grip; grip.SendEvent(move); }
            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = end })) { up.target = grip; grip.SendEvent(up); }
        }
    }
}
