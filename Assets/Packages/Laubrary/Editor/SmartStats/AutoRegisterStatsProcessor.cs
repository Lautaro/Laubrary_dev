using UnityEngine;
using UnityEditor;
using Lautaro.Stats.Engine;

namespace Lautaro.Stats.Editor
{
    /// <summary>
    /// Editor script that automatically adds AutoRegisterStatsBehaviour to GameObjects
    /// whose MonoBehaviours are marked with [AutoRegisterStats] attribute.
    /// </summary>
    [InitializeOnLoad]
    public static class AutoRegisterStatsProcessor
    {
        static AutoRegisterStatsProcessor()
        {
            ObjectFactory.componentWasAdded += OnComponentAdded;
        }

        private static void OnComponentAdded(Component component)
        {
            if (component is MonoBehaviour behaviour)
            {
                var hasAttribute = behaviour.GetType()
                    .GetCustomAttributes(typeof(AutoRegisterStatsAttribute), true)
                    .Length > 0;

                if (hasAttribute)
                {
                    var gameObject = behaviour.gameObject;
                    
                    if (gameObject.GetComponent<AutoRegisterStatsBehaviour>() == null)
                    {
                        gameObject.AddComponent<AutoRegisterStatsBehaviour>();
                    }
                }
            }
        }
    }
}
