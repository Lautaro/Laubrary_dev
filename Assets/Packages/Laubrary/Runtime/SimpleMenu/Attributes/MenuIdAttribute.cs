using System;

namespace Laubrary.SimpleMenu
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class MenuIdAttribute : Attribute
    {
        public string Id { get; }

        public MenuIdAttribute(string id)
        {
            Id = id;
        }
    }
}
