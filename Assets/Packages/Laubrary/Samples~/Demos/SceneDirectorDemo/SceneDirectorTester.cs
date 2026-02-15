using UnityEngine;

namespace Laubrary.SceneDirector.Samples
{
    public class SceneDirectorTester : MonoBehaviour
    {
        public void TriggerLoadingComplete()
        {
            var director = FindObjectOfType<MySceneDirector_Test>();
            if (director != null)
            {
                director.TriggerCategory("LoadingComplete");
                Debug.Log("[SceneDirectorTester] Triggered category: LoadingComplete", director);
            }
            else
            {
                Debug.LogWarning("[SceneDirectorTester] MySceneDirector_Test not found in scene.");
            }
        }

        public void TriggerStartTheAction()
        {
            var director = FindObjectOfType<MySceneDirector_Test>();
            if (director != null)
            {
                director.TriggerCategory("StartTheAction");
                Debug.Log("[SceneDirectorTester] Triggered category: StartTheAction", director);
            }
            else
            {
                Debug.LogWarning("[SceneDirectorTester] MySceneDirector_Test not found in scene.");
            }
        }
    }
}
