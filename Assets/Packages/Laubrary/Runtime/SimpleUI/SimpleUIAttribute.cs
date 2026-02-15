using System;

namespace Laubrary.SimpleUI
{
    [AttributeUsage(AttributeTargets.Class)]
    public class SimpleUIAttribute : Attribute
    {
        public bool AutoRefresh { get; }
        public bool bindAll = true;
        public NameMatchMode nameMatching = NameMatchMode.Exact;
        public int maxSearchDepth = 3;
        public MultiMatchMode multipleMatchBehavior = MultiMatchMode.Fail;
        public bool allowNullPoco = false;

        public SimpleUIAttribute(bool autoRefresh = false)
        {
            AutoRefresh = autoRefresh;
        }
    }
}
