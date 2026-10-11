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
/// session start — see PlayerSession) happens only through the continue button. That button
/// is hidden until the server has confirmed the end-of-game save (LogSender.FinalUploadConfirmed),
/// so the participant cannot reach the survey before their data is on the server. The upload is
/// retried a bounded number of times (retryDelaysSeconds) rather than forever — an earlier version
/// retried indefinitely with no cap, which meant a payload that reliably fails (e.g. the server's
/// hard size cap, or a genuinely slow upload/server) left the participant on a silent spinner
/// forever with no way through. Once retries are exhausted, or the server reports a failure no
/// retry could fix (413 Payload Too Large), saveFailedIndicator is shown instead and the continue
/// button stays hidden — unconfirmed data must not silently pass through.
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
    [Tooltip("Scene button that takes the participant to the Qualtrics follow-up survey. Hidden at Start. It is shown only after the server confirms the end-of-game save, and clicking it opens the survey. Label and styling are authored in the scene.")]
    public Button continueButton;

    [Header("Qualtrics Follow-up Redirect")]
    [Tooltip("Follow-up Qualtrics survey URL. {uid} is replaced with the participant's ID (PlayerSession.PlayerName) before redirecting. Leave empty to disable the redirect. If StreamingAssets/config.json sets a valid followUpSurveyUrl (http(s), containing {uid}), that link is used INSTEAD of this one — this is only the fallback.")]
    public string followUpSurveyUrlTemplate = "https://xxxx.qualtrics.com/jfe/form/xxxx/?uid={uid}";

    [Header("Saving Before Continue")]
    [Tooltip("Delay before each retry after a failed/unconfirmed upload attempt. The first attempt is immediate; after that, one retry follows each delay in this list in order (3 entries = 3 retries = 4 total attempts). If the server reports an error no retry could fix (413 Payload Too Large), the remaining delays are skipped and the save is given up on immediately. Once the list is exhausted without confirmation, the failure message below is shown and the continue button stays hidden.")]
    public float[] retryDelaysSeconds = { 5f, 10f, 20f };
    [Tooltip("Optional. A scene object (e.g. a 'Saving your data...' text) shown from the start of the end-of-game save until the server confirms it. Hidden at Start. If left empty, a toast is shown instead.")]
    public GameObject savingDataIndicator;
    [Tooltip("Optional. The text component already shown on savingDataIndicator (e.g. \"Saving your data, please wait. We will soon redirect you to the post-game survey.\"). If assigned, its wording is replaced in place with the failure message instead of popping up a separate object when the save could not be confirmed after all retries. If left empty (but savingDataIndicator isn't), the indicator is hidden and a toast is shown instead, since there would be no way to update its wording.")]
    public TextMeshProUGUI savingDataText;

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
    /// Reveals the panel (if enabled) and starts waiting for the server to confirm the end-of-game
    /// save. The continue button appears only after that confirmation. Safe to call more than once
    /// (e.g. if the Day 8 report is re-shown): the wait is started once.
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
    /// That starts the end-of-game upload (LogSender, "final" kind). This waits until LogSender
    /// reports the server's ack for that final upload, then shows the continue button.
    ///
    /// Failed and unconfirmed uploads are re-sent on the schedule in retryDelaysSeconds — bounded,
    /// not forever: a payload that fails will fail the same way every retry (a hard size cap, or a
    /// slow upload/server that reliably exceeds the request timeout), so retrying indefinitely
    /// would just leave the participant on a silent spinner forever with no way through. A day
    /// checkpoint still in flight is waited for first, so it cannot block the final upload.
    ///
    /// The continue button is shown only after the server has confirmed the final upload. If
    /// retries run out, or the server reports an error no retry could fix (413 Payload Too Large),
    /// the "saving" text/indicator is repurposed to tell the participant to contact the researcher
    /// instead (see ShowSaveFailed) and the continue button stays hidden — unconfirmed data must
    /// not silently pass through. If saving is not possible at all (data collection off,
    /// or no log system in the scene), the wait continues indefinitely and the error is logged
    /// once; that is a build/config problem rather than an upload failure, so there is nothing a
    /// give-up message would add.
    /// </summary>
    IEnumerator WaitForConfirmedSaveThenShowContinue()
    {
        ShowSavingIndicator(true);

        bool reportedCannotSave = false;
        int attempt = 0; // number of SendAllLogs() calls issued so far
        float nextAttemptAt = 0f; // first attempt right away
        while (true)
        {
            LogSender sender = LogSender.Instance;

            // The button must not appear without a confirmed save, so if saving is not possible at
            // all the wait simply continues (and says why, once). It never falls through to the button.
            if (sender == null || GameLogPanel.Instance == null || !GameLogPanel.DataCollectionEnabled)
            {
                if (!reportedCannotSave)
                {
                    Debug.LogError("[EndOfGamePanel] Cannot save the end-of-game log (no LogSender, no GameLogPanel, or data collection is disabled). The continue button stays hidden.");
                    reportedCannotSave = true;
                }
                yield return null;
                continue;
            }

            if (sender.FinalUploadConfirmed)
                break;

            if (sender.CurrentStatus == LogSender.SendStatus.Sending)
            {
                yield return null; // the final upload, or a checkpoint still in flight, will finish first
                continue;
            }

            // An attempt has just finished (or none has started yet) without confirmation.
            if (attempt > 0 && sender.LastFailureIsUnrecoverable)
            {
                // Retrying would resend the identical payload into the identical rejection — stop
                // immediately instead of burning the remaining retry budget on a guaranteed repeat.
                Debug.LogError($"[EndOfGamePanel] End-of-game save failed with an unrecoverable error (HTTP {sender.LastHttpResponseCode}: {sender.LastStatusMessage}). Giving up without further retries.");
                ShowSaveFailed(sender.LastStatusMessage);
                yield break;
            }

            if (attempt >= retryDelaysSeconds.Length + 1)
            {
                Debug.LogError($"[EndOfGamePanel] End-of-game save not confirmed after {attempt} attempts ({sender.LastStatusMessage}). Giving up.");
                ShowSaveFailed(sender.LastStatusMessage);
                yield break;
            }

            if (Time.unscaledTime >= nextAttemptAt)
            {
                if (attempt > 0)
                    Debug.LogWarning($"[EndOfGamePanel] End-of-game save not confirmed ({sender.LastStatusMessage}). Retry {attempt} of {retryDelaysSeconds.Length}.");

                sender.SendAllLogs();
                attempt++;
                nextAttemptAt = attempt <= retryDelaysSeconds.Length
                    ? Time.unscaledTime + retryDelaysSeconds[attempt - 1]
                    : Time.unscaledTime; // unreachable (the exhaustion check above catches this first) — kept safe regardless
            }

            yield return null;
        }

        ShowSavingIndicator(false);
        ShowContinueButton();
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
    /// error no retry could fix). The continue button is deliberately NOT shown here: unconfirmed
    /// data must not silently pass through. Any separate completion-code UI this scene has
    /// (showQualtricsInfoPanel/reminderText) is unaffected — ShowPanel() already reveals that
    /// independently of this save-confirmation wait.
    /// </summary>
    void ShowSaveFailed(string reason)
    {
        const string fallbackMessage = "We couldn't confirm that your data was saved. Please contact the researcher before closing this window.";

        if (savingDataText != null)
        {
            // Reuse the same "Saving your data..." text already on screen — swapping its wording
            // in place reads more naturally than hiding it and popping up a second object, and
            // needs no extra scene setup.
            savingDataText.text = fallbackMessage;
            if (savingDataIndicator != null)
                savingDataIndicator.SetActive(true);
        }
        else
        {
            // No text to update, so leaving the stale "still saving" indicator up would be
            // misleading — hide it and say so with a toast instead.
            ShowSavingIndicator(false);
            ToastManager.ShowToast(fallbackMessage, ToastType.Warning, true);
        }

        GameLogPanel.Instance?.LogError($"End-of-game save could not be confirmed, giving up: {reason}");
    }

    void ShowContinueButton()
    {
        if (continueButton == null)
        {
            // Without a button the participant has no way to reach the survey, so this must be visible.
            Debug.LogError("[EndOfGamePanel] The save is confirmed, but no continueButton is assigned. The participant cannot reach the survey.");
            return;
        }

        continueButton.gameObject.SetActive(true);
        Debug.Log("[EndOfGamePanel] End-of-game save confirmed. Continue button shown.");
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
