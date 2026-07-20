// =============================================================================
// Gracia Splats Render Feature
// =============================================================================
//
// RENDERING PATHS:
//
// Android (UNITY_ANDROID && !UNITY_EDITOR):
//   Uses split rendering for depth composition (splats can be occluded by scene geometry)
//   1. GraciaSplatsRenderPass (BeforeRenderingOpaques)
//      - Calls PrepareAndSort: compute pass to sort splats by depth
//   2. Unity renders opaque geometry (writes to depth buffer)
//   3. GraciaSplatsIntermediatePass (BeforeRenderingTransparents)  [Unity 6+]
//      GraciaSplatsLegacyIntermediatePass (BeforeRenderingTransparents)  [Unity 2023]
//      - Renders splats WITH depth testing against scene depth
//   4. GraciaSplatsMergePass (BeforeRenderingTransparents)
//      - Merges splats into final color buffer
//
// Non-Android (Editor, Windows, etc.):
//   Uses standard single-pass rendering with depth written
//   1. GraciaSplatsRenderPass (BeforeRenderingOpaques)
//      - Full splat render in one call (sort + render)
//   2. GraciaSplatsMergePass (BeforeRenderingTransparents)
//      - Merges splats into final color buffer using provided depth buffer
//
// APPLICATION SPACEWARP (ASW) - Quest/Pico VR motion vectors:
//   Enabled with USE_APPSW define (requires OVR_APPSW or PXR_APPSW)
//
//   Unity 6+ (UNITY_6000_0_OR_NEWER):
//     - GraciaSplatsApplicationSpaceWarpPass (AfterRenderingOpaques)
//     - Uses Render Graph API
//
//   Unity 2023 and earlier:
//     - Hooks into OculusMotionVectorPass via graciaPluginRenderFunc/graciaPluginEventID
//     - No dedicated pass class, integrates with URP's motion vector pass
//
// =============================================================================

#if UNITY_ANDROID && !UNITY_EDITOR && (OVR_APPSW || PXR_APPSW)
#define USE_APPSW
#endif

//#define USE_APPSW
//#define PXR_APPSW
//#define OVR_APPSW

using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

using System.Reflection;
using System.Runtime.InteropServices;


#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif
using UnityEngine.XR;



public interface SpaceWarpProvider
{
    public void SetSpaceWarp(bool value);
    public bool GetSpaceWarp();
}

#if USE_APPSW
#if OVR_APPSW
public class OVRSpaceWarpProvider : SpaceWarpProvider
{
    public void SetSpaceWarp(bool value)
    {
        OVRManager.SetSpaceWarp(value); ;
    }
    public bool GetSpaceWarp()
    {
        return OVRManager.GetSpaceWarp();
    }
}
#endif

#if PXR_APPSW
public class PXRSpaceWarpProvider : SpaceWarpProvider
{
    FieldInfo appswField;

    public PXRSpaceWarpProvider()
    {
        Unity.XR.PXR.PXR_Manager manager = Unity.XR.PXR.PXR_Manager.Instance;
        Type pxrManagerType = typeof(Unity.XR.PXR.PXR_Manager);
        appswField = pxrManagerType.GetField("appSpaceWarp", BindingFlags.NonPublic | BindingFlags.Instance);
    }

    public void SetSpaceWarp(bool value) 
    {
        Unity.XR.PXR.PXR_Manager.Instance.SetSpaceWarp(true);
    }
    public bool GetSpaceWarp()
    {
        if (appswField == null)
            return false;

        return (bool)appswField.GetValue(Unity.XR.PXR.PXR_Manager.Instance); ;
    }
}
#endif
#else
public class NoopSpaceWarpProvider : SpaceWarpProvider
{
    public void SetSpaceWarp(bool value)
    {
    }
    public bool GetSpaceWarp()
    {
        return false;
    }
}
#endif

public class GraciaSplatsRenderFeature : ScriptableRendererFeature
{
    // Scene depth texture native pointer for depth composition
    internal IntPtr m_SceneDepthNativePtr = IntPtr.Zero;

