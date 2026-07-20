#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

[InitializeOnLoad]
public class GraciaConfigurationManager
{
    private const string PlaybackEnabledKey = "Gracia_MintPlaybackEnabled";

    static GraciaConfigurationManager()
    {
        // Ensure default value is set to true for new users
        if (!EditorPrefs.HasKey(PlaybackEnabledKey))
        {
            EditorPrefs.SetBool(PlaybackEnabledKey, true);
        }
    }

    [MenuItem("Gracia/Configuration/Select Configuration File")]
    public static void SelectConfigurationFile()
    {
        string filePath = EditorUtility.OpenFilePanel("Select Configuration File", "", "json");
        
        if (string.IsNullOrEmpty(filePath))
        {
            Debug.Log("[Gracia] Configuration file selection cancelled");
            return;
        }

        try
        {
            string jsonConfig = File.ReadAllText(filePath);
            Gracia.ApplyConfiguration(jsonConfig);
            Debug.Log($"[Gracia] Configuration applied from: {filePath}");
            EditorUtility.DisplayDialog("Success", $"Configuration applied from:\n{Path.GetFileName(filePath)}", "OK");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Gracia] Failed to read/apply configuration: {e.Message}");
            EditorUtility.DisplayDialog("Error", $"Failed to apply configuration:\n{e.Message}", "OK");
        }
    }

    [MenuItem("Gracia/Configuration/Clear Configuration")]
    public static void ClearConfiguration()
    {
        Gracia.ClearConfiguration();
        Debug.Log("[Gracia] Configuration cleared");
    }

    [MenuItem("Gracia/Mint Playback Enabled")]
    public static void ToggleMintPlayback()
    {
        bool current = EditorPrefs.GetBool(PlaybackEnabledKey, true);
        EditorPrefs.SetBool(PlaybackEnabledKey, !current);
        Debug.Log($"[Gracia] Mint playback {(!current ? "enabled" : "paused")}");
    }

    [MenuItem("Gracia/Mint Playback Enabled", true)]
    public static bool ToggleMintPlaybackValidate()
    {
        Menu.SetChecked("Gracia/Mint Playback Enabled", EditorPrefs.GetBool(PlaybackEnabledKey, true));
        return true;
    }
}

#endif

