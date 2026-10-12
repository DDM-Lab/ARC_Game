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
/// The hand-off to the Qualtrics follow-up survey (carrying the participant ID captured at
/// session start — see PlayerSession) happens only through the continue button ("Back to
/// Survey"). That button is hidden until the end-of-game save has an outcome. While the upload
/// runs, savingDataText shows its progress, retries and a request to keep the window open, and
/// uploadProgressSlider (if assigned) fills up. If the server confirms the save
/// (LogSender.FinalUploadConfirmed), savingDataText shows the success message in successTextColor.
/// LogSender retries a bounded number of times (its finalRetryDelaysSeconds); if it gives up,
/// savingDataText shows the failure message with the participant's ID in failureTextColor. The
/// button appears in both cases.
/// </summary>
public class EndOfGamePanel : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Whether to show the manual completion-code panel/reminder at all. Default OFF: now " +
             "that the game auto-redirects to the follow-up survey (with the participant ID already " +
             "attached), this manual code panel is no longer needed for that flow. The survey hand-off " +
             "still happens only through the continue button — this only controls the panel/reminder UI. Turn " +
             "on to bring it back (e.g. as a visual fallback, or for a deployment without redirect).")]
    public bool showQualtricsInfoPanel = false;
    [Tooltip("The panel GameObject to show/hide. Should NOT be a full-screen raycast blocker — the player must be able to keep reading the Daily Report underneath.")]
    public GameObject panel;
    public Button closeButton;
    [Tooltip("Only revealed once the panel above is closed, so the code stays visible while reviewing the rest of the Day 8 report. Text is predefined in the scene — this script only shows/hides it.")]
    public TextMeshProUGUI reminderText;

    [Header("Continue to Survey")]
    [Tooltip("Scene button that takes the participant to the Qualtrics follow-up survey (\"Back to Survey\"). Hidden at Start. It is shown once the end-of-game save has an outcome — confirmed, or given up on (alongside the failure message) — and clicking it opens the survey. Label and styling are authored in the scene.")]
    public Button continueButton;

    [Header("Qualtrics Follow-up Redirect")]
    [Tooltip("Follow-up Qualtrics survey URL. {uid} is replaced with the participant's ID (PlayerSession.PlayerName) before redirecting. Leave empty to disable the redirect. If StreamingAssets/config.json sets a valid followUpSurveyUrl (http(s), containing {uid}), that link is used INSTEAD of this one — this is only the fallback.")]
    public string followUpSurveyUrlTemplate = "https://xxxx.qualtrics.com/jfe/form/xxxx/?uid={uid}";

    [Header("Saving Before Continue")]
    // Retries are LogSender's (finalRetryDelaysSeconds): it owns the upload, and the same policy
    // applies to the day checkpoints.
    [Tooltip("Optional. A scene object (e.g. a 'Saving your data...' text) shown from the start of the end-of-game save until the server confirms it. Hidden at Start. If left empty, a toast is shown instead.")]
    public GameObject savingDataIndicator;
    [Tooltip("Optional. The text component already shown on savingDataIndicator (e.g. \"Saving your data, please wait. We will soon redirect you to the post-game survey.\"). If assigned, its wording is replaced while the upload runs (progress, retries, a request to keep the window open), then by the success message (successTextColor) or the failure message with the participant's ID (failureTextColor), which stays on screen. If left empty (but savingDataIndicator isn't), the indicator is hidden and toasts are shown instead.")]
    public TextMeshProUGUI savingDataText;
    [Tooltip("Optional. A Slider used as the end-of-game upload's progress bar (made non-interactable). Hidden at Start; shown and filled while the upload runs; left full on success; hidden if the save cannot be confirmed.")]
    public Slider uploadProgressSlider;
    [Tooltip("savingDataText's color for the success message.")]
    public Color successTextColor = new Color(0f, 100f / 255f, 0f);    // dark green, #006400
    [Tooltip("savingDataText's color for the failure message.")]
    public Color failureTextColor = new Color(139f / 255f, 0f, 0f);    // dark red, #8B0000

    const string SuccessMessage = "Data Upload Success. You can go back to the survey now.";

    public static EndOfGamePanel Instance { get; private set; }

    private bool waitingForSave;   // the wait-for-confirmation coroutine has been started
    private bool isRedirecting;    // the survey has been opened (guards double clicks)

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

        if (continueButton != null)
        {
            continueButton.onClick.AddListener(OnContinueClicked);
            continueButton.gameObject.SetActive(false);
        }

        if (panel != null)
            panel.SetActive(false);

        if (reminderText != null)
            reminderText.gameObject.SetActive(false);

        if (savingDataIndicator != null)
            savingDataIndicator.SetActive(false);

        if (uploadProgressSlider != null)
        {
            uploadProgressSlider.interactable = false;   // a display, not a control
            uploadProgressSlider.gameObject.SetActive(false);
        }

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
    /// Reveals the panel (if enabled) and starts waiting for the outcome of the end-of-game save.
    /// The continue button appears only once there is one. Safe to call more than once (e.g. if the
    /// Day 8 report is re-shown): the wait is started once.
    /// </summary>
    public void ShowPanel()
    {
        if (showQualtricsInfoPanel && panel != null)
        {
            panel.SetActive(true);

            Debug.Log("[EndOfGamePanel] Shown");
            GameLogPanel.Instance?.LogUIInteraction($"Qualtrics code panel shown | session_id={PlayerSession.SessionId}");
        }

        if (waitingForSave) return;
        waitingForSave = true;

        if (isActiveAndEnabled)
            StartCoroutine(WaitForConfirmedSaveThenShowContinue());
        else
            Debug.LogError("[EndOfGamePanel] Inactive, so it cannot wait for the save. The continue button will not be shown.");
    }

    /// <summary>
    /// DailyReportManager calls GameLogPanel.TriggerEndGameLogSend() immediately before ShowPanel().
    /// That starts the end-of-game upload (LogSender, "final" kind), which retries on its own. This
    /// shows its progress and waits for the outcome, then shows the success message (ShowSaveSucceeded)
    /// or, if LogSender gives up, the failure message (ShowSaveFailed); the continue button appears
    /// in both cases. If no end-of-game upload is running (the trigger was skipped), it starts one.
    ///
    /// If saving is not possible at all (data collection off, or no log system in the scene), the
    /// wait continues indefinitely and the error is logged once; that is a build/config problem
    /// rather than an upload failure, so there is no outcome to show.
    /// </summary>
    IEnumerator WaitForConfirmedSaveThenShowContinue()
    {
        ShowSavingIndicator(true);
        SetProgress(0f, true);

        bool startRequested = false;
        bool reportedCannotSave = false;
        while (true)
        {
            LogSender sender = LogSender.Instance;
            if (sender != null)
            {
                if (sender.FinalUploadConfirmed)
                    break;

                if (sender.FinalUploadGaveUp)
                {
                    Debug.LogError($"[EndOfGamePanel] End-of-game save not confirmed ({sender.LastFailureReason}).");
                    ShowSaveFailed(sender.LastFailureReason);
                    yield break;
                }

                if (!sender.FinalUploadInProgress && !startRequested)
                {
                    startRequested = true;
                    sender.SendAllLogs();
                    continue;
                }
            }

            // The button must not appear without a confirmed save, so if saving is not possible at
            // all the wait simply continues (and says why, once). It never falls through to the button.
            if (sender == null || !sender.FinalUploadInProgress)
            {
                if (!reportedCannotSave)
                {
                    Debug.LogError("[EndOfGamePanel] Cannot save the end-of-game log (no LogSender, no GameLogPanel, or data collection is disabled). The continue button stays hidden.");
                    reportedCannotSave = true;
                }
            }
            else
            {
                ShowUploadStatus(sender);
            }

            yield return null;
        }

        ShowSaveSucceeded();
    }

    const string KeepOpen = "Please keep this window open.";

    /// <summary>Shows the end-of-game upload's progress on the slider and in savingDataText.</summary>
    void ShowUploadStatus(LogSender sender)
    {
        // Full once the body is sent (only the server's confirmation is left); empty while preparing
        // and while waiting to retry, since a retry starts the upload over.
        SetProgress(sender.Phase == LogSender.UploadPhase.Uploading && sender.BytesTotal > 0
                ? (float)sender.BytesSent / sender.BytesTotal
                : sender.Phase == LogSender.UploadPhase.WaitingForServer ? 1f : 0f,
            true);

        if (savingDataText == null) return;

        string text;
        switch (sender.Phase)
        {
            case LogSender.UploadPhase.Uploading:
                long total = sender.BytesTotal;
                int percent = total > 0 ? Mathf.Clamp(Mathf.FloorToInt(100f * sender.BytesSent / total), 0, 100) : 0;
                text = $"Uploading your game data: {percent}% ({FormatMegabytes(sender.BytesSent)} of {FormatMegabytes(total)}).\n" +
                       $"{KeepOpen} On a slow connection this can take a few minutes.";
                break;
            case LogSender.UploadPhase.WaitingForServer:
                text = $"Upload finished. Waiting for the server to confirm your data.\n{KeepOpen}";
                break;
            case LogSender.UploadPhase.WaitingToRetry:
                text = $"The upload did not go through ({sender.LastFailureReason}).\n" +
                       $"Trying again in {Mathf.CeilToInt(sender.SecondsUntilRetry)} s (attempt {sender.Attempt + 1} of {sender.MaxAttempts}). {KeepOpen}";
                break;
            default:
                text = $"Saving your game data.\n{KeepOpen}";
                break;
        }

        if (sender.Attempt > 1 && sender.Phase != LogSender.UploadPhase.WaitingToRetry)
            text += $" (Attempt {sender.Attempt} of {sender.MaxAttempts}.)";

        if (savingDataText.text != text)
            savingDataText.text = text;
    }

    static string FormatMegabytes(long bytes) =>
        (bytes / (1024f * 1024f)).ToString(bytes < 10L * 1024 * 1024 ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture) + " MB";

    void SetProgress(float fraction, bool visible)
    {
        if (uploadProgressSlider == null) return;
        uploadProgressSlider.normalizedValue = Mathf.Clamp01(fraction);
        if (uploadProgressSlider.gameObject.activeSelf != visible)
            uploadProgressSlider.gameObject.SetActive(visible);
    }

    /// <summary>The end-of-game save is confirmed: the success message, then the continue button.</summary>
    void ShowSaveSucceeded()
    {
        SetProgress(1f, true);

        if (savingDataText != null)
        {
            savingDataText.color = successTextColor;
            savingDataText.text = SuccessMessage;
            if (savingDataIndicator != null)
                savingDataIndicator.SetActive(true);
        }
        else
        {
            ShowSavingIndicator(false);
            ToastManager.ShowToast(SuccessMessage, ToastType.Success);
        }

        ShowContinueButton("confirmed");
    }

    void ShowSavingIndicator(bool show)
    {
        if (savingDataIndicator != null)
            savingDataIndicator.SetActive(show);
        else if (show)
            ToastManager.ShowToast("Saving your game data — please wait a moment...", ToastType.Info);
    }

    /// <summary>
    /// The end-of-game save could not be confirmed after exhausting retries (or failed with an
    /// error no retry could fix). The failure message gives the participant their ID to pass on to
    /// the researcher, and the continue button still appears so they can go back to the survey. Any
    /// separate completion-code UI this scene has (showQualtricsInfoPanel/reminderText) is
    /// unaffected — ShowPanel() already reveals that independently of this save-confirmation wait.
    /// </summary>
    void ShowSaveFailed(string reason)
    {
        // The ID is what the researcher needs to find (or recover) this participant's data.
        string message = "We couldn't confirm that your game data was saved. Please contact the researcher for assistance. " +
                         ParticipantIdSentence();

        // A partly filled bar next to the failure message would be misleading.
        SetProgress(0f, false);

        if (savingDataText != null)
        {
            // Reuse the same "Saving your data..." text already on screen — swapping its wording
            // in place reads more naturally than hiding it and popping up a second object, and
            // needs no extra scene setup.
            savingDataText.color = failureTextColor;
            savingDataText.text = message;
            if (savingDataIndicator != null)
                savingDataIndicator.SetActive(true);
        }
        else
        {
            // No text to update, so leaving the stale "still saving" indicator up would be
            // misleading — hide it and say so with a toast instead.
            ShowSavingIndicator(false);
            ToastManager.ShowToast(message, ToastType.Warning, true);
        }

        GameLogPanel.Instance?.LogError($"End-of-game save could not be confirmed, giving up: {reason}");
        ShowContinueButton("not confirmed");
    }

    static string ParticipantIdSentence()
    {
        string id = PlayerSession.PlayerName;
        if (!string.IsNullOrEmpty(id) && id != "Unknown")
            return $"Your participant ID is {id}.";
        // No participant ID (none in the URL and none entered): the session ID still identifies
        // this participant's files on the server.
        return $"Your session ID is {PlayerSession.SessionId}.";
    }

    void ShowContinueButton(string saveOutcome)
    {
        if (continueButton == null)
        {
            // Without a button the participant has no way to reach the survey, so this must be visible.
            Debug.LogError($"[EndOfGamePanel] End-of-game save {saveOutcome}, but no continueButton is assigned. The participant cannot reach the survey.");
            return;
        }

        continueButton.gameObject.SetActive(true);
        Debug.Log($"[EndOfGamePanel] End-of-game save {saveOutcome}. Continue button shown.");
    }

    /// <summary>Wired to the continue button. Opens the follow-up survey once.</summary>
    public void OnContinueClicked()
    {
        if (isRedirecting) return;
        isRedirecting = true;

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
