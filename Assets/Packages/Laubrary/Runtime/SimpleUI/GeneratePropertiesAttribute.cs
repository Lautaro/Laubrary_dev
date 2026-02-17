using System;

namespace Laubrary.SimpleUI
{
    [AttributeUsage(AttributeTargets.Class)]
    public class GenerateSimpleUIPropertiesAttribute : Attribute
    {
        public GenerateMode Mode { get; }

        public GenerateSimpleUIPropertiesAttribute(GenerateMode mode = GenerateMode.All)
        {
            Mode = mode;
        }
    }

    public enum GenerateMode
    {
        None,
        All,
        OptIn
    }
}
