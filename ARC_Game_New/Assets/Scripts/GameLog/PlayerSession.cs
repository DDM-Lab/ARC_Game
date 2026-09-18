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

        ShowPanel();
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