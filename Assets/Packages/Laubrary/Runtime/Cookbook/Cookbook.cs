using System;
using System.Collections;
using UnityEngine;

namespace Laubrary.Cookbook
{
    public static class Cookbook
    {
        [Obsolete("This method is deprecated. Please use DelayedExecutionScaled instead.")]
        public static void DelayedAnonymous(this MonoBehaviour monoBehaviour, float delay, Action action)
        {
            monoBehaviour.StartCoroutine(ExecuteAfterTime(delay, action));
        }

        /// <summary>Delays code execution in realtime not affected by scaling Time.time</summary>
        public static void DelayedExecution(this MonoBehaviour monoBehaviour, float delay, Action action)
        {   
            monoBehaviour.StartCoroutine(ExecuteAfterTimeRealtime(delay, action));
        }

        public static void DelayedExecutionScaled(this MonoBehaviour monoBehaviour, float delay, Action action)
        {
            monoBehaviour.StartCoroutine(ExecuteAfterTime(delay, action));
        }
        private static IEnumerator ExecuteAfterTime(float delay, Action action)
        {
            yield return new WaitForSeconds(delay);
            action();
        }
        private static IEnumerator ExecuteAfterTimeRealtime(float delay, Action action)
        {
            yield return new WaitForSecondsRealtime(delay);
            action();
        }

        public static bool GetKey(this KeyCode keyCode)
        {
            return Input.GetKey(keyCode);
        }
        public static bool GetKeyDown(this KeyCode keyCode)
        {
            return Input.GetKeyDown(keyCode);
        }

        public static T GetOrAddComponent<T>(this MonoBehaviour monoBehaviour) where T : Component
        {
                return monoBehaviour.gameObject.GetOrAddComponent<T>();
        }

        public static T GetOrAddComponent<T>(this GameObject go) where T : Component
        {
            if (go.TryGetComponent(out T instance))
                return instance;
            else
                return go.AddComponent<T>();
        }
    }
}