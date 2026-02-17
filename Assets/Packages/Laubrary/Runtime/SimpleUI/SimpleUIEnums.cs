namespace Laubrary.SimpleUI
{
    public enum NameMatchMode
    {
        Exact,
        CaseInsensitive,
        Loose
    }

    public enum MultiMatchMode
    {
        Fail,
        UseFirst
    }

    public enum BindingStatus
    {
        Success,
        Error,
        Skipped
    }

    public enum BindingMode
    {
        OneWay,
        TwoWay
    }

    public enum ConfidenceLevel
    {
        Explicit,
        Strong,
        Weak
    }
}
