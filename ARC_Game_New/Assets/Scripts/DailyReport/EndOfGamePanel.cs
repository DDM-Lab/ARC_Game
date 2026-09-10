using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Shown once, alongside the Day 8 Daily Report, to give the player their
/// predefined completion code for the Qualtrics post-game survey (all wording,
/// including the code itself, is authored directly in the scene — this script
/// only controls visibility/timing). Purely a player-facing prompt on top of the
/// report — does not touch report display, animation, or the data-collection/
/// logging flow in DailyReportUI/DailyReportManager.
/// </summary>
public class EndOfGamePanel : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("The panel GameObject to show/hide. Should NOT be a full-screen raycast blocker — the player must be able to keep reading the Daily Report underneath.")]
    public GameObject panel;
    public Button closeButton;
    [Tooltip("Only revealed once the panel above is closed, so the code stays visible while reviewing the rest of the Day 8 report. Text is predefined in the scene — this script only shows/hides it.")]
    public TextMeshProUGUI reminderText;


    public static EndOfGamePanel Instance { get; private set; }

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
