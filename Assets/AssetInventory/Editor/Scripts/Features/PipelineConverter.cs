// ConverterContainerId, ConverterId, ConverterFilter are deprecated but the new string-based API is not yet stable
#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
#if USE_URP
using UnityEditor.Rendering.Universal;
#endif
using UnityEngine;


namespace AssetInventory
{
    /// <summary>
    /// Handles conversion of materials from the Built-in Render Pipeline (BIRP) to the
    /// current Scriptable Render Pipeline (URP or HDRP).
    ///
    /// Two conversion mechanisms are available:
    /// 1. Unity's built-in converter (uses the Converters API, URP only)
    /// 2. Custom converter (manual property remapping, works for both URP and HDRP)
    ///
    /// The custom converter acts as a fallback when Unity's converter is unavailable,
    /// disabled, or fails.
    /// </summary>
    public static class PipelineConverter
    {
        #region Unity Converter (URP only)

        /// <summary>
        /// Runs the URP material converter if available (fire-and-forget).
        /// </summary>
        /// <returns>True if the Unity converter ran without error, false otherwise.</returns>
        public static bool RunUnityConverter()
        {
#if USE_URP
            if (AssetUtils.IsOnURP())
            {
                try
                {
                    Converters.RunInBatchMode(
                        ConverterContainerId.BuiltInToURP
                        , new List<ConverterId>
                        {
                            ConverterId.Material
                        }
                        , ConverterFilter.Inclusive
                    );
                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Could not run URP converter: {e.Message}");
                    return false;
                }
            }
#endif
            return false;
        }

        /// <summary>
        /// Runs the URP material converter and waits for it to complete.
        /// Uses reflection to await the callback-based Scan method which fires asynchronously.
        /// Use this instead of RunUnityConverter when subsequent operations (e.g. preview generation)
        /// depend on the materials being fully converted.
        /// </summary>
        /// <returns>True if the Unity converter ran successfully, false otherwise.</returns>
        public static async Task<bool> RunUnityConverterAsync()
        {
#if USE_URP
            if (!AssetUtils.IsOnURP()) return false;

            try
            {
                // Get converter types via reflection since FilterConverters is internal
                MethodInfo filterMethod = typeof(Converters).GetMethod("FilterConverters", BindingFlags.NonPublic | BindingFlags.Static);
                if (filterMethod == null) return false;

                List<Type> converterTypes = (List<Type>)filterMethod.Invoke(null, new object[]
                {
                    ConverterContainerId.BuiltInToURP,
                    new List<ConverterId> {ConverterId.Material},
                    ConverterFilter.Inclusive
                });

                bool anyConverted = false;
                foreach (Type converterType in converterTypes)
                {
                    object converter = Activator.CreateInstance(converterType);
                    if (converter == null) continue;

                    // Get the Scan method: void Scan(Action<List<IRenderPipelineConverterItem>>)
                    MethodInfo scanMethod = converter.GetType().GetMethod("Scan");
                    if (scanMethod == null) continue;

                    ParameterInfo[] scanParams = scanMethod.GetParameters();
                    if (scanParams.Length != 1) continue;

                    // Extract the callback delegate type: Action<List<IRenderPipelineConverterItem>>
                    Type callbackType = scanParams[0].ParameterType;

                    // Extract the item type from the generic arguments: List<T> -> T
                    Type listType = callbackType.GetGenericArguments()[0]; // List<IRenderPipelineConverterItem>
                    Type itemType = listType.GetGenericArguments()[0]; // IRenderPipelineConverterItem

                    // Get BeforeConvert, Convert, AfterConvert methods
                    MethodInfo beforeConvert = converter.GetType().GetMethod("BeforeConvert");
                    MethodInfo convertMethod = converter.GetType().GetMethod("Convert");
                    MethodInfo afterConvert = converter.GetType().GetMethod("AfterConvert");

                    TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

                    // Create callback state and get the generic instance method specialized to the item type
                    ConverterCallbackState state = new ConverterCallbackState
                    {
                        Converter = converter, BeforeConvert = beforeConvert,
                        ConvertMethod = convertMethod, AfterConvert = afterConvert, Tcs = tcs
                    };
                    MethodInfo helperMethod = typeof(ConverterCallbackState)
                        .GetMethod(nameof(ConverterCallbackState.OnScanFinished))
                        .MakeGenericMethod(itemType);

                    // Create the delegate that matches Action<List<IRenderPipelineConverterItem>>
                    Delegate callback = Delegate.CreateDelegate(callbackType, state, helperMethod);

                    // Invoke Scan with our callback
                    scanMethod.Invoke(converter, new object[] {callback});

                    // Wait for the callback to fire, with a timeout
                    Task completed = await Task.WhenAny(tcs.Task, Task.Delay(30000));
                    if (completed != tcs.Task)
                    {
                        Debug.LogWarning($"URP converter '{converterType.Name}' timed out after 30 seconds.");
                    }
                    else if (tcs.Task.Result)
                    {
                        anyConverted = true;
                    }
                }

                AssetDatabase.SaveAssets();
                return anyConverted;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not run async URP converter: {e.Message}");
                return false;
            }
#else
            await Task.CompletedTask;
            return false;
#endif
        }

