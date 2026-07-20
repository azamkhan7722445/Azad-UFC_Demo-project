#if UNITY_EDITOR_OSX
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

public static class iOSDeviceHelper
{
    public class iOSDevice
    {
        public string Identifier, Name, Model;
        public string DisplayName => Name + (string.IsNullOrEmpty(Model) ? "" : $" ({Model})");
    }

    public static List<iOSDevice> GetDevices()
    {
        var devices = new List<iOSDevice>();

        var (ok, output) = RunWithOutput("xcrun", "devicectl list devices");
        if (ok)
        {
            // Table format: Name | Hostname | Identifier | State | Model
            foreach (var line in output.Split('\n'))
            {
                if (line.Contains("----") || string.IsNullOrWhiteSpace(line)) continue;
                var match = Regex.Match(line, @"^(.+?)\s{2,}\S+\.coredevice\.local\s+([A-F0-9-]{36})\s+(connected|available)\s+(.+)$", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var model = Regex.Replace(match.Groups[4].Value.Trim(), @"\s*\([^)]+\)\s*$", "");
                    devices.Add(new iOSDevice { Name = match.Groups[1].Value.Trim(), Identifier = match.Groups[2].Value, Model = model });
                }
            }
        }

        return devices;
    }

    public static iOSDevice SelectDevice(List<iOSDevice> devices)
    {
        iOSDevice selected = null;
        var win = UnityEngine.ScriptableObject.CreateInstance<EditorWindow>();
        win.titleContent = new UnityEngine.GUIContent("Select iOS Device");
        win.minSize = win.maxSize = new UnityEngine.Vector2(380, 80 + devices.Count * 32);

        Action onGUI = () =>
        {
            UnityEngine.GUILayout.Space(8);
            UnityEngine.GUILayout.Label("Select Device:", EditorStyles.boldLabel);
            foreach (var d in devices)
                if (UnityEngine.GUILayout.Button(d.DisplayName, UnityEngine.GUILayout.Height(28))) { selected = d; win.Close(); }
            if (UnityEngine.GUILayout.Button("Cancel", UnityEngine.GUILayout.Height(28))) win.Close();
        };

        typeof(EditorWindow).GetField("m_OnGUIHandler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(win, onGUI);
        win.ShowModal();
        return selected;
    }

    public static async Task<int> UploadFilesAsync(iOSDevice device, string[] files, string bundleId,
                                                    Action<string> status, CancellationToken ct)
    {
        int uploaded = 0;

        for (int i = 0; i < files.Length; i++)
        {
            if (ct.IsCancellationRequested) break;
            var file = files[i];
            var name = Path.GetFileName(file);
            var sizeMB = new FileInfo(file).Length / (1024.0 * 1024.0);
            status?.Invoke($"[{i + 1}/{files.Length}] Uploading {name} ({sizeMB:F1} MB)...");

            var args = $"devicectl device copy to -d \"{device.Identifier}\" --source \"{file}\" --destination \"Documents/{name}\" --domain-type appDataContainer --domain-identifier \"{bundleId}\"";
            var (ok, output) = await Task.Run(() => RunWithOutput("xcrun", args), ct);

            if (ok)
                uploaded++;
            else
                UnityEngine.Debug.LogError($"devicectl failed for {name}: {output}");
        }
        return uploaded;
    }

    static (bool, string) RunWithOutput(string cmd, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(cmd, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true });
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(60000);
            return (p.ExitCode == 0, p.ExitCode == 0 ? stdout : $"[exit {p.ExitCode}] {stderr}");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
#endif
