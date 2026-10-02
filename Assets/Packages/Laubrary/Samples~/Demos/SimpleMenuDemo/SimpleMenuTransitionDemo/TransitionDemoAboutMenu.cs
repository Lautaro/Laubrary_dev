using UnityEngine;

namespace Laubrary.SimpleMenu.Samples
{
    /// <summary>
    /// About submenu for the transition demo.
    /// </summary>
    [SimpleMenu("About")]
    public class TransitionDemoAboutMenu : SimpleMenuBase
    {
        [SimpleMenuTextBox("SimpleMenu Transition Demo\n\nTransitions use SimpleMenuVisual,\nwhich is backed by Switcheroo.\n\nSupports: Alpha, Position, Scale\nor any combination of the three.")]
        private int _textDummy;

        [SimpleMenuLabel("Version 1.0")]
        private int _versionDummy;
    }
}