        /// <summary>
        /// State object and callback handler for the reflection-based converter scan.
        /// The generic OnScanFinished method is specialized at runtime via MakeGenericMethod
        /// to match the internal IRenderPipelineConverterItem type.
        /// </summary>
        private class ConverterCallbackState
        {
            public object Converter;
            public MethodInfo BeforeConvert;
            public MethodInfo ConvertMethod;
            public MethodInfo AfterConvert;
            public TaskCompletionSource<bool> Tcs;

            public void OnScanFinished<T>(List<T> items)
            {
                try
                {
                    BeforeConvert?.Invoke(Converter, null);
                    foreach (T item in items)
                    {
                        object[] args = {item, null};
                        ConvertMethod.Invoke(Converter, args);
                    }
                    AfterConvert?.Invoke(Converter, null);
                    Tcs.TrySetResult(true);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"URP converter callback failed: {e.Message}");
                    Tcs.TrySetResult(false);
                }
            }
        }

        #endregion

        #region Custom Converter (URP + HDRP)

        /// <summary>
        /// Converts material assets at the given project-relative paths from BIRP to the current
        /// render pipeline using the custom converter logic. Acts as fallback when Unity's
        /// built-in converter is unavailable or fails.
        /// </summary>
        public static void ConvertImportedMaterials(IEnumerable<string> importedPaths)
        {
            bool isOnURP = AssetUtils.IsOnURP();
            bool isOnHDRP = AssetUtils.IsOnHDRP();
            if (!isOnURP && !isOnHDRP) return;

            bool anyChanged = false;
            foreach (string path in importedPaths)
            {
                if (path == null || !path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) continue;

                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                string shaderName = mat.shader != null ? mat.shader.name : "";
                if (!PrefabPreviewUtilities.IsBIRPShader(shaderName)) continue;

                ConvertMaterial(mat, shaderName, isOnURP, isOnHDRP);
                anyChanged = true;
            }

            if (anyChanged) AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Scans all materials in the project and converts any remaining BIRP materials
        /// to the current render pipeline. Used after bulk/package imports.
        /// </summary>
        public static void ConvertAllProjectMaterials()
        {
            bool isOnURP = AssetUtils.IsOnURP();
            bool isOnHDRP = AssetUtils.IsOnHDRP();
            if (!isOnURP && !isOnHDRP) return;

            string[] materialGuids = AssetDatabase.FindAssets("t:Material");
            bool anyChanged = false;
            foreach (string guid in materialGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/")) continue;

                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                string shaderName = mat.shader != null ? mat.shader.name : "";
                if (!PrefabPreviewUtilities.IsBIRPShader(shaderName)) continue;

                ConvertMaterial(mat, shaderName, isOnURP, isOnHDRP);
                anyChanged = true;
            }

            if (anyChanged) AssetDatabase.SaveAssets();
        }

        #endregion

        #region Material Conversion

        /// <summary>
        /// Converts a single material asset in-place from BIRP to the appropriate SRP shader.
        /// Mirrors the conversion logic from Unity's built-in MaterialUpgrader providers:
        /// - Standard / Standard (Specular setup) → URP/Lit or HDRP/Lit
        /// - Legacy Shaders/* → URP/Simple Lit or HDRP/Lit
        /// - Particles/Standard Surface, Particles/Standard Unlit → URP/HDRP Particles shaders
        /// - Mobile/* → URP/Simple Lit or HDRP/Lit
        /// Handles rendering mode (opaque/cutout/fade/transparent), specular workflow,
        /// smoothness source, keywords, and blend state.
        /// </summary>
        private static void ConvertMaterial(Material mat, string shaderName, bool isOnURP, bool isOnHDRP)
        {
            // --- Modern BIRP particle shaders (Particles/Standard Surface, Particles/Standard Unlit) ---
            if (shaderName == "Particles/Standard Surface" || shaderName == "Particles/Standard Unlit")
            {
                ConvertParticleMaterial(mat, shaderName, isOnURP);
                return;
            }

            // --- Standard (metallic workflow) ---
            if (shaderName == "Standard")
            {
                ConvertStandardMaterial(mat, isOnURP, isOnHDRP, specularSetup: false);
                return;
            }

            // --- Standard (Specular setup) ---
            if (shaderName == "Standard (Specular setup)")
            {
                ConvertStandardMaterial(mat, isOnURP, isOnHDRP, specularSetup: true);
                return;
            }

            // --- Legacy / Mobile / Sprites shaders → SimpleLit (URP) or Lit (HDRP) ---
            ConvertLegacyMaterial(mat, shaderName, isOnURP, isOnHDRP);
        }

        /// <summary>
        /// Converts a Standard or Standard (Specular setup) material to URP/Lit or HDRP/Lit.
        /// Closely follows Unity's StandardUpgrader logic.
        /// </summary>
        private static void ConvertStandardMaterial(Material mat, bool isOnURP, bool isOnHDRP, bool specularSetup)
        {
            // Cache properties before shader change
            Color color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
            Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
            Vector2 tiling = mat.HasProperty("_MainTex") ? mat.GetTextureScale("_MainTex") : Vector2.one;
            Vector2 offset = mat.HasProperty("_MainTex") ? mat.GetTextureOffset("_MainTex") : Vector2.zero;
            float metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;
            float glossiness = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : 0.5f;
            float glossMapScale = mat.HasProperty("_GlossMapScale") ? mat.GetFloat("_GlossMapScale") : 1f;
            Texture metallicGlossMap = mat.HasProperty("_MetallicGlossMap") ? mat.GetTexture("_MetallicGlossMap") : null;
            Texture specGlossMap = mat.HasProperty("_SpecGlossMap") ? mat.GetTexture("_SpecGlossMap") : null;
            Color specColor = mat.HasProperty("_SpecColor") ? mat.GetColor("_SpecColor") : new Color(0.2f, 0.2f, 0.2f, 1f);
            Texture bumpMap = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
            float bumpScale = mat.HasProperty("_BumpScale") ? mat.GetFloat("_BumpScale") : 1f;
            Texture occlusionMap = mat.HasProperty("_OcclusionMap") ? mat.GetTexture("_OcclusionMap") : null;
            float occlusionStrength = mat.HasProperty("_OcclusionStrength") ? mat.GetFloat("_OcclusionStrength") : 1f;
            bool emissionEnabled = mat.IsKeywordEnabled("_EMISSION");
            Color emissionColor = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;
            Texture emissionMap = mat.HasProperty("_EmissionMap") ? mat.GetTexture("_EmissionMap") : null;
            float renderingMode = mat.HasProperty("_Mode") ? mat.GetFloat("_Mode") : 0f;
            float cutoff = mat.HasProperty("_Cutoff") ? mat.GetFloat("_Cutoff") : 0.5f;
            float glossyReflections = mat.HasProperty("_GlossyReflections") ? mat.GetFloat("_GlossyReflections") : 1f;
            Texture detailAlbedo = mat.HasProperty("_DetailAlbedoMap") ? mat.GetTexture("_DetailAlbedoMap") : null;
            Texture detailNormal = mat.HasProperty("_DetailNormalMap") ? mat.GetTexture("_DetailNormalMap") : null;
            float detailNormalScale = mat.HasProperty("_DetailNormalMapScale") ? mat.GetFloat("_DetailNormalMapScale") : 1f;
            Vector2 detailScale = mat.HasProperty("_DetailAlbedoMap") ? mat.GetTextureScale("_DetailAlbedoMap") : Vector2.one;
            Vector2 detailOffset = mat.HasProperty("_DetailAlbedoMap") ? mat.GetTextureOffset("_DetailAlbedoMap") : Vector2.zero;
            Texture detailMask = mat.HasProperty("_DetailMask") ? mat.GetTexture("_DetailMask") : null;

            // Change shader
            string targetShaderName = isOnURP ? "Universal Render Pipeline/Lit" : "HDRP/Lit";
            Shader targetShader = Shader.Find(targetShaderName);
            if (targetShader == null) return;

            mat.shader = targetShader;

            // --- Remap properties (mirrors Unity's StandardUpgrader RenameTexture/RenameColor/RenameFloat) ---
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mainTex != null && mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", mainTex);
                mat.SetTextureScale("_BaseMap", tiling);
                mat.SetTextureOffset("_BaseMap", offset);
            }

            // Smoothness: Unity picks from _GlossMapScale when metallic/spec map exists, else _Glossiness
            float smoothness;
            if (specularSetup)
            {
                smoothness = specGlossMap != null ? glossMapScale : glossiness;
            }
            else
            {
                smoothness = metallicGlossMap != null ? glossMapScale : glossiness;
            }
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);

