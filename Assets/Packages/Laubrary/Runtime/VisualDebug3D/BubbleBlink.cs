using Laubrary.VisualDebug3D.Helpers;
using UnityEngine;

namespace Laubrary.VisualDebug3D
{
    public class BubbleBlink : MonoBehaviour
    {
        public float blinkSpeed = 0.5f;
        public Color color = Color.red;
        public bool useUnlitMaterial = false;
        public int blinkCount = 0; // Default blink count is 0 which makes it live on

        private Material material;
        private float blinkTimer;
        private bool isVisible;
        private Renderer blinkRenderer;
        private MeshRenderer targetRenderer;
        private Transform targetTransform;
        private int remainingBlinks;
        private float scale;

        void Initialize()
        {
            blinkRenderer = GetComponent<Renderer>();
            material = VisualDebug3DHelper.CreateMaterial(useUnlitMaterial);
            material.color = color;

            if (targetRenderer)
                ScaleToCoverMesh();

            if (!useUnlitMaterial)
            {
                SetMaterialTransparent();
                material.color = new Color(color.r, color.g, color.b, 0.5f); // Semi-transparent
            }

            blinkRenderer.material = material;
            blinkTimer = 0;
            isVisible = true;
            remainingBlinks = blinkCount * 2; // Two changes (visible/invisible) per blink
        }

        void Update()
        {
            if (blinkSpeed > 0 && (remainingBlinks > 0 || blinkCount == 0))
            {
                blinkTimer += Time.deltaTime;
                if (blinkTimer >= blinkSpeed)
                {
                    blinkTimer = 0;
                    isVisible = !isVisible;

                    if (!useUnlitMaterial)
                    {
                        if (isVisible)
                        {
                            SetMaterialTransparent();
                            material.color = new Color(color.r, color.g, color.b, 0.5f); // Semi-transparent
                        }
                        else
                        {
                            material.color = new Color(color.r, color.g, color.b, 0f); // Fully transparent
                        }
                    }
                    else
                    {
                        blinkRenderer.enabled = isVisible;
                    }

                    if (blinkCount > 0)
                    {
                        remainingBlinks--;
                        if (remainingBlinks <= 0)
                        {
                            Destroy(gameObject); // Destroy the object after finishing blinking
                        }
                    }
                }
            }
        }

        void SetMaterialTransparent()
        {
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 1);
            material.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
        }

        void ScaleToCoverMesh()
        {
            // Calculate the local scaled size of the mesh
            Vector3 localScale = targetRenderer.transform.localScale;
            Bounds bounds = targetRenderer.bounds;
            Vector3 scaledSize = new Vector3(
                bounds.size.x / localScale.x,
                bounds.size.y / localScale.y,
                bounds.size.z / localScale.z
            );

            // Find the largest dimension of the scaled size
            float largestDimension = Mathf.Max(scaledSize.x, scaledSize.y, scaledSize.z) * scale;

            // Set the sphere size to cover the entire mesh, slightly larger
            transform.localScale = Vector3.one * (largestDimension + 0.1f);

            // Center the sphere at the bounds center
            transform.position = bounds.center;
        }

        public static BubbleBlink Blink(MeshRenderer mesh, Color? color = null, float blinkSpeed = 0.5f, int blinkCount = 5, bool useUnlitMaterial = false, float scale = 1.5f)
        {
            return Blink(color, blinkSpeed, blinkCount, useUnlitMaterial, mesh, scale, null);
        }

        public static BubbleBlink Blink(Vector3 position, Color? color = null, float blinkSpeed = 0.5f, int blinkCount = 5, bool useUnlitMaterial = false, float scale = 1.5f)
        {
            return Blink(color, blinkSpeed, blinkCount, useUnlitMaterial, null, scale, position);
        }

        static BubbleBlink Blink(Color? color = null, float blinkSpeed = 0.5f, int blinkCount = 5, bool useUnlitMaterial = false, MeshRenderer targetMesh = null, float scale = 1.5f, Vector3? position = null)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            BubbleBlink blinkingSphere = sphere.AddComponent<BubbleBlink>();

            blinkingSphere.blinkSpeed = blinkSpeed;
            blinkingSphere.useUnlitMaterial = useUnlitMaterial;
            blinkingSphere.blinkCount = blinkCount;
            blinkingSphere.scale = scale;

            if (color.HasValue)
                blinkingSphere.color = color.Value;
            else
                blinkingSphere.color = Color.white;

            if (targetMesh)
            {
                blinkingSphere.transform.parent = targetMesh.transform;
                blinkingSphere.targetRenderer = targetMesh;
            }
            else if (position.HasValue)
                blinkingSphere.transform.position = position.Value;
            else
            {
                Debug.LogError("To create a Blink3D either a V3 position or a Transform is needed");
                blinkingSphere.transform.position = Vector3.zero;
            }
            blinkingSphere.Initialize();
            return blinkingSphere;
        }
    }
}
