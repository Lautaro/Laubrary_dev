using System;

namespace Laubrary.SimpleUI
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class SimpleUIBindAttribute : Attribute
    {
        public BindingMode Mode { get; }

        public SimpleUIBindAttribute(BindingMode mode = BindingMode.OneWay)
        {
            Mode = mode;
        }
    }
}
