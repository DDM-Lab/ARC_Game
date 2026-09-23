using System.Collections;
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
/// logging flow in DailyReportUI/DailyReportManager. Gated by showQualtricsInfoPanel
/// (default OFF) — see that field's tooltip for why.
///
/// Also redirects the browser to the Qualtrics follow-up survey (carrying the
/// same participant ID captured at session start — see PlayerSession) once the
/// game is considered finished, regardless of the panel setting above. The redirect
/// waits for the end-of-game log upload (LogSender) to finish or time out first —
/// navigating away cancels an in-flight upload, which used to drop participants' data.
/// </summary>
public class EndOfGamePanel : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Whether to show the manual completion-code panel/reminder at all. Default OFF: now " +
             "that the game auto-redirects to the follow-up survey (with the participant ID already " +
             "attached), this manual code panel is no longer needed for that flow. The redirect below " +
             "still fires regardless of this setting — this only controls the panel/reminder UI. Turn " +
             "on to bring it back (e.g. as a visual fallback, or for a deployment without redirect).")]
    public bool showQualtricsInfoPanel = false;
    [Tooltip("The panel GameObject to show/hide. Should NOT be a full-screen raycast blocker — the player must be able to keep reading the Daily Report underneath.")]
    public GameObject panel;
    public Button closeButton;
    [Tooltip("Only revealed once the panel above is closed, so the code stays visible while reviewing the rest of the Day 8 report. Text is predefined in the scene — this script only shows/hides it.")]
    public TextMeshProUGUI reminderText;

    [Header("Qualtrics Follow-up Redirect")]
    [Tooltip("Follow-up Qualtrics survey URL. {uid} is replaced with the participant's ID (PlayerSession.PlayerName) before redirecting. Leave empty to disable the redirect. If StreamingAssets/config.json sets a valid followUpSurveyUrl (http(s), containing {uid}), that link is used INSTEAD of this one — this is only the fallback.")]
    public string followUpSurveyUrlTemplate = "https://xxxx.qualtrics.com/jfe/form/xxxx/?uid={uid}";

    [Header("Log Upload Before Redirect")]
    [Tooltip("Max seconds to wait for the end-of-game log upload to finish before redirecting anyway, so a slow or failing upload can never strand the participant. Should be at least LogSender's request timeout (30s) to let a slow upload report failure.")]
    public float maxUploadWaitSeconds = 30f;
    [Tooltip("How many times to re-send the log if an attempt fails inside the wait window.")]
    public int uploadRetries = 1;
    [Tooltip("Optional. A scene object (e.g. a 'Saving your data...' text) shown while the redirect is waiting on the log upload, and hidden again once the wait ends. Hidden at Start. If left empty, a toast is shown instead.")]
    public GameObject savingDataIndicator;

    public static EndOfGamePanel Instance { get; private set; }

    private bool isRedirecting;

    // followUpSurveyUrl from config.json once it has loaded and passed validation; empty until then
    // (and forever if config.json is unreachable, has no such key, or the value is unusable), in
    // which case followUpSurveyUrlTemplate is used exactly as before.
    private string configFollowUpSurveyUrl;

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

        if (savingDataIndicator != null)
            savingDataIndicator.SetActive(false);

        StartCoroutine(LoadFollowUpSurveyUrlFromConfig());
    }

    /// <summary>
    /// Reads followUpSurveyUrl from StreamingAssets/config.json so the survey link can be changed on
    /// the server without a rebuild. Strictly additive: any problem (config unreachable, key absent
    /// or blank, value unusable) leaves the scene's followUpSurveyUrlTemplate in charge, so this can
    /// never make the redirect worse than before. Done at startup, long before the Day 8 redirect.
    /// </summary>
    IEnumerator LoadFollowUpSurveyUrlFromConfig()
    {
        string path = Application.streamingAssetsPath + "/config.json";
        // The point of this is changing the link without rebuilding — a browser-cached copy of
        // config.json would silently defeat that, so bypass the cache when fetched over http(s).
        if (path.StartsWith("http", System.StringComparison.OrdinalIgnoreCase))
            path += "?v=" + System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using (UnityWebRequest req = UnityWebRequest.Get(path))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.Log("[EndOfGamePanel] config.json not available — using the follow-up survey link set in the scene.");
                yield break;
            }

            AppConfig config = null;
            try { config = JsonUtility.FromJson<AppConfig>(req.downloadHandler.text); }
            catch (System.ArgumentException) { /* malformed JSON — treated as "no override" below */ }

            string url = config?.followUpSurveyUrl?.Trim();
            if (string.IsNullOrEmpty(url))
                yield break; // no override configured — the scene's link is used

            if (!IsUsableSurveyUrl(url, out string problem))
            {
                // A bad value must not be allowed to break the redirect or drop participant ids.
                Debug.LogError($"[EndOfGamePanel] Ignoring followUpSurveyUrl in config.json ({problem}); using the scene's link. Value: {url}");
                GameLogPanel.Instance?.LogError($"followUpSurveyUrl in config.json ignored: {problem}");
                yield break;
            }

            configFollowUpSurveyUrl = url;
            Debug.Log($"[EndOfGamePanel] Follow-up survey link taken from config.json: {url}");
            GameLogPanel.Instance?.LogPlayerAction($"Follow-up survey link from config.json: {url}");
        }
    }

    static bool IsUsableSurveyUrl(string url, out string problem)
    {
        problem = null;
        if (!url.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase))
            problem = "must start with http:// or https://";
        else if (!url.Contains("{uid}"))
            problem = "must contain {uid}, otherwise the participant's id is not passed to the survey";
        return problem == null;
    }

    /// <summary>
    /// Reveals the panel. Safe to call more than once (e.g. if the Day 8 report
    /// is re-shown) — it just re-displays.
    /// </summary>
    public void ShowPanel()
    {
        if (showQualtricsInfoPanel && panel != null)
        {
            panel.SetActive(true);

            Debug.Log("[EndOfGamePanel] Shown");
            GameLogPanel.Instance?.LogUIInteraction($"Qualtrics code panel shown | session_id={PlayerSession.SessionId}");
        }

        // Only one wait-then-redirect at a time (this method is documented as safe to call twice).
        if (isRedirecting) return;
        isRedirecting = true;

        if (isActiveAndEnabled)
            StartCoroutine(WaitForLogUploadThenRedirect());
        else
            RedirectToFollowUpSurvey(); // can't run a coroutine — never lose the redirect itself
    }

    /// <summary>
    /// DailyReportManager calls GameLogPanel.TriggerEndGameLogSend() immediately before ShowPanel().
    /// That starts an asynchronous upload (LogSender.PostLogs), and LogSender flips CurrentStatus to
    /// Sending synchronously before its first yield — so an upload in flight is already visible here.
    /// Navigating to Qualtrics while it's still running aborts the request, so hold the redirect
    /// until it succeeds, fails out of retries, or maxUploadWaitSeconds runs out. If there is no
    /// upload in flight (collection disabled, no LogSender) there is nothing to wait for.
    /// </summary>
    IEnumerator WaitForLogUploadThenRedirect()
    {
        LogSender sender = LogSender.Instance;

        if (sender != null && sender.CurrentStatus == LogSender.SendStatus.Sending)
        {
            if (savingDataIndicator != null)
                savingDataIndicator.SetActive(true);
            else
                ToastManager.ShowToast("Saving your game data — please wait a moment...", ToastType.Info);

            float deadline = Time.unscaledTime + maxUploadWaitSeconds;
            int retriesLeft = uploadRetries;

            while (Time.unscaledTime < deadline)
            {
                if (sender.CurrentStatus == LogSender.SendStatus.Sending)
                {
                    yield return null;
                    continue;
                }

                if (sender.CurrentStatus == LogSender.SendStatus.Failed && retriesLeft > 0)
                {
                    retriesLeft--;
                    Debug.LogWarning($"[EndOfGamePanel] Log upload failed ({sender.LastStatusMessage}) — retrying ({retriesLeft} retries left after this).");
                    sender.SendAllLogs();
                    continue;
                }

                break; // Success, or failed with no retries left
            }

            if (sender.CurrentStatus != LogSender.SendStatus.Success)
                Debug.LogWarning($"[EndOfGamePanel] Redirecting without a confirmed log upload (status={sender.CurrentStatus}): {sender.LastStatusMessage}");

            // Upload is done (or given up on) — hide the wait message. Matters outside WebGL
            // (Application.OpenURL leaves this page open); in WebGL the page is about to navigate.
            if (savingDataIndicator != null)
                savingDataIndicator.SetActive(false);
        }

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
        string template = !string.IsNullOrEmpty(configFollowUpSurveyUrl) ? configFollowUpSurveyUrl : followUpSurveyUrlTemplate;
        if (string.IsNullOrEmpty(template)) return;

        string uid = UnityWebRequest.EscapeURL(PlayerSession.PlayerName);
        string url = template.Replace("{uid}", uid);

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
