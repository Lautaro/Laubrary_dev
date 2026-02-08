using System;

namespace Laubrary.SimpleMenu
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class SimpleMenuLabelAttribute : Attribute
    {
        public string Text { get; }

        public SimpleMenuLabelAttribute(string text)
        {
            Text = text;
        }
    }
}
