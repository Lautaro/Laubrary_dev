using System.Collections.Generic;
using UnityEngine;

namespace Laubrary.Zounds {

    public class ZoundsWindowProperties : ScriptableObject {

        private static ZoundsWindowProperties instance;
        public static ZoundsWindowProperties Instance {
            get {
                if (instance == null) {
                    instance = Resources.Load<ZoundsWindowProperties>("ZoundsWindowProperties");
                }
                return instance;
            }
        }

        [HideInInspector] public int selectedMainTab = 0;
        [HideInInspector] public int selectedZoundTab = 0;
        [HideInInspector] public ZoundTabProperties[] zoundTabProperties = new ZoundTabProperties[4]{
            new ZoundTabProperties(),
            new ZoundTabProperties(),
            new ZoundTabProperties(),
            new ZoundTabProperties(),
        };

        [HideInInspector] public bool showActiveZounds = false;
        [HideInInspector] public bool showManuallySetRoutings = true;
        [HideInInspector] public bool autoSave = false;
        [HideInInspector] public bool captureCallers = true;

        public static void DirtyAll() {
            foreach (var tabProperty in Instance.zoundTabProperties) {
                tabProperty.dirty = true;
            }
        }

        [System.Serializable]
        public class ZoundTabProperties {

            [System.Flags]
            public enum ZoundType {
                None = 0,
                Klip = 1 << 0,
                Zequence = 1 << 1,
                AudioClip = 1 << 2,
                Missing = 1 << 3,
                Everything = Klip | Zequence | AudioClip | Missing
            }

            public enum GroupBy {
                None, Tags, References, MixerGroup, Type
#if ZOUNDS_CONSIDER_FOLDERS
                Folder
#endif
            }

            public string searchText;
            public ZoundType selectedTypes;
            public List<string> selectedFolders = new List<string>();
            public List<string> selectedTags = new List<string>();
            public List<int> selectedReferences = new List<int>(); // zound ids
            public GroupBy groupBy = GroupBy.None;

            public bool dirty { get; set; } = false;

            public void ClearFilters() {
                searchText = "";
                selectedTypes = ZoundType.Everything;
                selectedFolders.Clear();
                selectedTags.Clear();
                selectedReferences.Clear();
            }

        }

    }

}
