using System;

namespace Laubrary.SimpleMenu
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class SimpleMenuButtonAttribute : Attribute
    {
        public string Label { get; }

        public SimpleMenuButtonAttribute(string label = null)
        {
            Label = label;
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class SimpleMenuAttribute : Attribute
    {
        public string Label { get; }

        public SimpleMenuAttribute(string label = null)
        {
            Label = label;
        }
    }
}
