using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zounds {

    public class RuntimeUpdater : MonoBehaviour {

        public System.Action onUpdate;

        private static RuntimeUpdater instance;

        public static RuntimeUpdater Instance {
            get {
                if (instance == null) {
                    if (Application.isPlaying) {
                        var go = new GameObject("ZoundsRuntimeUpdater");
                        instance = go.AddComponent<RuntimeUpdater>();
                        DontDestroyOnLoad(go);
                    }
                    else {
                        Debug.LogError("Can't use runtime updater during edit mode.");
                        return null;
                    }
                }
                return instance;
            }
        }

        private void Update() {
            onUpdate?.Invoke();
        }

        private void OnDestroy() {
            if (instance == this) {
                instance = null;
            }
        }

    }

}