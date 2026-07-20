#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Linq;

[InitializeOnLoad]
internal static class GraciaSplatsSetupVerifier
{
    public static class BuildDefines
    {
        public static bool AddDefine(string symbol, NamedBuildTarget target)
        {
            var defines = GetDefines(target);
            if (defines.Add(symbol))
            {
                SaveDefines(target, defines);
                return true;
            }
            return false;
        }

        public static bool RemoveDefine(string symbol, NamedBuildTarget target)
        {
            var defines = GetDefines(target);
            if (defines.Remove(symbol))
            {
                SaveDefines(target, defines);
                return true;
            }
            return false;
        }

        public static bool HasDefine(string symbol, NamedBuildTarget target)
        {
            return GetDefines(target).Contains(symbol);
        }

        static HashSet<string> GetDefines(NamedBuildTarget target)
        {
            var raw = PlayerSettings.GetScriptingDefineSymbols(target) ?? "";
            return new HashSet<string>(raw.Split(';', StringSplitOptions.RemoveEmptyEntries));
        }

        static void SaveDefines(NamedBuildTarget target, HashSet<string> set)
        {
            var merged = string.Join(";", set.OrderBy(s => s));
            PlayerSettings.SetScriptingDefineSymbols(target, merged);
        }
    }

    static bool _sessionDone;
    static GraciaSplatsSetupVerifier() => EditorApplication.delayCall += RunOncePerLaunch;

    [MenuItem("Gracia/Validate and Fix Project Settings", false, 100)]
    public static void ValidateAndFixProjectSettings()
    {
        if (ApplyFixes(out string summary, out bool gapiChanged))
        {
            Debug.Log("[GraciaSplats] Project settings fixed:\n" + summary);
            EditorUtility.DisplayDialog("GraciaSplats – Project Adjusted", summary, "OK");
            AssetDatabase.SaveAssets();

            if (gapiChanged)
            {
                if (EditorUtility.DisplayDialog("GraciaSplats – Restart Required",
                    "Unity must restart to apply Graphics-API changes.", "Restart Now", "Later"))
                {
                    EditorApplication.delayCall += () => EditorApplication.OpenProject(Environment.CurrentDirectory);
                }
            }
        }
        else
        {
            Debug.Log("[GraciaSplats] All project settings are correctly configured.");
            EditorUtility.DisplayDialog("GraciaSplats – All Settings Valid", 
                "All project settings are correctly configured for GraciaSplats.", "OK");
        }
    }

    static void RunOncePerLaunch()
    {
        if (_sessionDone)
            return;
        _sessionDone = true;

        if (Application.isBatchMode)
        {
            if (ProjectNeedsFixing(out string bad))
                Debug.LogError("[GraciaSplats] INVALID PROJECT SETTINGS:\n" + bad);
            return;
        }

        if (ApplyFixes(out string summary, out bool gapiChanged))
        {
            EditorUtility.DisplayDialog("GraciaSplats – project adjusted", summary, "OK");
            AssetDatabase.SaveAssets();

            if (gapiChanged)
            {
                EditorUtility.DisplayDialog("GraciaSplats – restart required",
                                            "Unity must restart to apply Graphics-API changes.", "Restart now");
                EditorApplication.delayCall += () => EditorApplication.OpenProject(Environment.CurrentDirectory);
            }
        }
    }

    static bool ApplyFixes(out string report, out bool graphicsApiChanged)
    {
        bool changed = false;
        graphicsApiChanged = false;
        var log = new StringBuilder();

        changed |= ForceVulkan(BuildTarget.StandaloneWindows64, log, ref graphicsApiChanged);
        changed |= ForceVulkan(BuildTarget.Android, log, ref graphicsApiChanged);
        changed |= EnsureQualityMsaaOff(log);
        changed |= EnsureHdrDisplayOff(log);
        changed |= FixUrpAssetsAndRenderers(log);
        changed |= EnsureSinglePassInstanced(log);
        changed |= SetupOvrAppswDefine(log);
        changed |= SetupPxrAppswDefine(log);

        report = log.ToString().TrimEnd();

        return changed;
    }

