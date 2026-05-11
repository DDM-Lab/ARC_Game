using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class AccessibilityManager : MonoBehaviour
{
    public static AccessibilityManager Instance { get; private set; }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void SpeakText(string text);
    [DllImport("__Internal")] static extern void CancelSpeech();
    [DllImport("__Internal")] static extern int IsSpeaking();
#else
    static void SpeakText(string text) => Debug.Log($"[TTS] {text}");
    static void CancelSpeech()        => Debug.Log("[TTS] Cancelled");
    static int  IsSpeaking()          => 0;
#endif

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else Destroy(gameObject);
    }

    void Update()
    {
        if (!Input.GetMouseButtonDown(1)) return;

        TMP_Text tmp = GetTMPUnderCursor();

        if (tmp == null || string.IsNullOrWhiteSpace(tmp.text))
        {
            CancelSpeech();
            return;
        }

        if (IsSpeaking() == 1)
        {
            CancelSpeech();
            return;
        }

        SpeakText(tmp.text);
    }

    TMP_Text GetTMPUnderCursor()
    {
        if (EventSystem.current == null) return null;

        var pointerData = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);

        // First pass: direct TMP hit
        foreach (var result in results)
        {
            TMP_Text tmp = result.gameObject.GetComponent<TMP_Text>();
            if (tmp != null) return tmp;
        }

        // Second pass: panel with an explicit read target (e.g. alert panel during typing)
        foreach (var result in results)
        {
            AccessibilityReadTarget target = result.gameObject.GetComponent<AccessibilityReadTarget>();
            if (target != null && target.targetText != null) return target.targetText;
        }

        return null;
    }
}
