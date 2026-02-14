using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EndGamePanel : MonoBehaviour
{
    [Header("UI References")]
    public GameObject endGamePanel;
    public Button viewReportButton;
    
    public static EndGamePanel Instance { get; private set; }
    
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
        if (viewReportButton != null)
        {
            viewReportButton.onClick.AddListener(OnViewReportClicked);
        }
        
        if (endGamePanel != null)
        {
            endGamePanel.SetActive(false);
        }
    }
    
    /// <summary>
    /// Show end game panel
    /// </summary>
    public void ShowEndGamePanel()
    {
        if (endGamePanel != null)
        {
            endGamePanel.SetActive(true);
            
            Debug.Log("End game panel displayed");
        }
    }
    
    /// <summary>
    /// Hide end game panel
    /// </summary>
    public void HideEndGamePanel()
    {
        if (endGamePanel != null)
        {
            endGamePanel.SetActive(false);
        }
    }
    
    /// <summary>
    /// Handle view report button click
    /// </summary>
    void OnViewReportClicked()
    {
        HideEndGamePanel();
        
        // Show daily report
        if (DailyReportManager.Instance != null)
        {
            DailyReportManager.Instance.ShowDailyReport();
        }
        
        Debug.Log("Opening final daily report from end game panel");
    }
}