    static bool ProjectNeedsFixing(out string report)
    {
        var log = new StringBuilder();
        bool bad = false;

        bad |= VulkanNeedsFix(BuildTarget.StandaloneWindows64, log);
        bad |= VulkanNeedsFix(BuildTarget.Android, log);
        bad |= QualityMsaaNeedsFix(log);
        bad |= HdrDisplayNeedsFix(log);
        bad |= UrpAssetsNeedFix(log);
        bad |= SinglePassInstancedNeedsFix(log);

        report = log.ToString().TrimEnd();
        return bad;
    }

    static bool ForceVulkan(BuildTarget tgt, StringBuilder log, ref bool gapiChanged)
    {
        bool changed = false;

        if (PlayerSettings.GetUseDefaultGraphicsAPIs(tgt))
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(tgt, false);
            changed = true;
            log?.AppendLine($"• {tgt}: Auto Graphics API → OFF");
        }

        var apis = PlayerSettings.GetGraphicsAPIs(tgt);
        if (apis.Length != 1 || apis[0] != GraphicsDeviceType.Vulkan)
        {
            PlayerSettings.SetGraphicsAPIs(tgt, new[] { GraphicsDeviceType.Vulkan });
            changed = true;
            log?.AppendLine($"• {tgt}: Graphics API → Vulkan only");
        }

        if (changed)
            gapiChanged = true;

