#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.IO;

public static class VulkanRestarter
{
    [MenuItem("Gracia/Restart in Vulkan Mode")]
    public static void RestartInVulkan()
    {
        string editorPath = EditorApplication.applicationPath;
        string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        
        ProcessStartInfo psi = new ProcessStartInfo();
        psi.FileName = editorPath;
        psi.Arguments = $"-projectPath \"{projectPath}\" -force-vulkan";
        
        UnityEngine.Debug.Log($"[Gracia] Restarting Unity with: {editorPath} {psi.Arguments}");
        Process.Start(psi);
        EditorApplication.Exit(0);
    }
}
#endif
