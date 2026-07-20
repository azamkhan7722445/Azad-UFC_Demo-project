using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Networking;
using System.IO;

public class GraciaDemoContentDownloader
{
    [MenuItem("Gracia/Add Demo Content to Scene/Doctor")]
    public static void DownloadDoctorDemo()
    {
        DownloadDemoContent("https://gstatic.gracia.ai/demo/doctor10.mint", "doctor_demo.mint", "DOCTOR", 7.0f,
                            new Vector3(-0.35f, 1.83f, 5.11f), new Vector3(5.616f, 66.282f, 8.001f));
    }

    [MenuItem("Gracia/Add Demo Content to Scene/Fashion")]
    public static void DownloadFashionDemo()
    {
        DownloadDemoContent("https://gstatic.gracia.ai/demo/fashion10.mint", "fashion_demo.mint", "FASHION", 7.0f,
                            new Vector3(-0.08f, 1.39f, 4.54f), new Vector3(50.121f, -137.188f, -20.183f));
    }

    [MenuItem("Gracia/Add Demo Content to Scene/Music")]
    public static void DownloadMusicDemo()
    {
        DownloadDemoContent("https://gstatic.gracia.ai/demo/music10.mint", "music_demo.mint", "MUSIC", 7.0f,
                            new Vector3(-0.08f, 1.39f, 4.54f), new Vector3(6.51f, 100.46f, -3.052f));
    }

    private static void DownloadDemoContent(string url, string defaultFileName, string objectName, float scale,
                                            Vector3 position, Vector3 rotation)
    {
        string savePath =
            EditorUtility.SaveFilePanel("Save Demo Content", Application.dataPath, defaultFileName, "mint");

        if (string.IsNullOrEmpty(savePath))
        {
            UnityEngine.Debug.Log("Download cancelled by user");
            return;
        }

        GameObject splatsObject = new GameObject(objectName);

        var splatsModel = splatsObject.AddComponent<GraciaSplatsModel>();
        splatsObject.transform.localScale = new Vector3(scale, scale, scale);
        splatsObject.transform.localPosition = position;
        splatsObject.transform.localRotation = Quaternion.Euler(rotation);

        var field = typeof(GraciaSplatsModel)
                        .GetField("fileName",
                                  System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null)
        {
            field.SetValue(splatsModel, savePath);
        }

        Selection.activeGameObject = splatsObject;

        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        StartDownload(url, savePath);
    }

    private static void StartDownload(string url, string savePath)
    {
        if (File.Exists(savePath))
        {
            UnityEngine.Debug.Log($"File already exists, skipping download: {savePath}");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(savePath));

        UnityEngine.Debug.Log($"Downloading {url}...");
        new FileDownloader(url, savePath).Start();
    }

    private class FileDownloader
    {
        private readonly string url;
        private readonly string targetPath;
        private readonly string tempPath;
        private UnityWebRequest request;
        private UnityWebRequestAsyncOperation operation;

        public FileDownloader(string url, string targetPath)
        {
            this.url = url;
            this.targetPath = targetPath;
            this.tempPath = targetPath + ".tmp";
        }

        public void Start()
        {
            request = new UnityWebRequest(url);
            request.downloadHandler = new DownloadHandlerFile(tempPath);
            operation = request.SendWebRequest();

            EditorApplication.update += UpdateProgress;
        }

        private void UpdateProgress()
        {
            if (operation?.isDone != true)
            {
                EditorUtility.DisplayProgressBar("Downloading Demo Content", $"Downloading from {url}...",
                                                 operation?.progress ?? 0f);
                return;
            }

            EditorApplication.update -= UpdateProgress;
            EditorUtility.ClearProgressBar();

            bool success = request.result == UnityWebRequest.Result.Success;

            if (success && File.Exists(tempPath))
            {
                try
                {
                    File.Move(tempPath, targetPath);
                    UnityEngine.Debug.Log($"Download completed: {targetPath}");
                    AssetDatabase.Refresh();
                }
                catch (System.Exception ex)
                {
                    UnityEngine.Debug.LogError($"Failed to move downloaded file: {ex.Message}");
                    success = false;
                }
            }
            else
            {
                UnityEngine.Debug.LogError($"Download failed: {request.error}");
            }

            if (!success && File.Exists(tempPath))
                File.Delete(tempPath);

            request?.Dispose();
        }
    }
}