    private static SpaceWarpProvider spaceWarpProvider = null;
    static GraciaSplatsRenderFeature()
    {
#if USE_APPSW
#if OVR_APPSW
      spaceWarpProvider = new OVRSpaceWarpProvider();         
#elif PXR_APPSW
        spaceWarpProvider = new PXRSpaceWarpProvider();
#endif
#else
        spaceWarpProvider = new NoopSpaceWarpProvider();
#endif
    }

    private static bool ShouldSkipCamera(Camera camera)
    {
        return camera.cameraType == CameraType.Preview;
    }

    private static bool IsSpaceWarpEnabled()
    {
        return spaceWarpProvider.GetSpaceWarp();
    }

    private static void SetSpaceWarp(bool value)
    {
        spaceWarpProvider.SetSpaceWarp(value);
    }

    class GraciaSplatsRenderPass : ScriptableRenderPass
    {
#if UNITY_6000_0_OR_NEWER
        private class PassData
        {
            public Camera camera;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder =
                       renderGraph.AddRasterRenderPass<PassData>("Gracia Splats Render Pass", out var passData))
            {
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                passData.camera = cameraData.camera;

                if (ShouldSkipCamera(passData.camera))
                {
                    return;
                }

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                                      { ExecutePassRenderGraph(data, context.cmd); });
            }
        }

        private void ExecutePassRenderGraph(PassData data, RasterCommandBuffer cmd)
        {
            Gracia.PrepareSplatsRendering(data.camera, IsSpaceWarpEnabled());

#if UNITY_ANDROID && !UNITY_EDITOR
            // Split rendering path: only prepare and sort, render happens in intermediate pass
            int eventId = data.camera.stereoEnabled
                ? Gracia.GetSplatsPrepareAndSortStereoEventID()
                : Gracia.GetSplatsPrepareAndSortEventID();
#else
            // Normal path: full render in one call
            int eventId = data.camera.stereoEnabled
                ? Gracia.GetSplatsRenderStereoEventID()
                : Gracia.GetSplatsRenderEventID();
#endif
            cmd.IssuePluginEvent(Gracia.GetRenderEventFunc(), eventId);
        }
#else
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (ShouldSkipCamera(renderingData.cameraData.camera))
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get("Gracia Splats Render Pass");
            ExecutePassTraditional(renderingData.cameraData.camera, cmd);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void ExecutePassTraditional(Camera camera, CommandBuffer cmd)
        {
            Gracia.PrepareSplatsRendering(camera, IsSpaceWarpEnabled());

#if UNITY_ANDROID && !UNITY_EDITOR
            // Split rendering path: only prepare and sort
            int eventId = camera.stereoEnabled
                ? Gracia.GetSplatsPrepareAndSortStereoEventID()
                : Gracia.GetSplatsPrepareAndSortEventID();
#else
            // Normal path: full render
            int eventId = camera.stereoEnabled
                ? Gracia.GetSplatsRenderStereoEventID()
                : Gracia.GetSplatsRenderEventID();
#endif
            cmd.IssuePluginEvent(Gracia.GetRenderEventFunc(), eventId);
        }
#endif
    }

    class GraciaSplatsMergePass : ScriptableRenderPass
    {
        private GraciaSplatsRenderFeature m_Feature;

        public GraciaSplatsMergePass(GraciaSplatsRenderFeature feature)
        {
            m_Feature = feature;
        }

#if UNITY_6000_0_OR_NEWER
        private class PassData
        {
            public Camera camera;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder =
                       renderGraph.AddRasterRenderPass<PassData>("Gracia Splats Merge Pass", out var passData))
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                passData.camera = cameraData.camera;

                if (ShouldSkipCamera(passData.camera))
                {
                    return;
                }

                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write | AccessFlags.Read);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                                      { ExecutePassRenderGraph(context.cmd); });
            }
        }

        private void ExecutePassRenderGraph(RasterCommandBuffer cmd)
        {
            cmd.IssuePluginEvent(Gracia.GetRenderEventFunc(), Gracia.GetSplatsMergeEventID());
        }
