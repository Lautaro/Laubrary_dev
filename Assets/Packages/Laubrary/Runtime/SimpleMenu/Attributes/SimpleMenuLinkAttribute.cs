using System;

namespace Laubrary.SimpleMenu
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class SimpleSubMenuAttribute : Attribute
    {
        public string Label { get; }

        public SimpleSubMenuAttribute(string label = null)
        {
            Label = label;
        }
    }
}
