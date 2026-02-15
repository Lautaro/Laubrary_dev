using System;

namespace Laubrary.SimpleMenu
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class SimpleMenuTextBoxAttribute : Attribute
    {
        public string Text { get; }

        public SimpleMenuTextBoxAttribute(string text)
        {
            Text = text;
        }
    }
}
