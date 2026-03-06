using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Attach to a GameObject in TitleScene.
/// Provides a Play button and a hidden multi-click trigger that opens the passcode modal.
///
/// SCENE SETUP (TitleScene):
///   • GameObject "TitleManager" → TitleScreenManager
///   • "PlayButton" (Button) → calls OnPlayButtonClicked()
///   • "LogoImage" (Button/Image with Button) → calls OnHiddenTriggerClicked()
///     (the player sees this as the game logo, not a button)
///   • PasscodeModal prefab in scene → assign to passcodeModal field
/// </summary>
public class TitleScreenManager : MonoBehaviour
{
    [Header("Scene Names (must match Build Settings)")]
    public string mainGameSceneName       = "MainScene";
    public string instructorConfigSceneName = "InstructorConfigScene";

    [Header("Passcode Modal")]
    public PasscodeModal passcodeModal;

    [Header("Hidden Trigger Settings")]
    [Tooltip("How many clicks on the hidden trigger open the passcode modal")]
    public int   clicksRequired  = 5;
    [Tooltip("Seconds before the click counter resets")]
    public float clickResetDelay = 2f;

    // ─────────────────────────────────────────────────────────────────────────
    private int   _clickCount   = 0;
    private float _lastClickTime = -999f;

    // ── Public button callbacks ───────────────────────────────────────────────

    /// <summary>Wired to the Play button's OnClick.</summary>
    public void OnPlayButtonClicked()
    {
        SceneManager.LoadScene(mainGameSceneName);
    }

    /// <summary>
    /// Wired to the hidden trigger element's OnClick.
    /// Accumulate clicks; open passcode on threshold.
    /// </summary>
    public void OnHiddenTriggerClicked()
    {
        float now = Time.unscaledTime;
        if (now - _lastClickTime > clickResetDelay)
            _clickCount = 0;

        _lastClickTime = now;
        _clickCount++;

        if (_clickCount >= clicksRequired)
        {
            _clickCount = 0;
            OpenPasscodeModal();
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    void OpenPasscodeModal()
    {
        if (passcodeModal == null)
        {
            Debug.LogError("TitleScreenManager: passcodeModal not assigned!");
            return;
        }
        passcodeModal.Show(OnPasscodeAccepted);
    }

    void OnPasscodeAccepted()
    {
        SceneManager.LoadScene(instructorConfigSceneName);
    }
}
