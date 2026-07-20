using DG.Tweening;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ChangeScene : MonoBehaviour
{
    [SerializeField] private string mainSceneName;
    public CanvasGroup loadingCanvasGroup;
    
    private void Awake()
    {
        loadingCanvasGroup.gameObject.SetActive(false);
        loadingCanvasGroup.alpha = 0;
    }

    public void LoadScene(string sceneName)
    {
        Debug.LogWarning("Scene Name : " + sceneName);
        StopAllCoroutines();
        StartCoroutine(CR_LoadScene(sceneName));
    }

    IEnumerator CR_LoadScene(string sceneName)
    {
        loadingCanvasGroup.gameObject.SetActive(true);
        yield return loadingCanvasGroup.DOFade(1f, 0.5f).WaitForCompletion();

        // Unload unused assets first
        yield return Resources.UnloadUnusedAssets();

        // Then force GC
        System.GC.Collect();

        // Optional: wait a frame to ensure cleanup settles
        yield return null;

        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);

        while (!asyncLoad.isDone)
            yield return null;

        yield return loadingCanvasGroup.DOFade(0f, 0.5f).WaitForCompletion();
        loadingCanvasGroup.gameObject.SetActive(false);
    }

    public void BackToMainScene()
    {
        LoadScene(mainSceneName);
    }
    public void ReloadScene()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}