using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class UltraLargeSceneLoader : MonoBehaviour
{
    private void Awake()
    {
        // This ensures the script stays alive across all scenes
        DontDestroyOnLoad(this.gameObject);
    }

    [ContextMenu("Reload")]
    public void ReloadHugeScene()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        StartCoroutine(PurgeAndLoad(sceneName));
    }

    private IEnumerator PurgeAndLoad(string sceneName)
    {
        // 1. Switch to a tiny 'Buffer' scene first
        AsyncOperation unloadOld = SceneManager.LoadSceneAsync("EmptyBufferScene");
        yield return unloadOld;

        // 2. Force clear everything from RAM
        // This is the most critical step for a 20GB file
        yield return Resources.UnloadUnusedAssets();
        System.GC.Collect();

        // 3. Optional: Wait a few frames for the OS to catch up
        yield return new WaitForSeconds(1.0f);

        // 4. Now load the big scene
        AsyncOperation loadNew = SceneManager.LoadSceneAsync(sceneName);
        loadNew.allowSceneActivation = false;

        while (loadNew.progress < 0.9f)
        {
            Debug.Log($"Loading Progress: {loadNew.progress * 100}%");
            yield return null;
        }

        // Finalize activation
        loadNew.allowSceneActivation = true;
    }
}