            // Workflow mode: 1.0 = Metallic, 0.0 = Specular
            if (mat.HasProperty("_WorkflowMode")) mat.SetFloat("_WorkflowMode", specularSetup ? 0f : 1f);

            // Metallic / Specular maps
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (metallicGlossMap != null && mat.HasProperty("_MetallicGlossMap")) mat.SetTexture("_MetallicGlossMap", metallicGlossMap);
            if (specularSetup)
            {
                if (mat.HasProperty("_SpecColor")) mat.SetColor("_SpecColor", specColor);
                if (specGlossMap != null && mat.HasProperty("_SpecGlossMap")) mat.SetTexture("_SpecGlossMap", specGlossMap);
            }

            // Normal map
            if (bumpMap != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", bumpMap);
                if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", bumpScale);
            }

            // Occlusion
            if (occlusionMap != null && mat.HasProperty("_OcclusionMap"))
            {
                mat.SetTexture("_OcclusionMap", occlusionMap);
                if (mat.HasProperty("_OcclusionStrength")) mat.SetFloat("_OcclusionStrength", occlusionStrength);
            }

            // Emission
            if (emissionEnabled)
            {
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emissionColor);
                if (emissionMap != null && mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", emissionMap);
            }

            // Environment reflections
            if (mat.HasProperty("_EnvironmentReflections")) mat.SetFloat("_EnvironmentReflections", glossyReflections);

