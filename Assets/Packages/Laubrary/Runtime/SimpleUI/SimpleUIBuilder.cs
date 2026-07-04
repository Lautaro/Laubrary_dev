using UnityEngine;

namespace Laubrary.SimpleUI
{
    /// <summary>
    /// Design-time tool to generate UI hierarchies for a SimpleUIView.
    /// This component can be removed once the UI structure is generated.
    /// </summary>
    [AddComponentMenu("Laubrary/SimpleUI/Simple UI Builder")]
    public class SimpleUIBuilder : MonoBehaviour
    {
        public enum ContainerLayout { Vertical, Horizontal, Grid }
        public enum FieldOrientation { Horizontal, Vertical }

        [Header("Target")]
        [Tooltip("The view that will bind to the generated elements.")]
        public SimpleUIView targetView;

        [Header("Settings")]
        [Tooltip("Optional: Settings asset that holds prefab mappings. If null, basic TextMeshPro will be used.")]
        public SimpleUISettings settings;

        [Header("Layout Settings")]
        public ContainerLayout containerLayout = ContainerLayout.Vertical;
        public FieldOrientation fieldOrientation = FieldOrientation.Horizontal;
        
        [Range(0, 50)]
        public float spacing = 8f;

        // Grid Settings
        [HideInInspector] public int columns = 2;
        [HideInInspector] public Vector2 cellSize = new Vector2(200, 40);

        private void Reset()
        {
            if (targetView == null)
                targetView = GetComponent<SimpleUIView>();
        }
    }
}
