using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class ReplayAndAutoplay : MonoBehaviour
{
    [SerializeField] private Sprite playIcon;
    [SerializeField] private Sprite pauseIcon;
    [SerializeField] private Image imagePlayPause;
    [SerializeField] private TextMeshProUGUI tmpPlayPauseText;

    [SerializeField] private GraciaSplatsModel graciaSplatsModel;

    private bool value = true;
    public void OnPlayPause()
    {
       
        if (value)
        {
            graciaSplatsModel.OnControlTimeStop();
        }
        else
        {
            graciaSplatsModel.OnControlTimeStart();
        }
        value = !value;
        imagePlayPause.sprite = value ? pauseIcon : playIcon;
        tmpPlayPauseText.text = value ? "Pause" : "Play";
    }


    public void Replay()
    {
        graciaSplatsModel.OnControlTimeStop();
        graciaSplatsModel.SetTime(0.0);
        graciaSplatsModel.OnControlTimeStart();
    }


#if UNITY_EDITOR
    private bool dummy = true;

    [ContextMenu("Test Play Pause")]
    public void TestPlayPause()
    {
        if (dummy)
        {
            graciaSplatsModel.OnControlTimeStop();
        }
        else
        {
            graciaSplatsModel.OnControlTimeStart();
        }
        dummy = !dummy;
        imagePlayPause.sprite = dummy ? pauseIcon : playIcon;
        tmpPlayPauseText.text = dummy ? "Pause" : "Play";
    }

    [ContextMenu("Test Replay")]
    public void TestReload()
    {
       Replay();
    }

#endif
}