            // Detail maps
            if (detailAlbedo != null && mat.HasProperty("_DetailAlbedoMap"))
            {
                mat.SetTexture("_DetailAlbedoMap", detailAlbedo);
                // In URP detail tile/offset is multiplied with base, so adjust accordingly
                if (isOnURP)
                {
                    Vector2 adjScale = new Vector2(
                        tiling.x != 0 ? detailScale.x / tiling.x : 0,
                        tiling.y != 0 ? detailScale.y / tiling.y : 0);
                    mat.SetTextureScale("_DetailAlbedoMap", adjScale);
                    mat.SetTextureOffset("_DetailAlbedoMap", new Vector2(
                        detailOffset.x - offset.x * adjScale.x,
                        detailOffset.y - offset.y * adjScale.y));
                }
                else
                {
                    mat.SetTextureScale("_DetailAlbedoMap", detailScale);
                    mat.SetTextureOffset("_DetailAlbedoMap", detailOffset);
                }
            }
            if (detailNormal != null && mat.HasProperty("_DetailNormalMap"))
            {
                mat.SetTexture("_DetailNormalMap", detailNormal);
                if (mat.HasProperty("_DetailNormalMapScale")) mat.SetFloat("_DetailNormalMapScale", detailNormalScale);
            }
            if (detailMask != null && mat.HasProperty("_DetailMask")) mat.SetTexture("_DetailMask", detailMask);