#else
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (ShouldSkipCamera(renderingData.cameraData.camera))
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get("Gracia Splats Merge Pass");
            ExecutePassTraditional(renderingData.cameraData.camera, cmd);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void ExecutePassTraditional(Camera camera, CommandBuffer cmd)
        {
            cmd.IssuePluginEvent(Gracia.GetRenderEventFunc(), Gracia.GetSplatsMergeEventID());
        }
#endif
    }

#if UNITY_6000_0_OR_NEWER
    // Intermediate pass for depth composition - renders splats with depth testing against scene depth
    class GraciaSplatsIntermediatePass : ScriptableRenderPass
    {
        private GraciaSplatsRenderFeature m_Feature;
        
        // Single depth data struct - ring buffer not needed because:
        // 1. Unity's Render Graph manages resource synchronization
        // 2. activeDepthTexture is already managed by Unity
        // 3. The data is passed synchronously within the render graph pass
        private static DepthTextureData s_DepthData;
        private static GCHandle s_DepthDataHandle;
        private static IntPtr s_DepthDataPtr;
        private static bool s_Initialized = false;
        
        static GraciaSplatsIntermediatePass()
        {
            InitializeDepthData();
        }

        private static void InitializeDepthData()
        {
            if (s_Initialized)
                return;
                
            s_DepthData = new DepthTextureData();
            // Pin the struct so we can pass its address to native code
            s_DepthDataHandle = GCHandle.Alloc(s_DepthData, GCHandleType.Pinned);
            s_DepthDataPtr = s_DepthDataHandle.AddrOfPinnedObject();
            
            s_Initialized = true;
        }

        public GraciaSplatsIntermediatePass(GraciaSplatsRenderFeature feature)
        {
            m_Feature = feature;
            // Request depth AND color texture inputs
            // Color (cameraOpaqueTexture) only exists after opaques complete - use as sync point
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Color);
            
            // Ensure depth data is initialized (in case static constructor didn't run)
            InitializeDepthData();
        }

        private class PassData
        {
            public Camera camera;
            public TextureHandle depthTexture;
            public int depthWidth;
            public int depthHeight;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            if (ShouldSkipCamera(cameraData.camera))
                return;

            // IMPORTANT: Use activeDepthTexture - the ACTUAL depth buffer used for rendering
            // NOT cameraDepthTexture which is a color texture copy for shader sampling
            TextureHandle activeDepthTexture = resourceData.activeDepthTexture;

            using (var builder = renderGraph.AddUnsafePass<PassData>(
                "Gracia Splats Intermediate Pass", out var passData))
            {
                passData.camera = cameraData.camera;
                passData.depthTexture = activeDepthTexture;
                passData.depthWidth = cameraData.cameraTargetDescriptor.width;
                passData.depthHeight = cameraData.cameraTargetDescriptor.height;

                // Declare depth texture usage for render graph dependency tracking
                if (activeDepthTexture.IsValid())
                    builder.UseTexture(activeDepthTexture, AccessFlags.Read);
                
                // NOTE: Pass ordering is handled by ConfigureEvent with kUnityVulkanEventConfigFlag_FlushCommandBuffers
                // in the native plugin, which ensures Unity flushes all command buffers (including opaques)
                // before our intermediate render event executes.
                    
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    var cmd = context.cmd;

                    IntPtr depthNativePtr = IntPtr.Zero;
                    int depthWidth = data.depthWidth;
                    int depthHeight = data.depthHeight;

                    // Get the actual depth buffer from the render graph's activeDepthTexture
                    // TextureHandle can be implicitly converted to RTHandle
                    if (data.depthTexture.IsValid())
                    {
                        try
                        {
                            RTHandle rtHandle = data.depthTexture;  // Implicit conversion
                            if (rtHandle != null && rtHandle.rt != null)
                            {
                                // Use GetNativeDepthBufferPtr() to get the ACTUAL depth buffer
                                // NOT GetNativeTexturePtr() which returns the color buffer
                                depthNativePtr = rtHandle.rt.GetNativeDepthBufferPtr();
                                
                                // IMPORTANT: Get actual texture dimensions from RTHandle, not cameraTargetDescriptor
                                // With dynamic resolution enabled, cameraTargetDescriptor has the BASE resolution,
                                // but the actual depth buffer is at the SCALED resolution
                                int rtWidth = rtHandle.rt.width;
                                int rtHeight = rtHandle.rt.height;
                                
                                depthWidth = rtWidth;
                                depthHeight = rtHeight;
                            }
                        }
                        catch (System.Exception)
                        {
                            // Failed to get RTHandle from TextureHandle
                        }
                    }

                    if (depthNativePtr == IntPtr.Zero)
                    {
                        return;
                    }

                    // Store depth data - no ring buffer needed as Render Graph handles synchronization
                    s_DepthData.texturePtr = depthNativePtr;
                    s_DepthData.width = depthWidth;
                    s_DepthData.height = depthHeight;
                    Marshal.StructureToPtr(s_DepthData, s_DepthDataPtr, false);

                    cmd.IssuePluginEventAndData(
                        Gracia.GetRenderEventAndDataFunc(),
                        Gracia.GetSplatsRenderIntermediateEventID(),
                        s_DepthDataPtr);
                });
            }
        }
    }
