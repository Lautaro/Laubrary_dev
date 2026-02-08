using System;

namespace Laubrary.SimpleMenu
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class SubMenuAttribute : Attribute
    {
        public string Label { get; }
        public Type MenuType { get; }

        public SubMenuAttribute(Type menuType, string label = null)
        {
            MenuType = menuType;
            Label = label;
        }
    }
}
