using System;

namespace Laubrary.SimpleUI
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public class SimpleUIPathAttribute : Attribute
    {
        public string Path { get; }
        
        public SimpleUIPathAttribute(string path)
        {
            Path = path;
        }
    }
}