#else
    // Legacy intermediate pass for Unity 2023 - uses traditional Execute() API instead of Render Graph
    // Enables depth composition on older Unity versions
    class GraciaSplatsLegacyIntermediatePass : ScriptableRenderPass
    {
        private GraciaSplatsRenderFeature m_Feature;
        
        // Single depth data struct for passing to native plugin
        private static DepthTextureData s_DepthData;
        private static GCHandle s_DepthDataHandle;
        private static IntPtr s_DepthDataPtr;
        private static bool s_Initialized = false;
        
        static GraciaSplatsLegacyIntermediatePass()
        {
            InitializeDepthData();
        }

        private static void InitializeDepthData()
        {
            if (s_Initialized)
                return;
                
            s_DepthData = new DepthTextureData();
            // Pin the struct so we can pass its address to native code
            s_DepthDataHandle = GCHandle.Alloc(s_DepthData, GCHandleType.Pinned);
            s_DepthDataPtr = s_DepthDataHandle.AddrOfPinnedObject();
            
            s_Initialized = true;
        }

        public GraciaSplatsLegacyIntermediatePass(GraciaSplatsRenderFeature feature)
        {
            m_Feature = feature;
            // Request depth texture input - this ensures depth is available when we execute
            ConfigureInput(ScriptableRenderPassInput.Depth);
            
            // Ensure depth data is initialized
            InitializeDepthData();
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (ShouldSkipCamera(renderingData.cameraData.camera))
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get("Gracia Splats Legacy Intermediate Pass");
            
            IntPtr depthNativePtr = IntPtr.Zero;
            int depthWidth = renderingData.cameraData.cameraTargetDescriptor.width;
            int depthHeight = renderingData.cameraData.cameraTargetDescriptor.height;

            // Try to get the depth buffer native pointer
            // In Unity 2023, we access the depth through the renderer's depth target
            try
            {
                var renderer = renderingData.cameraData.renderer;
                
                // Access the camera depth target - this is the actual depth buffer
                // Use reflection to access cameraDepthTargetHandle which may be internal
                var cameraDepthTargetField = renderer.GetType().GetProperty("cameraDepthTargetHandle", 
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (cameraDepthTargetField != null)
                {
                    var depthTarget = cameraDepthTargetField.GetValue(renderer);
                    if (depthTarget != null)
                    {
                        // depthTarget is an RTHandle
                        var rtProperty = depthTarget.GetType().GetProperty("rt");
                        if (rtProperty != null)
                        {
                            var rt = rtProperty.GetValue(depthTarget) as RenderTexture;
                            if (rt != null)
                            {
                                depthNativePtr = rt.GetNativeDepthBufferPtr();
                                depthWidth = rt.width;
                                depthHeight = rt.height;
                            }
                        }
                    }
                }
                
                // Fallback: try to get depth from global shader texture
                if (depthNativePtr == IntPtr.Zero)
                {
                    var depthTexture = Shader.GetGlobalTexture("_CameraDepthTexture");
                    if (depthTexture != null)
                    {
                        // Note: _CameraDepthTexture is a color-encoded depth copy, not the actual depth buffer
                        // This may not work correctly for depth composition - log a warning
                        Debug.LogWarning("GraciaSplatsLegacyIntermediatePass: Using _CameraDepthTexture fallback - depth composition may not work correctly");
                        depthNativePtr = depthTexture.GetNativeTexturePtr();
                        depthWidth = depthTexture.width;
                        depthHeight = depthTexture.height;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"GraciaSplatsLegacyIntermediatePass: Failed to get depth texture: {e.Message}");
            }

            if (depthNativePtr != IntPtr.Zero)
            {
                // Store depth data
                s_DepthData.texturePtr = depthNativePtr;
                s_DepthData.width = depthWidth;
                s_DepthData.height = depthHeight;
                Marshal.StructureToPtr(s_DepthData, s_DepthDataPtr, false);

                cmd.IssuePluginEventAndData(
                    Gracia.GetRenderEventAndDataFunc(),
                    Gracia.GetSplatsRenderIntermediateEventID(),
                    s_DepthDataPtr);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
#endif

#if USE_APPSW && UNITY_6000_0_OR_NEWER
    class GraciaSplatsApplicationSpaceWarpPass : ScriptableRenderPass
    {
        private GraciaSplatsRenderFeature m_Feature;
        private RTHandle m_XRMotionVectorColor;
        private TextureHandle xrMotionVectorColor;
        private RTHandle m_XRMotionVectorDepth;
        private TextureHandle xrMotionVectorDepth;

        public GraciaSplatsApplicationSpaceWarpPass(GraciaSplatsRenderFeature feature)
        {
            m_Feature = feature;
            base.profilingSampler = new ProfilingSampler(nameof(GraciaSplatsApplicationSpaceWarpPass));
            xrMotionVectorColor = TextureHandle.nullHandle;
            m_XRMotionVectorColor = null;
            xrMotionVectorDepth = TextureHandle.nullHandle;
            m_XRMotionVectorDepth = null;
        }

        private class PassData
        {
        }

        private void ImportXRMotionColorAndDepth(RenderGraph renderGraph, UniversalCameraData cameraData)
        {
            var rtMotionId = cameraData.xr.motionVectorRenderTarget;
            if (m_XRMotionVectorColor == null)
            {
                m_XRMotionVectorColor = RTHandles.Alloc(rtMotionId);
            }
            else if (m_XRMotionVectorColor.nameID != rtMotionId)
            {
                RTHandleStaticHelpers.SetRTHandleUserManagedWrapper(ref m_XRMotionVectorColor, rtMotionId);
            }

            // ID is the same since a RenderTexture encapsulates all the attachments, including both color+depth.
            var depthId = cameraData.xr.motionVectorRenderTarget;
            if (m_XRMotionVectorDepth == null)
            {
                m_XRMotionVectorDepth = RTHandles.Alloc(depthId);
            }
            else if (m_XRMotionVectorDepth.nameID != depthId)
            {
                RTHandleStaticHelpers.SetRTHandleUserManagedWrapper(ref m_XRMotionVectorDepth, depthId);
            }

            // Import motion color and depth into the render graph.
            RenderTargetInfo importInfo = new RenderTargetInfo();
            importInfo.width = cameraData.xr.motionVectorRenderTargetDesc.width;
            importInfo.height = cameraData.xr.motionVectorRenderTargetDesc.height;
            importInfo.volumeDepth = cameraData.xr.motionVectorRenderTargetDesc.volumeDepth;
            importInfo.msaaSamples = cameraData.xr.motionVectorRenderTargetDesc.msaaSamples;
            importInfo.format = cameraData.xr.motionVectorRenderTargetDesc.graphicsFormat;

            RenderTargetInfo importInfoDepth = new RenderTargetInfo();
            importInfoDepth = importInfo;
            importInfoDepth.format = cameraData.xr.motionVectorRenderTargetDesc.depthStencilFormat;

            ImportResourceParams importMotionColorParams = new ImportResourceParams();
            importMotionColorParams.clearOnFirstUse = false;
            importMotionColorParams.clearColor = Color.black;
            importMotionColorParams.discardOnLastUse = false;

            ImportResourceParams importMotionDepthParams = new ImportResourceParams();
            importMotionDepthParams.clearOnFirstUse = false;
            importMotionDepthParams.clearColor = Color.black;
            importMotionDepthParams.discardOnLastUse = false;

            xrMotionVectorColor = renderGraph.ImportTexture(m_XRMotionVectorColor, importInfo, importMotionColorParams);
            xrMotionVectorDepth =
                renderGraph.ImportTexture(m_XRMotionVectorDepth, importInfoDepth, importMotionDepthParams);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            if (!cameraData.xr.enabled || !cameraData.xr.singlePassEnabled || !cameraData.xr.hasMotionVectorPass)
            {
                return;
            }

            ImportXRMotionColorAndDepth(renderGraph, cameraData);


            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Gracia Splats Application Space Warp Pass",
                                                                           out var passData, base.profilingSampler))
            {
                builder.SetRenderAttachment(xrMotionVectorColor, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(xrMotionVectorDepth, AccessFlags.Write);

                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                                      {
                                          context.cmd.IssuePluginEvent(Gracia.GetRenderEventFunc(),
                                                                       Gracia.GetSplatsRenderAppswEventID());
                                      });
            }
        }

        public void Dispose()
        {
            m_XRMotionVectorColor?.Release();
            m_XRMotionVectorDepth?.Release();
        }
    }

    private GraciaSplatsApplicationSpaceWarpPass m_GraciaSplatsApplicationSpaceWarpPass;
#endif

#if UNITY_6000_0_OR_NEWER
    private GraciaSplatsIntermediatePass m_GraciaSplatsIntermediatePass;
#else
    private GraciaSplatsLegacyIntermediatePass m_GraciaSplatsLegacyIntermediatePass;
#endif

    private GraciaSplatsRenderPass m_GraciaSplatsRenderPass;
    private GraciaSplatsMergePass m_GraciaSplatsMergePass;

    /// <summary>
    /// Gets the depth format from the active URP renderer data.
    /// Falls back to system default if URP asset is not available.
    /// </summary>
    private GraphicsFormat GetUrpDepthFormat()
    {
        var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urpAsset == null)
        {
            Debug.Log("[Gracia] No URP asset found, using system default depth format");
            return SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil);
        }

        // Get the renderer data list via reflection (m_RendererDataList is internal)
        var rendererDataListField = typeof(UniversalRenderPipelineAsset)
            .GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);
        
        if (rendererDataListField == null)
        {
            Debug.Log("[Gracia] Could not access renderer data list, using system default depth format");
            return SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil);
        }

        var rendererDataList = rendererDataListField.GetValue(urpAsset) as ScriptableRendererData[];
        if (rendererDataList == null || rendererDataList.Length == 0)
        {
            Debug.Log("[Gracia] Renderer data list is empty, using system default depth format");
            return SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil);
        }

        // Get the first (default) renderer
        if (rendererDataList[0] is UniversalRendererData urd)
        {
            var depthFormat = DepthFormatToGraphicsFormat(urd.depthAttachmentFormat);
            Debug.Log($"[Gracia] Got depth format from URP: {urd.depthAttachmentFormat} -> {depthFormat}");
            return depthFormat;
        }

        Debug.Log("[Gracia] Default renderer is not UniversalRendererData, using system default depth format");
        return SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil);
    }

    /// <summary>
    /// Converts URP's DepthFormat enum to Unity's GraphicsFormat.
    /// </summary>
    private static GraphicsFormat DepthFormatToGraphicsFormat(DepthFormat depthFormat)
    {
        return depthFormat switch
        {
            DepthFormat.Depth_16 => GraphicsFormat.D16_UNorm,
            DepthFormat.Depth_16_Stencil_8 => GraphicsFormat.D16_UNorm_S8_UInt,
            DepthFormat.Depth_24 => GraphicsFormat.D24_UNorm,
            DepthFormat.Depth_24_Stencil_8 => GraphicsFormat.D24_UNorm_S8_UInt,
            DepthFormat.Depth_32 => GraphicsFormat.D32_SFloat,
            DepthFormat.Depth_32_Stencil_8 => GraphicsFormat.D32_SFloat_S8_UInt,
            _ => SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil) // Default uses platform preference
        };
    }

    public override void Create()
    {
        // Phase 2 init: create the native renderer with the correct depth format
        // Get depth format from URP renderer data (requires URP 15+)
        var depthFormat = GetUrpDepthFormat();
        int vkFormat = Gracia.DepthGraphicsFormatToVkFormat(depthFormat);
        Debug.Log($"[Gracia] Create: depthFormat={depthFormat}, vkFormat={vkFormat}");
        Gracia.SetDepthFormat(vkFormat);

        m_GraciaSplatsRenderPass = new GraciaSplatsRenderPass();
        m_GraciaSplatsMergePass = new GraciaSplatsMergePass(this);

#if UNITY_6000_0_OR_NEWER
        m_GraciaSplatsIntermediatePass = new GraciaSplatsIntermediatePass(this);
#else
        m_GraciaSplatsLegacyIntermediatePass = new GraciaSplatsLegacyIntermediatePass(this);
#endif

#if USE_APPSW
#if UNITY_6000_0_OR_NEWER
        m_GraciaSplatsApplicationSpaceWarpPass = new GraciaSplatsApplicationSpaceWarpPass(this);
#else
       UnityEngine.Rendering.Universal.Internal.OculusMotionVectorPass.graciaPluginRenderFunc = Gracia.GetRenderEventFunc();
       UnityEngine.Rendering.Universal.Internal.OculusMotionVectorPass.graciaPluginEventID = Gracia.GetSplatsRenderAppswEventID();
#endif
        SetSpaceWarp(true);
#endif
    }

    protected override void Dispose(bool disposing)
    {
        m_GraciaSplatsRenderPass = null;
        m_GraciaSplatsMergePass = null;

#if UNITY_6000_0_OR_NEWER
        m_GraciaSplatsIntermediatePass = null;
#else
        m_GraciaSplatsLegacyIntermediatePass = null;
#endif

#if USE_APPSW && UNITY_6000_0_OR_NEWER
        if (m_GraciaSplatsApplicationSpaceWarpPass != null)
        {
            m_GraciaSplatsApplicationSpaceWarpPass.Dispose();
        }
        m_GraciaSplatsApplicationSpaceWarpPass = null;
#endif
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (m_GraciaSplatsRenderPass == null || m_GraciaSplatsMergePass == null)
        {
            return;
        }

        m_GraciaSplatsRenderPass.renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
        renderer.EnqueuePass(m_GraciaSplatsRenderPass);

#if UNITY_ANDROID && !UNITY_EDITOR
        // Android: use split rendering path with depth composition
#if USE_APPSW && UNITY_6000_0_OR_NEWER
        // Run APPSW pass for motion vectors (Unity 6 only - uses Render Graph)
        if (IsSpaceWarpEnabled())
        {
            m_GraciaSplatsApplicationSpaceWarpPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            renderer.EnqueuePass(m_GraciaSplatsApplicationSpaceWarpPass);
        }
#endif

        // Intermediate pass renders splats with depth testing against scene geometry
#if UNITY_6000_0_OR_NEWER
        if (m_GraciaSplatsIntermediatePass != null)
        {
            m_GraciaSplatsIntermediatePass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            renderer.EnqueuePass(m_GraciaSplatsIntermediatePass);
        }
#else
        // Unity 2023: use legacy intermediate pass with traditional Execute() API
        if (m_GraciaSplatsLegacyIntermediatePass != null)
        {
            m_GraciaSplatsLegacyIntermediatePass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            renderer.EnqueuePass(m_GraciaSplatsLegacyIntermediatePass);
        }
#endif
#endif

        // Merge pass also at BeforeRenderingTransparents - will run after intermediate due to enqueue order
        m_GraciaSplatsMergePass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        renderer.EnqueuePass(m_GraciaSplatsMergePass);

#if USE_APPSW && !UNITY_6000_0_OR_NEWER
#if PXR_APPSW
        Unity.XR.PXR.PXR_Manager.Instance.SetSpaceWarp(true);
#endif
#endif
    }
}
