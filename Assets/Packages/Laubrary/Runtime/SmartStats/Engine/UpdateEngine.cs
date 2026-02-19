using System.Collections.Generic;
using UnityEngine;

namespace Lautaro.Stats.Engine
{
    public class UpdateEngine : MonoBehaviour
    {
        public static void Register(IUpdatable updatable)
        {
            Instance.RegisterUpdatable(updatable);
        }

        public static void UnRegister(IUpdatable updatable)
        {
            Instance.UnRegisterUpdatable(updatable);
        }

        private static UpdateEngine _instance;
        public static UpdateEngine Instance
        {
            get
            {
                if (_instance == null)
                {
                    var engineObject = new GameObject("UpdateEngine");
                    _instance = engineObject.AddComponent<UpdateEngine>();
                }
                return _instance;
            }
        }
    
        private List<IUpdatable> updatableInstances = new List<IUpdatable>();

        void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void RegisterUpdatable(IUpdatable updatable)
        {
            if (!updatableInstances.Contains(updatable))
            {
                updatableInstances.Add(updatable);
            }
        }

        private void UnRegisterUpdatable(IUpdatable updatable)
        {
            updatableInstances.Remove(updatable);
        }

        void Update()
        {
            for (int i = updatableInstances.Count - 1; i >= 0; i--)
            {
                if (updatableInstances[i] != null)
                {
                    updatableInstances[i].Update();
                }
                else
                {
                    updatableInstances.RemoveAt(i);
                }
            }
        }
    }
}
