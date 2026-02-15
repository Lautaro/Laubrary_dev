using System;

namespace Laubrary.SimpleUI
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class SimpleUIFormatAttribute : Attribute
    {
        public string Format { get; }
        
        public SimpleUIFormatAttribute(string format)
        {
            Format = format;
        }
    }
}
