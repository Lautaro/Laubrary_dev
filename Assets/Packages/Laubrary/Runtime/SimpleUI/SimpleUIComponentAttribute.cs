using System;

namespace Laubrary.SimpleUI
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class SimpleUIComponentAttribute : Attribute
    {
        public Type ComponentType { get; }
        
        public SimpleUIComponentAttribute(Type type)
        {
            ComponentType = type;
        }
    }
}
