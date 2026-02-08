using Laubrary.VisualDebug3D.Helpers;
using UnityEngine;
using UnityEngine.Rendering;

namespace Laubrary.VisualDebug3D
{
    public class MeshBlink : MonoBehaviour
    {
        public static void Create(GameObject gameObject, Color blinkColor, float blinkSpeed, int blinkCount, bool useUnlitMaterial = false)
        {
            var newGo = new GameObject("MeshBlink");
            MeshBlink blinker = newGo.AddComponent<MeshBlink>();
            blinker.blinkColor = blinkColor;
            blinker.blinkSpeed = blinkSpeed;
            blinker.blinkCount = blinkCount;
            blinker.useUnlitMaterial = useUnlitMaterial;
            blinker.meshRenderer = gameObject.GetComponent<MeshRenderer>();
            blinker.transform.parent = gameObject.transform;
            blinker.Initialize();
        }

        private Color blinkColor;
        private float blinkSpeed;
        public int blinkCount = 0;
        private bool useUnlitMaterial;

        private Material originalMaterial;
        private Material blinkMaterial;
        private MeshRenderer meshRenderer;

        private float blinkTimer = 0f;
        private bool isBlinking = false;
        private int remainingBlinks;

        private void Initialize()
        {
            if (meshRenderer == null)
            {
                Debug.LogError("No MeshRenderer found on the GameObject.");
                Destroy(this);
                return;
            }

            originalMaterial = meshRenderer.material;
            blinkMaterial = VisualDebug3DHelper.CreateMaterial(useUnlitMaterial);
            blinkMaterial.color = blinkColor;

            var shaderName = GetShaderName();
            Shader shader = Shader.Find(shaderName);

            if (shader != null)
            {
                blinkMaterial.shader = shader;
            }
            else
            {
                Debug.LogWarning($"Shader '{shaderName}' not found. Using original shader.");
            }

            if (!useUnlitMaterial)
            {
                SetMaterialTransparent(blinkMaterial);
                blinkMaterial.color = new Color(blinkColor.r, blinkColor.g, blinkColor.b, 0.5f); // Semi-transparent
            }

            remainingBlinks = blinkCount * 2; // Two changes (visible/invisible) per blink
        }

        private void Update()
        {
            if (blinkSpeed > 0 && (remainingBlinks > 0 || blinkCount == 0))
            {
                blinkTimer += Time.deltaTime;
                if (blinkTimer >= blinkSpeed)
                {
                    blinkTimer = 0;
                    isBlinking = !isBlinking;

                    if (!useUnlitMaterial)
                    {
                        if (isBlinking)
                        {
                            SetMaterialTransparent(blinkMaterial);
                            blinkMaterial.color = new Color(blinkColor.r, blinkColor.g, blinkColor.b, 0.5f); // Semi-transparent
                            meshRenderer.material = blinkMaterial;
                        }
                        else
                        {
                            blinkMaterial.color = new Color(blinkColor.r, blinkColor.g, blinkColor.b, 0f); // Fully transparent
                            meshRenderer.material = originalMaterial;
                        }
                    }
                    else
                    {
                        meshRenderer.enabled = isBlinking;
                    }

                    if (blinkCount > 0)
                    {
                        remainingBlinks--;
                        if (remainingBlinks <= 0)
                        {
                            meshRenderer.material = originalMaterial;
                            Destroy(gameObject); // Destroy the component after finishing blinking
                        }
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (meshRenderer != null)
            {
                meshRenderer.material = originalMaterial;
            }
        }

        private void SetMaterialTransparent(Material material)
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

        private string GetShaderName()
        {
            if (GraphicsSettings.defaultRenderPipeline != null)
            {
                string pipelineType = GraphicsSettings.defaultRenderPipeline.GetType().ToString();

                switch (pipelineType)
                {
                    case "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset":
                        return useUnlitMaterial ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit";
                    case "UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset":
                        return useUnlitMaterial ? "HDRP/Unlit" : "HDRP/Lit";
                    default:
                        Debug.LogWarning("Unsupported pipeline. Using Built-in pipeline shader.");
                        break;
                }
            }

            // Default/Built-in pipeline
            return useUnlitMaterial ? "Unlit/Color" : "Standard";
        }
    }
}
