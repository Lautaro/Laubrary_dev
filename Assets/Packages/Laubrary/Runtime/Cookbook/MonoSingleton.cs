using UnityEngine;

namespace Laubrary.Cookbook
{
    public class MonoSingleton<T> : MonoBehaviour where T : Component
    {
        // create a private reference to T instance
        private static T instance;

        public virtual void Awake()
        {
            if (instance == null)
            {
                instance = this as T;
            }
            else if (instance != this)
            {
                Destroy(gameObject);
            }
        }

        public static T I
        {
            get
            {
                // if instance is null
                if (instance == null)
                {
                    GameObject obj = new GameObject();
                    obj.name = typeof(T).Name;
                    instance = obj.AddComponent<T>();
                }
                return instance;
            }
        }
    }
}
