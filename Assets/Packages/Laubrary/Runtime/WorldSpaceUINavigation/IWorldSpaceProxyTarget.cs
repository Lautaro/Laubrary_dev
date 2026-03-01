namespace Laubrary.WorldSpaceUINavigation
{
    /// <summary>
    /// Implement this on any world-space component that should respond to
    /// selection state changes relayed by a WorldSpaceUIProxy.
    /// </summary>
    public interface IWorldSpaceProxyTarget
    {
        /// <summary>Called when the proxy representing this object is selected by the EventSystem.</summary>
        void OnProxySelected();

        /// <summary>Called when the proxy representing this object is deselected by the EventSystem.</summary>
        void OnProxyDeselected();
    }
}
