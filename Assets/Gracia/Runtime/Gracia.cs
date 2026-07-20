using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.XR;

// Struct for passing depth texture data to native plugin via IssuePluginEventAndData
// Must match the native DepthTextureData struct layout exactly
[StructLayout(LayoutKind.Sequential)]
public struct DepthTextureData
{
    public IntPtr texturePtr;    // Native texture pointer
    public int width;
    public int height;
}

public class Gracia
{
#if UNITY_IOS && !UNITY_EDITOR
    private const string PluginName = "__Internal";
#else
    private const string PluginName = "GfxPluginGraciaSplats";
#endif

    [DllImport(PluginName)]
    public static extern IntPtr GetRenderEventFunc();

    [DllImport(PluginName)]
    public static extern IntPtr GetRenderEventAndDataFunc();

    [DllImport(PluginName)]
    public static extern int GetSplatsRenderEventID();

    [DllImport(PluginName)]
    public static extern int GetSplatsRenderStereoEventID();

    [DllImport(PluginName)]
    public static extern int GetSplatsMergeEventID();

    [DllImport(PluginName)]
    public static extern int GetSplatsRenderAppswEventID();

    [DllImport(PluginName)]
    public static extern int GetSplatsPrepareAndSortEventID();

    [DllImport(PluginName)]
    public static extern int GetSplatsPrepareAndSortStereoEventID();

    [DllImport(PluginName)]
    public static extern int GetSplatsRenderIntermediateEventID();

    [DllImport(PluginName)]
    public static extern void SetProjectionAndViewMatrix(int eyeIndex, Matrix4x4 projectionMatrix,
                                                         Matrix4x4 viewMatrix);

    [DllImport(PluginName)]
    public static extern void SetModelMatrix(int handle, Matrix4x4 matrix);

    [DllImport(PluginName)]
    public static extern void SetSplatsEnabled(int handle, bool enabled);

    [DllImport(PluginName)]
    public static extern int AcquireSplatsHandle(int handle, string filePathUtf8);

    [DllImport(PluginName)]
    public static extern void ReleaseSplatsHandle(int handle);

    [DllImport(PluginName)]
    public static extern void SetMotionVectorsOnAndroid(bool value);

    [DllImport(PluginName)]
    public static extern void SetMsaa(int msaa);

    [DllImport(PluginName)]
    public static extern void SetDynamicResolution(int width, int height);

    [DllImport(PluginName)]
    public static extern void SetSplatsDeltaTime(int handle, float value);

    [DllImport(PluginName)]
    public static extern void GetLocalBounds(int handle, out float minX, out float minY, out float minZ, out float maxX,
                                             out float maxY, out float maxZ);

    [DllImport(PluginName)]
    public static extern void RewindVideo(int handle, float time);

    [DllImport(PluginName)]
    public static extern double GetVideoTime(int handle);

    [DllImport(PluginName)]
    public static extern void ApplyConfiguration(string jsonConfig);

    [DllImport(PluginName)]
    public static extern void ClearConfiguration();

    [DllImport(PluginName)]
    public static extern void SetDepthFormat(int vkFormat);

    /// <summary>
    /// Converts a Unity GraphicsFormat depth format to the corresponding VkFormat integer value.
    /// </summary>
    public static int DepthGraphicsFormatToVkFormat(GraphicsFormat format)
    {
        return format switch
        {
            GraphicsFormat.D16_UNorm => 124,           // VK_FORMAT_D16_UNORM
            GraphicsFormat.D16_UNorm_S8_UInt => 128,   // VK_FORMAT_D16_UNORM_S8_UINT
            GraphicsFormat.D24_UNorm => 125,           // VK_FORMAT_X8_D24_UNORM_PACK32
            GraphicsFormat.D24_UNorm_S8_UInt => 129,   // VK_FORMAT_D24_UNORM_S8_UINT
            GraphicsFormat.D32_SFloat => 126,          // VK_FORMAT_D32_SFLOAT
            GraphicsFormat.D32_SFloat_S8_UInt => 130,  // VK_FORMAT_D32_SFLOAT_S8_UINT
            _ => 129                                   // default: D24_S8 (Quest)
        };
    }

    public static void PrepareSplatsRendering(Camera camera, bool motionVectorsOnAndroid)
    {
        SetMotionVectorsOnAndroid(motionVectorsOnAndroid);

        SetMsaa(QualitySettings.antiAliasing);

        if (camera.stereoEnabled)
        {
            Matrix4x4[] viewMatrices = new Matrix4x4[2];
            Matrix4x4[] projMatrices = new Matrix4x4[2];

            projMatrices[0] =
                GL.GetGPUProjectionMatrix(camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left), false);
            projMatrices[1] =
                GL.GetGPUProjectionMatrix(camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right), false);

            viewMatrices[0] = Matrix4x4.Inverse(camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left));
            viewMatrices[1] = Matrix4x4.Inverse(camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right));

            for (int i = 0; i < 2; ++i)
            {
                SetProjectionAndViewMatrix(i, projMatrices[i], viewMatrices[i]);
            }

            SetDynamicResolution(XRSettings.eyeTextureWidth, XRSettings.eyeTextureHeight);
        }
        else
        {
            Matrix4x4 projMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            SetProjectionAndViewMatrix(0, projMatrix, camera.worldToCameraMatrix);
#else
            SetProjectionAndViewMatrix(0, projMatrix, camera.cameraToWorldMatrix);
#endif

            SetDynamicResolution(camera.pixelWidth, camera.pixelHeight);
        }
    }
}
