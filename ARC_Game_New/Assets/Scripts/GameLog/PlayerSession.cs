using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerSession : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject sessionPanel;
    [SerializeField] private TMP_InputField nameInputField;
    [SerializeField] private Button startButton;
    [SerializeField] private TextMeshProUGUI errorText;

    public static string SessionId { get; private set; } = "";
    public static string PlayerName { get; private set; } = "Unknown";
    public static bool IsSessionActive { get; private set; } = false;

    public static event Action OnSessionStarted;

    public static PlayerSession Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        SessionId = Guid.NewGuid().ToString().Substring(0, 8);
    }

    private void Start()
    {
        if (startButton != null)
            startButton.onClick.AddListener(OnStartButtonClicked);

        if (errorText != null)
            errorText.gameObject.SetActive(false);

        if (nameInputField != null)
        {
            string savedName = PlayerPrefs.GetString("PlayerName", "");
            if (!string.IsNullOrEmpty(savedName))
                nameInputField.text = savedName;
        }

        // Read the participant ID straight from this page's own URL, rather than relying only on
        // the wrapper HTML's unityInstance.SendMessage("PlayerSession", "SetParticipantIdFromUrl", uid)
        // call arriving at the right moment. That call fires the instant the Unity WebGL loader
        // resolves — which happens while the FIRST loaded scene (TitleScene) is showing, not this
        // one (TutorialScene, where this GameObject actually lives). Since the target doesn't exist
        // yet, Unity silently drops the message, and by the time this scene loads the uid is
        // already lost. Application.absoluteURL is empty in Editor/Standalone, so this still falls
        // through to the manual-entry panel exactly as before for local testing. The JS SendMessage
        // path is left in place as a harmless no-op/backup — if it ever does land after this
        // GameObject exists, it just re-sets the same value.
        string urlUid = GetQueryParam(Application.absoluteURL, "uid");
        if (!string.IsNullOrEmpty(urlUid))
        {
            SetParticipantIdFromUrl(urlUid);
            return;
        }

        ShowPanel();
    }

    /// <summary>Minimal query-string reader (avoids relying on System.Web, which Unity's WebGL
    /// runtime doesn't include). Returns null if the key isn't present.</summary>
    static string GetQueryParam(string url, string key)
    {
        if (string.IsNullOrEmpty(url)) return null;

        int queryStart = url.IndexOf('?');
        if (queryStart < 0) return null;
        string query = url.Substring(queryStart + 1);

        int fragmentStart = query.IndexOf('#');
        if (fragmentStart >= 0) query = query.Substring(0, fragmentStart);

        foreach (string pair in query.Split('&'))
        {
            int eq = pair.IndexOf('=');
            string k = eq >= 0 ? pair.Substring(0, eq) : pair;
            if (k != key) continue;
            string v = eq >= 0 ? pair.Substring(eq + 1) : "";
            return Uri.UnescapeDataString(v);
        }
        return null;
    }

    void ShowPanel()
    {
        if (sessionPanel != null)
            sessionPanel.SetActive(true);

        Time.timeScale = 0f;

        GameLogPanel.Instance?.LogUIInteraction("Player session panel shown");
    }

    void OnStartButtonClicked()
    {
        if (nameInputField == null) return;

        string inputName = nameInputField.text.Trim();

        if (string.IsNullOrEmpty(inputName))
        {
            if (errorText != null)
            {
                errorText.text = "Please enter your name.";
                errorText.gameObject.SetActive(true);
            }
            return;
        }

        CompleteSession(inputName);
    }

    /// <summary>
    /// Called from the browser wrapper's JS (via unityInstance.SendMessage("PlayerSession",
    /// "SetParticipantIdFromUrl", uid)) when a participant ID was found in the page's URL query
    /// string (Qualtrics redirect flow: ?uid=...). Skips the manual-entry panel entirely and
    /// starts the session with that ID through the same completion path the manual Start button
    /// uses. Ignored (leaving the manual-entry panel showing) if uid is missing/empty, so local
    /// testing without a query string still works exactly as before.
    /// </summary>
    public void SetParticipantIdFromUrl(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return;

        if (nameInputField != null)
            nameInputField.text = uid; // keep in sync in case anything else reads the field directly

        GameLogPanel.Instance?.LogPlayerAction($"Participant ID captured from URL: {uid}");
        CompleteSession(uid);
    }

    void CompleteSession(string id)
    {
        PlayerName = id;
        PlayerPrefs.SetString("PlayerName", id);
        PlayerPrefs.Save();
        IsSessionActive = true;

        Debug.Log($"[PlayerSession] Session started: {PlayerName} ({SessionId})");
        GameLogPanel.Instance?.LogPlayerAction($"Session started: {PlayerName} ({SessionId})");

        if (sessionPanel != null)
            sessionPanel.SetActive(false);

        Time.timeScale = 1f;

        OnSessionStarted?.Invoke();
    }

    public static string GetSessionFileName()
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string safeName = PlayerName.Replace(" ", "_");
        return $"{safeName}_{SessionId}_{timestamp}";
    }
}