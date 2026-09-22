using UnityEngine;

/// <summary>
/// In headless/batch mode, pause and silence the global AudioListener before
/// any AudioSource is given a chance to Play(). Keeps the Editor and shipped
/// player builds unaffected.
/// </summary>
public static class HeadlessAudioDisabler
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void DisableInBatchMode()
    {
        if (!Application.isBatchMode) return;
        AudioListener.pause = true;
        AudioListener.volume = 0f;
        Debug.Log("[HeadlessAudioDisabler] AudioListener paused/muted for batch mode.");
    }
}
