using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Shown once, alongside the Day 8 Daily Report, to give the player their
/// predefined completion code for the Qualtrics post-game survey (all wording,
/// including the code itself, is authored directly in the scene — this script
/// only controls visibility/timing). Purely a player-facing prompt on top of the
/// report — does not touch report display, animation, or the data-collection/
/// logging flow in DailyReportUI/DailyReportManager.
///
/// Also redirects the browser to the Qualtrics follow-up survey (carrying the
/// same participant ID captured at session start — see PlayerSession) the
/// instant the panel is shown, i.e. the moment the game is considered finished.
/// </summary>
public class EndOfGamePanel : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("The panel GameObject to show/hide. Should NOT be a full-screen raycast blocker — the player must be able to keep reading the Daily Report underneath.")]
    public GameObject panel;
    public Button closeButton;
    [Tooltip("Only revealed once the panel above is closed, so the code stays visible while reviewing the rest of the Day 8 report. Text is predefined in the scene — this script only shows/hides it.")]
    public TextMeshProUGUI reminderText;

    [Header("Qualtrics Follow-up Redirect")]
    [Tooltip("Follow-up Qualtrics survey URL. {uid} is replaced with the participant's ID (PlayerSession.PlayerName) before redirecting. Leave empty to disable the redirect.")]
    public string followUpSurveyUrlTemplate = "https://xxxx.qualtrics.com/jfe/form/xxxx/?uid={uid}";

    public static EndOfGamePanel Instance { get; private set; }

    // ── jslib import — see Assets/Plugins/WebGL/BrowserRedirect.jslib ──────────
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    static extern void RedirectToUrl(string url);
#endif

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(ClosePanel);

        if (panel != null)
            panel.SetActive(false);

        if (reminderText != null)
            reminderText.gameObject.SetActive(false);
    }

    /// <summary>
    /// Reveals the panel. Safe to call more than once (e.g. if the Day 8 report
    /// is re-shown) — it just re-displays.
    /// </summary>
    public void ShowPanel()
    {
        if (panel == null) return;

        panel.SetActive(true);

        Debug.Log("[EndOfGamePanel] Shown");
        GameLogPanel.Instance?.LogUIInteraction($"Qualtrics code panel shown | session_id={PlayerSession.SessionId}");

        RedirectToFollowUpSurvey();
    }

    /// <summary>
    /// Sends the browser straight to the follow-up Qualtrics survey, carrying the same
    /// participant ID (PlayerSession.PlayerName) that was captured at session start. Uses the
    /// BrowserRedirect.jslib plugin in WebGL builds; falls back to Application.OpenURL when
    /// running outside UNITY_WEBGL (e.g. in-editor testing), which opens the system browser
    /// instead of navigating the page.
    /// </summary>
    void RedirectToFollowUpSurvey()
    {
        if (string.IsNullOrEmpty(followUpSurveyUrlTemplate)) return;

        string uid = UnityWebRequest.EscapeURL(PlayerSession.PlayerName);
        string url = followUpSurveyUrlTemplate.Replace("{uid}", uid);

        Debug.Log($"[EndOfGamePanel] Redirecting to follow-up survey: {url}");
        GameLogPanel.Instance?.LogUIInteraction($"Redirecting to follow-up survey | uid={PlayerSession.PlayerName}");

#if UNITY_WEBGL && !UNITY_EDITOR
        RedirectToUrl(url);
#else
        Application.OpenURL(url);
#endif
    }

    public void ClosePanel()
    {
        if (panel != null)
            panel.SetActive(false);

        // Reveal the reminder now that the main panel is gone, so the code stays
        // visible while the player keeps reading the Day 8 report.
        if (reminderText != null)
            reminderText.gameObject.SetActive(true);

        GameLogPanel.Instance?.LogUIInteraction("Qualtrics code panel closed");
    }
}
