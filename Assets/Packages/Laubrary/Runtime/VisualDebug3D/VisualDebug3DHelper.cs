using UnityEngine;
using UnityEngine.Rendering;

namespace Laubrary.VisualDebug3D.Helpers
{
    public static class VisualDebug3DHelper
    {
        public static Material CreateMaterial(bool unlit)
        {
            string shaderName;

            if (GraphicsSettings.defaultRenderPipeline != null)
            {
                string pipelineType = GraphicsSettings.defaultRenderPipeline.GetType().Name;

                switch (pipelineType)
                {
                    case "UniversalRenderPipelineAsset":
                        shaderName = unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit";
                        break;
                    case "HDRenderPipelineAsset":
                        shaderName = unlit ? "HDRP/Unlit" : "HDRP/Lit";
                        break;
                    default:
                        Debug.LogWarning("Unsupported pipeline. Using Built-in pipeline shader.");
                        shaderName = unlit ? "Unlit/Color" : "Standard";
                        break;
                }
            }
            else
            {
                // Default to Built-in Render Pipeline shaders
                shaderName = unlit ? "Unlit/Color" : "Standard";
            }

            return CreateMaterial(shaderName);
        }
        private static Material CreateMaterial(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);

            if (shader != null)
            {
                Material material = new Material(shader);
                // You need to set the color after creating the material
                material.color = Color.white; // Set your desired default color here
                return material;
            }
            else
            {
                Debug.LogWarning($"Shader '{shaderName}' not found. Using default material.");
                Material defaultMaterial = new Material(Shader.Find("Standard"));
                defaultMaterial.color = Color.white; // Set your desired default color here
                return defaultMaterial;
            }
        }

    }
}
