using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

public class SplashLogoFade : MonoBehaviour
{
    [SerializeField] private  float fadeTime = 3f;
    CanvasGroup canvasGroup;
    [SerializeField] UnityEvent OnFadeOut;
    private void Awake() => canvasGroup = GetComponent<CanvasGroup>();

    private void OnEnable()
    {
        if (GameHelper.isShowSplashScreen)
        {
            OnFadeOut?.Invoke();
            gameObject.SetActive(false);
            return;
        }

        canvasGroup.alpha = 1;
        FadeOut();
    }

    private void FadeOut()
    {
        canvasGroup.DOFade(0, fadeTime).OnComplete(() =>
        {
            OnFadeOut?.Invoke();
            GameHelper.isShowSplashScreen = true;
            gameObject.SetActive(false);
        });
    }
}