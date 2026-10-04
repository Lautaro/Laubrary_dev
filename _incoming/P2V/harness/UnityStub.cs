namespace UnityEngine
{
    public class HeaderAttribute : System.Attribute { public HeaderAttribute(string s) { } }
    public class TooltipAttribute : System.Attribute { public TooltipAttribute(string s) { } }
    public class RangeAttribute : System.Attribute { public RangeAttribute(float a, float b) { } }
    public class MinAttribute : System.Attribute { public MinAttribute(float a) { } }
}
