using UnityEngine.UIElements;

namespace Laubrary.Zui
{
    /// The UI Toolkit twin of the IMGUI ZUI's per-call corner override (ZUICornerMask): which corners of a control are
    /// rounded, so buttons joined into a strip read as one piece (rounded only at its ends). It only sets a class —
    /// `zui-corners--all|left|right|top|bottom|square` — and the RADIUS stays whatever the control's own style says, so
    /// a skin decides how round and the call site decides which corners, exactly as in the IMGUI version. None clears
    /// the override and leaves the style's own corners. (Added for the Zounds UI Toolkit port, T-0457.)
    public static class ZuiCorners
    {
        static readonly string[] Classes =
            { "zui-corners--all", "zui-corners--left", "zui-corners--right", "zui-corners--top", "zui-corners--bottom", "zui-corners--square" };

        public static T Corners<T>(this T e, ZUICornerMask mask) where T : VisualElement
        {
            foreach (var c in Classes) e.RemoveFromClassList(c);
            switch (mask)
            {
                case ZUICornerMask.All:    e.AddToClassList(Classes[0]); break;
                case ZUICornerMask.Left:   e.AddToClassList(Classes[1]); break;
                case ZUICornerMask.Right:  e.AddToClassList(Classes[2]); break;
                case ZUICornerMask.Top:    e.AddToClassList(Classes[3]); break;
                case ZUICornerMask.Bottom: e.AddToClassList(Classes[4]); break;
                case ZUICornerMask.Square: e.AddToClassList(Classes[5]); break;
            }
            return e;
        }
    }
}