            // Alpha clip
            bool isAlphaTest = mat.IsKeywordEnabled("_ALPHATEST_ON") || Mathf.Approximately(renderingMode, 1f);
            if (isAlphaTest && mat.HasProperty("_AlphaClip"))
            {
                mat.SetFloat("_AlphaClip", 1f);
                if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", cutoff);
            }

            // Surface type and blend mode (mirrors Unity's UpdateSurfaceTypeAndBlendMode)
            // _Mode in BIRP: 0=Opaque, 1=Cutout, 2=Fade, 3=Transparent
            int mode = Mathf.RoundToInt(renderingMode);
            if (isOnURP)
            {
                switch (mode)
                {
                    case 3: // Transparent → Premultiply
                        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f); // Transparent
                        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 1f); // Premultiply
                        break;
                    case 2: // Fade → Alpha
                        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f); // Transparent
                        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f); // Alpha
                        break;
                    default: // Opaque or Cutout
                        mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f); // Opaque
                        break;
                }
            }
            else // HDRP
            {
                switch (mode)
                {
                    case 2: // Fade
                    case 3: // Transparent
                        if (mat.HasProperty("_SurfaceType")) mat.SetFloat("_SurfaceType", 1f);
                        if (mat.HasProperty("_BlendMode")) mat.SetFloat("_BlendMode", mode == 3 ? 4f : 0f); // Premultiply : Alpha
                        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        mat.EnableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
                        break;
                    default:
                        if (mat.HasProperty("_SurfaceType")) mat.SetFloat("_SurfaceType", 0f);
                        mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        break;
                }
            }

            // Keywords (mirrors Unity's UpdateStandardMaterialKeywords / UpdateStandardSpecularMaterialKeywords)
            SetKeyword(mat, "_NORMALMAP", bumpMap != null);
            SetKeyword(mat, "_OCCLUSIONMAP", occlusionMap != null);
            if (specularSetup)
            {
                SetKeyword(mat, "_METALLICSPECGLOSSMAP", specGlossMap != null);
                SetKeyword(mat, "_SPECULAR_SETUP", true);
            }
            else
            {
                SetKeyword(mat, "_METALLICSPECGLOSSMAP", metallicGlossMap != null);
            }
            mat.DisableKeyword("LOD_FADE_CROSSFADE");

            EditorUtility.SetDirty(mat);
        }

        /// <summary>
        /// Converts legacy BIRP shaders (Legacy Shaders/*, Mobile/*, Sprites/Diffuse)
        /// to URP/Simple Lit or HDRP/Lit. Mirrors Unity's StandardSimpleLightingUpgrader.
        /// </summary>
        private static void ConvertLegacyMaterial(Material mat, string shaderName, bool isOnURP, bool isOnHDRP)
        {
            // Cache common properties
            Color color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
            Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
            Vector2 tiling = mat.HasProperty("_MainTex") ? mat.GetTextureScale("_MainTex") : Vector2.one;
            Vector2 offset = mat.HasProperty("_MainTex") ? mat.GetTextureOffset("_MainTex") : Vector2.zero;
            Texture bumpMap = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
            float bumpScale = mat.HasProperty("_BumpScale") ? mat.GetFloat("_BumpScale") : 1f;
            float shininess = mat.HasProperty("_Shininess") ? mat.GetFloat("_Shininess") : 0.5f;
            Color specColor = mat.HasProperty("_SpecColor") ? mat.GetColor("_SpecColor") : new Color(0.2f, 0.2f, 0.2f, 1f);
            bool emissionEnabled = mat.IsKeywordEnabled("_EMISSION");
            Color emissionColor = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;
            Texture emissionMap = mat.HasProperty("_EmissionMap") ? mat.GetTexture("_EmissionMap") : null;
            Texture illumTex = mat.HasProperty("_Illum") ? mat.GetTexture("_Illum") : null;
            float cutoff = mat.HasProperty("_Cutoff") ? mat.GetFloat("_Cutoff") : 0.5f;

            // Determine upgrade parameters based on shader name (mirrors MaterialUpgraderProviders)
            bool isTransparent = shaderName.Contains("Transparent/") && !shaderName.Contains("Cutout/");
            bool isCutout = shaderName.Contains("Cutout/") || shaderName.Contains("Cutout");
            bool isSpecular = shaderName.Contains("Specular") || shaderName.Contains("VertexLit");
            bool isSelfIllum = shaderName.Contains("Self-Illumin");

            // Target shader
            string targetShaderName;
            if (isOnURP)
            {
                targetShaderName = "Universal Render Pipeline/Simple Lit";
            }
            else
            {
                // HDRP has no SimpleLit, use Lit
                targetShaderName = "HDRP/Lit";
            }
            Shader targetShader = Shader.Find(targetShaderName);
            if (targetShader == null)
            {
                // Fallback to Lit if SimpleLit not found
                targetShader = Shader.Find(isOnURP ? "Universal Render Pipeline/Lit" : "HDRP/Lit");
                if (targetShader == null) return;
            }

            mat.shader = targetShader;

            // Remap properties
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mainTex != null && mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", mainTex);
                mat.SetTextureScale("_BaseMap", tiling);
                mat.SetTextureOffset("_BaseMap", offset);
            }

            // Smoothness from shininess
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", shininess);

            // Normal map
            if (bumpMap != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", bumpMap);
                if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", bumpScale);
            }

            // Specular color
            if (isSpecular && mat.HasProperty("_SpecColor")) mat.SetColor("_SpecColor", specColor);

            // Self-Illumin: _Illum → _EmissionMap
            if (isSelfIllum && illumTex != null)
            {
                if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", illumTex);
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.white);
                mat.EnableKeyword("_EMISSION");
            }
            else if (emissionEnabled)
            {
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emissionColor);
                if (emissionMap != null && mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", emissionMap);
            }

            // Surface type
            if (isOnURP)
            {
                if (isTransparent)
                {
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
                    if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f); // Alpha
                }
                else
                {
                    mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);
                }

                if (isCutout)
                {
                    if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 1f);
                    if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", cutoff);
                }
            }
            else // HDRP
            {
                if (isTransparent)
                {
                    if (mat.HasProperty("_SurfaceType")) mat.SetFloat("_SurfaceType", 1f);
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.EnableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
                }
                else
                {
                    if (mat.HasProperty("_SurfaceType")) mat.SetFloat("_SurfaceType", 0f);
                    mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                }

                if (isCutout)
                {
                    if (mat.HasProperty("_AlphaCutoffEnable")) mat.SetFloat("_AlphaCutoffEnable", 1f);
                    if (mat.HasProperty("_AlphaCutoff")) mat.SetFloat("_AlphaCutoff", cutoff);
                    mat.EnableKeyword("_ALPHATEST_ON");
                }
            }

            // Keywords
            SetKeyword(mat, "_NORMALMAP", bumpMap != null);

            // Specular source for SimpleLit
            if (isOnURP)
            {
                if (isSpecular)
                {
                    if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", 1f);
                }
                else
                {
                    if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", 0f);
                }
            }

            mat.DisableKeyword("LOD_FADE_CROSSFADE");

            EditorUtility.SetDirty(mat);
        }

        /// <summary>
        /// Converts BIRP particle materials to URP or HDRP particle shaders.
        /// Handles both "modern" BIRP particle shaders (Particles/Standard Surface, Particles/Standard Unlit)
        /// and legacy simple particle shaders (Particles/Additive, Particles/Alpha Blended, Particles/Multiply, etc.).
        /// </summary>
        private static void ConvertParticleMaterial(Material mat, string shaderName, bool isOnURP)
        {
            // Cache common properties
            Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
            Vector2 tiling = mat.HasProperty("_MainTex") ? mat.GetTextureScale("_MainTex") : Vector2.one;
            Vector2 offset = mat.HasProperty("_MainTex") ? mat.GetTextureOffset("_MainTex") : Vector2.zero;

            // --- Modern BIRP particle shaders (Particles/Standard Unlit, Particles/Standard Surface) ---
            Color color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
            float glossiness = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : 0.5f;
            float mode = mat.HasProperty("_Mode") ? mat.GetFloat("_Mode") : 0f;
            float flipbook = mat.HasProperty("_FlipbookMode") ? mat.GetFloat("_FlipbookMode") : 0f;
            float cutoff = mat.HasProperty("_Cutoff") ? mat.GetFloat("_Cutoff") : 0.5f;

            // Determine target shader
            bool isUnlit = shaderName.Contains("Unlit");
            string targetShaderName;
            if (isOnURP)
            {
                targetShaderName = isUnlit
                    ? "Universal Render Pipeline/Particles/Unlit"
                    : "Universal Render Pipeline/Particles/Lit";
            }
            else
            {
                targetShaderName = "HDRP/Particles/Unlit";
            }

            Shader targetShader = Shader.Find(targetShaderName);
            if (targetShader == null) return;

            mat.shader = targetShader;

            // Remap properties
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mainTex != null && mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", mainTex);
                mat.SetTextureScale("_BaseMap", tiling);
                mat.SetTextureOffset("_BaseMap", offset);
            }
            if (!isUnlit && mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", glossiness);
            if (mat.HasProperty("_FlipbookBlending")) mat.SetFloat("_FlipbookBlending", flipbook);

            // Surface/blend mode (mirrors Unity's ParticleUpgrader.UpdateSurfaceBlendModes)
            int modeInt = Mathf.RoundToInt(mode);
            switch (modeInt)
            {
                case 0: // Opaque
                    mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);
                    break;
                case 1: // Cutout
                    mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);
                    if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 1f);
                    if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", cutoff);
                    break;
                case 2: // Fade → Alpha
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
                    if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
                    break;
                case 3: // Transparent → Premultiply
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
                    if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 1f);
                    break;
                case 4: // Additive
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
                    if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 2f);
                    break;
                case 6: // Modulate → Multiply
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
                    if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 3f);
                    break;
            }

            mat.DisableKeyword("LOD_FADE_CROSSFADE");
            EditorUtility.SetDirty(mat);
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Helper to set or clear a shader keyword on a material.
        /// </summary>
        private static void SetKeyword(Material mat, string keyword, bool enabled)
        {
            if (enabled) mat.EnableKeyword(keyword);
            else mat.DisableKeyword(keyword);
        }

        #endregion
    }
}

