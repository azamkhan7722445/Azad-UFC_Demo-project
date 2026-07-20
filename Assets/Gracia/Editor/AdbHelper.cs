using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

public static class AdbHelper
{
    public class AdbDevice
    {
        public string Serial { get; set; }
        public string Model { get; set; }
        public string DisplayName => string.IsNullOrEmpty(Model) ? Serial : $"{Model} ({Serial})";
        public override string ToString() => DisplayName;
    }

    private static string _adbPath;

    public static string GetAdbPath() => _adbPath ??= GetUnityAdbPath();

    private static string NormalizeAdbPath(string path) => path?.Replace('\\', '/');

    private static string GetUnityAdbPath()
    {
        var unity = Path.GetDirectoryName(EditorApplication.applicationPath);
#if UNITY_EDITOR_WIN
        var path = Path.Combine(unity, "Data", "PlaybackEngines", "AndroidPlayer", "SDK", "platform-tools", "adb.exe");
#else
        var path = Path.Combine(unity, "PlaybackEngines", "AndroidPlayer", "SDK", "platform-tools", "adb");
#endif
        return File.Exists(path) ? path : null;
    }

    private static async Task<(bool success, string output)> RunAsync(string args, CancellationToken ct = default)
    {
        var adb = GetAdbPath();
        if (adb == null)
            return (false, "ADB not found");

        try
        {
            using var process =
                Process.Start(new ProcessStartInfo(adb, args) { UseShellExecute = false, RedirectStandardOutput = true,
                                                                RedirectStandardError = true, CreateNoWindow = true });

            while (!process.HasExited && !ct.IsCancellationRequested)
                await Task.Delay(100, ct);

            if (ct.IsCancellationRequested)
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                }
                return (false, "Cancelled");
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            return (process.ExitCode == 0, string.IsNullOrEmpty(error) ? output : error);
        }
        catch (OperationCanceledException)
        {
            return (false, "Cancelled");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static (bool success, string output) Run(string args)
    {
        try
        {
            return RunAsync(args).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static List<AdbDevice> GetDevices()
    {
        var (success, output) = Run("devices -l");
        if (!success)
            return new List<AdbDevice>();

        return output.Split('\n')
            .Where(line => line.Contains("device") && !line.StartsWith("List"))
            .Select(line =>
                    {
                        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 2 || parts[1] != "device")
                            return null;

                        var device = new AdbDevice { Serial = parts[0] };
                        var modelMatch = Regex.Match(line, @"model:(\S+)");
                        if (modelMatch.Success)
                            device.Model = modelMatch.Groups[1].Value;
                        return device;
                    })
            .Where(d => d != null)
            .ToList();
    }

    public static bool CreateDirectory(string serial, string path) =>
        Run($"-s {serial} shell \"mkdir -p '{NormalizeAdbPath(path)}'\"").success;

    public static async Task CleanupTempFile(string serial, string tempPath) =>
        await RunAsync($"-s {serial} shell \"rm -f '{NormalizeAdbPath(tempPath)}'\"");

    public static async Task<int> UploadFilesWithProgressAsync(string serial, string[] files, string targetDir,
                                                               System.Action<string, float> progressCallback = null,
                                                               CancellationToken ct = default)
    {
        int uploaded = 0;

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested)
                break;

            var fileName = Path.GetFileName(file);
            var remotePath = $"{targetDir}/{fileName}";
            var localSize = new FileInfo(file).Length;

            // Check if file already exists with same size
            var (sizeOk, sizeOut) = await RunAsync(
                $"-s {serial} shell \"stat -c %s '{NormalizeAdbPath(remotePath)}' 2>/dev/null || echo 0\"", ct);
            if (sizeOk && long.TryParse(sizeOut.Trim(), out var remoteSize) && remoteSize == localSize &&
                remoteSize > 0)
            {
                progressCallback?.Invoke($"Skipping {fileName} (already exists)", 1.0f);
                uploaded++;
                continue;
            }

            var success = await UploadFileWithProgressAsync(
                serial, file, remotePath,
                (bytesTransferred, totalBytes) =>
                {
                    var progress = totalBytes > 0 ? (float)bytesTransferred / totalBytes : 0f;
                    var mb = bytesTransferred / 1024.0 / 1024.0;
                    var totalMb = totalBytes / 1024.0 / 1024.0;
                    progressCallback?.Invoke($"Uploading {fileName} ({mb:F1}MB / {totalMb:F1}MB)", progress);
                },
                ct);

            if (success)
                uploaded++;
        }

        return uploaded;
    }

    private static async Task<bool> UploadFileWithProgressAsync(string serial, string localPath, string remotePath,
                                                                System.Action<long, long> progressCallback,
                                                                CancellationToken ct)
    {
        var localSize = new FileInfo(localPath).Length;
        var tempPath = NormalizeAdbPath($"{Path.GetDirectoryName(remotePath)}/tmp_{Guid.NewGuid():N}.tmp");

        // Start upload task
        var uploadTask = RunAsync($"-s {serial} push \"{localPath}\" \"{tempPath}\"", ct);

        // Start progress monitoring task
        var progressTask = Task.Run(
            async () =>
            {
                while (!uploadTask.IsCompleted && !ct.IsCancellationRequested)
                {
                    var (sizeOk, sizeOut) = await RunAsync(
                        $"-s {serial} shell \"stat -c %s '{tempPath}' 2>/dev/null || echo 0\"", CancellationToken.None);
                    if (sizeOk && long.TryParse(sizeOut.Trim(), out var currentSize))
                    {
                        progressCallback?.Invoke(currentSize, localSize);
                    }
                    await Task.Delay(1000, ct); // Check every 1s
                }
            },
            ct);

        try
        {
            var (pushOk, _) = await uploadTask;
            if (!pushOk || ct.IsCancellationRequested)
            {
                await RunAsync($"-s {serial} shell \"rm -f '{tempPath}'\"", CancellationToken.None);
                return false;
            }

            // Move temp file to final location
            var (moveOk, _) =
                await RunAsync($"-s {serial} shell \"mv '{tempPath}' '{NormalizeAdbPath(remotePath)}'\"", ct);
            if (!moveOk)
            {
                await RunAsync($"-s {serial} shell \"rm -f '{tempPath}'\"", CancellationToken.None);
                return false;
            }

            // Report completion
            progressCallback?.Invoke(localSize, localSize);
            return true;
        }
        finally
        {
            try
            {
                await progressTask;
            }
            catch
            {
            }
        }
    }
}
