using Laubrary.SimpleUI;
using UnityEngine;

namespace Laubrary.Demos
{
    [SimpleUI]
    public class AutoGenerateUI_POCO
    {
        [SimpleUIBind]
        public string Name { get; set; }

        [SimpleUIBind]
        public int Age { get; set; }

        [SimpleUIBind]
        public string Email { get; set; }

        [SimpleUIBind]
        public string Color { get; set; }

        [SimpleUIBind]
        public int Moneys { get; set; }

        [SimpleUIBind]
        public string Snack { get; set; }
    }
}