        return changed;
    }

    static bool VulkanNeedsFix(BuildTarget tgt, StringBuilder log)
    {
        var apis = PlayerSettings.GetGraphicsAPIs(tgt);
        if (apis.Length == 1 && apis[0] == GraphicsDeviceType.Vulkan)
            return false;
        log?.AppendLine($"• {tgt}: not Vulkan-only");
        return true;
    }

    static bool EnsureQualityMsaaOff(StringBuilder log)
    {
        bool changed = false;
        int orig = QualitySettings.GetQualityLevel();

        for (int i = 0; i < QualitySettings.names.Length; ++i)
        {
            QualitySettings.SetQualityLevel(i, false);
            if (QualitySettings.antiAliasing != 0)
            {
                QualitySettings.antiAliasing = 0;
                changed = true;
            }
        }
        QualitySettings.SetQualityLevel(orig, false);
        if (changed)
            log.AppendLine("• Quality: MSAA OFF (all levels)");
        return changed;
    }

    static bool QualityMsaaNeedsFix(StringBuilder log)
    {
        int orig = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; ++i)
        {
            QualitySettings.SetQualityLevel(i, false);
            if (QualitySettings.antiAliasing != 0)
            {
                log?.AppendLine("• Quality: MSAA enabled");
                QualitySettings.SetQualityLevel(orig, false);
                return true;
            }
        }
        QualitySettings.SetQualityLevel(orig, false);
        return false;
    }

    static bool EnsureHdrDisplayOff(StringBuilder log)
    {
        bool changed = false;
        if (PlayerSettings.allowHDRDisplaySupport)
        {
            PlayerSettings.allowHDRDisplaySupport = false;
            changed = true;
        }
        if (PlayerSettings.useHDRDisplay)
        {
            PlayerSettings.useHDRDisplay = false;
            changed = true;
        }
        if (changed)
            log.AppendLine("• Project: HDR display OFF");
        return changed;
    }

    static bool HdrDisplayNeedsFix(StringBuilder log)
    {
        if (PlayerSettings.allowHDRDisplaySupport || PlayerSettings.useHDRDisplay)
        {
            log?.AppendLine("• Project: HDR display ON");
            return true;
        }
        return false;
    }

    const int RENDER_MODE_SINGLE_PASS_INSTANCED = 1;
    
    static string GetRenderModeName(int renderMode)
    {
        return renderMode switch
        {
            0 => "Multi Pass",
            1 => "Single Pass Instanced",
            2 => "Multiview",
            _ => renderMode.ToString()
        };
    }
    
    static bool EnsureSinglePassInstanced(StringBuilder log)
    {
        bool changed = false;
        
        changed |= EnsureOpenXRRenderMode(BuildTargetGroup.Standalone, log);
        changed |= EnsureOpenXRRenderMode(BuildTargetGroup.Android, log);
        
        return changed;
    }

    static bool EnsureOpenXRRenderMode(BuildTargetGroup buildTargetGroup, StringBuilder log)
    {
        Type openXRSettingsType = FindType("UnityEngine.XR.OpenXR.OpenXRSettings");
        if (openXRSettingsType == null)
            return false;
        
        var getSettingsMethod = openXRSettingsType.GetMethod("GetSettingsForBuildTargetGroup", 
            BindingFlags.Static | BindingFlags.Public);
        
        object settings = null;
        if (getSettingsMethod != null)
        {
            settings = getSettingsMethod.Invoke(null, new object[] { buildTargetGroup });
        }
        
        if (settings == null)
            return false;
        
        var renderModeField = openXRSettingsType.GetField("m_renderMode", 
            BindingFlags.Instance | BindingFlags.NonPublic);
        
        if (renderModeField == null)
            return false;
        
        int currentValue = (int)renderModeField.GetValue(settings);
        
        if (currentValue != RENDER_MODE_SINGLE_PASS_INSTANCED)
        {
            renderModeField.SetValue(settings, RENDER_MODE_SINGLE_PASS_INSTANCED);
            EditorUtility.SetDirty((UnityEngine.Object)settings);
            log?.AppendLine($"• OpenXR ({buildTargetGroup}): Render Mode → Single Pass Instanced (was {GetRenderModeName(currentValue)})");
            return true;
        }
        
        return false;
    }

    static bool SinglePassInstancedNeedsFix(StringBuilder log)
    {
        bool needsFix = false;
        
        needsFix |= OpenXRRenderModeNeedsFix(BuildTargetGroup.Standalone, log);
        needsFix |= OpenXRRenderModeNeedsFix(BuildTargetGroup.Android, log);
        
        return needsFix;
    }

    static bool OpenXRRenderModeNeedsFix(BuildTargetGroup buildTargetGroup, StringBuilder log)
    {
        Type openXRSettingsType = FindType("UnityEngine.XR.OpenXR.OpenXRSettings");
        if (openXRSettingsType == null)
            return false;
        
        var getSettingsMethod = openXRSettingsType.GetMethod("GetSettingsForBuildTargetGroup", 
            BindingFlags.Static | BindingFlags.Public);
        
        object settings = null;
        if (getSettingsMethod != null)
        {
            settings = getSettingsMethod.Invoke(null, new object[] { buildTargetGroup });
        }
        
        if (settings == null)
            return false;
        
        var renderModeField = openXRSettingsType.GetField("m_renderMode", 
            BindingFlags.Instance | BindingFlags.NonPublic);
        
        if (renderModeField == null)
            return false;
        
        int currentValue = (int)renderModeField.GetValue(settings);
        
        if (currentValue != RENDER_MODE_SINGLE_PASS_INSTANCED)
        {
            log?.AppendLine($"• OpenXR ({buildTargetGroup}): Render Mode is {GetRenderModeName(currentValue)} (expected Single Pass Instanced)");
            return true;
        }
        
        return false;
    }
    
    static Type FindType(string fullTypeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(fullTypeName, false, true);
            if (type != null)
                return type;
        }
        return null;
    }

    static bool FixUrpAssetsAndRenderers(StringBuilder log)
    {
        bool changed = false;
        var seenAssets = new HashSet<UniversalRenderPipelineAsset>();
        var seenRends = new HashSet<UniversalRendererData>();

        foreach (var urp in FindAllUrpAssets())
        {
            if (seenAssets.Add(urp) && FixUrpAsset(urp, seenRends, log))
                changed = true;
        }
        return changed;
    }

    static bool UrpAssetsNeedFix(StringBuilder log)
    {
        foreach (var urp in FindAllUrpAssets())
            if (UrpAssetNeedsFix(urp, log))
                return true;
        return false;
    }

    static IEnumerable<UniversalRenderPipelineAsset> FindAllUrpAssets()
    {
        foreach (string g in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            yield return AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(g));

        if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset def)
            yield return def;

        int o = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; ++i)
        {
            QualitySettings.SetQualityLevel(i, false);
            if (QualitySettings.renderPipeline is UniversalRenderPipelineAsset q)
                yield return q;
        }
        QualitySettings.SetQualityLevel(o, false);
    }

    static bool FixUrpAsset(UniversalRenderPipelineAsset urp, HashSet<UniversalRendererData> seenRends,
                            StringBuilder log)
    {
        bool dirty = false;

        if (urp.supportsHDR)
        {
            urp.supportsHDR = false;
            dirty = true;
        }
        if (urp.msaaSampleCount != 1)
        {
            urp.msaaSampleCount = 1;
            dirty = true;
        }

        foreach (var rd in GetRenderers(urp))
            if (rd is UniversalRendererData urd && seenRends.Add(urd))
                if (FixRenderer(urd, log))
                    dirty = true;

        if (dirty)
        {
            EditorUtility.SetDirty(urp);
            log.AppendLine($"• URP Asset '{urp.name}': HDR OFF, MSAA OFF");
        }
        return dirty;
    }

    static bool UrpAssetNeedsFix(UniversalRenderPipelineAsset urp, StringBuilder log)
    {
        bool bad = false;
        if (urp.supportsHDR || urp.msaaSampleCount != 1)
        {
            log?.AppendLine($"• URP Asset '{urp.name}' HDR/MSAA ON");
            bad = true;
        }

        foreach (var rd in GetRenderers(urp))
            if (rd is UniversalRendererData urd)
            {
                if (urd.intermediateTextureMode != IntermediateTextureMode.Always ||
                    !HasRenderFeature<GraciaSplatsRenderFeature>(urd) || !IsNativeRenderPassOn(urd))
                {
                    log?.AppendLine($"• Renderer '{urd.name}' needs fixes");
                    bad = true;
                }
            }
        return bad;
    }

    static ScriptableRendererData[] GetRenderers(UniversalRenderPipelineAsset urp)
    {
        var f = typeof(UniversalRenderPipelineAsset)
                    .GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);
        return f?.GetValue(urp) as ScriptableRendererData[] ?? Array.Empty<ScriptableRendererData>();
    }

    static bool FixRenderer(UniversalRendererData urd, StringBuilder log)
    {
        bool dirty = false;

        if (urd.intermediateTextureMode != IntermediateTextureMode.Always)
        {
            urd.intermediateTextureMode = IntermediateTextureMode.Always;
            dirty = true;
            log.AppendLine($"• Renderer '{urd.name}': IntermediateTexture → Always");
        }

        if (EnsureRenderFeature<GraciaSplatsRenderFeature>(urd, log))
            dirty = true;

        if (!IsNativeRenderPassOn(urd))
        {
            SetNativeRenderPass(urd, true);
            dirty = true;
            log.AppendLine($"• Renderer '{urd.name}': Use Native RenderPass → ON");
        }

        if (dirty)
            EditorUtility.SetDirty(urd);
        return dirty;
    }

    static bool HasRenderFeature<T>(UniversalRendererData urd)
        where T : ScriptableRendererFeature
    {
        foreach (var f in urd.rendererFeatures)
            if (f && f.GetType() == typeof(T))
                return true;
        return false;
    }

    static bool EnsureRenderFeature<T>(UniversalRendererData urd, StringBuilder log)
        where T : ScriptableRendererFeature
    {
        if (HasRenderFeature<T>(urd))
            return false; // already there

        var feature = ScriptableObject.CreateInstance<T>();
        feature.name = typeof(T).Name;
        AssetDatabase.AddObjectToAsset(feature, urd);
        urd.rendererFeatures.Add(feature);

        var so = new SerializedObject(urd);
        var lst = so.FindProperty("m_RendererFeatures");
        var map = so.FindProperty("m_RendererFeatureMap");
        so.Update();
        lst.arraySize = urd.rendererFeatures.Count;
        map.arraySize = urd.rendererFeatures.Count;
        so.ApplyModifiedProperties();

        log.AppendLine($"• Renderer '{urd.name}': added {typeof(T).Name}");
        return true;
    }

    static bool IsNativeRenderPassOn(UniversalRendererData urd)
    {
        var p = typeof(UniversalRendererData)
                    .GetProperty("useNativeRenderPass",
                                 BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null && p.PropertyType == typeof(bool))
            return (bool)p.GetValue(urd);

        var f = typeof(UniversalRendererData)
                    .GetField("m_UseNativeRenderPass", BindingFlags.Instance | BindingFlags.NonPublic);
        return f != null && (bool)f.GetValue(urd);
    }

    static void SetNativeRenderPass(UniversalRendererData urd, bool on)
    {
        var p = typeof(UniversalRendererData)
                    .GetProperty("useNativeRenderPass",
                                 BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null && p.PropertyType == typeof(bool))
        {
            p.SetValue(urd, on);
            return;
        }

        var f = typeof(UniversalRendererData)
                    .GetField("m_UseNativeRenderPass", BindingFlags.Instance | BindingFlags.NonPublic);
        if (f != null && f.FieldType == typeof(bool))
            f.SetValue(urd, on);
    }

    static bool IsTypeExists(string typeFullName, out Type otype)
    {
        string targetType = typeFullName;
        otype = null;

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            // The GetType method is case-sensitive. The false parameter here means it will not throw an exception if the type is not found.
            Type type = assembly.GetType(targetType, false, true);

            if (type != null)
            {
                otype = type;
                return true;
            }
        }
        return false;
    }

    static bool CheckIfMotionVectorPassExistsAndPatched()
    {
#if !UNITY_6000_0_OR_NEWER
        Type appswMotionVectorPassType = null;

        //check if OculusMotionVectorPass exists
        bool isPassExists = IsTypeExists("UnityEngine.Rendering.Universal.Internal.OculusMotionVectorPass", out appswMotionVectorPassType);
        if(!isPassExists)
        {
            Debug.LogWarning("[GraciaSplats] OVRManager detected, but OculusMotionVectorPass not found. Make sure URP version is compatible with GraciaSplats.");
            return false;
        }

       if(appswMotionVectorPassType == null)
        {
            Debug.LogWarning("[GraciaSplats] OVRManager detected, but OculusMotionVectorPass type could not be loaded. WTF?");
            return false;
        }
        var renderFuncField = appswMotionVectorPassType.GetField("graciaPluginRenderFunc", BindingFlags.Public | BindingFlags.Static);
        var eventIDField = appswMotionVectorPassType.GetField("graciaPluginEventID", BindingFlags.Public | BindingFlags.Static);

        if (renderFuncField == null || eventIDField == null)
        {
            Debug.LogWarning("[GraciaSplats] OVRManager detected, but OculusMotionVectorPass is not patched for GraciaSplats. Please re-apply GraciaSplats URP patch.");
            return false;
        }

        return true;
#else
        //6.0 + has built-in support for APPSW Motion Vectors, so we can assume it's fine
        return true;
#endif

    }

    static bool SetupAppswDefineForPlatform(System.Text.StringBuilder log, string defineName, string className, string changeStringPattern)
    {
        Type vrManagerType = null;

        bool enableOvrAppsw = IsTypeExists(className,out vrManagerType);

        if (enableOvrAppsw)
        {
            enableOvrAppsw = CheckIfMotionVectorPassExistsAndPatched();
        }

        if (enableOvrAppsw)
        {
            if (!BuildDefines.HasDefine(defineName, NamedBuildTarget.Android))
            {
                BuildDefines.AddDefine(defineName, NamedBuildTarget.Android);
                log?.AppendFormat(changeStringPattern,"enable");
                return true;
            }
        }
        else
        {
            if (BuildDefines.HasDefine(defineName, NamedBuildTarget.Android))
            {
                BuildDefines.RemoveDefine(defineName, NamedBuildTarget.Android);
                log?.AppendFormat(changeStringPattern, "disable");
                return true;
            }
        }

        return false;
    }

    static bool SetupOvrAppswDefine(System.Text.StringBuilder log)
    {
        return SetupAppswDefineForPlatform(log, "OVR_APPSW", "OVRManager", "• Meta XR: {0} OVR for Space Warp (Android)");
    }

    static bool SetupPxrAppswDefine(System.Text.StringBuilder log)
    {        
        return SetupAppswDefineForPlatform(log, "PXR_APPSW", "Unity.XR.PXR.PXR_Manager", "• PICO XR: {0} PXR for Space Warp (Android)");
    }
}
#endif
