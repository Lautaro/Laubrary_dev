using UnityEngine;

namespace Lautaro.Stats
{
    public static class Extensions
    {
        public static bool GetKey(this KeyCode keyCode)
        {
            return Input.GetKey(keyCode);
        }

        public static bool GetKeyDown(this KeyCode keyCode)
        {
            return Input.GetKeyDown(keyCode);
        }
    }
}
