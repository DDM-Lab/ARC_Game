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

        PlayerName = inputName;
        PlayerPrefs.SetString("PlayerName", inputName);
        PlayerPrefs.Save();
        IsSessionActive = true;

        Debug.Log($"[PlayerSession] Session started: {PlayerName} ({SessionId})");

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