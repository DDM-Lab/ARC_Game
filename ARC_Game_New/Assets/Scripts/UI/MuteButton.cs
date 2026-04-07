using UnityEngine;
using UnityEngine.UI;
using System;

public class MuteButton : MonoBehaviour
{
    [SerializeField] private Image  buttonImage;
    [SerializeField] private Sprite soundOnSprite;
    [SerializeField] private Sprite soundOffSprite;

    static readonly string PrefKey = "IsMuted";

    static bool _isMuted;
    static bool _initialized;
    static event Action<bool> OnMuteChanged;

    void OnEnable()
    {
        if (!_initialized)
        {
            _isMuted     = PlayerPrefs.GetInt(PrefKey, 0) == 1;
            AudioListener.volume = _isMuted ? 0f : 1f;
            _initialized = true;
        }
        OnMuteChanged += UpdateDisplay;
        UpdateDisplay(_isMuted);
    }

    void OnDisable()
    {
        OnMuteChanged -= UpdateDisplay;
    }

    public void OnClick()
    {
        _isMuted = !_isMuted;
        AudioListener.volume = _isMuted ? 0f : 1f;
        PlayerPrefs.SetInt(PrefKey, _isMuted ? 1 : 0);
        PlayerPrefs.Save();
        OnMuteChanged?.Invoke(_isMuted);
    }

    void UpdateDisplay(bool muted)
    {
        if (buttonImage == null) return;
        buttonImage.sprite = muted ? soundOffSprite : soundOnSprite;
    }
}
