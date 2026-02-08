using System;

namespace Laubrary.SimpleMenu
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class SimpleMenuLayoutAttribute : Attribute
    {
        public bool UseHorizontalLayout { get; }

        public SimpleMenuLayoutAttribute(bool useHorizontalLayout)
        {
            UseHorizontalLayout = useHorizontalLayout;
        }
    }
}
