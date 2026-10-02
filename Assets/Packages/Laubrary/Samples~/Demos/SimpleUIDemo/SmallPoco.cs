using System;
using UnityEngine;

namespace Laubrary.SimpleUI.Demo
{
    [SimpleUI(bindAll = false)]
    public class SmallPoco : SimpleUIPoco
    {
        [SimpleUIFormat("Level {0}")]
        private int score = 100;

        [SimpleUIBind]
        private string world = "";

        [SimpleUIFormat("Time: {0}")]
        private TimeSpan playTime = TimeSpan.Zero;

        [SimpleUIIgnore]
        public bool isIgnored;
        public void UpdateTime()
        {
            playTime.Add(TimeSpan.FromMilliseconds(Time.deltaTime));
        }
    }
}
