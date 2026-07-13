using UnityEngine;

namespace Laubrary.Zounds {
    internal class ZoundsTempData : ScriptableObject {

#if UNITY_EDITOR
        private static ZoundsTempData instance;
        public static ZoundsTempData Instance {
            get {
                if (instance == null) {
                    instance = Resources.Load<ZoundsTempData>("ZoundsTempData");
                    if (instance == null) {
                        var systemPath = ZoundsProject.Instance.projectSettings.systemFolderPath;
                        ZoundsProject.EnsureDirectoryExists(systemPath + "/Resources");
                        instance = CreateInstance<ZoundsTempData>();
                        UnityEditor.AssetDatabase.CreateAsset(instance, systemPath + "/Resources/ZoundsTempData.asset");
                    }
                }
                return instance;
            }
        }
#endif

    }
}
