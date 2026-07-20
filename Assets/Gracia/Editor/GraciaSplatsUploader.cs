using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;

public class GraciaSplatsUploader
{
    [MenuItem("Gracia/Upload Splats to Android Device")]
    public static async void UploadSplatsFiles()
    {
        EditorUtility.DisplayProgressBar("Upload Splats", "Initializing...", 0f);

        try
        {
            if (!await ValidateAndGetDataAsync())
                return;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static async Task<bool> ValidateAndGetDataAsync()
    {
        if (ShowError(string.IsNullOrEmpty(AdbHelper.GetAdbPath()),
                      "ADB not found. Please ensure Unity Android module is installed."))
            return false;

        var files = GetSplatsFiles();
        if (ShowError(files.Length == 0, "No splats files found to upload."))
            return false;

        var packageName = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        if (ShowError(string.IsNullOrEmpty(packageName),
                      "No Android package name configured. Please set it in Player Settings."))
            return false;

        EditorUtility.DisplayProgressBar("Upload Splats", "Scanning for devices...", 0.5f);
        var devices = await GetDevicesAsync();
        if (ShowError(devices.Count == 0,
                      "No Android devices found. Please connect a device with USB debugging enabled."))
            return false;

        EditorUtility.ClearProgressBar();
        var selectedDevice = SelectDevice(devices);
        if (selectedDevice == null)
            return false;

        UploadFilesAsync(selectedDevice.Serial, files, packageName);
        return true;
    }

    private static async Task<List<AdbHelper.AdbDevice>> GetDevicesAsync() =>
        await Task.Run(() => AdbHelper.GetDevices());

    private static bool ShowError(bool condition, string message)
    {
        if (condition)
            EditorUtility.DisplayDialog("Upload Splats", message, "OK");
        return condition;
    }

    private static string[] GetSplatsFiles() =>
        Resources.FindObjectsOfTypeAll<GraciaSplatsModel>()
            .Where(m => !EditorUtility.IsPersistent(m.transform.root.gameObject))
            .Select(m => typeof(GraciaSplatsModel)
                             .GetField("fileName", BindingFlags.NonPublic | BindingFlags.Instance)
                             ?.GetValue(m) as string)
            .Where(f => !string.IsNullOrEmpty(f) && File.Exists(f))
            .Distinct()
            .ToArray();

    private static AdbHelper.AdbDevice SelectDevice(List<AdbHelper.AdbDevice> devices) =>
        devices.Count == 1 ? devices[0] : DeviceSelectionWindow.ShowDialog(devices);

    public class DeviceSelectionWindow : EditorWindow
    {
        private List<AdbHelper.AdbDevice> devices;
        private AdbHelper.AdbDevice selectedDevice;

        public static AdbHelper.AdbDevice ShowDialog(List<AdbHelper.AdbDevice> devices)
        {
            var window = CreateInstance<DeviceSelectionWindow>();
            window.devices = devices;
            window.titleContent = new GUIContent("Select Android Device");
            window.minSize = window.maxSize = new Vector2(380, 108 + devices.Count * 35);
            window.ShowModal();
            window.Focus();
            return window.selectedDevice;
        }

        private void OnGUI()
        {
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label("Select Android Device:", EditorStyles.boldLabel);
            GUILayout.Space(8);
            GUILayout.EndHorizontal();

            GUILayout.Space(16);

            GUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.BeginVertical();

            foreach (var device in devices)
            {
                if (GUILayout.Button(device.DisplayName, GUILayout.Height(30)))
                {
                    selectedDevice = device;
                    Close();
                }
                GUILayout.Space(4);
            }

            GUILayout.Space(16);
            if (GUILayout.Button("Cancel", GUILayout.Height(30)))
            {
                selectedDevice = null;
                Close();
            }

            GUILayout.EndVertical();
            GUILayout.Space(5);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
    }

    private static async void UploadFilesAsync(string serial, string[] files, string packageName)
    {
        var targetDir = $"/sdcard/Android/data/{packageName}/files";

        EditorUtility.DisplayProgressBar("Upload Splats", "Creating directory on device...", 0f);
        var dirCreated = await Task.Run(() => AdbHelper.CreateDirectory(serial, targetDir));
        EditorUtility.ClearProgressBar();

        if (!dirCreated)
        {
            ShowUIDialog("Upload Failed", "Could not create target directory on device.");
            return;
        }

        using var cts = new CancellationTokenSource();

        try
        {
            var uploaded = await AdbHelper.UploadFilesWithProgressAsync(
                serial, files, targetDir,
                (message, progress) => EditorApplication.delayCall +=
                () =>
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Uploading Splats", message, progress))
                        cts.Cancel();
                },
                cts.Token);

            ShowUIDialog("Upload Complete", $"Successfully uploaded {uploaded} of {files.Length} files.");
        }
        catch (OperationCanceledException)
        {
            ShowUIDialog("Upload Cancelled", "Upload was cancelled by user.");
        }
        catch (Exception ex)
        {
            ShowUIDialog("Upload Error", $"Upload failed: {ex.Message}");
        }
    }

    private static void ShowUIDialog(string title, string message) => EditorApplication.delayCall += () =>
    {
        EditorUtility.ClearProgressBar();
        EditorUtility.DisplayDialog(title, message, "OK");
    };

#if UNITY_EDITOR_OSX
    [MenuItem("Gracia/Upload Splats to iOS Device")]
    public static async void UploadSplatsToIOS()
    {
        EditorUtility.DisplayProgressBar("Upload Splats", "Initializing...", 0f);
        try
        {
            var files = GetSplatsFiles();
            if (ShowError(files.Length == 0, "No splats files found to upload.")) return;

            var bundleId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS);
            if (ShowError(string.IsNullOrEmpty(bundleId), "No iOS bundle identifier configured.")) return;

            EditorUtility.DisplayProgressBar("Upload Splats", "Scanning for devices...", 0.5f);
            var devices = await Task.Run(() => iOSDeviceHelper.GetDevices());
            if (ShowError(devices.Count == 0, "No iOS devices found. Connect a device and try again.")) return;

            EditorUtility.ClearProgressBar();
            var device = devices.Count == 1 ? devices[0] : iOSDeviceHelper.SelectDevice(devices);
            if (device == null) return;

            using var cts = new CancellationTokenSource();
            int spin = 0;
            var spinner = new[] { "◐", "◓", "◑", "◒" };
            var uploaded = await iOSDeviceHelper.UploadFilesAsync(device, files, bundleId,
                msg => EditorApplication.delayCall += () =>
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Uploading Splats", $"{spinner[spin++ % 4]} {msg}", 0f)) cts.Cancel();
                }, cts.Token);

            ShowUIDialog("Upload Complete", $"Uploaded {uploaded} of {files.Length} files to {device.Name}.");
        }
        catch (OperationCanceledException) { ShowUIDialog("Upload Cancelled", "Cancelled by user."); }
        catch (Exception ex) { ShowUIDialog("Upload Error", ex.Message); }
        finally { EditorUtility.ClearProgressBar(); }
    }
#endif
}
